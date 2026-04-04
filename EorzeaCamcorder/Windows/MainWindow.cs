using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
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
            MinimumSize = new Vector2(150, 140)
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
        if (!string.IsNullOrWhiteSpace(_errorMessage)) 
        {
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DPSRed);
            ImGui.TextWrapped($"ERROR: {_errorMessage}");
            ImGui.PopStyleColor();

            if ((DateTime.Now - _errorTime).TotalSeconds > 10)
            {
                if (ImGui.Button("Dismiss Error")) _errorMessage = null;
            }
            ImGui.Separator();
        }

        if (plugin.Recorder.IsRecording)
        {
            ImGui.TextColored(ImGuiColors.ParsedGreen, "● RECORDING");
        }
        else if (plugin.Recorder.IsSaving)
        {
            ImGui.TextColored(ImGuiColors.ParsedOrange, "● SAVING / ENCODING...");
        }
        else
        {
            ImGui.TextColored(ImGuiColors.DalamudGrey, "● IDLE");
        }

        ImGui.Spacing();

        if (plugin.Recorder.IsSaving) ImGui.BeginDisabled();

        var btnText = plugin.Recorder.IsRecording ? "STOP RECORDING" : "START RECORDING";
        var btnColor = plugin.Recorder.IsRecording ? ImGuiColors.DPSRed : ImGuiColors.HealerGreen;

        ImGui.PushStyleColor(ImGuiCol.Button, btnColor);
        if (ImGui.Button(btnText, new Vector2(ImGui.GetContentRegionAvail().X, 40)))
        {
            if (plugin.Recorder.IsRecording)
            {
                Task.Run(async () => await plugin.Recorder.StopRecording());
            }
            else
            {
                _errorMessage = null;
                plugin.Recorder.StartRecording(); 
            }
        }
        ImGui.PopStyleColor();

        if (plugin.Recorder.IsSaving) ImGui.EndDisabled();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.Button("Open Folder", new Vector2(ImGui.GetContentRegionAvail().X / 2 - 5, 0)))
        {
            try
            {
                Directory.CreateDirectory(plugin.Configuration.OutputDirectory);
                Process.Start(new ProcessStartInfo
                {
                    FileName = plugin.Configuration.OutputDirectory,
                    UseShellExecute = true,
                    Verb = "open"
                });
            }
            catch (Exception ex)
            {
                _errorMessage = $"Could not open folder: {ex.Message}";
            }
        }

        ImGui.SameLine();

        if (ImGui.Button("Settings", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
        {
            plugin.ToggleConfigUi();
        }
    }
}
