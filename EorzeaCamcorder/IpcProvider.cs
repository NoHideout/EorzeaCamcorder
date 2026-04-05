using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder;

public class IpcProvider : IDisposable
{
    private readonly GameRecorder _recorder;
    private readonly Configuration _config;

    public const string ApiVersion = "EorzeaCamcorder.ApiVersion";
    public const string StartRecording = "EorzeaCamcorder.StartRecording";
    public const string StopRecording = "EorzeaCamcorder.StopRecording";
    public const string IsRecording = "EorzeaCamcorder.IsRecording";
    public const string SaveReplay = "EorzeaCamcorder.SaveReplay";
    public const string StartReplay = "EorzeaCamcorder.StartReplay";
    public const string StopReplay = "EorzeaCamcorder.StopReplay";
    public const string IsReplayBufferRunning = "EorzeaCamcorder.IsReplayRunning";

    private readonly ICallGateProvider<(int, int)> _apiVersion;
    private readonly ICallGateProvider<string, object> _startRecording;
    private readonly ICallGateProvider<object> _stopRecording;
    private readonly ICallGateProvider<bool> _isRecording;
    private readonly ICallGateProvider<string, object> _saveReplay;
    private readonly ICallGateProvider<string, object> _startReplay;
    private readonly ICallGateProvider<string> _stopReplay;
    private readonly ICallGateProvider<bool> _isReplayBufferRunning;

    public IpcProvider(IDalamudPluginInterface pi, GameRecorder recorder, Configuration config)
    {
        _recorder = recorder;
        _config = config;

        _apiVersion = pi.GetIpcProvider<(int, int)>(ApiVersion);
        _startRecording = pi.GetIpcProvider<string, object>(StartRecording);
        _stopRecording = pi.GetIpcProvider<object>(StopRecording);
        _isRecording = pi.GetIpcProvider<bool>(IsRecording);
        _saveReplay = pi.GetIpcProvider<string, object>(SaveReplay);
        _startReplay = pi.GetIpcProvider<string, object>(StartReplay);
        _stopReplay = pi.GetIpcProvider<string>(StopReplay);
        _isReplayBufferRunning = pi.GetIpcProvider<bool>(IsReplayBufferRunning);
    }

    private bool CanIpc() => _config.AllowIpc;

    private (int, int) HandleApiVersion() => (1, 2);

    private void HandleStartRecording(string customPath)
    {
        if (!CanIpc()) return;

        var context = _startRecording.GetContext();
        string initiator = context?.SourcePlugin?.Name ?? "Unknown Plugin";

        _recorder.StartRecording(customPath, initiator);
    }

    private void HandleStopRecording()
    {
        if (!CanIpc()) return;

        if (_recorder.IsRecording)
            _ = _recorder.StopRecording();
    }

    private void HandleSaveReplay(string customPath)
    {
        if (!CanIpc()) return;

        _recorder.SaveReplayBuffer(customPath);
    }

    private void HandleStartReplay(string _)
    {
        if (!CanIpc()) return;

        var context = _startReplay.GetContext();
        string initiator = context?.SourcePlugin?.Name ?? "Unknown Plugin";

        _recorder.StartReplayBuffer(initiator);
    }

    private void HandleStopReplay()
    {
        if (!CanIpc()) return;

        _ = _recorder.StopReplayBuffer();
    }

    private bool HandleIsRecording() => _recorder.IsRecording;

    private bool HandleIsReplayBufferRunning() => _recorder.IsReplayBufferRunning;

    public void Register()
    {
        _apiVersion.RegisterFunc(HandleApiVersion);
        _startRecording.RegisterAction(HandleStartRecording);
        _stopRecording.RegisterAction(HandleStopRecording);
        _isRecording.RegisterFunc(HandleIsRecording);
        _saveReplay.RegisterAction(HandleSaveReplay);
        _startReplay.RegisterAction(HandleStartReplay);
        _stopReplay.RegisterAction(HandleStopReplay);
        _isReplayBufferRunning.RegisterFunc(HandleIsReplayBufferRunning);
    }

    private void Unregister()
    {
        _apiVersion.UnregisterFunc();
        _startRecording.UnregisterAction();
        _stopRecording.UnregisterAction();
        _isRecording.UnregisterFunc();
        _saveReplay.UnregisterAction();
        _startReplay.UnregisterAction();
        _stopReplay.UnregisterAction();
        _isReplayBufferRunning.UnregisterFunc();
    }

    public void Dispose()
    {
        Unregister();
    }
}
