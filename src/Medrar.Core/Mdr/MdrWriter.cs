using System.Buffers.Binary;
using System.IO.Compression;
using System.Text.Json;

namespace Medrar.Core;

internal static class MdrWriter
{
    /// <summary>
    /// Escribe un archivo .mdr completo en <paramref name="output"/> (debe permitir leer, escribir y buscar,
    /// porque el registro de recuperación relee lo ya escrito).
    /// </summary>
    public static void Write(Stream output, IReadOnlyList<SourceItem> items, ArchiveOptions o, ProgressReporter progress)
    {
        long start = output.Position;
        bool encrypted = !string.IsNullOrEmpty(o.Password);
        byte[]? key = null;
        var header = new MdrHeader
        {
            Created = DateTime.UtcNow.Ticks,
            Solid = o.Solid,
            Encrypted = encrypted,
            EncryptedNames = encrypted && o.EncryptNames,
            Locked = o.Lock,
            Method = o.Level <= 0 ? (int)Codec.Store : (o.UseDeflate ? (int)Codec.Deflate : (int)Codec.Brotli),
        };
        if (encrypted)
        {
            var salt = MdrCrypto.NewSalt();
            var (k, check) = MdrCrypto.Derive(o.Password!, salt, MdrCrypto.DefaultIterations);
            key = k;
            header.Salt = Convert.ToBase64String(salt);
            header.Iter = MdrCrypto.DefaultIterations;
            header.Check = Convert.ToBase64String(check);
        }

        // --- cabecera ---
        var hjson = JsonSerializer.SerializeToUtf8Bytes(header, MdrFormat.Json);
        output.Write(MdrFormat.Magic);
        Span<byte> t4 = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(t4, (uint)hjson.Length); output.Write(t4);
        output.Write(hjson);
        BinaryPrimitives.WriteUInt32LittleEndian(t4, StreamUtil.Crc(hjson)); output.Write(t4);

        // --- datos ---
        long payloadStart = output.Position;
        Stream sink = encrypted ? new ChunkedAesWriteStream(output, key!, MdrFormat.PurposePayload) : new NonClosingStream(output);
        var counting = new CountingStream(sink, leaveOpen: true);
        var entries = new List<MdrEntry>();
        progress.Total = items.Where(i => !i.IsDirectory).Sum(i => i.Size);
        progress.FilesTotal = items.Count(i => !i.IsDirectory);

        if (o.Solid)
        {
            using var comp = Codec.CreateCompressor((byte)header.Method, counting, o.Level, o.DictionaryKb);
            long solidPos = 0;
            foreach (var it in items)
            {
                if (it.IsDirectory) { entries.Add(DirEntry(it)); continue; }
                progress.Begin(it.EntryPath);
                var e = FileEntry(it);
                e.M = (byte)header.Method;
                e.O = solidPos;
                var crcOut = new CrcWriteStream(new NonDisposingSink(comp), progress);
                using (var src = it.Open!()) StreamUtil.CopyTo(src, crcOut);
                e.S = crcOut.Count;
                e.C = crcOut.Crc;
                solidPos += e.S;
                entries.Add(e);
                progress.EndFile();
            }
            comp.Dispose();
            long packed = counting.Count;
            long totalS = Math.Max(1, entries.Where(x => !x.D).Sum(x => x.S));
            foreach (var e in entries.Where(x => !x.D)) e.Z = (long)Math.Round((double)packed * e.S / totalS);
        }
        else
        {
            foreach (var it in items)
            {
                if (it.IsDirectory) { entries.Add(DirEntry(it)); continue; }
                progress.Begin(it.EntryPath);
                var e = FileEntry(it);
                byte method = (byte)header.Method;
                if (it.Size == 0 || Glob.MatchesAny(it.EntryPath, o.StorePatterns)) method = Codec.Store;
                e.M = method;
                e.O = counting.Count;
                var comp = Codec.CreateCompressor(method, counting, o.Level, o.DictionaryKb);
                var crcOut = new CrcWriteStream(comp, progress);
                using (var src = it.Open!()) StreamUtil.CopyTo(src, crcOut);
                comp.Dispose();
                e.S = crcOut.Count;
                e.C = crcOut.Crc;
                e.Z = counting.Count - e.O;
                entries.Add(e);
                progress.EndFile();
            }
        }

        long payloadLogical = counting.Count;
        sink.Dispose();                       // escribe el último bloque cifrado, si lo hay
        output.Flush();

        // --- directorio ---
        var dir = new MdrDirectory { Comment = o.Comment, PayloadLength = payloadLogical, Entries = entries };
        var djson = JsonSerializer.SerializeToUtf8Bytes(dir, MdrFormat.Json);
        byte[] dcomp;
        using (var ms = new MemoryStream())
        {
            using (var br = new BrotliStream(ms, CompressionLevel.Optimal, leaveOpen: true)) br.Write(djson);
            dcomp = ms.ToArray();
        }
        long dirOffset = output.Position - start;
        if (header.EncryptedNames)
        {
            using var w = new ChunkedAesWriteStream(output, key!, MdrFormat.PurposeDirectory);
            w.Write(dcomp);
        }
        else output.Write(dcomp);
        long dirLength = (output.Position - start) - dirOffset;

        // --- recuperación ---
        long recOffset = 0, recLength = 0;
        long protectedLength = output.Position - start;
        if (o.RecoveryPercent > 0)
        {
            output.Flush();
            recOffset = protectedLength;
            recLength = RecoveryRecord.Build(output, protectedLength, o.RecoveryPercent, start);
        }

        // --- pie ---
        long total = (output.Position - start) + MdrFormat.FooterSize;
        output.Seek(0, SeekOrigin.End);
        output.Write(new MdrFooter(total, dirOffset, dirLength, recOffset, recLength).ToBytes());
        output.Flush();
    }

    private static MdrEntry FileEntry(SourceItem it) => new()
    {
        P = it.EntryPath.Replace('\\', '/').Trim('/'),
        T = it.Modified.ToUniversalTime().Ticks,
    };

    private static MdrEntry DirEntry(SourceItem it) => new()
    {
        P = it.EntryPath.Replace('\\', '/').Trim('/'),
        D = true,
        T = it.Modified.ToUniversalTime().Ticks,
    };
}
