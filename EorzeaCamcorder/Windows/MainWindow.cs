using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;

namespace EorzeaCamcorder.Windows;

public class MainWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private string? _errorMessage;
    private DateTime _errorTime;

    public MainWindow(Plugin plugin) : base("EorzeaCamcorder", ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse)
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(260, 200),
            MaximumSize = new Vector2(600, 600)
        };
        this.plugin = plugin;

        this.plugin.Recorder.OnRecordingError += (msg) =>
        {
            _errorMessage = msg;
            _errorTime = DateTime.Now;
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        DrawHeader();
        DrawError();
        DrawStatus();
        DrawRecordingControls();
        DrawReplayControls();
        DrawFooter();
    }

    private void DrawHeader()
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.Text("\uf03d"); // cute camera icon
        ImGui.PopFont();

        ImGui.SameLine();
        ImGui.TextColored(ImGuiColors.ParsedGold, "Eorzea Camcorder");

        ImGui.Separator();
    }

    private void DrawError()
    {
        if (string.IsNullOrWhiteSpace(_errorMessage)) return;

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DPSRed);
        ImGui.TextWrapped($"Error: {_errorMessage}");
        ImGui.PopStyleColor();

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

        if (plugin.Recorder.IsRecording && plugin.Recorder.IsReplayBufferRunning)
        {
            statusText = "● Recording + Replay Buffer";
            color = ImGuiColors.ParsedPink;
        }
        else if (plugin.Recorder.IsRecording)
        {
            statusText = "● Recording";
            color = ImGuiColors.HealerGreen;
        }
        else if (plugin.Recorder.IsReplayBufferRunning)
        {
            statusText = "● Replay Buffer Active";
            color = ImGuiColors.ParsedBlue;
        }
        else if (plugin.Recorder.IsSaving)
        {
            statusText = "● Saving...";
            color = ImGuiColors.ParsedOrange;
        }
        else
        {
            statusText = "● Idle";
            color = ImGuiColors.DalamudGrey;
        }

        ImGui.TextColored(color, statusText);
        ImGui.Spacing();
    }

    private void DrawRecordingControls()
    {
        bool isRecording = plugin.Recorder.IsRecording;
        var btnColor = isRecording ? ImGuiColors.DPSRed : ImGuiColors.HealerGreen;

        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        if (ImGui.Button(isRecording ? "Stop Recording" : "Start Recording", new Vector2(-1, 40)))
        {
            if (isRecording)
                Task.Run(async () => await plugin.Recorder.StopRecording());
            else
                plugin.Recorder.StartRecording();
        }
        ImGui.PopStyleColor();

        ImGui.Spacing();
    }

    private void DrawReplayControls()
    {
        ImGui.Separator();
        ImGui.Text("Replay Buffer");

        bool isReplay = plugin.Recorder.IsReplayBufferRunning;
        if (ImGui.Checkbox("Enable Replay Buffer", ref isReplay))
        {
            if (isReplay) plugin.Recorder.StartReplayBuffer();
            else Task.Run(async () => await plugin.Recorder.StopReplayBuffer());
        }

        ImGui.Spacing();

        bool saving = plugin.Recorder.IsSaving;
        bool replayActive = plugin.Recorder.IsReplayBufferRunning;

        if (!replayActive || saving)
            ImGui.BeginDisabled();

        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.ParsedBlue);
        if (ImGui.Button(saving ? "Saving Replay..." : "Save Replay", new Vector2(-1, 35)))
        {
            plugin.Recorder.SaveReplayBuffer();
        }
        ImGui.PopStyleColor();

        if (!replayActive || saving) ImGui.EndDisabled();

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
                Directory.CreateDirectory(plugin.Configuration.OutputDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = plugin.Configuration.OutputDirectory,
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
            plugin.ToggleConfigUi();
        }
    }
}
