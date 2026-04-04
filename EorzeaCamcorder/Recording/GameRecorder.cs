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

namespace EorzeaCamcorder.Recording;

public class GameRecorder : IDisposable
{
    private int _targetFps = 60;
    
    public IDalamudTextureWrap? PreviewTexture { get; private set; }
    public bool IsRecording { get; private set; } = false;
    public bool IsSaving { get; private set; } = false;
    public string Initiator { get; private set; } = "User";

    public event Action<string>? OnRecordingError;

    private CancellationTokenSource? _cancellationTokenSource;
    private BlockingCollection<CapturedFrame>? _frameQueue;
    private BlockingCollection<byte[]>? _audioQueue;
    private Task? _encoderTask;

    private AudioRecorder? _audioRecorder;
    private volatile bool _audioCaptureStarted = false;

    private Stopwatch _recordingStopwatch = new();
    private long _encodedFrameCount = 0;
    
    private readonly Configuration _config;
    public GameRecorder(Configuration config) 
    { 
        _config = config; 
    }
    
    public void StartRecording(string? customFilePath = null, string initiator = "User")
    {
        if (IsRecording || IsSaving) return;

        try
        {
            string? filePath = customFilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                if (!Directory.Exists(_config.OutputDirectory)) 
                {
                    Directory.CreateDirectory(_config.OutputDirectory);
                }
                filePath = Path.Combine(_config.OutputDirectory, $"Recording_{DateTime.Now:yyyyMMdd_HHmmss}.{_config.OutputFormat}");
            }
            else
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            _targetFps = _config.TargetFps;
            IsRecording = true;
            Initiator = initiator;

            _audioCaptureStarted = false;
            _encodedFrameCount = 0;
            _recordingStopwatch.Reset();

            Plugin.Log.Information($"Starting recording to {filePath} (Initiated by: {initiator})...");

            _audioRecorder = new AudioRecorder();
            _cancellationTokenSource = new CancellationTokenSource();
            
            _frameQueue = new BlockingCollection<CapturedFrame>(boundedCapacity: _targetFps * 2);
            _audioQueue = new BlockingCollection<byte[]>();
            
            var viewportId = ImGui.GetMainViewport().ID;

            _audioRecorder.StartToCallback(OnAudioDataReceived);
            
            _encoderTask = Task.Run(() => FFmpegMuxer.EncodeAsync(
                _cancellationTokenSource.Token, 
                _frameQueue, 
                _audioQueue, 
                _audioRecorder, 
                filePath,
                _config,
                OnRecordingError));

            Task.Run(async () => await CreateTextureWrap(_cancellationTokenSource.Token, viewportId));
        }
        catch (Exception ex)
        {
            Plugin.Log.Error(ex, "Failed to start recording.");
            OnRecordingError?.Invoke($"Failed to start: {ex.Message}");
            IsRecording = false;
            CleanupResources();
        }
    }

    private void OnAudioDataReceived(IntPtr data, int size)
    {
        if (!IsRecording || !_audioCaptureStarted || _audioQueue == null) return;

        byte[] safeData = new byte[size];
        Marshal.Copy(data, safeData, 0, size);
        _audioQueue.Add(safeData);
    }

    public async Task StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        IsSaving = true;

        _recordingStopwatch.Stop();
        _audioRecorder?.Stop();

        _frameQueue?.CompleteAdding();
        _audioQueue?.CompleteAdding();

        if (_encoderTask != null) try { await _encoderTask; } catch { }

        CleanupResources();
        IsSaving = false;
        Plugin.Log.Information("Recording saved.");
    }

    public void Update()
    {
        if (!IsRecording || PreviewTexture == null || _frameQueue == null || _frameQueue.IsAddingCompleted)
            return;

        if (!_recordingStopwatch.IsRunning)
        {
            _recordingStopwatch.Start();
            CaptureFrameAsync(1); 
            _encodedFrameCount = 1;
            return;
        }

        double elapsedSeconds = _recordingStopwatch.Elapsed.TotalSeconds;
        long expectedTotalFrames = (long)(elapsedSeconds * _targetFps);

        long framesToCapture = expectedTotalFrames - _encodedFrameCount;

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
            var result = await Plugin.TextureReadbackProvider.GetRawImageAsync(textureRef, leaveWrapOpen: true);

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

            // RGBA to BGRA
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

            var frame = new CapturedFrame
            {
                Data = rawBuffer,
                RepeatCount = repeatCount,
                Width = width,
                Height = height
            };

            try
            {
                if (!_audioCaptureStarted) _audioCaptureStarted = true;
                if (!queueRef.TryAdd(frame))
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rawBuffer);
                }
            }
            catch (InvalidOperationException) 
            {
                System.Buffers.ArrayPool<byte>.Shared.Return(rawBuffer);
            }
        }
        catch (Exception ex) { Plugin.Log.Error($"Frame capture error: {ex.Message}"); }
    }

    private async Task CreateTextureWrap(CancellationToken token, uint viewportId)
    {
        try
        {
            var textureArguments = new ImGuiViewportTextureArgs() { AutoUpdate = true, KeepTransparency = false, TakeBeforeImGuiRender = true, ViewportId = viewportId };
            PreviewTexture = await Plugin.TextureProvider.CreateFromImGuiViewportAsync(textureArguments, cancellationToken: token);
        }
        catch { }
    }

    private void CleanupResources()
    {
        _cancellationTokenSource?.Dispose(); _cancellationTokenSource = null;
        PreviewTexture?.Dispose(); PreviewTexture = null;
        _audioRecorder?.Dispose(); _audioRecorder = null;
    }

    public void Dispose() { if (IsRecording) _ = StopRecording(); }
}
