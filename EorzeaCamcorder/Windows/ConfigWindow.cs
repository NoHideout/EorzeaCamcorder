using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;

namespace EorzeaCamcorder.Windows;

public class ConfigWindow : Window, IDisposable
{
    private readonly Configuration configuration;

    public ConfigWindow(Plugin plugin) : base("XIV Recorder Settings")
    {
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse;
        Size = new Vector2(400, 300);
        SizeCondition = ImGuiCond.FirstUseEver;

        configuration = plugin.Configuration;
    }

    public void Dispose() { }

    public override void Draw()
    {
        bool save = false;

        ImGui.Text("Output Folder");
        var path = configuration.OutputPath;
        if (ImGui.InputText("##path", ref path, 255))
        {
            configuration.OutputPath = path;
            save = true;
        }
        ImGuiHelpers.ScaledDummy(5);

        int fps = configuration.FrameRate;
        if (ImGui.SliderInt("Frame Rate", ref fps, 10, 144))
        {
            configuration.FrameRate = fps;
            save = true;
        }

        int bitrateMbps = configuration.Bitrate / 1_000_000;
        if (ImGui.SliderInt("Bitrate (Mbps)", ref bitrateMbps, 5, 100))
        {
            configuration.Bitrate = bitrateMbps * 1_000_000;
            save = true;
        }
        
        if (save)
        {
            configuration.Save();
        }
    }
}
