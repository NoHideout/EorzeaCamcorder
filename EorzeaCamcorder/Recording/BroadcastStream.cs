using System;
using System.IO;

namespace EorzeaCamcorder.Recording;

public class BroadcastStream : Stream
{
    public RollingMemoryStream? RingBuffer { get; set; }
    public Stream? FileStream { get; set; }

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => 0;
    public override long Position { get => 0; set {  } }

    public override void Write(byte[] buffer, int offset, int count)
    {
        if (RingBuffer != null)
        {
            try { RingBuffer.Write(buffer, offset, count); } catch { }
        }

        if (FileStream != null)
        {
            try { FileStream.Write(buffer, offset, count); } catch { }
        }
    }

    public override void Flush() 
    {
        FileStream?.Flush();
    }

    public override void Close() { }

    public override int Read(byte[] buffer, int offset, int count) => 0; 
    public override long Seek(long offset, SeekOrigin origin) => 0;
    public override void SetLength(long value) { }
}
