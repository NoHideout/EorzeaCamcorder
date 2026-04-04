using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore;
using FFMpegCore.Enums;

namespace EorzeaCamcorder.Recording;

public static class FFmpegMuxer
{
    public static async Task EncodeAsync(
        CancellationToken token, 
        BlockingCollection<CapturedFrame> videoQueue, 
        BlockingCollection<byte[]> audioQueue, 
        AudioRecorder audioRecorder,
        string outputFileName, 
        Configuration config,
        Action<string>? onRecordingError)
    {
        try
        {
            if (!videoQueue.TryTake(out CapturedFrame firstFrame, 10000, token)) 
                throw new Exception("Timed out waiting for first frame.");

            int width = firstFrame.Width;
            int height = firstFrame.Height;

            // Wait/poll for audio format
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
            Plugin.Log.Information($"Starting FFmpeg Muxer... Res: {width}x{height}, Audio: {sr}Hz {bps}bps, Encoder: {targetCodec}");
            
            await FFMpegArguments
                  .FromPipeInput(videoPipeSource)
                  .AddPipeInput(audioPipeSource)
                  .OutputToFile(outputFileName, true, options => 
                  {
                      options
                          .WithVideoCodec(targetCodec) 
                          .WithAudioCodec(AudioCodec.Aac)
                          .WithFastStart()
                          .WithCustomArgument($"-b:v {config.VideoBitrateKbps}k")
                          .WithCustomArgument("-pix_fmt yuv420p");

                      if (config.ResolutionHeight > 0)
                      {
                          options.WithCustomArgument($"-vf scale=-2:{config.ResolutionHeight}");
                      }
                  })
                  .NotifyOnError(msg => Plugin.Log.Debug($"[FFmpeg] {msg}"))
                  .NotifyOnOutput(msg => Plugin.Log.Debug($"[FFmpeg] {msg}"))
                  .ProcessAsynchronously();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Plugin.Log.Error($"Encoding Error: {ex.Message}");
            onRecordingError?.Invoke($"Encoding failed: {ex.Message}");
        }
        finally 
        {
            audioRecorder.Stop(); 
        }
    }
}
