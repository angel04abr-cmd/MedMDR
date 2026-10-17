using System.Buffers;
using System.IO.Compression;
using System.IO.Hashing;

namespace Medrar.Core;

/// <summary>Cuenta los bytes que pasan y no cierra el flujo de destino.</summary>
internal sealed class CountingStream : Stream
{
    private readonly Stream _inner;
    private readonly bool _leaveOpen;
    public long Count { get; private set; }
    public CountingStream(Stream inner, bool leaveOpen = true) { _inner = inner; _leaveOpen = leaveOpen; }
    public override void Write(byte[] buffer, int offset, int count) { _inner.Write(buffer, offset, count); Count += count; }
    public override void Write(ReadOnlySpan<byte> buffer) { _inner.Write(buffer); Count += buffer.Length; }
    public override void Flush() => _inner.Flush();
    protected override void Dispose(bool disposing) { if (disposing && !_leaveOpen) _inner.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Count;
    public override long Position { get => Count; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>Una ventana [start, start+length) de otro flujo (seekable). No cierra el original.</summary>
public sealed class SubStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _length;
    private readonly bool _ownsInner;
    private long _pos;
    public SubStream(Stream inner, long start, long length, bool ownsInner = false) { _inner = inner; _start = start; _length = length; _ownsInner = ownsInner; }
    protected override void Dispose(bool disposing) { if (disposing && _ownsInner) _inner.Dispose(); base.Dispose(disposing); }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        long remaining = _length - _pos;
        if (remaining <= 0) return 0;
        if (buffer.Length > remaining) buffer = buffer[..(int)remaining];
        _inner.Seek(_start + _pos, SeekOrigin.Begin);
        int n = _inner.Read(buffer);
        _pos += n;
        return n;
    }
    public override long Seek(long offset, SeekOrigin origin)
    {
        _pos = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _pos + offset, _ => _length + offset };
        return _pos;
    }
    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _length;
    public override long Position { get => _pos; set => _pos = value; }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Lee como máximo N bytes de un flujo sin cerrarlo.</summary>
internal sealed class LimitedStream : Stream
{
    private readonly Stream _inner;
    private long _remaining;
    public LimitedStream(Stream inner, long length) { _inner = inner; _remaining = length; }
    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));
    public override int Read(Span<byte> buffer)
    {
        if (_remaining <= 0) return 0;
        if (buffer.Length > _remaining) buffer = buffer[..(int)_remaining];
        int n = _inner.Read(buffer);
        _remaining -= n;
        return n;
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Flush() { }
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

/// <summary>Escribe a un flujo y calcula el CRC32 de lo escrito.</summary>
internal sealed class CrcWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly Crc32 _crc = new();
    private readonly ProgressReporter? _progress;
    public long Count { get; private set; }
    public CrcWriteStream(Stream inner, ProgressReporter? progress = null) { _inner = inner; _progress = progress; }
    public uint Crc => _crc.GetCurrentHashAsUInt32();
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _crc.Append(buffer);
        _inner.Write(buffer);
        Count += buffer.Length;
        _progress?.Add(buffer.Length);
    }
    public override void Flush() => _inner.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => Count;
    public override long Position { get => Count; set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>Descarta los N primeros bytes escritos y deja pasar el resto (máx. M).</summary>
internal sealed class WindowWriteStream : Stream
{
    private readonly Stream _inner;
    private long _skip;
    private long _take;
    public WindowWriteStream(Stream inner, long skip, long take) { _inner = inner; _skip = skip; _take = take; }
    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));
    public override void Write(ReadOnlySpan<byte> buffer)
    {
        if (_skip > 0)
        {
            long s = Math.Min(_skip, buffer.Length);
            buffer = buffer[(int)s..];
            _skip -= s;
        }
        if (buffer.IsEmpty || _take <= 0) return;
        if (buffer.Length > _take) buffer = buffer[..(int)_take];
        _inner.Write(buffer);
        _take -= buffer.Length;
    }
    public bool Complete => _take <= 0;
    public override void Flush() { }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

public static class StreamUtil
{
    public const int BufferSize = 128 * 1024;

    public static void CopyTo(Stream source, Stream dest, ProgressReporter? progress = null)
    {
        var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int n;
            while ((n = source.Read(buf, 0, buf.Length)) > 0)
            {
                dest.Write(buf, 0, n);
                progress?.Add(n);
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    /// <summary>Copia exactamente <paramref name="count"/> bytes.</summary>
    public static void CopyExactly(Stream source, Stream dest, long count)
    {
        var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            while (count > 0)
            {
                int n = source.Read(buf, 0, (int)Math.Min(buf.Length, count));
                if (n <= 0) throw new EndOfStreamException();
                dest.Write(buf, 0, n);
                count -= n;
            }
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
    }

    public static uint ComputeCrc(Stream s)
    {
        var crc = new Crc32();
        var buf = ArrayPool<byte>.Shared.Rent(BufferSize);
        try
        {
            int n;
            while ((n = s.Read(buf, 0, buf.Length)) > 0) crc.Append(buf.AsSpan(0, n));
        }
        finally { ArrayPool<byte>.Shared.Return(buf); }
        return crc.GetCurrentHashAsUInt32();
    }

    public static uint Crc(ReadOnlySpan<byte> data)
    {
        var c = new Crc32();
        c.Append(data);
        return c.GetCurrentHashAsUInt32();
    }
}

/// <summary>Compresión: Deflate (nativo de .NET) y Brotli con calidad y ventana ajustables.</summary>
internal static class Codec
{
    public const byte Store = 0, Deflate = 1, Brotli = 2;

    public static string MethodName(byte m) => m switch { Store => "Almacenado", Deflate => "Deflate", Brotli => "Brotli", _ => "?" };

    public static int BrotliQuality(int level) => level switch { <= 1 => 1, 2 => 3, 3 => 5, 4 => 7, _ => 9 };

    public static int WindowBits(int dictionaryKb, int level)
    {
        if (dictionaryKb <= 0) return level <= 1 ? 18 : level <= 3 ? 22 : 24;
        int bits = (int)Math.Round(Math.Log2(dictionaryKb * 1024.0));
        return Math.Clamp(bits, 16, 24);
    }

    public static Stream CreateCompressor(byte method, Stream dest, int level, int dictionaryKb)
    {
        switch (method)
        {
            case Store: return new NonClosingStream(dest);
            case Deflate:
                return new DeflateStream(dest, new ZLibCompressionOptions { CompressionLevel = Math.Clamp(level * 2 - 1, 1, 9) }, leaveOpen: true);
            case Brotli:
                return new BrotliEncoderStream(dest, BrotliQuality(level), WindowBits(dictionaryKb, level));
            default: throw new UnsupportedFormatException("Método de compresión desconocido.");
        }
    }

    public static Stream CreateDecompressor(byte method, Stream source) => method switch
    {
        Store => source,
        Deflate => new DeflateStream(source, CompressionMode.Decompress, leaveOpen: true),
        Brotli => new BrotliStream(source, CompressionMode.Decompress, leaveOpen: true),
        _ => throw new UnsupportedFormatException("Método de compresión desconocido.")
    };
}

internal sealed class NonClosingStream : Stream
{
    private readonly Stream _inner;
    public NonClosingStream(Stream inner) => _inner = inner;
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
    public override void Flush() => _inner.Flush();
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>Brotli con control de calidad (1-11) y bits de ventana (10-24), usando BrotliEncoder.</summary>
internal sealed class BrotliEncoderStream : Stream
{
    private readonly Stream _dest;
    private BrotliEncoder _enc;
    private readonly byte[] _out = new byte[64 * 1024];
    private bool _done;

    public BrotliEncoderStream(Stream dest, int quality, int window)
    {
        _dest = dest;
        _enc = new BrotliEncoder(quality, window);
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            var status = _enc.Compress(data, _out, out int consumed, out int written, isFinalBlock: false);
            if (written > 0) _dest.Write(_out, 0, written);
            data = data[consumed..];
            if (status == System.Buffers.OperationStatus.InvalidData) throw new MedrarException("Error interno de compresión.");
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_done)
        {
            _done = true;
            System.Buffers.OperationStatus st;
            do
            {
                st = _enc.Compress(ReadOnlySpan<byte>.Empty, _out, out _, out int written, isFinalBlock: true);
                if (written > 0) _dest.Write(_out, 0, written);
            } while (st == System.Buffers.OperationStatus.DestinationTooSmall);
            _enc.Dispose();
        }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
