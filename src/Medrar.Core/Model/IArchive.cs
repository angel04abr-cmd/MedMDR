namespace Medrar.Core;

/// <summary>Un archivo comprimido abierto para leer (cualquier formato soportado).</summary>
public interface IArchive : IDisposable
{
    string Path { get; }
    string FormatName { get; }
    IReadOnlyList<ArchiveEntry> Entries { get; }
    string? Comment { get; }
    bool IsSolid { get; }
    bool IsEncrypted { get; }
    bool HasEncryptedNames { get; }
    bool IsLocked { get; }
    bool HasRecovery { get; }
    long ArchiveSize { get; }
    /// <summary>true si Medrar puede reescribirlo (añadir, eliminar, renombrar...).</summary>
    bool CanModify { get; }

    /// <summary>
    /// Extrae las entradas indicadas (solo ficheros). Para cada una pide un destino con <paramref name="openSink"/>
    /// (null = omitir), verifica la suma de control y llama a <paramref name="done"/>.
    /// Una entrada dañada se notifica con el error y se continúa con la siguiente.
    /// </summary>
    void ExtractEntries(IReadOnlyList<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openSink,
                        Action<ArchiveEntry, Exception?>? done, ProgressReporter progress);
}

public static class ArchiveExtensions
{
    public static IEnumerable<ArchiveEntry> Files(this IArchive a) => a.Entries.Where(e => !e.IsDirectory);

    public static void ExtractToStream(this IArchive a, ArchiveEntry entry, Stream dest)
    {
        Exception? error = null;
        a.ExtractEntries(new[] { entry }, _ => new NonDisposingSink(dest), (_, ex) => error = ex, ProgressReporter.None);
        if (error != null) throw error;
    }

    /// <summary>Lee un fichero del archivo en memoria (para el visor).</summary>
    public static byte[] ReadAll(this IArchive a, ArchiveEntry entry, long maxBytes = long.MaxValue)
    {
        using var ms = new MemoryStream();
        Stream sink = maxBytes == long.MaxValue ? ms : new LimitSink(ms, maxBytes);
        try { a.ExtractToStream(entry, sink); }
        catch (LimitReached) { }
        return ms.ToArray();
    }

    private sealed class LimitReached : Exception { }

    private sealed class LimitSink : Stream
    {
        private readonly Stream _ms; private readonly long _max;
        public LimitSink(Stream ms, long max) { _ms = ms; _max = max; }
        public override void Write(byte[] buffer, int offset, int count)
        {
            long room = _max - _ms.Length;
            if (room <= 0) throw new LimitReached();
            _ms.Write(buffer, offset, (int)Math.Min(room, count));
            if (_ms.Length >= _max) throw new LimitReached();
        }
        public override void Flush() { }
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => _ms.Length; public override long Position { get => _ms.Length; set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}

/// <summary>Envuelve un flujo para que el motor no lo cierre al terminar una entrada.</summary>
internal sealed class NonDisposingSink : Stream
{
    private readonly Stream _inner;
    public NonDisposingSink(Stream inner) => _inner = inner;
    public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);
    public override void Write(ReadOnlySpan<byte> buffer) => _inner.Write(buffer);
    public override void Flush() => _inner.Flush();
    public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}
