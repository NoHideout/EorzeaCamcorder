using System;
using Dalamud.Plugin.Services;
using EorzeaCamcorder.Recording;
using EorzeaCamcorder.Windows;

namespace EorzeaCamcorder;

public class CommandHandler
{
    private readonly GameRecorder _recorder;
    private readonly ConfigWindow _configWindow;
    private readonly MainWindow _mainWindow;
    private readonly FFmpegSetupWindow _setupWindow;
    private readonly IPluginLog _log;
    private readonly IChatGui _chat;

    public CommandHandler(
        GameRecorder recorder,
        ConfigWindow configWindow,
        MainWindow mainWindow,
        FFmpegSetupWindow setupWindow,
        IPluginLog log,
        IChatGui chat)
    {
        _recorder = recorder;
        _configWindow = configWindow;
        _mainWindow = mainWindow;
        _setupWindow = setupWindow;
        _log = log;
        _chat = chat;
    }

    public void Handle(string args)
    {
        var split = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (split.Length == 0)
        {
            _mainWindow.Toggle();
            return;
        }

        var group = split[0].ToLower();
        var sub = split.Length > 1 ? split[1].ToLower() : string.Empty;

        switch (group)
        {
            case "help":
            case "?":
                _chat.Print(
                "/ecam → Open main window\n" +
                        "/ecam config → Open configuration\n\n" +

                        "Recording:\n" +
                        "/ecam recording start → Start recording\n" +
                        "/ecam recording stop → Stop recording\n\n" +

                        "Replay Buffer:\n" +
                        "/ecam buffer start → Start replay buffer\n" +
                        "/ecam buffer stop → Stop replay buffer\n" +
                        "/ecam buffer save → Save replay buffer\n"
                );
                break;
            case "setup":
                _setupWindow.IsOpen = true;
                return;

            case "config":
                _configWindow.Toggle();
                return;

            case "recording":
                HandleRecording(sub);
                return;

            case "buffer":
            case "replay":
                HandleBuffer(sub);
                return;

            default:
                _chat.PrintError($"Unknown command group: {group}");
                return;
        }
    }

    private void HandleRecording(string sub)
    {
        switch (sub)
        {
            case "start":
                _recorder.StartRecording(null, "Command");
                break;

            case "stop":
                _ = _recorder.StopRecording();
                break;
            default:
                _chat.Print("Usage: /ecam recording [start|stop]");
                break;
        }
    }

    private void HandleBuffer(string sub)
    {
        switch (sub)
        {
            case "save":
                _recorder.SaveReplayBuffer();
                break;
            case "start":
                _recorder.StartReplayBuffer("Command");
                break;
            case "stop":
                _recorder.StopReplayBuffer();
                break;

            default:
                _chat.Print("Usage: /ecam buffer [save|start|stop]");
                break;
        }
    }
}
