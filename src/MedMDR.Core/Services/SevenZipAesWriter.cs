using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using SharpCompress.Compressors.LZMA;

namespace MedMDR.Core;

/// <summary>
/// Escritor de .7z con contraseña (AES-256 en el formato «7zAES» de 7-Zip), que es el que abren 7-Zip y otros programas.
/// Un solo bloque sólido: todos los ficheros → LZMA → AES-256-CBC. Con «nombres cifrados» la lista de ficheros también
/// va comprimida y cifrada (cabecera codificada), y sin la contraseña no se puede ni listar el contenido.
/// </summary>
/// <remarks>
/// Clave: SHA-256 repetido 2^19 veces sobre (sal + contraseña UTF-16LE + contador de 8 bytes). IV aleatorio de 16 bytes.
/// El 7z con AES no autentica los datos (solo lleva CRC32 de cada fichero): una clave mala o un archivo dañado se notan
/// por el CRC o porque falla la descompresión.
/// </remarks>
internal static class SevenZipAesWriter
{
    private const int CyclesPower = 19;
    private static readonly byte[] SigMagic = { (byte)'7', (byte)'z', 0xBC, 0xAF, 0x27, 0x1C };

    private sealed class Folder
    {
        public byte[] AesProps = Array.Empty<byte>();
        public byte[] LzmaProps = Array.Empty<byte>();
        public long PackSize;            // bytes guardados (cifrados, múltiplo de 16)
        public long AesOutSize;          // tamaño del flujo LZMA (sin relleno)
        public long UnpackSize;          // tamaño original
    }

    public static void Write(Stream output, IReadOnlyList<SourceItem> items, ArchiveOptions o, ProgressReporter progress)
    {
        progress.Total = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        progress.FilesTotal = items.Count(i => !i.IsDirectory);
        byte[] password = Encoding.Unicode.GetBytes(o.Password!.Normalize(NormalizationForm.FormC));
        int dict = o.Level <= 1 ? 1 << 20 : o.Level <= 3 ? 1 << 22 : 1 << 24;

        // 32 bytes reservados para la cabecera de firma (se rellena al final)
        output.Write(new byte[32]);

        // 1) comprimir todos los ficheros con datos en un solo flujo LZMA (temporal)
        var sizes = new List<long>();
        var crcs = new List<uint>();
        string lzTmp = Path.Combine(Path.GetTempPath(), "medmdr-7z-" + Guid.NewGuid().ToString("N")[..10]);
        Folder? mainFolder = null;
        long mainPackSize = 0;
        try
        {
            long totalUnpack = 0;
            byte[] lzmaProps;
            long lzmaLen;
            using (var t = new FileStream(lzTmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 128 * 1024, FileOptions.DeleteOnClose))
            {
                var lz = LzmaStream.Create(new LzmaEncoderProperties(false, dict), false, new NonClosingStream(t));
                lzmaProps = lz.Properties;
                foreach (var it in items.Where(i => !i.IsDirectory))
                {
                    progress.Token.ThrowIfCancellationRequested();
                    var crc = new Crc32();
                    long n = 0;
                    progress.Begin(it.EntryPath);
                    using (var src = it.Open!())
                    {
                        var buf = new byte[StreamUtil.BufferSize];
                        int r;
                        while ((r = src.Read(buf, 0, buf.Length)) > 0)
                        {
                            lz.Write(buf, 0, r);
                            crc.Append(buf.AsSpan(0, r));
                            n += r;
                            progress.Add(r);
                        }
                    }
                    progress.EndFile();
                    if (n == 0) { sizes.Add(0); crcs.Add(0); continue; }
                    sizes.Add(n);
                    crcs.Add(crc.GetCurrentHashAsUInt32());
                    totalUnpack += n;
                }
                lz.Dispose();                      // escribe lo que quede pendiente
                t.Flush();
                lzmaLen = t.Length;
                if (totalUnpack > 0)
                {
                    t.Position = 0;
                    mainFolder = EncryptStream(t, lzmaLen, lzmaProps, totalUnpack, password, output);
                    mainPackSize = mainFolder.PackSize;
                }
            }
        }
        finally { try { if (File.Exists(lzTmp)) File.Delete(lzTmp); } catch { } }

        // 2) cabecera con la lista de ficheros
        var files = items.ToList();
        byte[] header = BuildHeader(files, sizes, crcs, mainFolder, mainPackSize);

        // 3) la cabecera va en claro o, con nombres cifrados, comprimida y cifrada como otro bloque
        byte[] nextHeader;
        if (o.EncryptNames)
        {
            var folder = CompressAndEncrypt(header, password, dict, output);
            nextHeader = BuildEncodedHeader(folder, mainPackSize, header);
        }
        else nextHeader = header;

        long nextOffset = output.Position - 32;
        output.Write(nextHeader);
        output.Flush();

        // 4) cabecera de firma
        var sig = new byte[32];
        SigMagic.CopyTo(sig, 0);
        sig[6] = 0; sig[7] = 4;
        BinaryPrimitives.WriteInt64LittleEndian(sig.AsSpan(12), nextOffset);
        BinaryPrimitives.WriteInt64LittleEndian(sig.AsSpan(20), nextHeader.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(sig.AsSpan(28), StreamUtil.Crc(nextHeader));
        BinaryPrimitives.WriteUInt32LittleEndian(sig.AsSpan(8), StreamUtil.Crc(sig.AsSpan(12, 20)));
        output.Seek(0, SeekOrigin.Begin);
        output.Write(sig);
        output.Seek(0, SeekOrigin.End);
        output.Flush();
    }

    // ------------------------------------------------------------------ cifrado

    private static byte[] DeriveKey(byte[] password, byte[] salt)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var counter = new byte[8];
        long rounds = 1L << CyclesPower;
        // se concatena (sal + contraseña + contador) en un bloque para reducir llamadas
        var block = new byte[salt.Length + password.Length + 8];
        salt.CopyTo(block, 0);
        password.CopyTo(block, salt.Length);
        for (long i = 0; i < rounds; i++)
        {
            BinaryPrimitives.WriteInt64LittleEndian(block.AsSpan(salt.Length + password.Length), i);
            sha.AppendData(block);
        }
        return sha.GetHashAndReset();
    }

    /// <summary>Cifra <paramref name="length"/> bytes de <paramref name="plain"/> con AES-256-CBC y los escribe en <paramref name="output"/>.</summary>
    private static Folder EncryptStream(Stream plain, long length, byte[] lzmaProps, long unpackSize, byte[] password, Stream output)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var key = DeriveKey(password, salt);
        using var aes = Aes.Create();
        aes.Key = key;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var enc = aes.CreateEncryptor();

        var buf = new byte[64 * 1024];
        var outBuf = new byte[buf.Length];
        long left = length, written = 0;
        while (left > 0)
        {
            int n = plain.Read(buf, 0, (int)Math.Min(buf.Length, left));
            if (n <= 0) throw new EndOfStreamException();
            left -= n;
            int full = n / 16 * 16;
            if (full > 0) { enc.TransformBlock(buf, 0, full, outBuf, 0); output.Write(outBuf, 0, full); written += full; }
            int rest = n - full;
            if (rest > 0)
            {
                // quedan menos de 16 bytes: solo puede pasar al final del flujo; se rellenan con ceros
                if (left != 0) { /* cola parcial en mitad del flujo: se completa con la siguiente lectura */ plain.Seek(-rest, SeekOrigin.Current); left += rest; }
                else
                {
                    var last = new byte[16];
                    Array.Copy(buf, full, last, 0, rest);
                    enc.TransformBlock(last, 0, 16, outBuf, 0);
                    output.Write(outBuf, 0, 16);
                    written += 16;
                }
            }
        }
        CryptographicOperations.ZeroMemory(key);
        // propiedades del codificador AES: byte0 = ciclos | 0x80 (hay sal) | 0x40 (hay IV); byte1 = (tamaño sal-1)<<4 | (tamaño IV-1)
        var props = new byte[2 + 16 + 16];
        props[0] = (byte)(CyclesPower | 0x80 | 0x40);
        props[1] = (byte)((15 << 4) | 15);
        salt.CopyTo(props, 2);
        iv.CopyTo(props, 18);
        return new Folder { AesProps = props, LzmaProps = lzmaProps, PackSize = written, AesOutSize = length, UnpackSize = unpackSize };
    }

    private static Folder CompressAndEncrypt(byte[] data, byte[] password, int dict, Stream output)
    {
        using var ms = new MemoryStream();
        byte[] props;
        using (var lz = LzmaStream.Create(new LzmaEncoderProperties(false, Math.Min(dict, 1 << 20)), false, new NonClosingStream(ms)))
        {
            props = lz.Properties;
            lz.Write(data, 0, data.Length);
        }
        ms.Position = 0;
        return EncryptStream(ms, ms.Length, props, data.Length, password, output);
    }

    // ------------------------------------------------------------------ cabecera

    private sealed class W
    {
        public readonly MemoryStream S = new();
        public void B(int b) => S.WriteByte((byte)b);
        public void Bytes(ReadOnlySpan<byte> b) => S.Write(b);
        public void U32(uint v) { Span<byte> t = stackalloc byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(t, v); S.Write(t); }
        public void U64(ulong v) { Span<byte> t = stackalloc byte[8]; BinaryPrimitives.WriteUInt64LittleEndian(t, v); S.Write(t); }
        /// <summary>Número de 7z: la cantidad de bits 1 al principio del primer byte dice cuántos bytes más siguen.</summary>
        public void Num(ulong value)
        {
            int i;
            byte first = 0, mask = 0x80;
            for (i = 0; i < 8; i++)
            {
                if (value < (1UL << (7 * (i + 1)))) { first |= (byte)(value >> (8 * i)); break; }
                first |= mask;
                mask >>= 1;
            }
            B(first);
            for (; i > 0; i--) { B((byte)value); value >>= 8; }
        }
    }

    private static void WriteFolder(W w, Folder f)
    {
        w.Num(2);                                        // dos codificadores: AES (lee los datos guardados) → LZMA
        w.B(0x24); w.Bytes(new byte[] { 0x06, 0xF1, 0x07, 0x01 }); w.Num((ulong)f.AesProps.Length); w.Bytes(f.AesProps);
        w.B(0x23); w.Bytes(new byte[] { 0x03, 0x01, 0x01 }); w.Num((ulong)f.LzmaProps.Length); w.Bytes(f.LzmaProps);
        w.Num(1); w.Num(0);                              // enlace: la entrada 1 (LZMA) se alimenta de la salida 0 (AES); la entrada 0 (AES) es el bloque guardado
    }

    private static byte[] BuildHeader(List<SourceItem> files, List<long> sizes, List<uint> crcs, Folder? folder, long packSize)
    {
        var w = new W();
        w.B(0x01);                                       // Header
        if (folder != null)
        {
            w.B(0x04);                                   // MainStreamsInfo
            WritePackInfo(w, 0, new[] { packSize });
            WriteUnpackInfo(w, folder, null);
            // SubStreamsInfo: ficheros con datos dentro del bloque
            var dataIdx = sizes.Select((s, i) => (s, i)).Where(x => x.s > 0).ToList();
            w.B(0x08);
            w.B(0x0D); w.Num((ulong)dataIdx.Count);
            if (dataIdx.Count > 1)
            {
                w.B(0x09);
                for (int i = 0; i < dataIdx.Count - 1; i++) w.Num((ulong)dataIdx[i].s);
            }
            w.B(0x0A); w.B(1);                           // todos los CRC definidos
            foreach (var (_, i) in dataIdx) w.U32(crcs[i]);
            w.B(0x00);                                   // fin SubStreamsInfo
            w.B(0x00);                                   // fin MainStreamsInfo
        }

        // FilesInfo
        w.B(0x05);
        w.Num((ulong)files.Count);
        // «sin flujo» = carpetas y ficheros vacíos (sizes lleva una entrada por cada elemento que no es carpeta, en orden)
        var emptyStream = new List<bool>();
        int dataK = 0;
        foreach (var f in files) emptyStream.Add(f.IsDirectory || sizes[dataK++] == 0 ? true : false);
        if (emptyStream.Any(x => x))
        {
            WriteBitProp(w, 0x0E, emptyStream);
            var emptyFile = files.Where((f, i) => emptyStream[i]).Select(f => !f.IsDirectory).ToList();
            if (emptyFile.Any(x => x)) WriteBitProp(w, 0x0F, emptyFile);
        }
        // nombres
        var names = new MemoryStream();
        foreach (var f in files)
        {
            names.Write(Encoding.Unicode.GetBytes(f.EntryPath.Replace('\\', '/').Trim('/')));
            names.Write(new byte[2]);
        }
        w.B(0x11); w.Num((ulong)names.Length + 1); w.B(0); w.Bytes(names.ToArray());
        // fechas de modificación
        w.B(0x14); w.Num((ulong)(2 + 8 * files.Count)); w.B(1); w.B(0);
        foreach (var f in files) w.U64((ulong)ToFileTime(f.Modified));
        // atributos
        w.B(0x15); w.Num((ulong)(2 + 4 * files.Count)); w.B(1); w.B(0);
        foreach (var f in files) w.U32(f.IsDirectory ? 0x10u : 0x20u);
        w.B(0x00);                                       // fin FilesInfo
        w.B(0x00);                                       // fin Header
        return w.S.ToArray();
    }

    private static long SizeOf(SourceItem f, List<SourceItem> files, List<long> sizes)
    {
        // sizes lleva una entrada por cada elemento que NO es carpeta, en el mismo orden
        int k = 0;
        foreach (var x in files)
        {
            if (ReferenceEquals(x, f)) return x.IsDirectory ? 0 : sizes[k];
            if (!x.IsDirectory) k++;
        }
        return 0;
    }

    private static long ToFileTime(DateTime t)
    {
        try { return t.ToFileTimeUtc(); } catch { return DateTime.UtcNow.ToFileTimeUtc(); }
    }

    private static void WriteBitProp(W w, int id, List<bool> bits)
    {
        var bytes = new byte[(bits.Count + 7) / 8];
        for (int i = 0; i < bits.Count; i++) if (bits[i]) bytes[i / 8] |= (byte)(0x80 >> (i % 8));
        w.B(id); w.Num((ulong)bytes.Length); w.Bytes(bytes);
    }

    private static void WritePackInfo(W w, long packPos, long[] packSizes)
    {
        w.B(0x06);
        w.Num((ulong)packPos);
        w.Num((ulong)packSizes.Length);
        w.B(0x09);
        foreach (var s in packSizes) w.Num((ulong)s);
        w.B(0x00);
    }

    private static void WriteUnpackInfo(W w, Folder f, uint? folderCrc)
    {
        w.B(0x07);
        w.B(0x0B); w.Num(1); w.B(0);                     // un bloque, sin datos externos
        WriteFolder(w, f);
        w.B(0x0C); w.Num((ulong)f.AesOutSize); w.Num((ulong)f.UnpackSize);   // tamaños de salida de cada codificador
        if (folderCrc != null) { w.B(0x0A); w.B(1); w.U32(folderCrc.Value); }
        w.B(0x00);
    }

    /// <summary>Cabecera «codificada»: apunta al bloque (comprimido y cifrado) donde está la cabecera de verdad.</summary>
    private static byte[] BuildEncodedHeader(Folder f, long packPos, byte[] rawHeader)
    {
        var w = new W();
        w.B(0x17);                                       // EncodedHeader
        WritePackInfo(w, packPos, new[] { f.PackSize });
        WriteUnpackInfo(w, f, StreamUtil.Crc(rawHeader));
        w.B(0x00);
        return w.S.ToArray();
    }
}
