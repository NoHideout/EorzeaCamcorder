using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder;
//Todo Start/Stop buffer
public class IpcProvider : IDisposable
{
    private readonly GameRecorder _recorder;
    private readonly Configuration _config;
    
    public const string StartRecording = "EorzeaCamcorder.StartRecording";
    public const string StopRecording = "EorzeaCamcorder.StopRecording";
    public const string IsRecording = "EorzeaCamcorder.IsRecording";
    public const string SaveReplay = "EorzeaCamcorder.SaveReplay";
    public const string IsReplayBufferRunning = "EorzeaCamcorder.IsReplayBufferRunning";


    private readonly ICallGateProvider<string, object> _startRecording;
    private readonly ICallGateProvider<object> _stopRecording;
    private readonly ICallGateProvider<bool> _isRecording;
    private readonly ICallGateProvider<string, object> _saveReplay;
    private readonly ICallGateProvider<bool> _isReplayBufferRunning;

    public IpcProvider(IDalamudPluginInterface pi, GameRecorder recorder, Configuration config)
    {
        _recorder = recorder;
        _config = config;

        _startRecording = pi.GetIpcProvider<string, object>(StartRecording);
        _stopRecording = pi.GetIpcProvider<object>(StopRecording);
        _isRecording = pi.GetIpcProvider<bool>(IsRecording);
        _saveReplay = pi.GetIpcProvider<string, object>(SaveReplay);
        _isReplayBufferRunning = pi.GetIpcProvider<bool>(IsReplayBufferRunning);
    }
    
    private void HandleStartRecording(string customPath)
    {
        if (!_config.AllowIpc) return;
        
        var context = _startRecording.GetContext();
        string initiator = context?.SourcePlugin?.Name ?? "Unknown Plugin";

        _recorder.StartRecording(customPath, initiator);
    }

    private void HandleStopRecording()
    {
        if (!_config.AllowIpc) return;
        
        if (_recorder.IsRecording)
            _ = _recorder.StopRecording();
    }


    private void HandleSaveReplay(string customPath)
    {
        if (!_config.AllowIpc) return;
        _recorder.SaveReplayBuffer(customPath);
    }

    //probably fine not being restricted
    private bool HandleIsRecording() => _recorder.IsRecording;
    private bool HandleIsReplayBufferRunning()
        => _recorder.IsReplayBufferRunning;

    
    public void Register()
    {
        _startRecording.RegisterAction(HandleStartRecording);
        _stopRecording.RegisterAction(HandleStopRecording);
        _isRecording.RegisterFunc(HandleIsRecording);
        _saveReplay.RegisterAction(HandleSaveReplay);
        _isReplayBufferRunning.RegisterFunc(HandleIsReplayBufferRunning);
    }

    private void Unregister()
    {
        _startRecording.UnregisterAction();
        _stopRecording.UnregisterAction();
        _isRecording.UnregisterFunc();
        _saveReplay.UnregisterAction();
        _isReplayBufferRunning.UnregisterFunc();
    }

    public void Dispose()
    {
        Unregister();
    }
}
