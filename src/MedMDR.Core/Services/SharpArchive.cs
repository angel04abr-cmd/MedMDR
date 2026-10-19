using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace MedMDR.Core;

/// <summary>Lectura de RAR, 7Z, ZIP, TAR, GZ, BZ2, XZ, LZ... con la biblioteca libre SharpCompress.</summary>
public sealed class SharpArchive : IArchive
{
    private readonly IArchive_ _archive;
    private readonly string? _password;
    private readonly Stream? _embeddedStream;     // si el archivo va dentro de un autoextraíble (solo lectura)
    private readonly List<ArchiveEntry> _entries = new();

    // alias interno para no chocar con nuestra propia interfaz IArchive
    private sealed class IArchive_
    {
        public SharpCompress.Archives.IArchive Inner = null!;
    }

    public string Path { get; }
    public string FormatName { get; private set; } = "Archivo";
    public IReadOnlyList<ArchiveEntry> Entries => _entries;
    public string? Comment => null;
    public bool IsSolid { get; private set; }
    public bool IsEncrypted { get; private set; }
    public bool HasEncryptedNames { get; private set; }
    private bool _maskedCrc;     // RAR con contenido cifrado: la suma guardada va «enmascarada» con la clave
    public bool IsLocked => ArchiveEditor.IsReadOnlyFile(Path);
    public bool HasRecovery => File.Exists(RecoveryRecord.SidecarPathFor(Path));
    public long ArchiveSize { get; }
    public bool CanModify => _embeddedStream == null && Format is ArchiveFormat.Zip or ArchiveFormat.SevenZip;
    public ArchiveFormat? Format { get; private set; }

    private SharpArchive(string path, string? password, Stream? embedded = null)
    {
        Path = path;
        _password = password;
        _embeddedStream = embedded;
        _archive = new IArchive_();
        ArchiveSize = embedded?.Length ?? new FileInfo(path).Length;
    }

    /// <summary>Abre un ZIP/7Z/... que va dentro de otro fichero (autoextraíble). Es dueño del flujo.</summary>
    internal static SharpArchive Open(Stream embedded, string displayPath, string? password, ArchiveFormat? format)
    {
        var a = new SharpArchive(displayPath, password, embedded);
        try { a.Load(format); }
        catch { a.Dispose(); throw; }
        return a;
    }

    public static SharpArchive Open(string path, string? password = null)
    {
        var a = new SharpArchive(path, password);
        try { a.Load(null); }
        catch { a.Dispose(); throw; }
        return a;
    }

    /// <summary>¿Es un problema de contraseña (falta, o no coincide)? SharpCompress usa tipos y textos distintos según el formato.</summary>
    private static bool IsPasswordProblem(Exception ex)
    {
        if (ex is CryptographicException || ex is System.Security.Cryptography.CryptographicException) return true;
        for (var e = ex; e != null; e = e.InnerException)
            if (e.GetType().Name.Contains("Cryptographic", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("password", StringComparison.OrdinalIgnoreCase) ||
                e.Message.Contains("contraseña", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    /// <summary>Con contraseña dada, un fallo de descompresión al abrir una cabecera cifrada suele ser contraseña mala.</summary>
    private static bool IsPasswordLooking(Exception ex) =>
        ex.GetType().Name.Contains("DataError") || ex is InvalidDataException || ex is InvalidFormatException || ex is EndOfStreamException || ex is IndexOutOfRangeException || ex is ArgumentException;

    /// <summary>Falta contraseña (no se dio ninguna) o es incorrecta (se dio y no sirve).</summary>
    private MedMDRException PasswordError() =>
        string.IsNullOrEmpty(_password) ? new PasswordRequiredException() : new WrongPasswordException();

    private void Load(ArchiveFormat? embeddedFormat)
    {
        try
        {
            var opts = new ReaderOptions { Password = _password, LeaveStreamOpen = _embeddedStream != null };
            _archive.Inner = _embeddedStream != null ? ArchiveFactory.OpenArchive(_embeddedStream, opts) : ArchiveFactory.OpenArchive(Path, opts);
            var inner = _archive.Inner;
            FormatName = inner.Type.ToString().ToUpperInvariant();
            Format = embeddedFormat ?? ArchiveBuilder.FormatFromName(Path);
            if (_password != null && _embeddedStream == null && Format == ArchiveFormat.SevenZip) HasEncryptedNames = ProbeEncryptedHeader();
            IsSolid = inner.IsSolid;
            _maskedCrc = inner.Type == ArchiveType.Rar;
            int i = 0;
            foreach (var e in inner.Entries)
            {
                // SharpCompress marca como «cifrados» los ficheros vacíos de un 7z; sin datos no hay nada cifrado
                bool enc = e.IsEncrypted && !e.IsDirectory && e.Size > 0;
                if (enc) IsEncrypted = true;
                _entries.Add(new ArchiveEntry
                {
                    Path = (e.Key ?? "").Replace('\\', '/').TrimStart('/'),
                    IsDirectory = e.IsDirectory,
                    Size = e.Size,
                    PackedSize = e.CompressedSize,
                    Modified = e.LastModifiedTime ?? DateTime.Now,
                    Crc = e.IsDirectory ? null : (uint?)(uint)e.Crc,
                    IsEncrypted = enc,
                    Method = FormatName,
                    Index = i++,
                    Tag = e
                });
            }
        }
        catch (MedMDRException) { throw; }
        catch (Exception ex) when (IsPasswordProblem(ex)) { throw PasswordError(); }
        catch (Exception ex) when (_password != null && IsPasswordLooking(ex)) { throw new WrongPasswordException(); }
        catch (Exception ex)
        {
            throw new UnsupportedFormatException("No se puede abrir el archivo: formato no reconocido o dañado. (" + ex.Message + ")");
        }
    }

    private bool ProbeEncryptedHeader()
    {
        try
        {
            using var a = ArchiveFactory.OpenArchive(Path, new ReaderOptions());
            _ = a.Entries.Count();
            return false;
        }
        catch { return true; }
    }

    public void ExtractEntries(IReadOnlyList<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openSink,
                               Action<ArchiveEntry, Exception?>? done, ProgressReporter progress)
    {
        var wanted = entries.Where(e => !e.IsDirectory).Select(e => e.Index).ToHashSet();
        if (wanted.Count == 0) return;
        // contenido cifrado y sin contraseña: pedirla antes de empezar (en vez de fallar a mitad)
        if (string.IsNullOrEmpty(_password) && _entries.Any(e => wanted.Contains(e.Index) && e.IsEncrypted))
            throw new PasswordRequiredException();
        foreach (var entry in _entries.Where(e => wanted.Contains(e.Index)).ToList())
        {
            var se = (IArchiveEntry)entry.Tag!;
            var sink = openSink(entry);
            if (sink == null) continue;
            Exception? err = null;
            progress.Begin(entry.Path);
            try
            {
                var crc = new CrcWriteStream(sink, progress);
                using (var src = se.OpenEntryStream()) StreamUtil.CopyTo(src, crc);
                // En RAR con contenido cifrado la suma guardada está «enmascarada» con la clave, así que no se puede
                // comparar con el CRC32 normal: ahí solo se detectan errores por fallo de descifrado/descompresión.
                if (!(IsEncrypted && _maskedCrc) && entry.Crc is uint c && c != 0 && crc.Crc != c)
                {
                    // un fichero cifrado cuya suma no cuadra casi siempre es una contraseña incorrecta
                    if (entry.IsEncrypted) throw new WrongPasswordException();
                    err = new ChecksumException(entry.Path);
                }
            }
            catch (MedMDRException) { throw; }
            catch (Exception ex) when (IsPasswordProblem(ex)) { throw PasswordError(); }
            catch (Exception ex) when (entry.IsEncrypted && ex.GetType().Name.Contains("DataError")) { throw string.IsNullOrEmpty(_password) ? new PasswordRequiredException() : new WrongPasswordException(); }
            catch (InvalidFormatException) { err = entry.IsEncrypted ? new WrongPasswordException() : new ChecksumException(entry.Path); }
            catch (InvalidDataException) { err = entry.IsEncrypted ? new WrongPasswordException() : new ChecksumException(entry.Path); }
            finally { sink.Dispose(); }
            progress.EndFile();
            done?.Invoke(entry, err);
        }
    }

    public void Dispose() { _archive.Inner?.Dispose(); _embeddedStream?.Dispose(); }
}
