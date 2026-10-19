using System.Buffers.Binary;

namespace MedMDR.Core;

/// <summary>
/// Autoextraíbles: ejecutable de extracción + archivo (de cualquier formato) pegado detrás + un pie de 32 bytes que dice
/// dónde empieza el archivo y de qué tipo es. Así el .exe sabe leer su propio contenido sin depender de la extensión.
/// </summary>
public static class SfxBuilder
{
    private static readonly byte[] TrailerMagic = { (byte)'M', (byte)'D', (byte)'R', (byte)'S', (byte)'F', (byte)'X', 0, 1 };
    public const int TrailerSize = 32;

    /// <summary>Módulo ligero (necesita .NET 10) o completo (incluye .NET; pesa bastante más).</summary>
    public static string? FindStub(bool includeRuntime = false)
    {
        string dir = AppContext.BaseDirectory;
        string name = includeRuntime ? "MedMDR.Sfx.Full.exe" : "MedMDR.Sfx.exe";
        foreach (var p in new[] { Path.Combine(dir, name), Path.Combine(dir, "sfx", name) })
            if (File.Exists(p)) return p;
        return null;
    }

    private static int FormatCode(ArchiveFormat? f) => f switch
    {
        ArchiveFormat.Mdr => 0, ArchiveFormat.Zip => 1, ArchiveFormat.Tar => 2, ArchiveFormat.TarGz => 3, ArchiveFormat.Gz => 4, ArchiveFormat.SevenZip => 5, _ => 255
    };

    internal static ArchiveFormat? FormatFromCode(int c) => c switch
    {
        0 => ArchiveFormat.Mdr, 1 => ArchiveFormat.Zip, 2 => ArchiveFormat.Tar, 3 => ArchiveFormat.TarGz, 4 => ArchiveFormat.Gz, 5 => ArchiveFormat.SevenZip, _ => null
    };

    /// <summary>Crea un autoextraíble: stub + archivo + pie. El formato se deduce del contenido del archivo.</summary>
    public static void Combine(string archivePath, string outputExe, bool includeRuntime = false, ArchiveFormat? format = null)
    {
        string stub = FindStub(includeRuntime) ?? throw new MedMDRException(includeRuntime
            ? "No se encuentra el módulo autoextraíble completo (MedMDR.Sfx.Full.exe) junto al programa."
            : "No se encuentra el módulo autoextraíble (MedMDR.Sfx.exe) junto al programa.");
        format ??= DetectFormat(archivePath);
        long start;
        long length = new FileInfo(archivePath).Length;
        string tmp = outputExe + ".tmp" + Guid.NewGuid().ToString("N")[..6];
        try
        {
            using (var o = new FileStream(tmp, FileMode.Create, FileAccess.Write))
            {
                using (var s = File.OpenRead(stub)) s.CopyTo(o);
                start = o.Position;
                using (var a = File.OpenRead(archivePath)) a.CopyTo(o);
                var t = new byte[TrailerSize];
                BinaryPrimitives.WriteInt64LittleEndian(t, start);
                BinaryPrimitives.WriteInt64LittleEndian(t.AsSpan(8), length);
                BinaryPrimitives.WriteInt32LittleEndian(t.AsSpan(16), FormatCode(format));
                TrailerMagic.CopyTo(t, 24);
                o.Write(t);
            }
            File.Move(tmp, outputExe, overwrite: true);
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    private static ArchiveFormat? DetectFormat(string path)
    {
        using var fs = File.OpenRead(path);
        if (MdrFooter.Locate(fs) != null) return ArchiveFormat.Mdr;
        var head = new byte[6];
        fs.Seek(0, SeekOrigin.Begin);
        int n = fs.Read(head, 0, 6);
        if (n >= 4 && head[0] == 'P' && head[1] == 'K') return ArchiveFormat.Zip;
        if (n >= 6 && head[0] == '7' && head[1] == 'z' && head[2] == 0xBC && head[3] == 0xAF) return ArchiveFormat.SevenZip;
        if (n >= 2 && head[0] == 0x1F && head[1] == 0x8B)
            return ArchiveBuilder.FormatFromName(path) == ArchiveFormat.Gz ? ArchiveFormat.Gz : ArchiveFormat.TarGz;
        return ArchiveBuilder.FormatFromName(path);
    }

    /// <summary>Lee el pie de un autoextraíble. Null si el fichero no lo tiene.</summary>
    public static (long Start, long Length, ArchiveFormat? Format)? ReadTrailer(Stream raw)
    {
        if (raw.Length < TrailerSize + 1024) return null;
        var t = new byte[TrailerSize];
        raw.Seek(-TrailerSize, SeekOrigin.End);
        raw.ReadExactly(t);
        if (!t.AsSpan(24, 8).SequenceEqual(TrailerMagic)) return null;
        long start = BinaryPrimitives.ReadInt64LittleEndian(t);
        long length = BinaryPrimitives.ReadInt64LittleEndian(t.AsSpan(8));
        if (start < 0 || length < 0 || start + length + TrailerSize != raw.Length) return null;
        return (start, length, FormatFromCode(BinaryPrimitives.ReadInt32LittleEndian(t.AsSpan(16))));
    }

    public static bool IsSfx(string path)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return ReadTrailer(fs) != null || MdrFooter.Locate(fs) is { BaseOffset: > 0 };
        }
        catch { return false; }
    }

    /// <summary>Convierte un archivo existente (cualquier formato que MedMDR cree) en autoextraíble.</summary>
    public static string ConvertToSfx(string archivePath, string? outputExe = null, bool includeRuntime = false)
    {
        if (IsSfx(archivePath)) throw new UnsupportedFormatException("Ese fichero ya es un autoextraíble.");
        var fmt = DetectFormat(archivePath);
        if (fmt == null) throw new UnsupportedFormatException("Solo se pueden convertir en autoextraíbles los archivos .mdr, ZIP, 7Z, TAR, TAR.GZ y GZ.");
        outputExe ??= ArchiveBuilder.WithoutArchiveExtension(archivePath) + ".exe";
        if (string.Equals(Path.GetFullPath(outputExe), Path.GetFullPath(archivePath), StringComparison.OrdinalIgnoreCase))
            throw new UnsupportedFormatException("El autoextraíble tiene que tener otro nombre que el archivo original.");
        Combine(archivePath, outputExe, includeRuntime, fmt);
        return outputExe;
    }

    /// <summary>Quita el módulo autoextraíble y guarda el archivo original junto al .exe, con la extensión que le corresponde.</summary>
    public static string Unpack(string exePath)
    {
        ArchiveFormat? fmt = null;
        using (var raw = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (ReadTrailer(raw) is { } t) fmt = t.Format;
            else if (MdrFooter.Locate(raw) != null) fmt = ArchiveFormat.Mdr;
        }
        if (fmt == null) throw new UnsupportedFormatException("El fichero no es un autoextraíble de MedMDR.");
        string stem = ArchiveBuilder.WithoutArchiveExtension(Path.GetFullPath(exePath));
        string dest = stem + ArchiveBuilder.Extension(fmt.Value);
        if (File.Exists(dest)) dest = Extractor.UniqueName(dest);
        return ExtractArchivePart(exePath, dest);
    }

    /// <summary>Quita el módulo autoextraíble y deja solo el archivo original.</summary>
    public static string ExtractArchivePart(string exePath, string outputArchive)
    {
        using var raw = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        long start, length;
        if (ReadTrailer(raw) is { } t) { start = t.Start; length = t.Length; }
        else if (MdrFooter.Locate(raw) is { } loc) { start = loc.BaseOffset; length = loc.Footer.ArchiveLength; }
        else throw new UnsupportedFormatException("El fichero no contiene un archivo.");
        raw.Seek(start, SeekOrigin.Begin);
        using var o = new FileStream(outputArchive, FileMode.Create, FileAccess.Write);
        StreamUtil.CopyExactly(raw, o, length);
        return outputArchive;
    }
}
