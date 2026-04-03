using System;
using System.Diagnostics;
using Dalamud.Plugin;
using Dalamud.Plugin.Ipc;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder;

public class IpcProvider : IDisposable
{
    public const string StartRecordingKey = "EorzeaCamcorder.StartRecording";
    public const string StopRecordingKey = "EorzeaCamcorder.StopRecording";
    public const string IsRecordingKey = "EorzeaCamcorder.IsRecording";

    private readonly ICallGateProvider<string, int, int, object> _startRecordingProvider;
    private readonly ICallGateProvider<object> _stopRecordingProvider;
    private readonly ICallGateProvider<bool> _isRecordingProvider;

    private readonly GameRecorder _recorder;

    public IpcProvider(IDalamudPluginInterface pluginInterface, GameRecorder recorder)
    {
        _recorder = recorder;

        //string path, int format, int bitrate, int fps
        _startRecordingProvider = pluginInterface.GetIpcProvider<string, int, int, object>(StartRecordingKey);
        _startRecordingProvider.RegisterAction(StartRecording);

        _stopRecordingProvider = pluginInterface.GetIpcProvider<object>(StopRecordingKey);
        _stopRecordingProvider.RegisterAction(StopRecording);

        _isRecordingProvider = pluginInterface.GetIpcProvider<bool>(IsRecordingKey);
        _isRecordingProvider.RegisterFunc(IsRecording);
    }

    // Old way, should finally implement new one
    private void StartRecording(string path, int bitrate, int fps)
    {
        string initiator = "Unknown Plugin";
        try
        {
            var stackTrace = new StackTrace();
            foreach (var frame in stackTrace.GetFrames())
            {
                var method = frame.GetMethod();
                var assembly = method?.DeclaringType?.Assembly;

                if (assembly == null) continue;

                var name = assembly.GetName().Name;

                if (!string.IsNullOrEmpty(name) &&
                    !name.StartsWith("System") &&
                    !name.StartsWith("Microsoft") &&
                    !name.StartsWith("Dalamud") &&
                    name != "SamplePlugin" && // Adjust name later CBA
                    name != "EorzeaCamcorder")
                {
                    initiator = name;
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning($"Error determining IPC initiator: {ex.Message}");
        }

        _recorder.StartRecording(path, bitrate, fps, initiator);
    }

    private void StopRecording()
    {
        if (_recorder.IsRecording)
        {
            _ = _recorder.StopRecording();
        }
    }

    private bool IsRecording()
    {
        return _recorder.IsRecording;
    }

    public void Dispose()
    {
        _startRecordingProvider.UnregisterAction();
        _stopRecordingProvider.UnregisterAction();
        _isRecordingProvider.UnregisterFunc();
    }
}
