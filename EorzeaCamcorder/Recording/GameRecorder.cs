using System;
using System.Collections.Concurrent;
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

    private int _savingTasks = 0;
    public bool IsSaving => _savingTasks > 0;

    public event Action<string>? OnRecordingError;

    private CancellationTokenSource? _cancellationTokenSource;
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

        if (!Directory.Exists(_config.OutputDirectory)) Directory.CreateDirectory(_config.OutputDirectory);

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

        await CheckEngineStop();

        Interlocked.Increment(ref _savingTasks);
        _ = Task.Run(async () => {
            try { await FFmpegMuxer.RemuxToFinalFormatAsync(tempPath, finalPath, true); }
            finally { Interlocked.Decrement(ref _savingTasks); }
        });
    }

    public void StartReplayBuffer(string initiator = "User")
    {
        if (IsReplayBufferRunning) return;

        int capacityBytes = ((_config.VideoBitrateKbps + 200) * 1024 / 8) * _config.ReplayBufferSeconds;
        _ringBuffer = new RollingMemoryStream(capacityBytes);
        
        _broadcastStream.RingBuffer = _ringBuffer;
        IsReplayBufferRunning = true;
        
        Log.Information($"Replay Buffer ({_config.ReplayBufferSeconds}s) activated in RAM.");
        EnsureEngineRunning(initiator);
    }

    public async Task StopReplayBuffer()
    {
        if (!IsReplayBufferRunning) return;
        IsReplayBufferRunning = false;

        _broadcastStream.RingBuffer = null;
        _ringBuffer = null;

        Log.Information("Replay Buffer deactivated.");
        await CheckEngineStop();
    }

    public void SaveReplayBuffer(string? customFilePath = null)
    {
        if (!IsReplayBufferRunning || _ringBuffer == null) return;

        byte[] snapshot = _ringBuffer.TakeSnapshot();
        
        if (!Directory.Exists(_config.OutputDirectory)) Directory.CreateDirectory(_config.OutputDirectory);
        string finalPath = customFilePath ?? Path.Combine(_config.OutputDirectory, $"Replay_{DateTime.Now:yyyyMMdd_HHmmss}.{_config.OutputFormat}");
        string tempTsFile = finalPath + ".temp.ts";

        Interlocked.Increment(ref _savingTasks);
        Task.Run(async () => {
            try 
            {
                await File.WriteAllBytesAsync(tempTsFile, snapshot);
                await FFmpegMuxer.RemuxToFinalFormatAsync(tempTsFile, finalPath, true);
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
        if (!IsEngineRunning || PreviewTexture == null || _frameQueue == null || _frameQueue.IsAddingCompleted) return;

        if (!_recordingStopwatch.IsRunning)
        {
            _recordingStopwatch.Start();
            CaptureFrameAsync(1); 
            _encodedFrameCount = 1;
            return;
        }

        double elapsedSeconds = _recordingStopwatch.Elapsed.TotalSeconds;
        long expectedFrames = (long)(elapsedSeconds * _targetFps);
        long framesToCapture = expectedFrames - _encodedFrameCount;

        if (framesToCapture > 0)
        {
            _encodedFrameCount += framesToCapture;
            CaptureFrameAsync((int)framesToCapture);
        }
    }

    private async void CaptureFrameAsync(int repeatCount)
    {
        var queueRef = _frameQueue;
        var textureRef = PreviewTexture;

        if (textureRef == null || queueRef == null || queueRef.IsAddingCompleted) return;

        try
        {
            var result = await TextureReadback.GetRawImageAsync(textureRef, leaveWrapOpen: true);

            if (queueRef.IsAddingCompleted) return;
            if (result.RawData == null) return;

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

            try
            {
                if (!_audioCaptureStarted) _audioCaptureStarted = true;
                if (!queueRef.TryAdd(frame)) System.Buffers.ArrayPool<byte>.Shared.Return(rawBuffer);
            }
            catch (InvalidOperationException) { System.Buffers.ArrayPool<byte>.Shared.Return(rawBuffer); }
        }
        catch (Exception ex) { Log.Error($"Frame capture error: {ex.Message}"); }
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
