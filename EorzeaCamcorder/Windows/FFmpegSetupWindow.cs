using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using FFMpegCore;
using FFMpegCore.Extensions.Downloader;
    
namespace EorzeaCamcorder.Windows;

public class FFmpegSetupWindow : Window
{
    private bool _isDownloading = false;
    private string _downloadMessage = "";

    public FFmpegSetupWindow() : base("FFmpeg Required - Setup")
    {
        Size = new Vector2(550, 320);
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("EorzeaCamcorder requires FFmpeg to encode video, but it could not be found on your system.");
        ImGui.Spacing();
        
        if (_isDownloading)
        {
            ImGui.TextColored(new Vector4(0.0f, 1.0f, 0.0f, 1.0f), "Downloading FFmpeg...");
            ImGui.TextWrapped(_downloadMessage);
            return;
        }
        
        ImGui.Separator();
        
        ImGui.Text("Option 1: Automatic Installation (Recommended)");
        ImGui.TextWrapped("Automatically download FFmpeg binaries specifically for this plugin.");
        if (ImGui.Button("Download Automatically"))
        {
            StartAutomaticDownload();
        }
        
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Text("Option 2: Manual Installation");
        ImGui.TextWrapped("Download FFmpeg yourself and add it to your system's PATH. You will need to restart the game after doing this.");
        if (ImGui.Button("Open Download Page (gyan.dev)"))
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "https://www.gyan.dev/ffmpeg/builds/",
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Could not open browser: {ex.Message}");
            }
        }

        ImGui.Spacing();
        ImGui.TextWrapped("1. Download 'ffmpeg-release-essentials.zip'\n2. Extract the archive somewhere safe.\n3. Add the extracted 'bin' folder to your Windows Environment Variables (PATH).\n4. Restart Final Fantasy XIV.");
    }

    private void StartAutomaticDownload()
    {
        _isDownloading = true;
        _downloadMessage = "Starting download, please wait. This may take a bit...";

        Task.Run(async () =>
        {
            try
            {
                var configDir = Plugin.PluginInterface.ConfigDirectory.FullName;
                GlobalFFOptions.Configure(new FFOptions { BinaryFolder = configDir });
                
                await FFMpegDownloader.DownloadBinaries();

                _downloadMessage = "Download complete! You can now use the plugin.";
                Plugin.Log.Information("FFmpeg successfully downloaded and configured automatically.");
                
                await Task.Delay(3000);
                IsOpen = false;
                _isDownloading = false;
            }
            catch (Exception ex)
            {
                Plugin.Log.Error($"Failed to download FFmpeg: {ex}");
                _downloadMessage = $"Download failed: {ex.Message}\nPlease try the manual installation.";
                _isDownloading = false;
            }
        });
    }
}
