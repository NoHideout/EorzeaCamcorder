using System;
using System.IO;
using Dalamud.Configuration;
using Dalamud.Plugin;

namespace EorzeaCamcorder;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public int TargetFps { get; set; } = 60;
    public int VideoBitrateKbps { get; set; } = 8000;
    public int ResolutionHeight { get; set; } = 0;
    public bool ShowAdvancedSettings { get; set; } = false;
    public string OutputFormat { get; set; } = "mp4";
    public string VideoEncoder { get; set; } = "Software (x264)";
    
    public int ReplayBufferSeconds { get; set; } = 30;
    
    [NonSerialized]
    private IDalamudPluginInterface? _pluginInterface;

    public void Initialize(IDalamudPluginInterface pluginInterface)
    {
        _pluginInterface = pluginInterface;
        
        if (string.IsNullOrEmpty(OutputDirectory))
        {
            OutputDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EorzeaCamcorder");
        }
    }

    public void Save()
    {
        _pluginInterface!.SavePluginConfig(this);
    }
}
