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
using Vortice.MediaFoundation;

namespace EorzeaCamcorder.Recording;

public class GameRecorder : IDisposable
{
    private int _targetFps = 60;
    private long _ticksPerFrame = 166666;

    private readonly Guid VideoFormatH264 = VideoFormatGuids.H264;
    private readonly Guid InputFormat = VideoFormatGuids.Argb32;

    public IDalamudTextureWrap? PreviewTexture { get; private set; }
    public bool IsRecording { get; private set; } = false;
    public bool IsSaving { get; private set; } = false;
    public string Initiator { get; private set; } = "User";

    public event Action<string>? OnRecordingError;

    private CancellationTokenSource? _cancellationTokenSource;
    private BlockingCollection<CapturedFrame>? _frameQueue;
    private Task? _encoderTask;

    private AudioRecorder? _audioRecorder;
    private IMFSinkWriter? _sinkWriter;
    private int _audioStreamIndex = -1;

    private long _totalAudioBytes = 0;
    private int _audioBytesPerSecond = 0;
    private volatile bool _audioCaptureStarted = false;

    private Stopwatch _recordingStopwatch = new();
    private long _encodedFrameCount = 0;

    private ConcurrentQueue<byte[]> _preStartAudioBuffer = new();

    private struct CapturedFrame
    {
        public byte[] Data;
        public long PresentationTime;
        public long Duration;
        public int Width;
        public int Height;
    }

    public GameRecorder() { MediaFactory.MFStartup(); }

    public void StartRecording(string filePath, int bitrate, int fps, string initiator = "User")
    {
        if (IsRecording || IsSaving) return;

        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            _targetFps = fps;
            _ticksPerFrame = 10_000_000 / fps;
            IsRecording = true;
            Initiator = initiator;

            _totalAudioBytes = 0;
            _audioCaptureStarted = false;
            _encodedFrameCount = 0;
            _recordingStopwatch.Reset();

            _preStartAudioBuffer = new ConcurrentQueue<byte[]>();
            Plugin.Log.Information($"Starting recording initiated by {initiator}...");

            _audioRecorder = new AudioRecorder();
            _cancellationTokenSource = new CancellationTokenSource();
            _frameQueue = new BlockingCollection<CapturedFrame>(boundedCapacity: _targetFps * 2);
            var viewportId = ImGui.GetMainViewport().ID;

            _audioRecorder.StartToCallback(OnAudioDataReceived);
            _encoderTask = Task.Run(() => EncoderLoop(_cancellationTokenSource.Token, _frameQueue, filePath, bitrate));

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
        if (!IsRecording) return;
        if (!_audioCaptureStarted) return;

        byte[] safeData = new byte[size];
        Marshal.Copy(data, safeData, 0, size);

        if (_sinkWriter == null || _audioStreamIndex == -1)
        {
            _preStartAudioBuffer.Enqueue(safeData);
            return;
        }

        while (_preStartAudioBuffer.TryDequeue(out var bufferedData))
            WriteAudioToSink(bufferedData);

        WriteAudioToSink(safeData);
    }

    private void WriteAudioToSink(byte[] data)
    {
        try
        {
            if (_audioBytesPerSecond == 0) return;

            long sampleTime = (_totalAudioBytes * 10_000_000) / _audioBytesPerSecond;
            long duration = (data.Length * 10_000_000) / _audioBytesPerSecond;

            var buffer = MediaFactory.MFCreateMemoryBuffer(data.Length);
            buffer.Lock(out IntPtr ptr, out _, out _);
            Marshal.Copy(data, 0, ptr, data.Length);
            buffer.Unlock();
            buffer.CurrentLength = data.Length;

            using var sample = MediaFactory.MFCreateSample();
            sample.AddBuffer(buffer);
            sample.SampleTime = sampleTime;
            sample.SampleDuration = duration;

            lock (_sinkWriter!)
            {
                _sinkWriter.WriteSample(_audioStreamIndex, sample);
            }

            _totalAudioBytes += data.Length;
            buffer.Dispose();
        }
        catch { }
    }

    public async Task StopRecording()
    {
        if (!IsRecording) return;
        IsRecording = false;
        IsSaving = true;

        _recordingStopwatch.Stop();

        _audioRecorder?.Stop();

        _frameQueue?.CompleteAdding();

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
            CaptureFrameAsync(0, _ticksPerFrame); 
            _encodedFrameCount = 1;
            return;
        }

        double elapsedSeconds = _recordingStopwatch.Elapsed.TotalSeconds;
        long expectedTotalFrames = (long)(elapsedSeconds * _targetFps);

        long framesToCapture = expectedTotalFrames - (_encodedFrameCount - 1);

        if (framesToCapture > 0)
        {
            long pts = _encodedFrameCount * _ticksPerFrame;
            long duration = framesToCapture * _ticksPerFrame;

            _encodedFrameCount += framesToCapture;
            CaptureFrameAsync(pts, duration);
        }
    }

    private async void CaptureFrameAsync(long pts, long duration)
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

            for (int y = 0; y < height; y++)
            {
                int srcRowStart = y * pitchInUints;
                int dstRowStart = (height - 1 - y) * strideInUints; 

                for (int x = 0; x < width; x++)
                {
                    uint pixel = srcSpan[srcRowStart + x];
                    dstSpan[dstRowStart + x] = (pixel & 0xFF00FF00) | ((pixel & 0x00FF0000) >> 16) | ((pixel & 0x000000FF) << 16);
                }
            }

            var frame = new CapturedFrame
            {
                Data = rawBuffer,
                PresentationTime = pts,
                Duration = duration,
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

    private void EncoderLoop(CancellationToken token, BlockingCollection<CapturedFrame> queue, string outputFileName, int bitrate)
    {
        _sinkWriter = null;
        int videoStreamIndex = -1;

        try
        {
            CapturedFrame firstFrame;
            if (!queue.TryTake(out firstFrame, 10000, token)) throw new Exception("Timed out waiting for first frame.");

            int width = firstFrame.Width;
            int height = firstFrame.Height;

            try { _sinkWriter = MediaFactory.MFCreateSinkWriterFromURL(outputFileName, null, null); }
            catch (Exception ex) { throw new Exception($"Failed to create file at {outputFileName}. Check permissions.", ex); }

            using var mediaTypeOut = MediaFactory.MFCreateMediaType();
            mediaTypeOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            mediaTypeOut.Set(MediaTypeAttributeKeys.Subtype, VideoFormatH264);
            mediaTypeOut.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitrate);
            mediaTypeOut.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            
            mediaTypeOut.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)width << 32) | (uint)height);
            mediaTypeOut.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)_targetFps << 32) | 1);
            mediaTypeOut.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1);
            
            videoStreamIndex = _sinkWriter.AddStream(mediaTypeOut);

            using var mediaTypeIn = MediaFactory.MFCreateMediaType();
            mediaTypeIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            mediaTypeIn.Set(MediaTypeAttributeKeys.Subtype, InputFormat);
            mediaTypeIn.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            
            mediaTypeIn.Set(MediaTypeAttributeKeys.FrameSize, ((ulong)width << 32) | (uint)height);
            mediaTypeIn.Set(MediaTypeAttributeKeys.FrameRate, ((ulong)_targetFps << 32) | 1);
            mediaTypeIn.Set(MediaTypeAttributeKeys.PixelAspectRatio, ((ulong)1 << 32) | 1);
            
            _sinkWriter.SetInputMediaType(videoStreamIndex, mediaTypeIn, null);

            try
            {
                int sr = 0, ch = 0, bps = 0;
                for (int i = 0; i < 50; i++)
                {
                    try { _audioRecorder!.GetFormat(out sr, out ch, out bps); if (sr != 0) break; }
                    catch { if (i == 49) throw; Thread.Sleep(100); }
                }

                uint blockAlignment = (uint)(ch * (bps / 8));
                uint bytesPerSecond = (uint)(sr * blockAlignment);
                
                _audioBytesPerSecond = (int)bytesPerSecond;

                using var audioTypeOut = MediaFactory.MFCreateMediaType();
                audioTypeOut.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                audioTypeOut.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
                audioTypeOut.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)ch);
                audioTypeOut.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)sr);
                audioTypeOut.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
                audioTypeOut.Set(MediaTypeAttributeKeys.AvgBitrate, 192000u);
                
                _audioStreamIndex = _sinkWriter.AddStream(audioTypeOut);

                using var audioTypeIn = MediaFactory.MFCreateMediaType();
                audioTypeIn.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
                
                audioTypeIn.Set(MediaTypeAttributeKeys.Subtype, bps == 32 ? AudioFormatGuids.Float : AudioFormatGuids.Pcm);
                
                audioTypeIn.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)ch);
                audioTypeIn.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)sr);
                audioTypeIn.Set(MediaTypeAttributeKeys.AudioBitsPerSample, (uint)bps);
                audioTypeIn.Set(MediaTypeAttributeKeys.AudioBlockAlignment, blockAlignment);
                audioTypeIn.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, bytesPerSecond);
                
                _sinkWriter.SetInputMediaType(_audioStreamIndex, audioTypeIn, null);
            }
            catch (Exception ex) 
            { 
                Plugin.Log.Error($"Audio setup failed: {ex.Message}"); 
                _audioStreamIndex = -1; 
            }

            _sinkWriter.BeginWriting();

            void WriteVideoFrame(CapturedFrame f)
            {
                var buffer = MediaFactory.MFCreateMemoryBuffer(f.Data.Length);
                buffer.Lock(out IntPtr ptr, out _, out _);
                Marshal.Copy(f.Data, 0, ptr, f.Data.Length);
                buffer.Unlock();
                buffer.CurrentLength = f.Data.Length;

                using var sample = MediaFactory.MFCreateSample();
                sample.AddBuffer(buffer);
                sample.SampleTime = f.PresentationTime;
                sample.SampleDuration = f.Duration;

                lock (_sinkWriter) { _sinkWriter.WriteSample(videoStreamIndex, sample); }
                buffer.Dispose();
                System.Buffers.ArrayPool<byte>.Shared.Return(f.Data);
            }

            WriteVideoFrame(firstFrame);

            foreach (var frame in queue.GetConsumingEnumerable(token))
            {
                WriteVideoFrame(frame);
            }

            _audioRecorder?.Stop();
            lock (_sinkWriter) { _sinkWriter.Finalize(); }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Encoding Error: {ex.Message}");
            OnRecordingError?.Invoke($"Encoding failed: {ex.Message}");
        }
        finally { _sinkWriter?.Dispose(); _sinkWriter = null; }
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
