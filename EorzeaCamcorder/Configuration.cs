using System;
using System.Collections.Generic;
using System.IO;
using Dalamud.Configuration;
using Dalamud.Plugin;
using EorzeaCamcorder.Trigger;

namespace EorzeaCamcorder;

public enum ReplayEventPosition
{
    End = 0,
    Middle = 1,
    Start = 2
}

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; }
    public string OutputDirectory { get; set; } = string.Empty;
    public int TargetFps { get; set; } = 60;
    public int VideoBitrateKbps { get; set; } = 8000;
    public int ResolutionHeight { get; set; } = 0;
    public string OutputFormat { get; set; } = "mp4";
    public string VideoEncoder { get; set; } = "Software (x264)";
    public bool AllowIpc { get; set; } = false;
    public int ReplayBufferSeconds { get; set; } = 30;
    public ReplayEventPosition ReplayEventPosition { get; set; } = ReplayEventPosition.Middle;    
    
    public List<TriggerConfig> Triggers { get; set; } = new();
    
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
