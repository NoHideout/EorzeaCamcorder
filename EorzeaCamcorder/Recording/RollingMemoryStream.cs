using System;
using System.IO;

namespace EorzeaCamcorder.Recording;

public class RollingMemoryStream : Stream
{
    private readonly byte[] _buffer;
    private int _head;
    private bool _isFilled;
    private readonly object _lock = new();

    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => _isFilled ? _buffer.Length : _head;
    public override long Position { get => _head; set { } }

    public RollingMemoryStream(int capacityBytes)
    {
        _buffer = new byte[capacityBytes];
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            int remaining = _buffer.Length - _head;
            
            if (count <= remaining)
            {
                Buffer.BlockCopy(buffer, offset, _buffer, _head, count);
                _head += count;
                if (_head == _buffer.Length)
                {
                    _head = 0;
                    _isFilled = true;
                }
            }
            else
            {
                Buffer.BlockCopy(buffer, offset, _buffer, _head, remaining);
                int overflow = count - remaining;
                Buffer.BlockCopy(buffer, offset + remaining, _buffer, 0, overflow);
                _head = overflow;
                _isFilled = true;
            }
        }
    }

    public byte[] TakeSnapshot()
    {
        lock (_lock)
        {
            int size = _isFilled ? _buffer.Length : _head;
            byte[] snapshot = new byte[size];

            if (!_isFilled)
            {
                Buffer.BlockCopy(_buffer, 0, snapshot, 0, _head);
            }
            else
            {
                int tailLength = _buffer.Length - _head;
                Buffer.BlockCopy(_buffer, _head, snapshot, 0, tailLength);
                Buffer.BlockCopy(_buffer, 0, snapshot, tailLength, _head);
            }

            return snapshot;
        }
    }

    public override void Flush() { }
    public override int Read(byte[] buffer, int offset, int count) => 0;
    public override long Seek(long offset, SeekOrigin origin) => 0;
    public override void SetLength(long value) { }
}
