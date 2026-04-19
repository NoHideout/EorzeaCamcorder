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
    private static Configuration Config => Service.Config;

    private static string GetCodec() => EncoderReg.GetProfile(Config.SelectedVideoEncoder).FFmpegCodec;
    
    private static void ApplyEncodingOptions(FFMpegArgumentOptions options)
    {
        options
            .WithVideoCodec(GetCodec())
            .WithAudioCodec(AudioCodec.Aac)
            .WithCustomArgument($"-b:v {Config.VideoBitrateKbps}k")
            .WithCustomArgument("-pix_fmt yuv420p")
            .WithCustomArgument($"-g {Config.TargetFps}");

        if (Config.ResolutionHeight > 0)
        {
            options.WithCustomArgument($"-vf scale=-2:{Config.ResolutionHeight}");
        }
    }

    public static async Task StartCaptureEngineAsync(
        CancellationToken token,
        BlockingCollection<CapturedFrame> videoQueue,
        BlockingCollection<byte[]> audioQueue,
        AudioRecorder audioRecorder,
        BroadcastStream outputStream,
        Action<string>? onRecordingError)
    {
        try
        {
            if (!videoQueue.TryTake(out CapturedFrame firstFrame, 10000, token)) return;

            int width = firstFrame.Width;
            int height = firstFrame.Height;

            audioRecorder.GetFormat(out int sr, out int ch, out int bps);
            string audioFmt = bps == 32 ? "f32le" : "s16le";

            var videoPipeSource = new VideoPipeSource(videoQueue, firstFrame, width, height, Config.TargetFps);
            var audioPipeSource = new AudioPipeSource(audioQueue, sr, ch, audioFmt);

            string codec = GetCodec();

            Log.Information($"Starting FFmpeg: Res: {width}x{height}, Encoder: {codec}");

            await FFMpegArguments
                .FromPipeInput(videoPipeSource)
                .AddPipeInput(audioPipeSource)
                .OutputToPipe(new StreamPipeSink(outputStream), options =>
                {
                    ApplyEncodingOptions(options);
                    options.ForceFormat("mpegts");
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
        finally
        {
            audioRecorder.Stop();
        }
    }

    public static async Task RemuxFinalFormatAsync(
        string inputTsFile,
        string finalFilePath,
        bool deleteInput,
        int? trimFromEndSeconds = null,
        string? metadataFilePath = null)
    {
        try
        {
            Log.Information($"Processing video to: {finalFilePath}");

            var config = Service.Config;
            bool isReplay = trimFromEndSeconds.HasValue;

            var args = FFMpegArguments.FromFileInput(inputTsFile, true, options =>
            {
                if (isReplay)
                {
                    options.WithCustomArgument($"-sseof -{trimFromEndSeconds!.Value}");
                }
            });

            if (!string.IsNullOrEmpty(metadataFilePath) && File.Exists(metadataFilePath))
            {
                args = args.AddFileInput(metadataFilePath);
            }

            await args.OutputToFile(finalFilePath, true, options =>
            {
                options.WithFastStart();

                if (isReplay)
                {
                    ApplyEncodingOptions(options);
                }
                else
                {
                    if (!string.IsNullOrEmpty(metadataFilePath) && File.Exists(metadataFilePath))
                    {
                        options.WithCustomArgument("-map 0 -map_metadata 1 -c copy");
                    }
                    else
                    {
                        options.WithCustomArgument("-c copy");
                    }
                }
            })
            .ProcessAsynchronously();

            Log.Information($"Successfully saved: {finalFilePath}");
        }
        catch (Exception ex)
        {
            Log.Error($"Failed to process video: {ex.Message}");
        }
        finally
        {
            if (deleteInput)
            {
                try { File.Delete(inputTsFile); } catch { }
            }

            if (!string.IsNullOrEmpty(metadataFilePath))
            {
                try { File.Delete(metadataFilePath); } catch { }
            }
        }
    }

    public static async Task<bool> TestEncoderAsync(EncoderType type)
    {
        var profile = EncoderReg.GetProfile(type);
        if (!profile.IsHardwareAccelerated) return true;

        try
        {
            int width = 320; 
            int height = 240;
            int stride = width * 4;
            int frameSize = stride * height;

            byte[] rawData = System.Buffers.ArrayPool<byte>.Shared.Rent(frameSize);
        
            Array.Clear(rawData, 0, rawData.Length);

            var frame = new CapturedFrame { 
                Data = rawData, 
                RepeatCount = 1, 
                Width = width, 
                Height = height 
            };

            using var testQueue = new BlockingCollection<CapturedFrame>();
            testQueue.Add(frame);
            testQueue.CompleteAdding();

            var videoPipe = new VideoPipeSource(testQueue, frame, width, height, 1);

            await FFMpegArguments
                  .FromPipeInput(videoPipe)
                  .OutputToFile("NUL", true, options => options
                    .WithVideoCodec(profile.FFmpegCodec)
                    .WithCustomArgument("-frames:v 1")
                    .ForceFormat("null"))
                  .ProcessAsynchronously();
        
            return true;
        }
        catch (Exception ex)
        {
            Service.Log.Debug($"Hardware encoder {type} test failed: {ex.Message}");
            return false;
        }
    }
}
