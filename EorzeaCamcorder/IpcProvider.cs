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

    private readonly ICallGateProvider<string, object> _startRecordingProvider;
    private readonly ICallGateProvider<object> _stopRecordingProvider;
    private readonly ICallGateProvider<bool> _isRecordingProvider;

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

    public void Dispose()
    {
        _startRecordingProvider.UnregisterAction();
        _stopRecordingProvider.UnregisterAction();
        _isRecordingProvider.UnregisterFunc();
    }
}
