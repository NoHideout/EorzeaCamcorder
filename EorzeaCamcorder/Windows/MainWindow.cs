using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder.Windows;

public class MainWindow : Window, IDisposable
{
    private GameRecorder Recorder => Service.Recorder;
    
    private string? _errorMessage;
    private DateTime _errorTime;

    public MainWindow() : base("EorzeaCamcorder", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 200),
            MaximumSize = new Vector2(600, 600)
        };

        Recorder.OnRecordingError += (msg) =>
        {
            _errorMessage = msg;
            _errorTime = DateTime.Now;
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawError();
        DrawStatus();
        DrawRecordingControls();
        DrawReplayControls();
        DrawFooter();
    }

    private void DrawError()
    {
        if (string.IsNullOrWhiteSpace(_errorMessage)) return;

        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DPSRed))
        {
            ImGui.TextWrapped($"Error: {_errorMessage}");
        }

        if ((DateTime.Now - _errorTime).TotalSeconds > 5)
        {
            if (ImGui.SmallButton("Dismiss")) _errorMessage = null;
        }

        ImGui.Separator();
    }

    private void DrawStatus()
    {
        string statusText;
        Vector4 color;

        if (Recorder.IsRecording && Recorder.IsReplayBufferRunning)
        {
            statusText = "● Recording + Replay Buffer";
            color = ImGuiColors.ParsedPink;
        }
        else if (Recorder.IsRecording)
        {
            statusText = "● Recording";
            color = ImGuiColors.HealerGreen;
        }
        else if (Recorder.IsReplayBufferRunning)
        {
            statusText = "● Replay Buffer Active";
            color = ImGuiColors.ParsedBlue;
        }
        else
        {
            statusText = "● Idle";
            color = ImGuiColors.DalamudGrey;
        }

        if (Recorder.IsSaving)
        {
            statusText += " (Saving...)";
            color = ImGuiColors.ParsedOrange;
        }
        else if (Recorder.IsWaiting)
        {
            statusText += " (Waiting for more footage...)";
            color = ImGuiColors.ParsedGold;
        }

        ImGui.TextColored(color, statusText);
        ImGui.Spacing();
    }

    private void DrawRecordingControls()
    {
        bool isRecording = Recorder.IsRecording;
        var btnColor = isRecording ? ImGuiColors.DPSRed : ImGuiColors.HealerGreen;

        using (ImRaii.PushColor(ImGuiCol.Button, btnColor))
        {
            if (ImGui.Button(isRecording ? "Stop Recording" : "Start Recording", new Vector2(-1, 40)))
            {
                if (isRecording)
                    Task.Run(async () => await Recorder.StopRecording());
                else
                    Recorder.StartRecording();
            }
        }

        ImGui.Spacing();
    }

    private void DrawReplayControls()
    {
        ImGui.Separator();
        ImGui.Text("Replay Buffer");

        bool isReplay = Recorder.IsReplayBufferRunning;
        if (ImGui.Checkbox("Enable Replay Buffer", ref isReplay))
        {
            if (isReplay) Recorder.StartReplayBuffer();
            else Task.Run(async () => await Recorder.StopReplayBuffer());
        }

        ImGui.Spacing();

        bool saving = Recorder.IsSaving;
        bool replayActive = Recorder.IsReplayBufferRunning;

        using (ImRaii.Disabled(!replayActive || saving))
        {
            using (ImRaii.PushColor(ImGuiCol.Button, ImGuiColors.ParsedBlue))
            {
                if (ImGui.Button(saving ? "Saving Replay..." : "Save Replay", new Vector2(-1, 35)))
                {
                    Recorder.SaveReplayBuffer();
                }
            }
        }

        ImGui.Spacing();
    }

    private void DrawFooter()
    {
        ImGui.Separator();
        ImGui.Spacing();

        float half = ImGui.GetContentRegionAvail().X / 2;

        if (ImGui.Button("Open Folder", new Vector2(half - 5, 30)))
        {
            try
            {
                Directory.CreateDirectory(Service.Config.OutputDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = Service.Config.OutputDirectory,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                _errorMessage = ex.Message;
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Settings", new Vector2(half, 30)))
        {
            Service.ConfigWindow.Toggle();
        }
    }
}
