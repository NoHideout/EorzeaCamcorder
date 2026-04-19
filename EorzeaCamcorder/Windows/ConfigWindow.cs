using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using EorzeaCamcorder.Recording;

namespace EorzeaCamcorder.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration config = Service.Config;
    
    public ConfigWindow() : base("EorzeaCamcorder Settings")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 350)
        };
    }

    public void Dispose() { }

    public override void Draw()
    {
        ImGui.TextDisabled("Settings are automatically saved when you close this window.");
        ImGui.Separator();
        ImGui.Spacing();
        if (ImGui.CollapsingHeader("General Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            bool allowIpc = config.AllowIpc;
            if (ImGui.Checkbox("Enable IPC.", ref allowIpc))
            {
                config.AllowIpc = allowIpc;
            }
            DrawTooltipIfHovered("This setting allows other plugins to start, stop and save recordings/replays.");
            
            string outDir = config.OutputDirectory;
            if (ImGui.InputText("Output Directory", ref outDir, 512))
            {
                config.OutputDirectory = outDir;
            }

            string[] formats = { "mp4", "mkv" };
            int currentFormatIdx = Array.IndexOf(formats, config.OutputFormat);
            if (currentFormatIdx == -1) currentFormatIdx = 0; // fallback to mp4

            if (ImGui.Combo("Container Format", ref currentFormatIdx, formats, formats.Length))
            {
                config.OutputFormat = formats[currentFormatIdx];
            }
            DrawTooltipIfHovered("MKV is highly recommended! If the game crashes during recording, an MKV file will still be playable. MP4 files will corrupt.");
            
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Video & Encoding", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int resHeight = config.ResolutionHeight;
            string[] resOptions = { "Source (No Scaling)", "720p", "1080p", "1440p", "2160p (4K)" };
            int[] resValues = { 0, 720, 1080, 1440, 2160 };
            
            int currentResIndex = Array.IndexOf(resValues, resHeight);
            if (currentResIndex == -1) currentResIndex = 0;

            if (ImGui.Combo("Output Resolution", ref currentResIndex, resOptions, resOptions.Length))
            {
                config.ResolutionHeight = resValues[currentResIndex];
            }
            DrawTooltipIfHovered("Scaling preserves aspect ratio. 'Source' records exactly what you see.");

            int fps = config.TargetFps;
            if (ImGui.SliderInt("Target FPS", ref fps, 15, 120))
            {
                config.TargetFps = fps;
            }

            int bitrate = config.VideoBitrateKbps;
            if (ImGui.SliderInt("Bitrate (kbps)", ref bitrate, 1000, 20000))
            {
                config.VideoBitrateKbps = bitrate;
            }

            string[] encoders = EncoderReg.ProfileNames;
            int currentEncoderIdx = Array.FindIndex(EncoderReg.Profiles, p => p.Type == config.SelectedVideoEncoder);
            if (currentEncoderIdx == -1) currentEncoderIdx = 0;
            
            if (ImGui.Combo("Video Encoder", ref currentEncoderIdx, encoders, encoders.Length))
            {
                config.SelectedVideoEncoder = EncoderReg.Profiles[currentEncoderIdx].Type;
            }
            DrawTooltipIfHovered("Hardware encoding offloads work to your GPU, saving CPU performance.\nIf recordings fail to start, your GPU may not support the selected encoder.");
            
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Replay Buffer", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int replaySec = config.ReplayBufferSeconds;
            if (ImGui.SliderInt("Buffer Length (sec)", ref replaySec, 5, 120))
            {
                config.ReplayBufferSeconds = replaySec;
            }
            DrawTooltipIfHovered("How many seconds of gameplay the Buffer mode will keep in RAM.");
            
            string[] posNames = { "At the End", "In the Middle", "At the Start" };
            int currentPos = (int)config.ReplayEventPosition;
            if (ImGui.Combo("Trigger Event Position", ref currentPos, posNames, posNames.Length))
            {
                config.ReplayEventPosition = (ReplayEventPosition)currentPos;
            }
            DrawTooltipIfHovered("Where the event that triggered the save should appear in the clip.");
            
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Advanced"))
        {
            ImGui.Spacing();
            if (ImGui.Button("Scan & Recover Crashed Recordings", new Vector2(ImGui.GetContentRegionAvail().X, 30)))
            {
                Service.Recorder.RecoverOrphanedFiles();
            }
            DrawTooltipIfHovered("This will scan your output directory and attempt to remux any orphaned .ts files.");
            
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Experimental"))
        {
            if (ImGui.Button("Open Trigger Configuration", new Vector2(ImGui.GetContentRegionAvail().X, 30)))
            {
                Service.TriggerWindow.Toggle();
            }
        }
    }

    public override void OnClose()
    {
        base.OnClose();
        config.Save();
    }

    private void DrawTooltipIfHovered(string description)
    {
        if (!ImGui.IsItemHovered()) return;

        using var tooltip = ImRaii.Tooltip();
        if (tooltip)
        {
            ImGui.TextUnformatted(description);
        }
    }
}
