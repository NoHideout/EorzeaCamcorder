using System;
using System.IO;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace EorzeaCamcorder;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 0;

    public string OutputPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EorzeaCamcorder");
    public int FrameRate { get; set; } = 60;
    public int Bitrate { get; set; } = 80_000_000; // 80 Mbps default
    
    [NonSerialized]
    private IDalamudPluginInterface? pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        this.pluginInterface = pluginInterface;
    }

    public void Save()
    {
        pluginInterface!.SavePluginConfig(this);
    }
}
