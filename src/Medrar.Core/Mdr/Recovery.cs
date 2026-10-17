using System.Buffers.Binary;
using System.Text;

namespace Medrar.Core;

public sealed record RepairResult(bool Success, int BlocksChecked, int BlocksDamaged, int BlocksRepaired, string Message, string? OutputPath);

/// <summary>Aritmética en GF(2⁸) (polinomio 0x11D) para los códigos de recuperación.</summary>
internal static class Gf256
{
    private static readonly byte[] Exp = new byte[512];
    private static readonly byte[] Log = new byte[256];
    private static readonly byte[][] Tables = new byte[256][];

    static Gf256()
    {
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            Exp[i] = (byte)x;
            Log[x] = (byte)i;
            x <<= 1;
            if ((x & 0x100) != 0) x ^= 0x11D;
        }
        for (int i = 255; i < 512; i++) Exp[i] = Exp[i - 255];
    }

    public static byte Mul(byte a, byte b) => a == 0 || b == 0 ? (byte)0 : Exp[Log[a] + Log[b]];
    public static byte Inv(byte a) => a == 0 ? throw new DivideByZeroException() : Exp[255 - Log[a]];

    /// <summary>Tabla de «multiplicar por c» para todos los bytes (se calcula una vez por coeficiente).</summary>
    public static byte[] Table(byte c)
    {
        var t = Tables[c];
        if (t != null) return t;
        t = new byte[256];
        for (int v = 0; v < 256; v++) t[v] = Mul(c, (byte)v);
        return Tables[c] = t;
    }

    /// <summary>dst ^= c · src</summary>
    public static void MulAdd(byte c, ReadOnlySpan<byte> src, Span<byte> dst)
    {
        if (c == 0) return;
        if (c == 1) { for (int i = 0; i < src.Length; i++) dst[i] ^= src[i]; return; }
        var t = Table(c);
        for (int i = 0; i < src.Length; i++) dst[i] ^= t[src[i]];
    }

    /// <summary>Coeficiente de la matriz de Cauchy: fila de paridad i, bloque de datos j (G = bloques de datos por grupo).</summary>
    public static byte Coef(int g, int i, int j) => Inv((byte)((g + i) ^ j));
}

/// <summary>
/// Registro de recuperación (códigos Reed-Solomon sobre bloques de 16 KB). Cada grupo de hasta ~200 bloques lleva
/// M bloques de paridad: se pueden reconstruir hasta M bloques dañados (o perdidos) de ese grupo, estén donde estén.
/// Va dentro del archivo .mdr o, para cualquier otro formato, en un fichero aparte <c>nombre.ext.mdrrec</c>.
/// </summary>
public static class RecoveryRecord
{
    private const int BlockSize = 16 * 1024;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("MDRRC\0\x02\0");
    public const string SidecarExtension = ".mdrrec";

    public static string SidecarPathFor(string archivePath) => archivePath + SidecarExtension;

    /// <summary>Bloques de datos por grupo para un porcentaje dado (G + paridad ≤ 256).</summary>
    internal static int GroupSize(int percent)
    {
        percent = Math.Clamp(percent, 1, 100);
        int g = (int)Math.Min(200, Math.Floor(256.0 * 100 / (100 + percent)));
        while (g + ParityFor(g, percent) > 256) g--;
        return g;
    }

    /// <summary>Bloques de paridad de un grupo con <paramref name="k"/> bloques de datos.</summary>
    internal static int ParityFor(int k, int percent) => Math.Max(Math.Min(k, 2), (int)Math.Ceiling(k * Math.Clamp(percent, 1, 100) / 100.0));   // al menos 2 (un archivo pequeño no se queda sin protección real)

    // ------------------------------------------------------------------ construir

    /// <summary>Calcula el registro de los primeros <paramref name="length"/> bytes (desde <paramref name="baseOffset"/>) de <paramref name="file"/>.</summary>
    internal static byte[] BuildBytes(Stream file, long length, int percent, long baseOffset = 0, ProgressReporter? progress = null)
    {
        percent = Math.Clamp(percent, 1, 100);
        int G = GroupSize(percent);
        int nBlocks = (int)((length + BlockSize - 1) / BlockSize);
        int nGroups = (nBlocks + G - 1) / G;
        var crcs = new uint[nBlocks];
        var parityBlocks = new List<byte[]>();
        var data = new byte[G][];
        for (int i = 0; i < G; i++) data[i] = new byte[BlockSize];

        file.Seek(baseOffset, SeekOrigin.Begin);
        for (int g = 0; g < nGroups; g++)
        {
            int first = g * G, k = Math.Min(G, nBlocks - first);
            for (int j = 0; j < k; j++)
            {
                int want = (int)Math.Min(BlockSize, length - (long)(first + j) * BlockSize);
                Array.Clear(data[j]);
                file.ReadExactly(data[j], 0, want);
                crcs[first + j] = StreamUtil.Crc(data[j]);
                progress?.Add(want);
            }
            int mg = ParityFor(k, percent);
            var par = new byte[mg][];
            Parallel.For(0, mg, i =>
            {
                var p = new byte[BlockSize];
                for (int j = 0; j < k; j++) Gf256.MulAdd(Gf256.Coef(G, i, j), data[j], p);
                par[i] = p;
            });
            parityBlocks.AddRange(par);
        }

        using var ms = new MemoryStream();
        ms.Write(Magic);
        var t = new byte[8];
        void U32(uint v) { BinaryPrimitives.WriteUInt32LittleEndian(t, v); ms.Write(t, 0, 4); }
        U32(BlockSize); U32((uint)G); U32((uint)percent); U32((uint)nBlocks);
        BinaryPrimitives.WriteInt64LittleEndian(t, length); ms.Write(t, 0, 8);
        foreach (var c in crcs) U32(c);
        foreach (var p in parityBlocks) ms.Write(p);
        foreach (var p in parityBlocks) U32(StreamUtil.Crc(p));
        var body = ms.ToArray();
        var res = new byte[body.Length + 4];
        body.CopyTo(res, 0);
        BinaryPrimitives.WriteUInt32LittleEndian(res.AsSpan(body.Length), StreamUtil.Crc(body));
        return res;
    }

    /// <summary>Escribe el registro justo detrás de los datos protegidos (dentro del .mdr). Devuelve su tamaño.</summary>
    internal static long Build(Stream file, long length, int percent, long baseOffset = 0)
    {
        var rec = BuildBytes(file, length, percent, baseOffset);
        file.Seek(baseOffset + length, SeekOrigin.Begin);
        file.Write(rec);
        return rec.Length;
    }

    /// <summary>Crea (o reemplaza) el fichero de recuperación de cualquier archivo. Devuelve su ruta.</summary>
    public static string CreateSidecar(string archivePath, int percent, ProgressReporter? progress = null)
    {
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (progress != null) { progress.Total = fs.Length; }
        var rec = BuildBytes(fs, fs.Length, percent, 0, progress);
        string side = SidecarPathFor(archivePath);
        if (File.Exists(side)) { var a = File.GetAttributes(side); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(side, a & ~FileAttributes.ReadOnly); }
        File.WriteAllBytes(side, rec);
        return side;
    }

    public static void DeleteSidecar(string archivePath)
    {
        try { string s = SidecarPathFor(archivePath); if (File.Exists(s)) File.Delete(s); } catch { }
    }

    /// <summary>Porcentaje del fichero de recuperación que acompaña a un archivo (0 si no hay).</summary>
    public static int SidecarPercent(string archivePath)
    {
        try
        {
            string s = SidecarPathFor(archivePath);
            if (!File.Exists(s)) return 0;
            using var fs = File.OpenRead(s);
            var h = new byte[20];
            if (fs.Read(h, 0, 20) < 20 || !h.AsSpan(0, 8).SequenceEqual(Magic)) return 0;
            return (int)Math.Clamp(BinaryPrimitives.ReadUInt32LittleEndian(h.AsSpan(16)), 0, 100);
        }
        catch { return 0; }
    }

    // ------------------------------------------------------------------ leer

    private sealed class Parsed
    {
        public int G, Pct, NBlocks; public long Length;
        public uint[] Crcs = Array.Empty<uint>();
        public byte[][] Parity = Array.Empty<byte[]>();
        public uint[] ParityCrcs = Array.Empty<uint>();
        public int GroupCount => (NBlocks + G - 1) / G;
        public int ParityOffset(int group) { int off = 0; for (int g = 0; g < group; g++) off += ParityCount(g); return off; }
        public int ParityCount(int group) => ParityFor(Math.Min(G, NBlocks - group * G), Pct);
    }

    private static Parsed? Parse(byte[] rec)
    {
        if (rec.Length < 40 || !rec.AsSpan(0, 8).SequenceEqual(Magic)) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(rec.Length - 4)) != StreamUtil.Crc(rec.AsSpan(0, rec.Length - 4))) return null;
        var p = new Parsed();
        int pos = 8;
        if ((int)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(pos)) != BlockSize) return null; pos += 4;
        p.G = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(pos)); pos += 4;
        p.Pct = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(pos)); pos += 4;
        p.NBlocks = (int)BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(pos)); pos += 4;
        p.Length = BinaryPrimitives.ReadInt64LittleEndian(rec.AsSpan(pos)); pos += 8;
        if (p.G <= 0 || p.Pct <= 0 || p.Pct > 100 || p.G + ParityFor(p.G, p.Pct) > 256) return null;
        p.Crcs = new uint[p.NBlocks];
        for (int i = 0; i < p.NBlocks; i++) { p.Crcs[i] = BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(pos)); pos += 4; }
        int nPar = 0;
        for (int g = 0; g < p.GroupCount; g++) nPar += p.ParityCount(g);
        if (rec.Length < pos + (long)nPar * (BlockSize + 4) + 4) return null;
        p.Parity = new byte[nPar][];
        for (int i = 0; i < nPar; i++) { p.Parity[i] = rec.AsSpan(pos, BlockSize).ToArray(); pos += BlockSize; }
        p.ParityCrcs = new uint[nPar];
        for (int i = 0; i < nPar; i++) { p.ParityCrcs[i] = BinaryPrimitives.ReadUInt32LittleEndian(rec.AsSpan(pos)); pos += 4; }
        return p;
    }

    /// <summary>Localiza el registro: el fichero aparte si existe, o el que lleva dentro un .mdr.</summary>
    private static Parsed? ReadRecord(string archivePath, string? sidecar, out long baseOffset, out bool isSidecar, out string? error)
    {
        baseOffset = 0; isSidecar = false; error = null;
        sidecar ??= SidecarPathFor(archivePath);
        if (File.Exists(sidecar))
        {
            isSidecar = true;
            var parsed = Parse(File.ReadAllBytes(sidecar));
            if (parsed == null) error = "El fichero de recuperación (.mdrrec) está dañado.";
            return parsed;
        }
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var loc = MdrFooter.Locate(fs);
        if (loc == null)
        {
            error = "Este archivo no tiene registro de recuperación (ni fichero .mdrrec a su lado), o su final está dañado o truncado.";
            return null;
        }
        var (footer, bo) = loc.Value;
        baseOffset = bo;
        if (footer.RecLength <= 0) { error = "Este archivo no tiene registro de recuperación."; return null; }
        fs.Seek(bo + footer.RecOffset, SeekOrigin.Begin);
        var rec = new byte[footer.RecLength];
        fs.ReadExactly(rec);
        var p = Parse(rec);
        if (p == null) error = "El propio registro de recuperación está dañado.";
        return p;
    }

    /// <summary>Bloques cuyo contenido no coincide (los que faltan por haberse truncado el fichero cuentan como dañados).</summary>
    private static List<int> Scan(Stream fs, long bo, Parsed p, ProgressReporter? progress)
    {
        var damaged = new List<int>();
        var buf = new byte[BlockSize];
        for (int i = 0; i < p.NBlocks; i++)
        {
            long off = (long)i * BlockSize;
            int want = (int)Math.Min(BlockSize, p.Length - off);
            Array.Clear(buf);
            int got = 0;
            if (bo + off < fs.Length)
            {
                fs.Seek(bo + off, SeekOrigin.Begin);
                got = fs.Read(buf, 0, (int)Math.Min(want, fs.Length - (bo + off)));
                while (got > 0 && got < want) { int n = fs.Read(buf, got, want - got); if (n <= 0) break; got += n; }
            }
            if (got < want || StreamUtil.Crc(buf) != p.Crcs[i]) damaged.Add(i);
            progress?.Add(want);
        }
        return damaged;
    }

    /// <summary>Comprueba el archivo contra su registro de recuperación, sin modificar nada.</summary>
    public static RepairResult Test(string archivePath, string? sidecarPath = null, ProgressReporter? progress = null)
    {
        var p = ReadRecord(archivePath, sidecarPath, out long bo, out _, out string? err);
        if (p == null) return new RepairResult(false, 0, 0, 0, err!, null);
        using var fs = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var damaged = Scan(fs, bo, p, progress);
        return new RepairResult(damaged.Count == 0, p.NBlocks, damaged.Count, 0,
            damaged.Count == 0 ? "El registro de recuperación está correcto: no hay bloques dañados." : $"{damaged.Count} bloque(s) dañado(s) o ausente(s).", null);
    }

    /// <summary>Repara en una COPIA (<paramref name="outputPath"/>) y deja el original intacto.</summary>
    public static RepairResult Repair(string archivePath, string outputPath, string? sidecarPath = null, ProgressReporter? progress = null)
    {
        var p = ReadRecord(archivePath, sidecarPath, out long bo, out bool isSidecar, out string? err);
        if (p == null) return new RepairResult(false, 0, 0, 0, err!, null);
        File.Copy(archivePath, outputPath, overwrite: true);
        try { var a = File.GetAttributes(outputPath); if ((a & FileAttributes.ReadOnly) != 0) File.SetAttributes(outputPath, a & ~FileAttributes.ReadOnly); } catch { }
        using var fs = new FileStream(outputPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        long needed = bo + p.Length;
        if (isSidecar && fs.Length < needed) fs.SetLength(needed);              // archivo truncado: se alarga con ceros y se reconstruye
        var damaged = Scan(fs, bo, p, progress);
        if (damaged.Count == 0)
            return new RepairResult(true, p.NBlocks, 0, 0, "El archivo no tiene daños; se ha dejado una copia idéntica.", outputPath);

        int repaired = 0;
        foreach (var grp in damaged.GroupBy(i => i / p.G))
            repaired += RepairGroup(fs, bo, p, grp.Key, grp.ToList());
        fs.Flush();
        bool ok = repaired == damaged.Count;
        string where = isSidecar ? " con el fichero .mdrrec" : "";
        return new RepairResult(ok, p.NBlocks, damaged.Count, repaired,
            ok ? $"Reparados {repaired} bloque(s) dañado(s){where}."
               : $"Se repararon {repaired} de {damaged.Count} bloque(s) dañados{where}; el resto supera lo que el registro puede recuperar.", outputPath);
    }

    private static int RepairGroup(FileStream fs, long bo, Parsed p, int group, List<int> bad)
    {
        int first = group * p.G, k = Math.Min(p.G, p.NBlocks - first);
        int parOff = p.ParityOffset(group), mg = p.ParityCount(group);
        // paridades utilizables: las que no están dañadas
        var good = new List<int>();
        for (int i = 0; i < mg; i++) if (StreamUtil.Crc(p.Parity[parOff + i]) == p.ParityCrcs[parOff + i]) good.Add(i);
        int e = bad.Count;
        if (e > good.Count) return 0;                                     // más daño del que se puede reparar en este grupo
        var rows = good.Take(e).ToArray();
        var badLocal = bad.Select(b => b - first).ToArray();
        var badSet = badLocal.ToHashSet();

        // lado derecho: paridad ^ aportación de los bloques buenos
        var rhs = new byte[e][];
        for (int r = 0; r < e; r++) rhs[r] = (byte[])p.Parity[parOff + rows[r]].Clone();
        var buf = new byte[BlockSize];
        for (int j = 0; j < k; j++)
        {
            if (badSet.Contains(j)) continue;
            long off = (long)(first + j) * BlockSize;
            int want = (int)Math.Min(BlockSize, p.Length - off);
            Array.Clear(buf);
            fs.Seek(bo + off, SeekOrigin.Begin);
            fs.ReadExactly(buf, 0, want);
            for (int r = 0; r < e; r++) Gf256.MulAdd(Gf256.Coef(p.G, rows[r], j), buf, rhs[r]);
        }

        // matriz e×e de los bloques dañados y su inversa (Gauss-Jordan en GF(256))
        var a = new byte[e, e];
        for (int r = 0; r < e; r++) for (int c = 0; c < e; c++) a[r, c] = Gf256.Coef(p.G, rows[r], badLocal[c]);
        var inv = Invert(a, e);
        if (inv == null) return 0;

        var restored = new byte[e][];
        for (int d = 0; d < e; d++)
        {
            restored[d] = new byte[BlockSize];
            for (int r = 0; r < e; r++) Gf256.MulAdd(inv[d, r], rhs[r], restored[d]);
            if (StreamUtil.Crc(restored[d]) != p.Crcs[bad[d]]) return 0;  // no cuadra: no se escribe nada de este grupo
        }
        for (int d = 0; d < e; d++)
        {
            long off = (long)bad[d] * BlockSize;
            int len = (int)Math.Min(BlockSize, p.Length - off);
            fs.Seek(bo + off, SeekOrigin.Begin);
            fs.Write(restored[d], 0, len);
        }
        return e;
    }

    private static byte[,]? Invert(byte[,] m, int n)
    {
        var a = (byte[,])m.Clone();
        var inv = new byte[n, n];
        for (int i = 0; i < n; i++) inv[i, i] = 1;
        for (int col = 0; col < n; col++)
        {
            int pivot = -1;
            for (int r = col; r < n; r++) if (a[r, col] != 0) { pivot = r; break; }
            if (pivot < 0) return null;
            if (pivot != col)
                for (int c = 0; c < n; c++) { (a[col, c], a[pivot, c]) = (a[pivot, c], a[col, c]); (inv[col, c], inv[pivot, c]) = (inv[pivot, c], inv[col, c]); }
            byte pinv = Gf256.Inv(a[col, col]);
            for (int c = 0; c < n; c++) { a[col, c] = Gf256.Mul(a[col, c], pinv); inv[col, c] = Gf256.Mul(inv[col, c], pinv); }
            for (int r = 0; r < n; r++)
            {
                if (r == col || a[r, col] == 0) continue;
                byte f = a[r, col];
                for (int c = 0; c < n; c++) { a[r, c] ^= Gf256.Mul(f, a[col, c]); inv[r, c] ^= Gf256.Mul(f, inv[col, c]); }
            }
        }
        return inv;
    }
}
