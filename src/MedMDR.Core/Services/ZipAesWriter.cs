using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace MedMDR.Core;

/// <summary>
/// Escritor de ZIP con contraseña: cifrado AES-256 en el formato de WinZip (AE-2), el que abren 7-Zip,
/// WinZip y casi todos los programas modernos. El ZIP normal sin contraseña lo escribe .NET.
/// </summary>
/// <remarks>
/// Cada fichero: [sal 16 B][verificador de contraseña 2 B][datos cifrados (AES-CTR)][código de autenticación 10 B (HMAC-SHA1)].
/// Clave: PBKDF2-HMAC-SHA1, 1000 iteraciones (lo fija el formato) → clave AES 32 B + clave HMAC 32 B + verificador 2 B.
/// Los nombres de los ficheros NO se cifran (el formato ZIP no lo permite); para ocultarlos hay que usar .mdr.
/// </remarks>
internal static class ZipAesWriter
{
    private const int SaltLen = 16, KeyLen = 32, AuthLen = 10;

    private sealed class CdEntry
    {
        public byte[] Name = Array.Empty<byte>();
        public ushort Time, Date, Method;
        public uint Crc, CompSize, Size, Offset;
        public bool IsDir;
        public byte[] Extra = Array.Empty<byte>();
        public ushort Flags;
    }

    public static void Write(Stream output, IReadOnlyList<SourceItem> items, ArchiveOptions o, ProgressReporter progress)
    {
        progress.Total = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        progress.FilesTotal = items.Count(i => !i.IsDirectory);
        if (items.Count >= ushort.MaxValue) throw new UnsupportedFormatException("Demasiados ficheros para un ZIP con contraseña (máximo 65 534). Usa .mdr o .7z.");
        var password = Encoding.UTF8.GetBytes(o.Password!.Normalize(NormalizationForm.FormC));
        var cd = new List<CdEntry>();
        long pos = 0;

        void Write(ReadOnlySpan<byte> b) { output.Write(b); pos += b.Length; }

        foreach (var it in items)
        {
            progress.Token.ThrowIfCancellationRequested();
            string name = it.EntryPath.Replace('\\', '/').Trim('/');
            var e = new CdEntry { IsDir = it.IsDirectory, Flags = 0x0800 };
            (e.Date, e.Time) = DosDateTime(it.Modified);
            if (it.IsDirectory)
            {
                e.Name = Encoding.UTF8.GetBytes(name + "/");
                e.Method = 0;
                e.Offset = Checked(pos);
                WriteLocal(Write, e);
                cd.Add(e);
                continue;
            }
            e.Name = Encoding.UTF8.GetBytes(name);
            progress.Begin(name);

            // 1) comprimir a un temporal (así se conoce el tamaño final antes de escribir la cabecera)
            string tmp = Path.Combine(Path.GetTempPath(), "medmdr-zip-" + Guid.NewGuid().ToString("N")[..10]);
            try
            {
                ushort realMethod = (it.Size == 0 || o.Level <= 0 || Glob.MatchesAny(name, o.StorePatterns)) ? (ushort)0 : (ushort)8;
                long usize;
                using (var t = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.DeleteOnClose))
                {
                    using (var src = it.Open!())
                    {
                        var counter = new CountOnly(progress);
                        if (realMethod == 0) { StreamUtil.CopyTo(src, new TeeStream(t, counter)); }
                        else
                        {
                            using var def = new DeflateStream(t, new ZLibCompressionOptions { CompressionLevel = Math.Clamp(o.Level * 2 - 1, 1, 9) }, leaveOpen: true);
                            StreamUtil.CopyTo(src, new TeeStream(def, counter));
                        }
                        usize = counter.Count;
                    }
                    t.Flush();
                    long clen = t.Length;
                    long total = SaltLen + 2 + clen + AuthLen;
                    if (usize >= uint.MaxValue || total >= uint.MaxValue)
                        throw new UnsupportedFormatException($"«{name}» es demasiado grande para un ZIP con contraseña (límite 4 GB). Usa .mdr.");

                    // 2) derivar claves y escribir cabecera + datos cifrados
                    var salt = RandomNumberGenerator.GetBytes(SaltLen);
                    var keys = Rfc2898DeriveBytes.Pbkdf2(password, salt, 1000, HashAlgorithmName.SHA1, 2 * KeyLen + 2);
                    var encKey = keys.AsSpan(0, KeyLen).ToArray();
                    var authKey = keys.AsSpan(KeyLen, KeyLen).ToArray();
                    var verifier = keys.AsSpan(2 * KeyLen, 2).ToArray();

                    e.Method = 99;
                    e.Flags |= 0x0001;
                    e.Crc = 0;                                  // AE-2: no se guarda el CRC
                    e.Size = (uint)usize;
                    e.CompSize = (uint)total;
                    e.Extra = AesExtra(realMethod);
                    e.Offset = Checked(pos);
                    WriteLocal(Write, e);
                    Write(salt);
                    Write(verifier);

                    t.Position = 0;
                    using var aes = Aes.Create();
                    aes.Key = encKey;
                    using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA1, authKey);
                    var buf = new byte[64 * 1024];
                    var ks = new byte[buf.Length];
                    var ctr = new byte[buf.Length];
                    ulong counterValue = 1;
                    int n;
                    while ((n = t.Read(buf, 0, buf.Length)) > 0)
                    {
                        int blocks = (n + 15) / 16;
                        for (int b = 0; b < blocks; b++)
                        {
                            var slot = ctr.AsSpan(b * 16, 16);
                            slot.Clear();
                            BinaryPrimitives.WriteUInt64LittleEndian(slot, counterValue++);
                        }
                        aes.EncryptEcb(ctr.AsSpan(0, blocks * 16), ks.AsSpan(0, blocks * 16), PaddingMode.None);
                        for (int k = 0; k < n; k++) buf[k] ^= ks[k];
                        hmac.AppendData(buf, 0, n);
                        Write(buf.AsSpan(0, n));
                    }
                    Write(hmac.GetHashAndReset().AsSpan(0, AuthLen));
                }
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
            cd.Add(e);
            progress.EndFile();
        }

        // directorio central
        long cdStart = pos;
        Span<byte> h = stackalloc byte[46];
        foreach (var e in cd)
        {
            h.Clear();
            BinaryPrimitives.WriteUInt32LittleEndian(h, 0x02014b50);
            BinaryPrimitives.WriteUInt16LittleEndian(h[4..], e.Method == 99 ? (ushort)0x0033 : (ushort)0x0014);
            BinaryPrimitives.WriteUInt16LittleEndian(h[6..], e.Method == 99 ? (ushort)51 : (ushort)20);
            BinaryPrimitives.WriteUInt16LittleEndian(h[8..], e.Flags);
            BinaryPrimitives.WriteUInt16LittleEndian(h[10..], e.Method);
            BinaryPrimitives.WriteUInt16LittleEndian(h[12..], e.Time);
            BinaryPrimitives.WriteUInt16LittleEndian(h[14..], e.Date);
            BinaryPrimitives.WriteUInt32LittleEndian(h[16..], e.Crc);
            BinaryPrimitives.WriteUInt32LittleEndian(h[20..], e.CompSize);
            BinaryPrimitives.WriteUInt32LittleEndian(h[24..], e.Size);
            BinaryPrimitives.WriteUInt16LittleEndian(h[28..], (ushort)e.Name.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(h[30..], (ushort)e.Extra.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(h[32..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(h[34..], 0);
            BinaryPrimitives.WriteUInt16LittleEndian(h[36..], 0);
            BinaryPrimitives.WriteUInt32LittleEndian(h[38..], e.IsDir ? 0x10u : 0u);
            BinaryPrimitives.WriteUInt32LittleEndian(h[42..], e.Offset);
            Write(h);
            Write(e.Name);
            Write(e.Extra);
        }
        long cdSize = pos - cdStart;
        Span<byte> end = stackalloc byte[22];
        BinaryPrimitives.WriteUInt32LittleEndian(end, 0x06054b50);
        BinaryPrimitives.WriteUInt16LittleEndian(end[8..], (ushort)cd.Count);
        BinaryPrimitives.WriteUInt16LittleEndian(end[10..], (ushort)cd.Count);
        BinaryPrimitives.WriteUInt32LittleEndian(end[12..], Checked(cdSize));
        BinaryPrimitives.WriteUInt32LittleEndian(end[16..], Checked(cdStart));
        Write(end);
        output.Flush();
    }

    private static uint Checked(long v) =>
        v >= uint.MaxValue ? throw new UnsupportedFormatException("El ZIP con contraseña no puede superar 4 GB. Usa .mdr o .7z.") : (uint)v;

    private static byte[] AesExtra(ushort realMethod)
    {
        var x = new byte[11];
        BinaryPrimitives.WriteUInt16LittleEndian(x, 0x9901);
        BinaryPrimitives.WriteUInt16LittleEndian(x.AsSpan(2), 7);
        BinaryPrimitives.WriteUInt16LittleEndian(x.AsSpan(4), 2);       // AE-2
        x[6] = (byte)'A'; x[7] = (byte)'E';
        x[8] = 3;                                                       // AES-256
        BinaryPrimitives.WriteUInt16LittleEndian(x.AsSpan(9), realMethod);
        return x;
    }

    private static void WriteLocal(Action<ReadOnlySpan<byte>> write, CdEntry e)
    {
        Span<byte> h = stackalloc byte[30];
        BinaryPrimitives.WriteUInt32LittleEndian(h, 0x04034b50);
        BinaryPrimitives.WriteUInt16LittleEndian(h[4..], e.Method == 99 ? (ushort)51 : (ushort)20);
        BinaryPrimitives.WriteUInt16LittleEndian(h[6..], e.Flags);
        BinaryPrimitives.WriteUInt16LittleEndian(h[8..], e.Method);
        BinaryPrimitives.WriteUInt16LittleEndian(h[10..], e.Time);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], e.Date);
        BinaryPrimitives.WriteUInt32LittleEndian(h[14..], e.Crc);
        BinaryPrimitives.WriteUInt32LittleEndian(h[18..], e.CompSize);
        BinaryPrimitives.WriteUInt32LittleEndian(h[22..], e.Size);
        BinaryPrimitives.WriteUInt16LittleEndian(h[26..], (ushort)e.Name.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(h[28..], (ushort)e.Extra.Length);
        write(h);
        write(e.Name);
        write(e.Extra);
    }

    private static (ushort Date, ushort Time) DosDateTime(DateTime t)
    {
        if (t.Year < 1980) t = new DateTime(1980, 1, 1);
        if (t.Year > 2107) t = new DateTime(2107, 12, 31);
        return ((ushort)(((t.Year - 1980) << 9) | (t.Month << 5) | t.Day), (ushort)((t.Hour << 11) | (t.Minute << 5) | (t.Second / 2)));
    }

    /// <summary>Cuenta los bytes y avanza el progreso.</summary>
    private sealed class CountOnly
    {
        private readonly ProgressReporter _p;
        public long Count { get; private set; }
        public CountOnly(ProgressReporter p) => _p = p;
        public void Add(int n) { Count += n; _p.Add(n); }
    }

    private sealed class TeeStream : Stream
    {
        private readonly Stream _dest; private readonly CountOnly _count;
        public TeeStream(Stream dest, CountOnly count) { _dest = dest; _count = count; }
        public override void Write(byte[] buffer, int offset, int count) { _dest.Write(buffer, offset, count); _count.Add(count); }
        public override void Write(ReadOnlySpan<byte> buffer) { _dest.Write(buffer); _count.Add(buffer.Length); }
        public override void Flush() => _dest.Flush();
        public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
