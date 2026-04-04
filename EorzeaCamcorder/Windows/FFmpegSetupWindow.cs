using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Windowing;
using FFMpegCore;
using FFMpegCore.Extensions.Downloader;
    
namespace EorzeaCamcorder.Windows;

public class FFmpegSetupWindow : Window
{
    private bool _isDownloading = false;
    private string _downloadMessage = "";

    public FFmpegSetupWindow() : base("EorzeaCamcorder - Setup")
    {
        Size = new Vector2(550, 400); 
        SizeCondition = ImGuiCond.FirstUseEver;
        Flags = ImGuiWindowFlags.NoCollapse;
    }

    public override void Draw()
    {
        ImGui.TextWrapped("EorzeaCamcorder requires FFmpeg to encode video, but it could not be found on your system.");
        ImGui.Spacing();
        
        if (_isDownloading)
        {
            ImGui.Spacing();
            ImGui.TextColored(ImGuiColors.HealerGreen, "Downloading FFmpeg...");
            ImGui.TextWrapped(_downloadMessage);
            return;
        }
        
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.TextColored(ImGuiColors.ParsedGreen, "Option 1: Automatic Installation (Recommended)");
        ImGui.Indent();
        ImGui.TextWrapped("Automatically download FFmpeg binaries specifically for this plugin.");
        ImGui.Spacing();

        float indentSpace = ImGui.GetStyle().IndentSpacing;
        float fullBtnWidth = ImGui.GetContentRegionAvail().X - indentSpace;

        ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.HealerGreen);
        if (ImGui.Button("Download Automatically", new Vector2(fullBtnWidth, 35)))
        {
            StartAutomaticDownload();
        }
        ImGui.PopStyleColor();
        ImGui.Unindent();
        
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.TextColored(ImGuiColors.DalamudGrey, "Option 2: Manual Installation");
        ImGui.Indent();
        ImGui.TextWrapped("Download FFmpeg and extract the .exe files directly into this plugin's config folder.");
        ImGui.TextWrapped("Alternatively, advanced users may add the binaries to their system's PATH environment variable.");
        ImGui.TextWrapped("Please note: Manual installations are not officially supported.");
        ImGui.Spacing();
        
        float itemSpacing = ImGui.GetStyle().ItemSpacing.X;
        float availWidth = ImGui.GetContentRegionAvail().X - indentSpace;
        float halfWidth = (availWidth - itemSpacing) / 2;
        
        if (ImGui.Button("Open Download Page", new Vector2(halfWidth, 0)))
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://ffmpeg.org/download.html", UseShellExecute = true }); }
            catch (Exception ex) { Plugin.Log.Error($"Could not open browser: {ex.Message}"); }
        }

        ImGui.SameLine();

        if (ImGui.Button("Open Config Directory", new Vector2(halfWidth, 0)))
        {
            try 
            { 
                Process.Start(new ProcessStartInfo 
                { 
                    FileName = Plugin.PluginInterface.ConfigDirectory.FullName, 
                    UseShellExecute = true, 
                    Verb = "open" 
                }); 
            }
            catch (Exception ex) { Plugin.Log.Error($"Could not open config directory: {ex.Message}"); }
        }

        ImGui.Spacing();
        ImGui.Spacing();
        
        ImGui.TextColored(ImGuiColors.DalamudGrey3, "Instructions:");
        ImGui.BulletText("Download 'ffmpeg-release-essentials.zip'");
        ImGui.BulletText("Extract the archive.");
        ImGui.BulletText("Copy 'ffmpeg.exe' and 'ffplay.exe' from the bin folder into the Config Directory.");
        ImGui.BulletText("Restart the plugin or FFXIV.");
        
        ImGui.Unindent();
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        
        ImGui.TextColored(ImGuiColors.DalamudGrey3, "License Information");
        ImGui.TextWrapped("This plugin uses FFmpeg to handle video encoding. FFmpeg is free software licensed under the GNU General Public License (GPLv2). EorzeaCamcorder does not distribute FFmpeg directly.");
        
        if (ImGui.Button("Read FFmpeg License"))
        {
            try { Process.Start(new ProcessStartInfo { FileName = "https://ffmpeg.org/legal.html", UseShellExecute = true }); }
            catch (Exception ex) { Plugin.Log.Error($"Could not open browser: {ex.Message}"); }
        }
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
                Plugin.Log.Information("FFmpeg successfully downloaded.");
                
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
