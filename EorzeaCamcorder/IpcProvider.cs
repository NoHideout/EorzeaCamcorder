using System;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder;

public class IpcProvider : IDisposable
{
    public const string StartRecordingKey = "EorzeaCamcorder.StartRecording";
    public const string StopRecordingKey = "EorzeaCamcorder.StopRecording";
    public const string IsRecordingKey = "EorzeaCamcorder.IsRecording";
    public const string SaveReplayKey = "EorzeaCamcorder.SaveReplay";
    public const string IsReplayBufferRunningKey = "EorzeaCamcorder.IsReplayBufferRunning";

    private readonly ICallGateProvider<string, object> _startRecordingProvider;
    private readonly ICallGateProvider<object> _stopRecordingProvider;
    private readonly ICallGateProvider<bool> _isRecordingProvider;
    private readonly ICallGateProvider<string, object> _saveReplayProvider;
    private readonly ICallGateProvider<bool> _isReplayBufferRunningProvider;
    
    private readonly GameRecorder _recorder;
    
    public IpcProvider(IDalamudPluginInterface pluginInterface, GameRecorder recorder)
    {
        _recorder = recorder;

        _startRecordingProvider = pluginInterface.GetIpcProvider<string, object>(StartRecordingKey);
        _startRecordingProvider.RegisterAction(StartRecording);

        _stopRecordingProvider = pluginInterface.GetIpcProvider<object>(StopRecordingKey);
        _stopRecordingProvider.RegisterAction(StopRecording);

        _isRecordingProvider = pluginInterface.GetIpcProvider<bool>(IsRecordingKey);
        _isRecordingProvider.RegisterFunc(IsRecording);
        
        _saveReplayProvider = pluginInterface.GetIpcProvider<string, object>(SaveReplayKey);
        _saveReplayProvider.RegisterAction(SaveReplay);
        
        _isReplayBufferRunningProvider = pluginInterface.GetIpcProvider<bool>(IsReplayBufferRunningKey);
        _isReplayBufferRunningProvider.RegisterFunc(IsReplayBufferRunning);
    }

    private void StartRecording(string customPath)
    {
        var context = _startRecordingProvider.GetContext();
        string initiator = context?.SourcePlugin?.Name ?? "Unknown Plugin";

        _recorder.StartRecording(customPath, initiator);
    }

    private void StopRecording()
    {
        if (_recorder.IsRecording) _ = _recorder.StopRecording();
    }

    private bool IsRecording() => _recorder.IsRecording;
    
    private void SaveReplay(string customPath) => _recorder.SaveReplayBuffer(customPath);
    private bool IsReplayBufferRunning() => _recorder.IsReplayBufferRunning;
    
    public void Dispose()
    {
        _startRecordingProvider.UnregisterAction();
        _stopRecordingProvider.UnregisterAction();
        _isRecordingProvider.UnregisterFunc();
        _saveReplayProvider.UnregisterAction();
        _isReplayBufferRunningProvider.UnregisterFunc();
    }
}
