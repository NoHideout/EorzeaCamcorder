using System;
using System.IO;
using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Game.Text;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EorzeaCamcorder.Recording;
using EorzeaCamcorder.Trigger;
using EorzeaCamcorder.Windows;
using FFMpegCore;

namespace EorzeaCamcorder;

public sealed class Plugin : IDalamudPlugin
{
    private IDalamudPluginInterface DalamudInterface => Service.PluginInterface;
    private ICommandManager CommandManager => Service.CommandManager;
    private IDtrBar DtrBar => Service.DtrBar;
    private IPluginLog Log => Service.Log;

    private Configuration Config => Service.Config;
    private GameRecorder Recorder => Service.Recorder;
    private IpcProvider IpcProvider => Service.IpcProvider;
    private ConfigWindow ConfigWindow => Service.ConfigWindow;
    private MainWindow MainWindow => Service.MainWindow;
    private FFmpegSetupWindow FFmpegSetupWindow => Service.FFmpegSetupWindow;

    private readonly CommandHandler CommandHandler;
    private const string CommandName = "/ecam";
    
    public readonly WindowSystem WindowSystem = new("EorzeaCamcorder");
   
    private IDtrBarEntry? _dtrEntry;

    public Plugin(IDalamudPluginInterface pluginInterface)
    {
        pluginInterface.Create<Service>();

        DalamudInterface.UiBuilder.DisableGposeUiHide = true;
        DalamudInterface.UiBuilder.DisableCutsceneUiHide = true; // enables plugin to record during cutscene

        Service.Config = DalamudInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Config.Initialize(DalamudInterface);

        Service.Recorder = new GameRecorder(Config);
        Service.IpcProvider = new IpcProvider();
        IpcProvider.Register();

        Service.ConfigWindow = new ConfigWindow();
        Service.MainWindow = new MainWindow();
        Service.FFmpegSetupWindow = new FFmpegSetupWindow();
        Service.TriggerManager = new TriggerManager();
        Service.TriggerWindow = new TriggerWindow();

        WindowSystem.AddWindow(Service.TriggerWindow);
        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);
        WindowSystem.AddWindow(FFmpegSetupWindow);
        
        CommandHandler = new CommandHandler();
        
        if (!CheckFFmpeg())
        {
            FFmpegSetupWindow.IsOpen = true;
        }

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Opens the EorzeaCamcorder main window"
        });
        
        _dtrEntry = DtrBar.Get("EorzeaCamcorder");
        if (_dtrEntry != null)
        {
            _dtrEntry.Shown = false;
            _dtrEntry.Text = "REC";
            _dtrEntry.OnClick = _ => ToggleMainUi();
        }

        DalamudInterface.UiBuilder.Draw += Draw;
        DalamudInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        DalamudInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    private bool CheckFFmpeg()
    {
        var exeName = "ffmpeg.exe";
        var configDir = DalamudInterface.ConfigDirectory.FullName;

        if (File.Exists(Path.Combine(configDir, exeName)))
        {
            GlobalFFOptions.Configure(new FFOptions { BinaryFolder = configDir });
            Log.Debug($"FFmpeg found in {configDir}");
            return true;
        }
        Log.Debug($"FFmpeg not found in {configDir}. Checking PATH.");
    
        var envPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            var paths = envPath.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
            foreach (var path in paths)
            {
                if (path.IndexOfAny(Path.GetInvalidPathChars()) >= 0) continue;

                string fullPath = Path.Combine(path.Trim('"'), exeName);
                if (File.Exists(fullPath))
                {
                    Log.Debug($"FFmpeg found in PATH: {fullPath}");
                    return true;
                }
            }
        }
        return false;
    }

    public void Dispose()
    {
        DalamudInterface.UiBuilder.Draw -= Draw;
        DalamudInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        DalamudInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        _dtrEntry?.Remove();

        ConfigWindow.Dispose();
        MainWindow.Dispose();
        IpcProvider.Dispose();
        Service.TriggerManager.Dispose();
        Service.TriggerWindow.Dispose();
        Recorder.Dispose();
        CommandManager.RemoveHandler(CommandName);
    }
    
    private void OnCommand(string command, string args)
    {
        CommandHandler.Handle(args);
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
        bool isBuffer = Recorder.IsReplayBufferRunning;
        bool shouldShow = isRecording || isBuffer;

        _dtrEntry.Shown = shouldShow;

        if (!shouldShow) return;

        if (isRecording)
        {
            _dtrEntry.Text = SeIconChar.Circle.ToIconString() + " REC";
            _dtrEntry.Tooltip = $"Recording active\nStarted by: {Recorder.Initiator}";
        }
        else if (isBuffer)
        {
            _dtrEntry.Text = SeIconChar.Square.ToIconString() + " BUFF";
            _dtrEntry.Tooltip = "Replay buffer is running";
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
