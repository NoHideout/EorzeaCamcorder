using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EorzeaCamcorder.Recording;
using EorzeaCamcorder.Trigger;
using EorzeaCamcorder.Windows;

namespace EorzeaCamcorder;

public class Service
{
    [PluginService] public static IChatGui Chat { get; private set; } = null!;
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] public static IPluginLog Log { get; private set; } = null!;
    [PluginService] public static ITextureReadbackProvider TextureReadbackProvider { get; private set; } = null!;
    [PluginService] public static IDtrBar DtrBar { get; private set; } = null!;
    [PluginService] public static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] public static IClientState ClientState { get; private set; } = null!;
    [PluginService] public static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDutyState DutyState { get; private set; } = null!;

    public static Configuration Config { get; set; } = null!;
    public static GameRecorder Recorder { get; set; } = null!;
    
    public static ConfigWindow ConfigWindow { get; set; } = null!;
    public static MainWindow MainWindow { get; set; } = null!;
    public static FFmpegSetupWindow FFmpegSetupWindow { get; set; } = null!;
    public static IpcProvider IpcProvider { get; set; } = null!;
    public static TriggerManager TriggerManager { get; set; } = null!;
    public static TriggerWindow TriggerWindow { get; set; } = null!;
}
