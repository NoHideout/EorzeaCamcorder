using Dalamud.Game.Command;
using Dalamud.Game.Gui.Dtr;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using EorzeaCamcorder.Recording;
using EorzeaCamcorder.Windows;

namespace EorzeaCamcorder;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static ITextureReadbackProvider TextureReadbackProvider { get; private set; } = null!;
    [PluginService] internal static IDtrBar DtrBar { get; private set; } = null!;

    private const string CommandName = "/rec";

    public Configuration Configuration { get; init; }
    public GameRecorder Recorder { get; init; }
    public IpcProvider IpcProvider { get; init; }

    public readonly WindowSystem WindowSystem = new("EorzeaCamcorder");
    private ConfigWindow ConfigWindow { get; init; }
    private MainWindow MainWindow { get; init; }

    private IDtrBarEntry? _dtrEntry;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Configuration.Initialize(PluginInterface);

        Recorder = new GameRecorder();
        IpcProvider = new IpcProvider(PluginInterface, Recorder);

        ConfigWindow = new ConfigWindow(this);
        MainWindow = new MainWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MainWindow);

        PluginInterface.UiBuilder.DisableGposeUiHide =true;

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


        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
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
    }

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
            _dtrEntry.Text = "Rec...";
            _dtrEntry.Tooltip = $"EorzeaCamcorder is active.\nStarted by: {Recorder.Initiator}";
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MainWindow.Toggle();
}
