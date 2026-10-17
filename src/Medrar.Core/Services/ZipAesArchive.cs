using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace Medrar.Core;

/// <summary>
/// Lector de ZIP con cifrado AES de WinZip (AE-1/AE-2) que VERIFICA el código de autenticación de cada fichero.
/// (La biblioteca general lee estos ZIP pero no comprueba esa firma, así que un fichero manipulado pasaría como bueno.)
/// Solo maneja ZIP «normales» (sin Zip64) con métodos almacenado, deflate y AES; con otros se usa el lector general.
/// </summary>
public sealed class ZipAesArchive : IArchive
{
    private sealed class Rec
    {
        public string Name = "";
        public ushort Flags, Method, RealMethod, Strength, AesVersion;
        public uint Crc, CompSize, Size, LocalOffset;
        public ushort Time, Date;
        public bool IsDir;
    }

    private readonly Stream _s;
    private readonly string? _password;
    private readonly bool _ownsStream;
    private readonly List<ArchiveEntry> _entries = new();

    public string Path { get; }
    public string FormatName => "ZIP (AES)";
    public IReadOnlyList<ArchiveEntry> Entries => _entries;
    public string? Comment => null;
    public bool IsSolid => false;
    public bool IsEncrypted => true;
    public bool HasEncryptedNames => false;
    public bool IsLocked => ArchiveEditor.IsReadOnlyFile(Path);
    public bool HasRecovery => File.Exists(RecoveryRecord.SidecarPathFor(Path));
    public long ArchiveSize { get; }
    public bool CanModify { get; }

    private ZipAesArchive(Stream s, string path, string? password, bool ownsStream, bool embedded)
    {
        _s = s; Path = path; _password = password; _ownsStream = ownsStream; CanModify = !embedded; ArchiveSize = s.Length;
    }

    /// <summary>Devuelve null si el ZIP no tiene ficheros cifrados con AES o no es de los que este lector maneja.</summary>
    public static ZipAesArchive? TryOpen(Stream s, string path, string? password, bool ownsStream, bool embedded)
    {
        var recs = ReadCentralDirectory(s);
        if (recs == null || !recs.Any(r => r.Method == 99)) return null;
        if (recs.Any(r => r.Method != 99 && r.Method != 0 && r.Method != 8)) return null;
        if (recs.Any(r => r.Method == 99 && (r.AesVersion is not (1 or 2) || r.Strength is < 1 or > 3 || (r.RealMethod != 0 && r.RealMethod != 8)))) return null;
        var a = new ZipAesArchive(s, path, password, ownsStream, embedded);
        int i = 0;
        foreach (var r in recs)
        {
            a._entries.Add(new ArchiveEntry
            {
                Path = r.Name.Replace('\\', '/').TrimStart('/').TrimEnd('/'),
                IsDirectory = r.IsDir,
                Size = r.Size,
                PackedSize = r.CompSize,
                Modified = DosToDate(r.Date, r.Time),
                Crc = r.IsDir || r.Method == 99 && r.AesVersion == 2 ? null : r.Crc,
                IsEncrypted = r.Method == 99 && !r.IsDir,
                Method = r.Method == 99 ? "AES-" + (r.Strength == 1 ? 128 : r.Strength == 2 ? 192 : 256) : r.Method == 8 ? "Deflate" : "Almacenado",
                Index = i++,
                Tag = r
            });
        }
        return a;
    }

    private static List<Rec>? ReadCentralDirectory(Stream s)
    {
        if (s.Length < 22) return null;
        int tail = (int)Math.Min(s.Length, 66_000);
        var buf = new byte[tail];
        s.Seek(s.Length - tail, SeekOrigin.Begin);
        s.ReadExactly(buf);
        int eocd = -1;
        for (int i = tail - 22; i >= 0; i--)
            if (buf[i] == 0x50 && buf[i + 1] == 0x4B && buf[i + 2] == 5 && buf[i + 3] == 6) { eocd = i; break; }
        if (eocd < 0) return null;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(buf.AsSpan(eocd + 10));
        uint cdSize = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eocd + 12));
        uint cdOff = BinaryPrimitives.ReadUInt32LittleEndian(buf.AsSpan(eocd + 16));
        if (count == 0xFFFF || cdSize == 0xFFFFFFFF || cdOff == 0xFFFFFFFF) return null;       // Zip64: lo lee el lector general
        if (cdOff + (long)cdSize > s.Length) return null;
        var cd = new byte[cdSize];
        s.Seek(cdOff, SeekOrigin.Begin);
        s.ReadExactly(cd);
        var list = new List<Rec>();
        int p = 0;
        for (int n = 0; n < count; n++)
        {
            if (p + 46 > cd.Length || BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p)) != 0x02014b50) return null;
            var r = new Rec
            {
                Flags = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 8)),
                Method = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 10)),
                Time = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 12)),
                Date = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 14)),
                Crc = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p + 16)),
                CompSize = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p + 20)),
                Size = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p + 24)),
                LocalOffset = BinaryPrimitives.ReadUInt32LittleEndian(cd.AsSpan(p + 42)),
            };
            if (r.CompSize == 0xFFFFFFFF || r.Size == 0xFFFFFFFF || r.LocalOffset == 0xFFFFFFFF) return null;
            int nl = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 28)), el = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 30)), cl = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(p + 32));
            if (p + 46 + nl + el + cl > cd.Length) return null;
            var enc = (r.Flags & 0x0800) != 0 ? Encoding.UTF8 : Encoding.Latin1;
            r.Name = enc.GetString(cd, p + 46, nl);
            r.IsDir = r.Name.EndsWith('/') || r.Name.EndsWith('\\');
            // campo extra AES (0x9901)
            int x = p + 46 + nl, xEnd = x + el;
            while (x + 4 <= xEnd)
            {
                ushort id = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(x)), len = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(x + 2));
                if (id == 0x9901 && len >= 7)
                {
                    r.AesVersion = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(x + 4));
                    r.Strength = cd[x + 8];
                    r.RealMethod = BinaryPrimitives.ReadUInt16LittleEndian(cd.AsSpan(x + 9));
                }
                x += 4 + len;
            }
            list.Add(r);
            p += 46 + nl + el + cl;
        }
        return list;
    }

    private static DateTime DosToDate(ushort date, ushort time)
    {
        try { return new DateTime(1980 + (date >> 9), Math.Max(1, (date >> 5) & 15), Math.Max(1, date & 31), time >> 11, (time >> 5) & 63, (time & 31) * 2); }
        catch { return DateTime.Now; }
    }

    public void ExtractEntries(IReadOnlyList<ArchiveEntry> entries, Func<ArchiveEntry, Stream?> openSink,
                               Action<ArchiveEntry, Exception?>? done, ProgressReporter progress)
    {
        var wanted = entries.Where(e => !e.IsDirectory).Select(e => e.Index).ToHashSet();
        if (wanted.Count == 0) return;
        if (string.IsNullOrEmpty(_password) && _entries.Any(e => wanted.Contains(e.Index) && e.IsEncrypted))
            throw new PasswordRequiredException();
        foreach (var entry in _entries.Where(e => wanted.Contains(e.Index)).ToList())
        {
            var r = (Rec)entry.Tag!;
            var sink = openSink(entry);
            if (sink == null) continue;
            Exception? err = null;
            progress.Begin(entry.Path);
            try
            {
                var crc = new CrcWriteStream(sink, progress);
                ExtractOne(r, crc);
                // AE-1 y los ZIP sin cifrar traen CRC32; AE-2 no (lo sustituye el código de autenticación)
                bool hasCrc = r.Method != 99 || r.AesVersion == 1;
                if (hasCrc && crc.Crc != r.Crc) err = new ChecksumException(entry.Path);
            }
            catch (ChecksumException ce) { err = ce; }
            catch (InvalidDataException) { err = new ChecksumException(entry.Path); }
            catch (EndOfStreamException) { err = new ChecksumException(entry.Path); }
            finally { sink.Dispose(); }
            progress.EndFile();
            done?.Invoke(entry, err);
        }
    }

    private void ExtractOne(Rec r, Stream dest)
    {
        // saltar la cabecera local
        var lh = new byte[30];
        _s.Seek(r.LocalOffset, SeekOrigin.Begin);
        _s.ReadExactly(lh);
        if (BinaryPrimitives.ReadUInt32LittleEndian(lh) != 0x04034b50) throw new InvalidDataException();
        long dataStart = r.LocalOffset + 30 + BinaryPrimitives.ReadUInt16LittleEndian(lh.AsSpan(26)) + BinaryPrimitives.ReadUInt16LittleEndian(lh.AsSpan(28));

        if (r.Method != 99)
        {
            _s.Seek(dataStart, SeekOrigin.Begin);
            var limited = new LimitedStream(_s, r.CompSize);
            if (r.Method == 0) StreamUtil.CopyExactly(limited, dest, r.Size);
            else { using var d = new DeflateStream(limited, CompressionMode.Decompress, leaveOpen: true); StreamUtil.CopyExactly(d, dest, r.Size); }
            return;
        }

        int keyLen = r.Strength == 1 ? 16 : r.Strength == 2 ? 24 : 32;
        int saltLen = keyLen / 2;
        long cipherLen = (long)r.CompSize - saltLen - 2 - 10;
        if (cipherLen < 0) throw new InvalidDataException();
        _s.Seek(dataStart, SeekOrigin.Begin);
        var salt = new byte[saltLen];
        _s.ReadExactly(salt);
        var verifier = new byte[2];
        _s.ReadExactly(verifier);
        var keys = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(_password!.Normalize(NormalizationForm.FormC)), salt, 1000, HashAlgorithmName.SHA1, 2 * keyLen + 2);
        if (!keys.AsSpan(2 * keyLen, 2).SequenceEqual(verifier)) throw new WrongPasswordException();

        using var aes = Aes.Create();
        aes.Key = keys.AsSpan(0, keyLen).ToArray();
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, keys.AsSpan(keyLen, keyLen).ToArray());

        // descifrar a un temporal (el código de autenticación se comprueba ANTES de dar nada por bueno)
        string tmp = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "medrar-zaes-" + Guid.NewGuid().ToString("N")[..10]);
        try
        {
            using (var t = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.DeleteOnClose))
            {
                var buf = new byte[64 * 1024];
                var ks = new byte[buf.Length];
                var ctr = new byte[buf.Length];
                ulong counter = 1;
                long left = cipherLen;
                while (left > 0)
                {
                    int n = _s.Read(buf, 0, (int)Math.Min(buf.Length, left));
                    if (n <= 0) throw new EndOfStreamException();
                    hmac.AppendData(buf, 0, n);
                    int blocks = (n + 15) / 16;
                    for (int b = 0; b < blocks; b++) { var slot = ctr.AsSpan(b * 16, 16); slot.Clear(); BinaryPrimitives.WriteUInt64LittleEndian(slot, counter++); }
                    aes.EncryptEcb(ctr.AsSpan(0, blocks * 16), ks.AsSpan(0, blocks * 16), PaddingMode.None);
                    for (int k = 0; k < n; k++) buf[k] ^= ks[k];
                    t.Write(buf, 0, n);
                    left -= n;
                }
                var auth = new byte[10];
                _s.ReadExactly(auth);
                var calc = hmac.GetHashAndReset();
                if (!CryptographicOperations.FixedTimeEquals(auth, calc.AsSpan(0, 10)))
                    throw new ChecksumException(r.Name);            // manipulado o dañado (la contraseña ya se comprobó)
                t.Position = 0;
                if (r.RealMethod == 0) StreamUtil.CopyExactly(t, dest, r.Size);
                else { using var d = new DeflateStream(t, CompressionMode.Decompress, leaveOpen: true); StreamUtil.CopyExactly(d, dest, r.Size); }
            }
        }
        finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
    }

    public void Dispose() { if (_ownsStream) _s.Dispose(); }
}
