using System.IO.Compression;
using SharpCompress.Compressors.LZMA;

namespace MedMDR.Core;

/// <summary>LZMA como método de compresión de .mdr (id 3): 5 bytes de propiedades + flujo con marca de fin.</summary>
internal sealed class LzmaEncodeStream : Stream
{
    private readonly LzmaStream _enc;

    public LzmaEncodeStream(Stream dest, int dictionaryBytes)
    {
        _enc = LzmaStream.Create(new LzmaEncoderProperties(true, dictionaryBytes), false, new NonClosingStream(dest));
        dest.Write(_enc.Properties, 0, _enc.Properties.Length);
    }

    public override void Write(byte[] buffer, int offset, int count) => _enc.Write(buffer, offset, count);
    public override void Flush() { }
    protected override void Dispose(bool disposing) { if (disposing) _enc.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

internal sealed class LzmaDecodeStream : Stream
{
    private readonly LzmaStream _dec;

    public LzmaDecodeStream(Stream source)
    {
        var props = new byte[5];
        source.ReadExactly(props);
        _dec = LzmaStream.Create(props, source, leaveOpen: true);   // sin tamaños: el flujo termina con su marca de fin
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        try { return _dec.Read(buffer, offset, count); }
        catch (Exception ex) when (ex is not OperationCanceledException and not IOException)
        {
            // Datos dañados: el decodificador puede fallar de formas muy distintas; se unifican como «datos no válidos».
            throw new InvalidDataException("Los datos LZMA están dañados.", ex);
        }
    }
    public override void Flush() { }
    protected override void Dispose(bool disposing) { if (disposing) _dec.Dispose(); base.Dispose(disposing); }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>
/// Nivel «Ultra» de .mdr: elige entre Brotli (calidad 11) y LZMA según lo que mejor comprima los datos.
/// Se prueban los dos sobre una muestra (los ficheros pequeños se prueban enteros) y se usa el ganador para todo el bloque.
/// En medidas con texto gana Brotli y con programas gana LZMA.
/// </summary>
internal static class Ultra
{
    public const int SolidSampleBytes = 1 << 20;
    public const int FileSampleBytes = 512 << 10;
    private const int MaxSampledFiles = 16;

    public static byte Choose(ReadOnlySpan<byte> sample, UltraMethod forced)
    {
        if (forced == UltraMethod.Brotli) return Codec.Brotli;
        if (forced == UltraMethod.Lzma) return Codec.Lzma;
        if (sample.Length < 256) return Codec.Brotli;
        return LzmaSize(sample) < BrotliSize(sample) ? Codec.Lzma : Codec.Brotli;
    }

    private static int BrotliSize(ReadOnlySpan<byte> data)
    {
        var dst = new byte[BrotliEncoder.GetMaxCompressedLength(data.Length)];
        return BrotliEncoder.TryCompress(data, dst, out int written, 11, 22) ? written : int.MaxValue;
    }

    private static int LzmaSize(ReadOnlySpan<byte> data)
    {
        using var ms = new MemoryStream();
        using (var lz = new LzmaEncodeStream(ms, Math.Clamp(NextPow2(data.Length), 1 << 16, 4 << 20))) lz.Write(data);
        return (int)ms.Length;
    }

    internal static int NextPow2(long n)
    {
        int p = 1 << 16;
        while (p < n && p < (1 << 30)) p <<= 1;
        return p;
    }

    /// <summary>Muestra de un fichero: entero si es pequeño; si no, un trozo del medio (o del principio si no se puede saltar).</summary>
    public static byte[] SampleOfFile(SourceItem it, int max)
    {
        using var s = it.Open!();
        long size = it.Size;
        if (size > max && s.CanSeek) s.Seek((size - max) / 2, SeekOrigin.Begin);
        var buf = new byte[(int)Math.Min(size, max)];
        int got = 0;
        while (got < buf.Length) { int n = s.Read(buf, got, buf.Length - got); if (n <= 0) break; got += n; }
        return got == buf.Length ? buf : buf[..got];
    }

    /// <summary>Muestra de un conjunto de ficheros (modo sólido): un trozo de cada uno de hasta 16 ficheros repartidos por la lista.</summary>
    public static byte[] SampleOfSet(IReadOnlyList<SourceItem> items, int max, Func<SourceItem, bool> skip)
    {
        var files = items.Where(i => !i.IsDirectory && i.Size > 0 && !skip(i)).ToList();
        if (files.Count == 0) return Array.Empty<byte>();
        int take = Math.Min(files.Count, MaxSampledFiles);
        int each = Math.Max(1, max / take);
        using var ms = new MemoryStream();
        for (int k = 0; k < take; k++)
        {
            var f = files[(int)((long)k * files.Count / take)];
            try { ms.Write(SampleOfFile(f, each)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
        return ms.ToArray();
    }
}
