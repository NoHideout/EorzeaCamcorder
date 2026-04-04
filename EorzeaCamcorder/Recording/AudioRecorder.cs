using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using TerraFX.Interop.Windows;
using static TerraFX.Interop.Windows.Windows;

namespace EorzeaCamcorder.Recording;

public delegate void AudioCaptureCallback(byte[] data, int size);

public unsafe class AudioRecorder : IDisposable
{
    private IAudioClient* _audioClient;
    private IAudioCaptureClient* _captureClient;

    private Thread? _captureThread;
    private CancellationTokenSource? _cts;
    private AudioCaptureCallback? _callback;

    private volatile bool _isRecording;
    private bool _disposed;
    private bool _comInitialized;

    private int _sampleRate, _channels, _bitsPerSample;

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    private struct PropVariantBlob
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public uint cbSize;
        [FieldOffset(16)] public byte* pBlobData;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ActivationParams
    {
        public uint ActivationType;
        public uint TargetProcessId;
        public uint ProcessLoopbackMode;
    }

    [ComImport, Guid("41D949AB-9862-444A-80F6-C261334DA5EB"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivateAudioInterfaceCompletionHandlerNet
    {
        [PreserveSig] int ActivateCompleted(IntPtr operation);
    }

    private sealed class ActivationHandler : IActivateAudioInterfaceCompletionHandlerNet
    {
        private readonly Action<IntPtr> _onComplete;
        public ActivationHandler(Action<IntPtr> onComplete) => _onComplete = onComplete;
        public int ActivateCompleted(IntPtr operation) { _onComplete(operation); return 0; }
    }

    public AudioRecorder()
    {
        if (CoInitializeEx(null, (uint)COINIT.COINIT_MULTITHREADED) >= 0)
            _comInitialized = true;
    }

    public void StartToCallback(AudioCaptureCallback callback)
    {
        if (_disposed || _isRecording || _audioClient != null)
            throw new InvalidOperationException("Already recording or disposed.");

        _callback = callback;

        Exception? activationEx = null;
        using var activationEvent = new ManualResetEventSlim(false);

        var handler = new ActivationHandler(opPtr =>
        {
            IActivateAudioInterfaceAsyncOperation* asyncOp = null;

            try
            {
                asyncOp = (IActivateAudioInterfaceAsyncOperation*)opPtr;

                HRESULT hr;
                IUnknown* unknown = null;

                asyncOp->GetActivateResult(&hr, &unknown);
                if (hr < 0 || unknown == null)
                    throw new COMException("Activation failed", hr);

                IAudioClient* client = null;
                Guid iid = typeof(IAudioClient).GUID;

                if (unknown->QueryInterface(&iid, (void**)&client) < 0)
                    throw new COMException("QueryInterface for IAudioClient failed");

                unknown->Release();
                _audioClient = client;

                WAVEFORMATEX* format = null;

                if (_audioClient->GetMixFormat(&format) < 0 || format == null)
                {
                    format = (WAVEFORMATEX*)Marshal.AllocCoTaskMem(sizeof(WAVEFORMATEX));
                    *format = new WAVEFORMATEX
                    {
                        wFormatTag = 1,
                        nChannels = 2,
                        nSamplesPerSec = 48000,
                        wBitsPerSample = 16,
                        nBlockAlign = 4,
                        nAvgBytesPerSec = 192000,
                        cbSize = 0
                    };
                }

                _sampleRate = (int)format->nSamplesPerSec;
                _channels = format->nChannels;
                _bitsPerSample = format->wBitsPerSample;

                hr = _audioClient->Initialize(
                    AUDCLNT_SHAREMODE.AUDCLNT_SHAREMODE_SHARED,
                    0x00020000,
                    2_000_000,
                    0,
                    format,
                    null
                );

                Marshal.FreeCoTaskMem((IntPtr)format);

                if (hr < 0)
                    throw new COMException("IAudioClient::Initialize failed", hr);

                IAudioCaptureClient* capture = null;
                iid = typeof(IAudioCaptureClient).GUID;

                if (_audioClient->GetService(&iid, (void**)&capture) < 0)
                    throw new COMException("GetService(IAudioCaptureClient) failed");

                _captureClient = capture;
            }
            catch (Exception ex)
            {
                activationEx = ex;
            }
            finally
            {
                if (asyncOp != null)
                    asyncOp->Release();

                activationEvent.Set();
            }
        });

        IntPtr handlerPtr = Marshal.GetComInterfaceForObject(handler, typeof(IActivateAudioInterfaceCompletionHandlerNet));

        try
        {
            var activationParams = new ActivationParams
            {
                ActivationType = 1,
                TargetProcessId = (uint)Process.GetCurrentProcess().Id,
                ProcessLoopbackMode = 0
            };

            var prop = new PropVariantBlob
            {
                vt = 65,
                cbSize = (uint)sizeof(ActivationParams),
                pBlobData = (byte*)&activationParams
            };

            IActivateAudioInterfaceAsyncOperation* asyncOp = null;
            Guid iid = typeof(IAudioClient).GUID;

            fixed (char* deviceId = @"VAD\Process_Loopback")
            {
                int hr = ActivateAudioInterfaceAsync(
                    deviceId,
                    &iid,
                    (PROPVARIANT*)&prop,
                    (IActivateAudioInterfaceCompletionHandler*)handlerPtr,
                    &asyncOp
                );

                if (hr < 0)
                    throw new COMException("ActivateAudioInterfaceAsync failed", hr);
            }

            if (!activationEvent.Wait(5000))
                throw new TimeoutException("Audio activation timed out.");

            if (activationEx != null)
                throw activationEx;
        }
        finally
        {
            Marshal.Release(handlerPtr);
        }

        _cts = new CancellationTokenSource();
        _isRecording = true;

        _captureThread = new Thread(CaptureLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal
        };

        _captureThread.Start();
    }

    private void CaptureLoop()
    {
        if (_audioClient->Start() < 0)
            return;

        int bytesPerFrame = (_channels * _bitsPerSample) / 8;
        var token = _cts!.Token;

        while (!token.IsCancellationRequested)
        {
            uint packetSize;

            if (_captureClient->GetNextPacketSize(&packetSize) < 0)
                break;

            while (packetSize > 0)
            {
                byte* data;
                uint frames, flags;
                ulong devPos, qpc;

                if (_captureClient->GetBuffer(&data, &frames, &flags, &devPos, &qpc) < 0)
                    break;

                if (frames > 0 && _callback != null)
                {
                    int size = (int)frames * bytesPerFrame;

                    byte[] managed = new byte[size];
                    Marshal.Copy((IntPtr)data, managed, 0, size);

                    _callback(managed, size);
                }

                _captureClient->ReleaseBuffer(frames);
                _captureClient->GetNextPacketSize(&packetSize);
            }

            Thread.Sleep(5);
        }

        _audioClient->Stop();
    }

    public void Stop()
    {
        if (!_isRecording)
            return;

        _isRecording = false;

        _cts?.Cancel();
        _captureThread?.Join(1000);

        _cts?.Dispose();
        _cts = null;
        _callback = null;
    }

    public void GetFormat(out int sampleRate, out int channels, out int bitsPerSample)
    {
        if (_sampleRate == 0)
            throw new InvalidOperationException("Format not initialized.");

        sampleRate = _sampleRate;
        channels = _channels;
        bitsPerSample = _bitsPerSample;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Stop();

        if (_captureClient != null)
        {
            _captureClient->Release();
            _captureClient = null;
        }

        if (_audioClient != null)
        {
            _audioClient->Release();
            _audioClient = null;
        }

        if (_comInitialized)
            CoUninitialize();

        _disposed = true;
        GC.SuppressFinalize(this);
    }
}
