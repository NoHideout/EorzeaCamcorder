using System;
using Dalamud.Plugin.Services;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder;

public class CommandHandler
{
    private IChatGui Chat => Service.Chat;
    private GameRecorder Recorder => Service.Recorder;
    
    public void Handle(string args)
    {
        var split = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var group = split.Length > 0 ? split[0].ToLower() : "toggle";
        var sub = split.Length > 1 ? split[1].ToLower() : string.Empty;

        switch (group)
        {
            case "toggle":
                Service.MainWindow.Toggle();
                break;
            case "help":
            case "?":
                Chat.Print(
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
                Service.FFmpegSetupWindow.IsOpen = true;
                return;

            case "config":
                Service.ConfigWindow.Toggle();
                return;

            case "recording":
                HandleRecording(sub);
                return;

            case "buffer":
            case "replay":
                HandleBuffer(sub);
                return;

            default:
                Chat.PrintError($"Unknown command group: {group}");
                return;
        }
    }

    private void HandleRecording(string sub)
    {
        switch (sub)
        {
            case "start":
                Recorder.StartRecording(null, "Command");
                break;

            case "stop":
                _ = Recorder.StopRecording();
                break;
            default:
                Chat.Print("Usage: /ecam recording [start|stop]");
                break;
        }
    }

    private void HandleBuffer(string sub)
    {
        switch (sub)
        {
            case "save":
                Recorder.SaveReplayBuffer();
                break;
            case "start":
                Recorder.StartReplayBuffer("Command");
                break;
            case "stop":
                _ = Recorder.StopReplayBuffer();
                break;

            default:
                Chat.Print("Usage: /ecam buffer [save|start|stop]");
                break;
        }
    }
}
