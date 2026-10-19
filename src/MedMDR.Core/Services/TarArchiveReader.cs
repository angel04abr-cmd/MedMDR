using System.Formats.Tar;
using System.IO.Compression;

namespace MedMDR.Core;

/// <summary>Lectura de .tar, .tar.gz y .tgz con las clases nativas de .NET.</summary>
public sealed class TarArchiveReader : IArchive
{
    private readonly bool _gzip;
    private readonly List<ArchiveEntry> _entries = new();
    private readonly Stream? _embeddedStream;     // si va dentro de un autoextraíble

    public string Path { get; }
    public string FormatName => _gzip ? "TAR.GZ" : "TAR";
    public IReadOnlyList<ArchiveEntry> Entries => _entries;
    public string? Comment => null;
    public bool IsSolid => _gzip;
    public bool IsEncrypted => false;
    public bool HasEncryptedNames => false;
    public bool IsLocked => ArchiveEditor.IsReadOnlyFile(Path);
    public bool HasRecovery => File.Exists(RecoveryRecord.SidecarPathFor(Path));
    public long ArchiveSize { get; }
    public bool CanModify => _embeddedStream == null;

    public static bool Handles(string path)
    {
        string n = path.ToLowerInvariant();
        return n.EndsWith(".tar") || n.EndsWith(".tar.gz") || n.EndsWith(".tgz");
    }

    private TarArchiveReader(string path, Stream? embedded = null, bool gzip = false)
    {
        Path = path;
        _embeddedStream = embedded;
        if (embedded != null) { _gzip = gzip; ArchiveSize = embedded.Length; return; }
        string n = path.ToLowerInvariant();
        _gzip = n.EndsWith(".tar.gz") || n.EndsWith(".tgz");
        ArchiveSize = new FileInfo(path).Length;
    }

    /// <summary>Abre un TAR/TAR.GZ que va dentro de otro fichero (autoextraíble). Es dueño del flujo.</summary>
    internal static TarArchiveReader OpenStream(Stream embedded, string displayPath, bool gzip)
    {
        var a = new TarArchiveReader(displayPath, embedded, gzip);
        a.Load();
        return a;
    }

    public static TarArchiveReader Open(string path)
    {
        var a = new TarArchiveReader(path);
        a.Load();
        return a;
    }

    private Stream OpenData()
    {
        Stream fs;
        if (_embeddedStream != null) { _embeddedStream.Seek(0, SeekOrigin.Begin); fs = new NonDisposingSink2(_embeddedStream); }
        else fs = new FileStream(Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024);
        return _gzip ? new GZipStream(fs, CompressionMode.Decompress) : fs;
    }

    /// <summary>Lectura de un flujo que no se cierra al terminar.</summary>
    private sealed class NonDisposingSink2 : Stream
    {
        private readonly Stream _i;
        public NonDisposingSink2(Stream i) => _i = i;
        public override int Read(byte[] buffer, int offset, int count) => _i.Read(buffer, offset, count);
        public override int Read(Span<byte> buffer) => _i.Read(buffer);
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private void Load()
    {
        try
        {
            using var s = OpenData();
            using var r = new TarReader(s);
            int i = 0;
            TarEntry? e;
            while ((e = r.GetNextEntry(copyData: false)) != null)
            {
                if (e.EntryType is TarEntryType.GlobalExtendedAttributes) continue;
                bool isDir = e.EntryType == TarEntryType.Directory;
                if (!isDir && e.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile)) continue;
                _entries.Add(new ArchiveEntry
                {
                    Path = e.Name.Replace('\\', '/').TrimStart('/').TrimEnd('/'),
                    IsDirectory = isDir,
                    Size = isDir ? 0 : e.Length,
                    PackedSize = isDir ? 0 : e.Length,
                    Modified = e.ModificationTime.LocalDateTime,
                    Method = _gzip ? "Gzip" : "Almacenado",
                    Index = i++
                });
            }
        }
        catch (InvalidDataException ex) { throw new UnsupportedFormatException("El archivo está dañado o no es un TAR válido. (" + ex.Message + ")"); }
        catch (IOException ex) when (ex is not FileNotFoundException) { throw new UnsupportedFormatException("El archivo está dañado o incompleto. (" + ex.Message + ")"); }
    }

    public void ExtractEntries(IReadOnlyList<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openSink,
                               Action<ArchiveEntry, Exception?>? done, ProgressReporter progress)
    {
        var wanted = entries.Where(e => !e.IsDirectory).Select(e => e.Index).ToHashSet();
        if (wanted.Count == 0) return;
        var byIndex = _entries.ToDictionary(e => e.Index);
        using var s = OpenData();
        using var r = new TarReader(s);
        int i = 0;
        TarEntry? te;
        while ((te = r.GetNextEntry(copyData: false)) != null)
        {
            if (te.EntryType is TarEntryType.GlobalExtendedAttributes) continue;
            bool isDir = te.EntryType == TarEntryType.Directory;
            if (!isDir && te.EntryType is not (TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile)) continue;
            int idx = i++;
            if (!wanted.Remove(idx)) continue;
            var entry = byIndex[idx];
            var sink = openSink(entry);
            if (sink != null)
            {
                progress.Begin(entry.Path);
                try
                {
                    if (te.DataStream != null) StreamUtil.CopyTo(te.DataStream, sink, progress);
                }
                finally { sink.Dispose(); }
                progress.EndFile();
                done?.Invoke(entry, null);
            }
            if (wanted.Count == 0) break;
        }
    }

    public void Dispose() => _embeddedStream?.Dispose();
}
