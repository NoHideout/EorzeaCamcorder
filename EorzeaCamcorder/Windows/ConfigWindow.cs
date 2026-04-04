using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

namespace EorzeaCamcorder.Windows;

public class ConfigWindow : Window, IDisposable
{
    private Configuration Configuration;

    public ConfigWindow(Plugin plugin) : base("EorzeaCamcorder Configuration")
    {
        SizeConstraints = new WindowSizeConstraints
        {
            MinimumSize = new Vector2(450, 350)
        };
        Configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        bool save = false;

        if (ImGui.CollapsingHeader("General Settings", ImGuiTreeNodeFlags.DefaultOpen))
        {
            string outDir = Configuration.OutputDirectory;
            if (ImGui.InputText("Output Directory", ref outDir, 512))
            {
                Configuration.OutputDirectory = outDir;
                save = true;
            }

            string[] formats = { "mp4", "mkv" };
            int currentFormatIdx = Array.IndexOf(formats, Configuration.OutputFormat);
            if (currentFormatIdx == -1) currentFormatIdx = 0; // fallback to mp4

            if (ImGui.Combo("Container Format", ref currentFormatIdx, formats, formats.Length))
            {
                Configuration.OutputFormat = formats[currentFormatIdx];
                save = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("MKV is highly recommended! If the game crashes during recording, an MKV file will still be playable. MP4 files will corrupt.");
            }
            
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Video & Encoding", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int resHeight = Configuration.ResolutionHeight;
            string[] resOptions = { "Source (No Scaling)", "720p", "1080p", "1440p", "2160p (4K)" };
            int[] resValues = { 0, 720, 1080, 1440, 2160 };
            
            int currentResIndex = Array.IndexOf(resValues, resHeight);
            if (currentResIndex == -1) currentResIndex = 0;

            if (ImGui.Combo("Output Resolution", ref currentResIndex, resOptions, resOptions.Length))
            {
                Configuration.ResolutionHeight = resValues[currentResIndex];
                save = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Scaling preserves aspect ratio. 'Source' records exactly what you see.");
            }

            int fps = Configuration.TargetFps;
            if (ImGui.SliderInt("Target FPS", ref fps, 15, 120))
            {
                Configuration.TargetFps = fps;
                save = true;
            }

            int bitrate = Configuration.VideoBitrateKbps;
            if (ImGui.SliderInt("Bitrate (kbps)", ref bitrate, 1000, 20000))
            {
                Configuration.VideoBitrateKbps = bitrate;
                save = true;
            }

            string[] encoders = { "Software (x264)", "NVIDIA (NVENC)", "AMD (AMF)", "Intel (QSV)" };
            int currentEncoderIdx = Array.IndexOf(encoders, Configuration.VideoEncoder);
            if (currentEncoderIdx == -1) currentEncoderIdx = 0;

            if (ImGui.Combo("Video Encoder", ref currentEncoderIdx, encoders, encoders.Length))
            {
                Configuration.VideoEncoder = encoders[currentEncoderIdx];
                save = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("Hardware encoding offloads work to your GPU, saving CPU performance.\nIf recordings fail to start, your GPU may not support the selected encoder.");
            }
            
            ImGui.Spacing();
        }

        if (ImGui.CollapsingHeader("Replay Buffer", ImGuiTreeNodeFlags.DefaultOpen))
        {
            int replaySec = Configuration.ReplayBufferSeconds;
            if (ImGui.SliderInt("Buffer Length (sec)", ref replaySec, 5, 60))
            {
                Configuration.ReplayBufferSeconds = replaySec;
                save = true;
            }
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip("How many seconds of gameplay the Buffer mode will keep in RAM.");
            }
            
            ImGui.Spacing();
        }

        if (save)
        {
            Configuration.Save();
        }
    }
}
