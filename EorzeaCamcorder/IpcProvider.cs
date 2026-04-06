using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder;

public class IpcProvider : IDisposable
{
    private IDalamudPluginInterface Pi => Service.PluginInterface;
    private GameRecorder Recorder => Service.Recorder;
    
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
    private readonly ICallGateProvider<string?, int?, object> _saveReplay;
    private readonly ICallGateProvider<string, object> _startReplay;
    private readonly ICallGateProvider<string> _stopReplay;
    private readonly ICallGateProvider<bool> _isReplayBufferRunning;

    public IpcProvider()
    {
        _apiVersion = Pi.GetIpcProvider<(int, int)>(ApiVersion);
        _startRecording = Pi.GetIpcProvider<string, object>(StartRecording);
        _stopRecording = Pi.GetIpcProvider<object>(StopRecording);
        _isRecording = Pi.GetIpcProvider<bool>(IsRecording);
        _saveReplay = Pi.GetIpcProvider<string?, int?, object>(SaveReplay);
        _startReplay = Pi.GetIpcProvider<string, object>(StartReplay);
        _stopReplay = Pi.GetIpcProvider<string>(StopReplay);
        _isReplayBufferRunning = Pi.GetIpcProvider<bool>(IsReplayBufferRunning);
    }

    private bool CanIpc() => Service.Config.AllowIpc;

    private (int, int) HandleApiVersion() => (1, 2);

    private void HandleStartRecording(string customPath)
    {
        if (!CanIpc()) return;

        var context = _startRecording.GetContext();
        string initiator = context?.SourcePlugin?.Name ?? "Unknown Plugin";

        Recorder.StartRecording(customPath, initiator);
    }

    private void HandleStopRecording()
    {
        if (!CanIpc()) return;

        if (Recorder.IsRecording)
            _ = Recorder.StopRecording();
    }

    private void HandleSaveReplay(string? customPath, int? eventPositionOverride)
    {
        if (!CanIpc()) return;
        ReplayEventPosition? posOverride = eventPositionOverride.HasValue ? (ReplayEventPosition)eventPositionOverride.Value : null;
        Recorder.SaveReplayBuffer(customPath, posOverride);
    }

    private void HandleStartReplay(string _)
    {
        if (!CanIpc()) return;

        var context = _startReplay.GetContext();
        string initiator = context?.SourcePlugin?.Name ?? "Unknown Plugin";

        Recorder.StartReplayBuffer(initiator);
    }

    private void HandleStopReplay()
    {
        if (!CanIpc()) return;

        _ = Recorder.StopReplayBuffer();
    }

    private bool HandleIsRecording() => Recorder.IsRecording;

    private bool HandleIsReplayBufferRunning() => Recorder.IsReplayBufferRunning;

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
