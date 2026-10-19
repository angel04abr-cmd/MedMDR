using System.IO.Compression;
using System.Text.Json;

namespace MedMDR.Core;

/// <summary>Lectura de archivos .mdr (también cuando van detrás de un .exe autoextraíble o repartidos en volúmenes).</summary>
public sealed class MdrArchive : IArchive
{
    private readonly Stream _raw;          // fichero completo (o volúmenes unidos)
    private readonly Stream _arc;          // solo la parte del archivo (sin el .exe delante)
    private readonly MdrHeader _header;
    private readonly MdrFooter _footer;
    private readonly long _payloadStart;
    private byte[]? _key;
    private List<ArchiveEntry> _entries = new();
    private MdrDirectory? _dir;
    private readonly bool _embedded;       // dentro de un autoextraíble: solo lectura

    public string Path { get; }
    public string FormatName => "MedMDR (.mdr)";
    public IReadOnlyList<ArchiveEntry> Entries => _entries;
    public string? Comment => _dir?.Comment;
    public bool IsSolid => _header.Solid;
    public bool IsEncrypted => _header.Encrypted;
    public bool HasEncryptedNames => _header.EncryptedNames;
    public bool IsLocked => _header.Locked || ArchiveEditor.IsReadOnlyFile(Path);
    /// <summary>La marca de bloqueo guardada dentro del archivo (sin contar el atributo de solo lectura del fichero).</summary>
    public bool HeaderLocked => _header.Locked;
    public bool HasRecovery => _footer.RecLength > 0;
    public long ArchiveSize { get; }
    public bool CanModify => !_embedded;
    public DateTime Created => new DateTime(_header.Created, DateTimeKind.Utc).ToLocalTime();
    public string MethodName => Codec.MethodName((byte)_header.Method);
    /// <summary>Porcentaje aproximado del registro de recuperación (0 si no hay).</summary>
    public int RecoveryPercent
    {
        get
        {
            if (_footer.RecLength <= 0) return 0;
            var b = new byte[4];
            _arc.Seek(_footer.RecOffset + 16, SeekOrigin.Begin);     // porcentaje guardado en la cabecera del registro
            _arc.ReadExactly(b);
            return (int)Math.Clamp(BitConverter.ToUInt32(b), 0u, 100u);
        }
    }
    /// <summary>true si todavía no se ha podido leer el directorio (faltaba la contraseña).</summary>
    public bool NeedsPassword => _dir == null;

    private MdrArchive(string path, Stream raw, Stream arc, MdrHeader header, MdrFooter footer, long payloadStart, long size, bool embedded)
    {
        Path = path; _raw = raw; _arc = arc; _header = header; _footer = footer; _payloadStart = payloadStart; ArchiveSize = size; _embedded = embedded;
    }

    public static bool LooksLikeMdr(string path)
    {
        try
        {
            using var s = OpenRaw(path);
            return MdrFooter.Locate(s) != null;
        }
        catch { return false; }
    }

    internal static Stream OpenRaw(string path)
    {
        var vols = VolumeSet.FindParts(path);
        if (vols != null) return new JoinedStream(vols);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
    }

    /// <summary>Abre el archivo. Si está cifrado y no se da contraseña, lanza <see cref="PasswordRequiredException"/>
    /// (cuando los nombres están cifrados) o abre con el listado visible pero sin poder extraer.</summary>
    public static MdrArchive Open(string path, string? password = null) => Open(OpenRaw(path), path, password, embedded: false);

    /// <summary>Abre desde un flujo (el archivo es dueño del flujo y lo cierra al terminar). <paramref name="embedded"/> = va dentro de un autoextraíble.</summary>
    internal static MdrArchive Open(Stream raw, string path, string? password, bool embedded)
    {
        try
        {
            var loc = MdrFooter.Locate(raw) ?? throw new UnsupportedFormatException("No es un archivo .mdr válido (o está dañado o truncado).");
            var (footer, baseOffset) = loc;
            var arc = new SubStream(raw, baseOffset, footer.ArchiveLength);

            // cabecera
            var magic = new byte[8];
            arc.Seek(0, SeekOrigin.Begin);
            arc.ReadExactly(magic);
            if (!magic.AsSpan().SequenceEqual(MdrFormat.Magic)) throw new UnsupportedFormatException("Cabecera .mdr incorrecta.");
            var l4 = new byte[4];
            arc.ReadExactly(l4);
            int hlen = (int)BitConverter.ToUInt32(l4);
            if (hlen <= 0 || hlen > 1_000_000) throw new UnsupportedFormatException("Cabecera .mdr dañada.");
            var hjson = new byte[hlen];
            arc.ReadExactly(hjson);
            arc.ReadExactly(l4);
            if (BitConverter.ToUInt32(l4) != StreamUtil.Crc(hjson)) throw new MedMDRException("La cabecera del archivo está dañada.");
            var header = JsonSerializer.Deserialize<MdrHeader>(hjson, MdrFormat.Json) ?? throw new MedMDRException("Cabecera ilegible.");
            if (header.V > 1) throw new UnsupportedFormatException("Este archivo lo creó una versión más nueva de MedMDR.");

            var a = new MdrArchive(path, raw, arc, header, footer, 8 + 4 + hlen + 4, footer.ArchiveLength, embedded || baseOffset > 0);
            a.Unlock(password);
            return a;
        }
        catch
        {
            raw.Dispose();
            throw;
        }
    }

    /// <summary>Aplica la contraseña (si hace falta) y lee el directorio.</summary>
    public void Unlock(string? password)
    {
        if (_header.Encrypted)
        {
            if (string.IsNullOrEmpty(password))
            {
                if (_header.EncryptedNames) throw new PasswordRequiredException();
            }
            else
            {
                var salt = Convert.FromBase64String(_header.Salt!);
                var (key, check) = MdrCrypto.Derive(password, salt, _header.Iter);
                if (!MdrCrypto.CheckMatches(check, Convert.FromBase64String(_header.Check!))) throw new WrongPasswordException();
                _key = key;
            }
        }
        LoadDirectory();
    }

    private void LoadDirectory()
    {
        Stream src;
        if (_header.EncryptedNames)
        {
            long phys = _footer.DirLength;
            long chunks = (phys + MdrCrypto.ChunkSize + MdrCrypto.TagSize - 1) / (MdrCrypto.ChunkSize + MdrCrypto.TagSize);
            long logical = phys - chunks * MdrCrypto.TagSize;
            src = new ChunkedAesReadStream(_arc, _footer.DirOffset, logical, _key!, MdrFormat.PurposeDirectory);
        }
        else src = new SubStream(_arc, _footer.DirOffset, _footer.DirLength);

        byte[] json;
        using (src)
        using (var br = new BrotliStream(src, CompressionMode.Decompress))
        using (var ms = new MemoryStream())
        {
            try { br.CopyTo(ms); }
            catch (InvalidDataException) { throw new MedMDRException("El directorio del archivo está dañado."); }
            json = ms.ToArray();
        }
        _dir = JsonSerializer.Deserialize<MdrDirectory>(json, MdrFormat.Json) ?? throw new MedMDRException("Directorio ilegible.");
        _entries = new List<ArchiveEntry>(_dir.Entries.Count);
        int i = 0;
        foreach (var e in _dir.Entries)
        {
            _entries.Add(new ArchiveEntry
            {
                Path = e.P,
                IsDirectory = e.D,
                Size = e.S,
                PackedSize = e.Z,
                Modified = new DateTime(e.T, DateTimeKind.Utc).ToLocalTime(),
                Crc = e.D ? null : e.C,
                IsEncrypted = _header.Encrypted,
                Method = e.D ? "" : Codec.MethodName((byte)e.M),
                Index = i++,
                Tag = e
            });
        }
    }

    private Stream OpenPayload()
    {
        if (_header.Encrypted)
        {
            if (_key == null) throw new PasswordRequiredException();
            return new ChunkedAesReadStream(_arc, _payloadStart, _dir!.PayloadLength, _key, MdrFormat.PurposePayload);
        }
        return new SubStream(_arc, _payloadStart, _dir!.PayloadLength);
    }

    public void ExtractEntries(IReadOnlyList<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openSink,
                               Action<ArchiveEntry, Exception?>? done, ProgressReporter progress)
    {
        if (_dir == null) throw new PasswordRequiredException();
        if (_header.Encrypted && _key == null) throw new PasswordRequiredException();
        using var payload = OpenPayload();

        if (_header.Solid)
            ExtractSolid(payload, entries, openSink, done, progress);
        else
        {
            foreach (var entry in entries)
            {
                if (entry.IsDirectory) continue;
                var e = (MdrEntry)entry.Tag!;
                var sink = openSink(entry);
                if (sink == null) { continue; }
                Exception? err = null;
                progress.Begin(entry.Path);
                try
                {
                    var crc = new CrcWriteStream(sink, progress);
                    payload.Seek(e.O, SeekOrigin.Begin);
                    var limited = new LimitedStream(payload, e.Z);
                    if (e.M == Codec.Store) StreamUtil.CopyExactly(limited, crc, e.S);
                    else
                    {
                        using var dec = Codec.CreateDecompressor((byte)e.M, limited);
                        StreamUtil.CopyExactly(dec, crc, e.S);
                    }
                    if (crc.Crc != e.C) err = new ChecksumException(entry.Path);
                }
                catch (InvalidDataException) { err = new ChecksumException(entry.Path); }
                catch (EndOfStreamException) { err = new ChecksumException(entry.Path); }
                finally { sink.Dispose(); }
                progress.EndFile();
                done?.Invoke(entry, err);
            }
        }
    }

    private void ExtractSolid(Stream payload, IReadOnlyList<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openSink,
                              Action<ArchiveEntry, Exception?>? done, ProgressReporter progress)
    {
        var ordered = entries.Where(x => !x.IsDirectory).OrderBy(x => ((MdrEntry)x.Tag!).O).ToList();
        if (ordered.Count == 0) return;
        payload.Seek(0, SeekOrigin.Begin);
        using var dec = Codec.CreateDecompressor((byte)_header.Method, payload);
        long pos = 0;
        var skipBuf = new byte[StreamUtil.BufferSize];
        foreach (var entry in ordered)
        {
            var e = (MdrEntry)entry.Tag!;
            // saltar hasta el comienzo de este fichero
            long skip = e.O - pos;
            while (skip > 0)
            {
                int n = dec.Read(skipBuf, 0, (int)Math.Min(skipBuf.Length, skip));
                if (n <= 0) throw new ChecksumException(entry.Path);
                skip -= n;
                pos += n;
                progress.Token.ThrowIfCancellationRequested();
            }
            var sink = openSink(entry);
            Exception? err = null;
            progress.Begin(entry.Path);
            try
            {
                var crc = new CrcWriteStream(sink ?? Stream.Null, progress);
                StreamUtil.CopyExactly(dec, crc, e.S);
                pos += e.S;
                if (crc.Crc != e.C) err = new ChecksumException(entry.Path);
            }
            catch (InvalidDataException) { throw new ChecksumException(entry.Path); }
            catch (EndOfStreamException) { throw new ChecksumException(entry.Path); }
            finally { sink?.Dispose(); }
            progress.EndFile();
            if (sink != null) done?.Invoke(entry, err);
        }
    }

    public void Dispose()
    {
        _arc.Dispose();
        _raw.Dispose();
        if (_key != null) Array.Clear(_key);
    }
}
