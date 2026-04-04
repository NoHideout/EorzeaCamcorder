using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EorzeaCamcorder.Recording;
using EorzeaCamcorder.Windows;
using FFMpegCore;

namespace EorzeaCamcorder;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureReadbackProvider TextureReadbackProvider { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    private const string CommandName = "/ecam";
    //private const string SetupCommandName = "/ecamsetup";

    public Configuration Configuration { get; init; }
    public GameRecorder Recorder { get; init; }
    public IpcProvider IpcProvider { get; init; }

    public readonly WindowSystem WindowSystem = new("EorzeaCamcorder");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }
    public FFmpegSetupWindow FFmpegSetupWindow { get; init; }
    private IDtrBarEntry? _dtrEntry;

    public Plugin()
    {
        PluginInterface.UiBuilder.DisableGposeUiHide =true;
        PluginInterface.UiBuilder.DisableCutsceneUiHide = true; // enables plugin to record during cutscene

        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        Recorder = new GameRecorder(Configuration);
        IpcProvider = new IpcProvider(PluginInterface, Recorder);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);
        FFmpegSetupWindow = new FFmpegSetupWindow();
        
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(FFmpegSetupWindow);

        if (!checkFFmpeg())
        {
            FFmpegSetupWindow.IsOpen = true;
        }

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the EorzeaCamcorder main window"
        });
        /*
        CommandManager.AddHandler(SetupCommandName, new CommandInfo(OnSetupCommand)
        {
            HelpMessage = "Debug: Opens the FFmpeg Setup Window"
        });
        */
        _dtrEntry = DtrBar.Get("EorzeaCamcorder");
        if (_dtrEntry != null)
        {
            _dtrEntry.Shown = false;
            _dtrEntry.Text = "REC";
            _dtrEntry.OnClick = _ => ToggleMainUi();
        }


        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    private bool checkFFmpeg()
    {
        var exeName = "ffmpeg.exe";
        var configDir = PluginInterface.ConfigDirectory.FullName;

        if (File.Exists(Path.Combine(configDir, exeName)))
        {
            GlobalFFOptions.Configure(new FFOptions {BinaryFolder = configDir});
            Log.Debug($"FFmpeg found in {configDir}");
            return true;
        }
        Log.Debug($"FFmpeg not found in {configDir}");
        
        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (envPath != null)
        {
            foreach (var path in envPath.Split(Path.PathSeparator))
            {
                try
                {
                    if (File.Exists(Path.Combine(path, exeName)))
                    {
                        Log.Debug($"FFmpeg found in {path}");
                        return true;
                    }
                }
                catch
                {
                    // ignored
                }
            }
        }
        return false;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        _dtrEntry?.Remove();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        IpcProvider.Dispose();
        Recorder.Dispose();

        CommandManager.RemoveHandler(CommandName);
        //CommandManager.RemoveHandler(SetupCommandName);
    }
    //private void OnSetupCommand(string command, string args) => FFmpegSetupWindow.IsOpen = true;
    
    private void OnCommand(string command, string args)
    {
        MainWindow.Toggle();
    }

    private void Draw()
    {
        WindowSystem.Draw();
        Recorder.Update();
        UpdateDtrBar();
    }

    private void UpdateDtrBar()
    {
        if (_dtrEntry == null) return;

        bool isRecording = Recorder.IsRecording;

        if (_dtrEntry.Shown != isRecording)
        {
            _dtrEntry.Shown = isRecording;
        }

        if (isRecording)
        {
            _dtrEntry.Text = SeIconChar.Circle.ToIconString() + " REC";
            _dtrEntry.Tooltip = $"EorzeaCamcorder is active.\nStarted by: {Recorder.Initiator}";
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
