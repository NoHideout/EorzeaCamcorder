using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Plugin.Services;
using FFMpegCore;
using FFMpegCore.Enums;
using FFMpegCore.Pipes;

namespace EorzeaCamcorder.Recording;

public static class FFmpegMuxer
{
    private static IPluginLog Log => Service.Log;
    
    public static async Task StartCaptureEngineAsync(
        CancellationToken token, 
        BlockingCollection<CapturedFrame> videoQueue, 
        BlockingCollection<byte[]> audioQueue, 
        AudioRecorder audioRecorder,
        BroadcastStream outputStream, 
        Configuration config,
        Action<string>? onRecordingError)
    {
        try
        {
            if (!videoQueue.TryTake(out CapturedFrame firstFrame, 10000, token)) return;

            int width = firstFrame.Width;
            int height = firstFrame.Height;

            int sr = 0, ch = 0, bps = 0;
            for (int i = 0; i < 50; i++)
            {
                try { audioRecorder.GetFormat(out sr, out ch, out bps); if (sr != 0) break; }
                catch { if (i == 49) throw; Thread.Sleep(100); }
            }

            string audioFmt = bps == 32 ? "f32le" : "s16le";

            var videoPipeSource = new VideoPipeSource(videoQueue, firstFrame, width, height, config.TargetFps);
            var audioPipeSource = new AudioPipeSource(audioQueue, sr, ch, audioFmt);

            string targetCodec = config.VideoEncoder switch
            {
                "NVIDIA (NVENC)" => "h264_nvenc",
                "AMD (AMF)" => "h264_amf",
                "Intel (QSV)" => "h264_qsv",
                _ => "libx264"
            };

            Log.Information($"Starting Background FFmpeg Engine... Res: {width}x{height}, Encoder: {targetCodec}");

            await FFMpegArguments
                  .FromPipeInput(videoPipeSource)
                  .AddPipeInput(audioPipeSource)
                  .OutputToPipe(new StreamPipeSink(outputStream), options => 
                  {
                      options
                          .WithVideoCodec(targetCodec) 
                          .WithAudioCodec(AudioCodec.Aac)
                          .ForceFormat("mpegts")
                          .WithCustomArgument($"-b:v {config.VideoBitrateKbps}k")
                          .WithCustomArgument("-pix_fmt yuv420p")
                          .WithCustomArgument($"-g {config.TargetFps}");

                      if (config.ResolutionHeight > 0)
                          options.WithCustomArgument($"-vf scale=-2:{config.ResolutionHeight}");
                  })
                  .NotifyOnError(msg => Log.Debug($"[FFmpeg Engine] {msg}"))
                  .ProcessAsynchronously();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Log.Error($"Capture Engine Error: {ex.Message}");
            onRecordingError?.Invoke($"Engine failed: {ex.Message}");
        }
        finally { audioRecorder.Stop(); }
    }

    public static async Task RemuxToFinalFormatAsync(string inputTsFile, string finalFilePath, bool deleteInput)
    {
        try
        {
            Log.Information($"Remuxing stream to: {finalFilePath}");

            await FFMpegArguments
                .FromFileInput(inputTsFile)
                .OutputToFile(finalFilePath, true, options => options
                    .WithCustomArgument("-c copy")
                    .WithFastStart())
                .ProcessAsynchronously();

            Log.Information($"Successfully saved: {finalFilePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to process video: {ex.Message}");
        }
        finally
        {
            if (deleteInput && File.Exists(inputTsFile)) File.Delete(inputTsFile);
        }
    }
}
