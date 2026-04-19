using System;
using System.IO;
using System.Reflection;

namespace EorzeaCamcorder;

public static class UpdateManager
{
    public const int CurrentConfigVersion = 1;

    public static bool ProcessUpdates(Configuration config)
    {
        bool wasUpdated = false;

        if (config.Version < CurrentConfigVersion)
        {
            Service.Log.Information($"Migrating config from v{config.Version} to v{CurrentConfigVersion}");
            
            while (config.Version < CurrentConfigVersion)
            {
                switch (config.Version)
                {
                    case 0:
                        
                        config.Version = 1;
                        break;

                    default:
                        Service.Log.Warning($"Unknown config version {config.Version}, resetting to defaults.");
                        ResetToDefaults(config);
                        config.Version = CurrentConfigVersion;
                        break;
                }
            }
            wasUpdated = true;
        }

        if (!IsValidDirectory(config.OutputDirectory))
        {
            config.OutputDirectory = GetDefaultOutputDirectory();
            wasUpdated = true;
        }

        if (config.Triggers == null)
        {
            config.Triggers = new();
            wasUpdated = true;
        }

        return wasUpdated;
    }

    public static void ResetToDefaults(Configuration targetConfig)
    {
        var def = new Configuration();

        foreach (PropertyInfo prop in typeof(Configuration).GetProperties())
        {
            if (prop.Name == nameof(Configuration.Version)) 
                continue;
            
            if (prop.CanWrite)
            {
                prop.SetValue(targetConfig, prop.GetValue(def));
            }
        }
        targetConfig.Triggers?.Clear();
        targetConfig.OutputDirectory = GetDefaultOutputDirectory();
    }

    private static bool IsValidDirectory(string path)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(path)) return false;
            var fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) || Directory.CreateDirectory(fullPath).Exists;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDefaultOutputDirectory() => 
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), "EorzeaCamcorder");
}
