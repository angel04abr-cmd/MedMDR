namespace MedMDR.Core;

public static class ArchiveOpener
{
    /// <summary>
    /// Abre cualquier archivo soportado. Si está cifrado y falta la contraseña lanza
    /// <see cref="PasswordRequiredException"/>; si es incorrecta, <see cref="WrongPasswordException"/>.
    /// </summary>
    public static IArchive Open(string path, string? password = null)
    {
        if (!File.Exists(path) && VolumeSet.FindParts(path) == null)
            throw new FileNotFoundException("No se encuentra el archivo.", path);

        var embedded = OpenEmbedded(path, password);
        if (embedded != null) return embedded;

        if (MdrArchive.LooksLikeMdr(path))
            return MdrArchive.Open(path, password);

        if (TarArchiveReader.Handles(path))
            return TarArchiveReader.Open(path);

        // ZIP con cifrado AES: lector propio que comprueba la firma de cada fichero
        if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            try
            {
                var z = ZipAesArchive.TryOpen(fs, path, password, ownsStream: true, embedded: false);
                if (z != null) return z;
            }
            catch { }
            fs.Dispose();
        }

        return SharpArchive.Open(path, password);
    }

    /// <summary>Autoextraíble (.exe con un archivo pegado detrás y su pie): se abre solo lectura.</summary>
    private static IArchive? OpenEmbedded(string path, string? password)
    {
        if (VolumeSet.IsVolumeName(path)) return null;
        FileStream raw;
        try { raw = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024); }
        catch { return null; }
        try
        {
            var t = SfxBuilder.ReadTrailer(raw);
            if (t == null) { raw.Dispose(); return null; }
            var sub = new SubStream(raw, t.Value.Start, t.Value.Length, ownsInner: true);
            switch (t.Value.Format)
            {
                case ArchiveFormat.Mdr: return MdrArchive.Open(sub, path, password, embedded: true);
                case ArchiveFormat.Tar: return TarArchiveReader.OpenStream(sub, path, gzip: false);
                case ArchiveFormat.TarGz: return TarArchiveReader.OpenStream(sub, path, gzip: true);
                case ArchiveFormat.Zip:
                    {
                        var z = ZipAesArchive.TryOpen(sub, path, password, ownsStream: true, embedded: true);
                        if (z != null) return z;
                        return SharpArchive.Open(sub, path, password, t.Value.Format);
                    }
                default: return SharpArchive.Open(sub, path, password, t.Value.Format);
            }
        }
        catch { raw.Dispose(); throw; }
    }

    public static readonly string[] SupportedExtensions =
    {
        ".mdr", ".zip", ".rar", ".7z", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".lz", ".zst", ".cab", ".iso", ".jar", ".001"
    };

    public static bool LooksLikeArchive(string path)
    {
        string ext = Path.GetExtension(path).ToLowerInvariant();
        if (SupportedExtensions.Contains(ext)) return true;
        if (ext == ".exe") return SfxBuilder.IsSfx(path);
        return false;
    }
}
