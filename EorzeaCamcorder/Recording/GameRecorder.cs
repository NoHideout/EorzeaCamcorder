using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Dalamud.Plugin.Services;

namespace EorzeaCamcorder.Recording;

public class GameRecorder : IDisposable
{
    private IPluginLog Log => Service.Log;
    private ITextureReadbackProvider TextureReadback => Service.TextureReadbackProvider;
    private ITextureProvider TextureProvider => Service.TextureProvider;
    
    
    private readonly Configuration _config;
    private int _targetFps = 60;

    public IDalamudTextureWrap? PreviewTexture { get; private set; }
    public bool IsEngineRunning { get; private set; } = false;
    public bool IsRecording { get; private set; } = false;
    public bool IsReplayBufferRunning { get; private set; } = false;
    public string Initiator { get; private set; } = "User";

    private bool _isCapturing = false;
    private int _savingTasks = 0;
    public bool IsSaving => _savingTasks > 0;
    private int _waitingTasks = 0;
    public bool IsWaiting => _waitingTasks > 0;
    
    public event Action<string>? OnRecordingError;

    private CancellationTokenSource? _cancellationTokenSource;
    private CancellationTokenSource? _replayCancellationTokenSource;
    private BlockingCollection<CapturedFrame>? _frameQueue;
    private BlockingCollection<byte[]>? _audioQueue;
    private Task? _encoderTask;

    private AudioRecorder? _audioRecorder;
    private volatile bool _audioCaptureStarted = false;

    private Stopwatch _recordingStopwatch = new();
    private long _encodedFrameCount = 0;

    private readonly BroadcastStream _broadcastStream = new();
    private RollingMemoryStream? _ringBuffer;
    private FileStream? _activeFileStream;
    
    private string? _currentRecordingFinalPath;
    private string? _currentRecordingTempPath;
    
    private List<(long TimestampMs, string Title)> _chapterMarkers = new();

    public GameRecorder(Configuration config) { _config = config; }

    private void EnsureEngineRunning(string initiator)
    {
        if (IsEngineRunning) return;

        try
        {
            _targetFps = _config.TargetFps;
            IsEngineRunning = true;
            Initiator = initiator;

            _audioCaptureStarted = false;
            _encodedFrameCount = 0;
            _recordingStopwatch.Reset();
            _chapterMarkers.Clear();
            
            _audioRecorder = new AudioRecorder();
            _cancellationTokenSource = new CancellationTokenSource();
            
            _frameQueue = new BlockingCollection<CapturedFrame>(boundedCapacity: _targetFps * 2);
            _audioQueue = new BlockingCollection<byte[]>();
            
            var viewportId = ImGui.GetMainViewport().ID;
            _audioRecorder.StartToCallback(OnAudioDataReceived);
            
            _encoderTask = Task.Run(() => FFmpegMuxer.StartCaptureEngineAsync(
                _cancellationTokenSource.Token, _frameQueue, _audioQueue, 
                _audioRecorder, _broadcastStream, _config, OnRecordingError));

            Task.Run(async () => await CreateTextureWrap(_cancellationTokenSource.Token, viewportId));
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start capture engine.");
            IsEngineRunning = false;
            CleanupResources();
        }
    }

    private async Task CheckEngineStop()
    {
        if (!IsRecording && !IsReplayBufferRunning && IsEngineRunning)
        {
            IsEngineRunning = false;
            _recordingStopwatch.Stop();
            _audioRecorder?.Stop();

            _frameQueue?.CompleteAdding();
            _audioQueue?.CompleteAdding();

            if (_encoderTask != null) try { await _encoderTask; } catch { }
            CleanupResources();
        }
    }
    
    public void StartRecording(string? customFilePath = null, string initiator = "User")
    {
        if (IsRecording) return;

        Directory.CreateDirectory(_config.OutputDirectory);

        _currentRecordingFinalPath = customFilePath ?? Path.Combine(_config.OutputDirectory, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.{_config.OutputFormat}");
        _currentRecordingTempPath = _currentRecordingFinalPath + ".ts";

        _activeFileStream = new FileStream(_currentRecordingTempPath, FileMode.Create, FileAccess.Write, FileShare.Read);
        _broadcastStream.FileStream = _activeFileStream;
        
        IsRecording = true;
        Log.Information($"Started Recording to: {_currentRecordingFinalPath}");
        EnsureEngineRunning(initiator);
    }

    public async Task StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;

        _broadcastStream.FileStream = null;
        _activeFileStream?.Dispose();
        _activeFileStream = null;

        string tempPath = _currentRecordingTempPath!;
        string finalPath = _currentRecordingFinalPath!;
        
        string? metadataFile = null;
        if (_chapterMarkers.Count > 0)
        {
            metadataFile = finalPath + ".metadata.txt";
            await GenerateMetadataFileAsync(metadataFile, _chapterMarkers, _recordingStopwatch.ElapsedMilliseconds);
        }
        
        await CheckEngineStop();

        Interlocked.Increment(ref _savingTasks);
        _ = Task.Run(async () => {
            try { await FFmpegMuxer.RemuxToFinalFormatAsync(tempPath, finalPath, true, null, metadataFile); }
            finally { Interlocked.Decrement(ref _savingTasks); }
        });
    }

    public void StartReplayBuffer(string initiator = "User")
    {
        if (IsReplayBufferRunning) return;
        // ReSharper disable once PossibleLossOfFraction
        int capacityBytes = (int)(((_config.VideoBitrateKbps + 200) * 1024L / 8L) * _config.ReplayBufferSeconds * 1.5);
        _ringBuffer = new RollingMemoryStream(capacityBytes);
        
        _replayCancellationTokenSource = new CancellationTokenSource();
        
        _broadcastStream.RingBuffer = _ringBuffer;
        IsReplayBufferRunning = true;
        
        Log.Information($"Replay Buffer ({_config.ReplayBufferSeconds}s) activated in RAM.");
        EnsureEngineRunning(initiator);
    }

    public async Task StopReplayBuffer()
    {
        if (!IsReplayBufferRunning) return;
        IsReplayBufferRunning = false;
        
        _replayCancellationTokenSource?.Cancel();
        _replayCancellationTokenSource?.Dispose();
        _replayCancellationTokenSource = null;
        
        _broadcastStream.RingBuffer = null;
        _ringBuffer = null;

        Log.Information("Replay Buffer deactivated.");
        await CheckEngineStop();
    }

    public void SaveReplayBuffer(string? customFilePath = null, ReplayEventPosition? positionOverride = null)
    {
        if (!IsReplayBufferRunning || _ringBuffer == null) return;

        _ = SaveReplayBufferDelayed(customFilePath, positionOverride);
    }

    private async Task SaveReplayBufferDelayed(string? customFilePath, ReplayEventPosition? positionOverride)
    {
        var ringBuffer = _ringBuffer;
        var token = _replayCancellationTokenSource?.Token ?? CancellationToken.None;
        if (!IsReplayBufferRunning || ringBuffer == null) return;
    
        ReplayEventPosition targetPosition = positionOverride ?? _config.ReplayEventPosition;

        int delayMs = targetPosition switch
        {
            ReplayEventPosition.Start => _config.ReplayBufferSeconds * 1000,
            ReplayEventPosition.Middle => (_config.ReplayBufferSeconds * 1000) / 2,
            _ => 0
        };

        if (delayMs > 0)
        {
            Interlocked.Increment(ref _waitingTasks);
            try 
            { 
                await Task.Delay(delayMs, token); 
            }
            catch (TaskCanceledException) { return; }
            finally 
            {
                Interlocked.Decrement(ref _waitingTasks); 
            }
        }
        
        byte[] snapshot = ringBuffer.TakeSnapshot();

        Directory.CreateDirectory(_config.OutputDirectory);
        string finalPath = customFilePath ?? Path.Combine(_config.OutputDirectory, $"Replay_{DateTime.Now:yyyyMMdd_HHmmss}.{_config.OutputFormat}");
        string tempTsFile = finalPath + ".temp.ts";

        Interlocked.Increment(ref _savingTasks);

        _ = Task.Run(async () =>
        {
            try
            {
                await File.WriteAllBytesAsync(tempTsFile, snapshot);
                await FFmpegMuxer.RemuxToFinalFormatAsync(tempTsFile, finalPath, true, _config.ReplayBufferSeconds);
            }
            finally { Interlocked.Decrement(ref _savingTasks); }
        });
    }
    
    private void OnAudioDataReceived(byte[] data, int size)
    {
        if (!IsEngineRunning || !_audioCaptureStarted || _audioQueue == null) return;
        _audioQueue.Add(data);
    }

    public void Update()
    {
        if (!IsEngineRunning || PreviewTexture == null || _frameQueue == null || _frameQueue.IsAddingCompleted || _isCapturing) return;

        if (!_recordingStopwatch.IsRunning)
        {
            _recordingStopwatch.Start();
            CaptureFrameAsync(1);
            return;
        }

        double elapsedSeconds = _recordingStopwatch.Elapsed.TotalSeconds;
        long expectedFrames = (long)(elapsedSeconds * _targetFps);
        long framesToCapture = expectedFrames - _encodedFrameCount;

        if (framesToCapture > 0)
        {
            CaptureFrameAsync((int)framesToCapture);
        }
    }

    private async void CaptureFrameAsync(int repeatCount)
{
    var queueRef = _frameQueue;
    var textureRef = PreviewTexture;

    if (textureRef == null || queueRef == null || queueRef.IsAddingCompleted) return;

    _isCapturing = true;

    try
    {
        var result = await TextureReadback.GetRawImageAsync(textureRef, leaveWrapOpen: true);

        if (queueRef.IsAddingCompleted || result.RawData == null) return;

        var spec = result.Specification;
        byte[] rawSource = result.RawData;

        int width = spec.Width;
        int height = spec.Height;
        int pitch = spec.Pitch;

        if (width % 2 != 0) width--;
        if (height % 2 != 0) height--;

        int stride = width * 4;
        byte[] rawBuffer = System.Buffers.ArrayPool<byte>.Shared.Rent(stride * height);
        var srcSpan = MemoryMarshal.Cast<byte, uint>(rawSource.AsSpan());
        var dstSpan = MemoryMarshal.Cast<byte, uint>(rawBuffer.AsSpan());

        int pitchInUints = pitch / 4;
        int strideInUints = width; 

        for (int y = 0; y < height; y++)
        {
            int srcRowStart = y * pitchInUints;
            int dstRowStart = y * strideInUints; 

            for (int x = 0; x < width; x++)
            {
                uint pixel = srcSpan[srcRowStart + x];
                dstSpan[dstRowStart + x] = (pixel & 0xFF00FF00) | ((pixel & 0x00FF0000) >> 16) | ((pixel & 0x000000FF) << 16);
            }
        }

        var frame = new CapturedFrame { Data = rawBuffer, RepeatCount = repeatCount, Width = width, Height = height };

        if (!_audioCaptureStarted) _audioCaptureStarted = true;
        
        if (!queueRef.TryAdd(frame)) 
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rawBuffer);
        }
        else
        {
            _encodedFrameCount += repeatCount;
        }
    }
    catch (InvalidOperationException) 
    { 
        // queue closed mid-add
    }
    catch (Exception ex) 
    { 
        Log.Error($"Frame capture error: {ex.Message}"); 
    }
    finally
    {
        _isCapturing = false;
    }
}

    private async Task CreateTextureWrap(CancellationToken token, uint viewportId)
    {
        try
        {
            var textureArguments = new ImGuiViewportTextureArgs() { AutoUpdate = true, KeepTransparency = false, TakeBeforeImGuiRender = true, ViewportId = viewportId };
            PreviewTexture = await TextureProvider.CreateFromImGuiViewportAsync(textureArguments, cancellationToken: token);
        }
        catch { }
    }
    
    public void RecoverOrphanedFiles()
    {
        if (!Directory.Exists(_config.OutputDirectory)) return;

        Task.Run(async () =>
        {
            string[] tsFiles = Directory.GetFiles(_config.OutputDirectory, "*.ts");
            foreach (var tsFile in tsFiles)
            {
                try
                {
                    using (File.Open(tsFile, FileMode.Open, FileAccess.Read, FileShare.None)) { }
                    string finalPath = tsFile.Substring(0, tsFile.Length - 3);

                    Log.Information($"Recovering orphaned file: {tsFile}");
                    
                    Interlocked.Increment(ref _savingTasks);
                    try 
                    { 
                        await FFmpegMuxer.RemuxToFinalFormatAsync(tsFile, finalPath, true); 
                    }
                    finally 
                    { 
                        Interlocked.Decrement(ref _savingTasks); 
                    }
                }
                catch (IOException) { }
                catch (Exception ex)
                {
                    Log.Error($"Failed to recover {tsFile}: {ex.Message}");
                }
            }
        });
    }
    
    public void AddChapterMarker(string title)
    {
        if (!IsRecording || !_recordingStopwatch.IsRunning) return;
    
        _chapterMarkers.Add((_recordingStopwatch.ElapsedMilliseconds, title));
        Service.Log.Verbose($"Added chapter '{title}' at {_recordingStopwatch.ElapsedMilliseconds}ms");
    }
    
    private async Task GenerateMetadataFileAsync(string filePath, List<(long TimestampMs, string Title)> markers, long totalDurationMs)
    {
        using var writer = new StreamWriter(filePath);
        await writer.WriteLineAsync(";FFMETADATA1");
        if (markers.Count > 0 && markers[0].TimestampMs > 0) // need to write an initial marker so the first marker isnt stretched
        {
            await writer.WriteLineAsync("[CHAPTER]");
            await writer.WriteLineAsync("TIMEBASE=1/1000");
            await writer.WriteLineAsync("START=0");
            await writer.WriteLineAsync($"END={markers[0].TimestampMs}");
            await writer.WriteLineAsync("title=Recording Started");
        }
        
        for (int i = 0; i < markers.Count; i++)
        {
            long start = markers[i].TimestampMs;
            long end = (i + 1 < markers.Count) ? markers[i + 1].TimestampMs : totalDurationMs;

            await writer.WriteLineAsync("[CHAPTER]");
            await writer.WriteLineAsync("TIMEBASE=1/1000");
            await writer.WriteLineAsync($"START={start}");
            await writer.WriteLineAsync($"END={end}");
            string safeTitle = markers[i].Title.Replace("=", " ").Replace(";", " ").Replace("#", " "); //jic i add user strings for markers someday
            await writer.WriteLineAsync($"title={safeTitle}");
        }
    }
    
    private void CleanupResources()
    {
        _cancellationTokenSource?.Dispose(); _cancellationTokenSource = null;
        PreviewTexture?.Dispose(); PreviewTexture = null;
        _audioRecorder?.Dispose(); _audioRecorder = null;
        _ringBuffer = null;
        _activeFileStream?.Dispose(); _activeFileStream = null;
        _broadcastStream.RingBuffer = null;
        _broadcastStream.FileStream = null;
    }

    public void Dispose() 
    { 
        if (IsRecording) _ = StopRecording(); 
        if (IsReplayBufferRunning) _ = StopReplayBuffer();
    }
}
