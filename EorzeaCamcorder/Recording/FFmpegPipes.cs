using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FFMpegCore.Pipes;

namespace EorzeaCamcorder.Recording;

public class VideoPipeSource : IPipeSource
{
    private readonly BlockingCollection<CapturedFrame> _queue;
    private readonly CapturedFrame _firstFrame;
    public int Width { get; }
    public int Height { get; }
    public int Fps { get; }

    public VideoPipeSource(BlockingCollection<CapturedFrame> queue, CapturedFrame firstFrame, int width, int height, int fps)
    {
        _queue = queue;
        _firstFrame = firstFrame;
        Width = width;
        Height = height;
        Fps = fps;
    }

    public string GetStreamArguments() => $"-f rawvideo -pixel_format bgra -video_size {Width}x{Height} -framerate {Fps}";

    public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        int frameSize = Width * Height * 4;

        // Write first dequeued
        for (int i = 0; i < _firstFrame.RepeatCount; i++)
        {
            await outputStream.WriteAsync(_firstFrame.Data, 0, frameSize, cancellationToken);
        }
        System.Buffers.ArrayPool<byte>.Shared.Return(_firstFrame.Data);

        // Process
        foreach (var frame in _queue.GetConsumingEnumerable(cancellationToken))
        {
            for (int i = 0; i < frame.RepeatCount; i++)
            {
                await outputStream.WriteAsync(frame.Data, 0, frameSize, cancellationToken);
            }
            System.Buffers.ArrayPool<byte>.Shared.Return(frame.Data);
        }
    }
}

public class AudioPipeSource : IPipeSource
{
    private readonly BlockingCollection<byte[]> _queue;
    public int SampleRate { get; }
    public int Channels { get; }
    public string Format { get; }

    public AudioPipeSource(BlockingCollection<byte[]> queue, int sampleRate, int channels, string format)
    {
        _queue = queue;
        SampleRate = sampleRate;
        Channels = channels;
        Format = format;
    }

    public string GetStreamArguments() => $"-f {Format} -ar {SampleRate} -ac {Channels}";

    public async Task WriteAsync(Stream outputStream, CancellationToken cancellationToken)
    {
        foreach (var buffer in _queue.GetConsumingEnumerable(cancellationToken))
        {
            await outputStream.WriteAsync(buffer, 0, buffer.Length, cancellationToken);
        }
    }
}
