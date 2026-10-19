using System.Formats.Tar;
using System.IO.Compression;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;

namespace MedMDR.Core;

/// <summary>Crea archivos en cualquiera de los formatos soportados a partir de una lista de <see cref="SourceItem"/>.</summary>
public static class ArchiveBuilder
{
    /// <summary>Extensión recomendada para cada formato.</summary>
    public static string Extension(ArchiveFormat f) => f switch
    {
        ArchiveFormat.Mdr => ".mdr",
        ArchiveFormat.Zip => ".zip",
        ArchiveFormat.Tar => ".tar",
        ArchiveFormat.TarGz => ".tar.gz",
        ArchiveFormat.Gz => ".gz",
        ArchiveFormat.SevenZip => ".7z",
        _ => ".mdr"
    };

    public static ArchiveFormat? FormatFromName(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.EndsWith(".tar.gz") || n.EndsWith(".tgz")) return ArchiveFormat.TarGz;
        if (n.EndsWith(".mdr")) return ArchiveFormat.Mdr;
        if (n.EndsWith(".zip")) return ArchiveFormat.Zip;
        if (n.EndsWith(".7z")) return ArchiveFormat.SevenZip;
        if (n.EndsWith(".tar")) return ArchiveFormat.Tar;
        if (n.EndsWith(".gz")) return ArchiveFormat.Gz;
        return null;
    }

    /// <summary>Qué opciones admite cada formato (la interfaz lo usa para activar o desactivar casillas).</summary>
    public static (bool Password, bool Solid, bool EncryptNames, bool Comment) Capabilities(ArchiveFormat f) => f switch
    {
        ArchiveFormat.Mdr => (true, true, true, true),
        ArchiveFormat.Zip => (true, false, false, false),
        ArchiveFormat.SevenZip => (true, false, true, false),
        _ => (false, false, false, false),
    };

    /// <summary>
    /// Crea el archivo en <paramref name="outputPath"/>. Si hay volúmenes devuelve el primero (.001).
    /// El archivo se escribe primero en un temporal y solo se coloca en su sitio si todo fue bien.
    /// </summary>
    public static string Create(string outputPath, IReadOnlyList<SourceItem> items, ArchiveOptions options, ProgressReporter? progress = null)
    {
        progress ??= ProgressReporter.None;
        var o = options.Clone();
        Validate(o, items);

        string dir = Path.GetDirectoryName(Path.GetFullPath(outputPath)) ?? ".";
        Directory.CreateDirectory(dir);
        string tmp = Path.Combine(dir, "." + Path.GetFileName(outputPath) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 128 * 1024))
            {
                switch (o.Format)
                {
                    case ArchiveFormat.Mdr: MdrWriter.Write(fs, items, o, progress); break;
                    case ArchiveFormat.Zip:
                        if (!string.IsNullOrEmpty(o.Password)) ZipAesWriter.Write(fs, items, o, progress);
                        else WriteZip(fs, items, o, progress);
                        break;
                    case ArchiveFormat.SevenZip:
                        if (!string.IsNullOrEmpty(o.Password)) SevenZipAesWriter.Write(fs, items, o, progress);
                        else WriteSevenZip(fs, items, o, progress);
                        break;
                    case ArchiveFormat.Tar: WriteTar(fs, items, progress); break;
                    case ArchiveFormat.TarGz:
                        using (var gz = new GZipStream(fs, ZLibLevel(o), leaveOpen: true)) WriteTar(gz, items, progress);
                        break;
                    case ArchiveFormat.Gz:
                        WriteGz(fs, items, o, progress);
                        break;
                }
            }

            string final = outputPath;
            if (o.SelfExtracting)
            {
                final = WithoutArchiveExtension(outputPath) + ".exe";
                SfxBuilder.Combine(tmp, final, o.SfxIncludeRuntime);
                File.Delete(tmp);
            }
            else
            {
                File.Move(tmp, final, overwrite: true);
            }

            if (o.VolumeSizeBytes > 0 && !o.SelfExtracting)
            {
                var parts = VolumeSet.Split(final, o.VolumeSizeBytes, progress);
                if (o.Lock) foreach (var p in parts) LockFile(p);
                return parts[0];
            }
            // el bloqueo de los formatos que no son .mdr es la marca de «solo lectura» del fichero
            if (o.Lock && (o.Format != ArchiveFormat.Mdr || o.SelfExtracting)) LockFile(final);
            // recuperación para formatos distintos de .mdr: fichero aparte (.mdrrec)
            if (o.RecoveryPercent > 0 && (o.Format != ArchiveFormat.Mdr || o.SelfExtracting)) RecoveryRecord.CreateSidecar(final, o.RecoveryPercent, progress);
            return final;
        }
        catch
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            throw;
        }
    }

    /// <summary>Quita la extensión de archivo (también «.tar.gz» entera).</summary>
    public static string WithoutArchiveExtension(string path)
    {
        string n = path.ToLowerInvariant();
        if (n.EndsWith(".tar.gz")) return path[..^7];
        return Path.Combine(Path.GetDirectoryName(path) ?? "", Path.GetFileNameWithoutExtension(path));
    }

    /// <summary>Marca el fichero como de solo lectura (así MedMDR lo trata como «bloqueado»).</summary>
    public static void LockFile(string path)
    {
        var a = File.GetAttributes(path);
        File.SetAttributes(path, a | FileAttributes.ReadOnly);
    }

    public static void UnlockFile(string path)
    {
        var a = File.GetAttributes(path);
        File.SetAttributes(path, a & ~FileAttributes.ReadOnly);
    }

    private static void Validate(ArchiveOptions o, IReadOnlyList<SourceItem> items)
    {
        var cap = Capabilities(o.Format);
        if (!string.IsNullOrEmpty(o.Password) && !cap.Password)
            throw new UnsupportedFormatException("La contraseña solo está disponible en los formatos .mdr, ZIP y 7Z.");
        if (o.EncryptNames && !cap.EncryptNames && !string.IsNullOrEmpty(o.Password))
            o.EncryptNames = false;                                   // ZIP no puede ocultar los nombres: se cifra solo el contenido
        if (o.Format == ArchiveFormat.Gz && items.Count(i => !i.IsDirectory) != 1)
            throw new UnsupportedFormatException("El formato .gz solo guarda un fichero. Usa .tar.gz para varios.");
        if (o.SelfExtracting && o.VolumeSizeBytes > 0)
            throw new UnsupportedFormatException("Un archivo autoextraíble no se puede dividir en volúmenes.");
    }

    private static CompressionLevel ZLibLevel(ArchiveOptions o) => o.Level switch
    {
        <= 0 => CompressionLevel.NoCompression,
        1 => CompressionLevel.Fastest,
        >= 5 => CompressionLevel.SmallestSize,
        _ => CompressionLevel.Optimal
    };

    private static void WriteZip(Stream output, IReadOnlyList<SourceItem> items, ArchiveOptions o, ProgressReporter progress)
    {
        progress.Total = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        progress.FilesTotal = items.Count(i => !i.IsDirectory);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true, System.Text.Encoding.UTF8);
        foreach (var it in items)
        {
            string name = it.EntryPath.Replace('\\', '/').Trim('/');
            if (it.IsDirectory)
            {
                var d = zip.CreateEntry(name + "/");
                d.LastWriteTime = Clamp(it.Modified);
                continue;
            }
            progress.Begin(name);
            var level = Glob.MatchesAny(name, o.StorePatterns) ? CompressionLevel.NoCompression : ZLibLevel(o);
            var entry = zip.CreateEntry(name, level);
            entry.LastWriteTime = Clamp(it.Modified);
            using (var es = entry.Open())
            using (var src = it.Open!()) StreamUtil.CopyTo(src, es, progress);
            progress.EndFile();
        }
    }

    private static void WriteSevenZip(Stream output, IReadOnlyList<SourceItem> items, ArchiveOptions o, ProgressReporter progress)
    {
        progress.Total = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        progress.FilesTotal = items.Count(i => !i.IsDirectory);
        // el escritor de 7z siempre usa LZMA: el nivel «almacenar» se traduce en el más rápido
        var opts = new SevenZipWriterOptions(CompressionType.LZMA)
        {
            LeaveStreamOpen = true,
            CompressHeader = true,
            CompressionLevel = Math.Clamp(o.Level <= 0 ? 1 : o.Level * 2 - 1, 1, 9),
        };
        using var w = new SevenZipWriter(output, opts);
        foreach (var it in items)
        {
            progress.Token.ThrowIfCancellationRequested();
            string name = it.EntryPath.Replace('\\', '/').Trim('/');
            if (it.IsDirectory) { w.WriteDirectory(name, it.Modified); continue; }
            progress.Begin(name);
            using var src = it.Open!();
            using var counted = new ProgressReadStream(src, progress);
            w.Write(name, counted, it.Modified);
            progress.EndFile();
        }
    }

    private static DateTimeOffset Clamp(DateTime t)
    {
        var min = new DateTime(1980, 1, 1);
        return t < min ? min : t;
    }

    private static void WriteTar(Stream output, IReadOnlyList<SourceItem> items, ProgressReporter progress)
    {
        progress.Total = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        progress.FilesTotal = items.Count(i => !i.IsDirectory);
        using var tar = new TarWriter(output, TarEntryFormat.Pax, leaveOpen: true);
        foreach (var it in items)
        {
            string name = it.EntryPath.Replace('\\', '/').Trim('/');
            if (it.IsDirectory)
            {
                var d = new PaxTarEntry(TarEntryType.Directory, name + "/") { ModificationTime = it.Modified.ToUniversalTime() };
                tar.WriteEntry(d);
                continue;
            }
            progress.Begin(name);
            using var src = it.Open!();
            var e = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                ModificationTime = it.Modified.ToUniversalTime(),
                DataStream = new ProgressReadStream(src, progress)
            };
            tar.WriteEntry(e);
            progress.EndFile();
        }
    }

    private static void WriteGz(Stream output, IReadOnlyList<SourceItem> items, ArchiveOptions o, ProgressReporter progress)
    {
        var it = items.First(i => !i.IsDirectory);
        progress.Total = it.Size;
        progress.FilesTotal = 1;
        progress.Begin(it.EntryPath);
        using (var gz = new GZipStream(output, ZLibLevel(o), leaveOpen: true))
        using (var src = it.Open!()) StreamUtil.CopyTo(src, gz, progress);
        progress.EndFile();
    }

    private sealed class ProgressReadStream : Stream
    {
        private readonly Stream _inner; private readonly ProgressReporter _p;
        public ProgressReadStream(Stream inner, ProgressReporter p) { _inner = inner; _p = p; }
        public override int Read(byte[] buffer, int offset, int count) { int n = _inner.Read(buffer, offset, count); if (n > 0) _p.Add(n); return n; }
        public override int Read(Span<byte> buffer) { int n = _inner.Read(buffer); if (n > 0) _p.Add(n); return n; }
        public override bool CanRead => true; public override bool CanSeek => _inner.CanSeek; public override bool CanWrite => false;
        public override long Length => _inner.Length; public override long Position { get => _inner.Position; set => _inner.Position = value; }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
