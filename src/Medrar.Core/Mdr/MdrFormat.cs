using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Medrar.Core;

/*
 * Formato .mdr (diseño propio de Medrar, versión 1)
 * ------------------------------------------------
 *   [Cabecera]   "MEDRAR\x1A\x01" | u32 longitud | JSON de la cabecera | u32 CRC32 del JSON
 *   [Datos]      Un flujo de bytes: ficheros comprimidos uno tras otro, o un único flujo comprimido (modo sólido).
 *                Si hay contraseña, todo el flujo va cifrado con AES-256-GCM en bloques de 64 KB.
 *   [Directorio] JSON (lista de entradas + comentario) comprimido con Brotli; cifrado si se piden nombres cifrados.
 *   [Recuperación] Opcional. Bloques de paridad XOR + CRC32 de cada bloque para reparar daños.
 *   [Pie]        48 bytes con las posiciones de todo lo anterior. El pie va siempre al final, así que un
 *                archivo autoextraíble (.exe + archivo) se localiza leyendo solo el final del fichero.
 */
internal static class MdrFormat
{
    public static readonly byte[] Magic = { (byte)'M', (byte)'E', (byte)'D', (byte)'R', (byte)'A', (byte)'R', 0x1A, 0x01 };
    public static readonly byte[] FooterMagic = { (byte)'M', (byte)'D', (byte)'R', (byte)'E', (byte)'N', (byte)'D', 0x00, 0x01 };
    public const int FooterSize = 48;
    public const uint PurposePayload = 1;
    public const uint PurposeDirectory = 2;

    public static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = null
    };
}

internal sealed class MdrHeader
{
    public int V { get; set; } = 1;
    public string App { get; set; } = "Medrar";
    public long Created { get; set; }
    public bool Solid { get; set; }
    public bool Encrypted { get; set; }
    public bool EncryptedNames { get; set; }
    public bool Locked { get; set; }
    public int Method { get; set; }
    public string? Salt { get; set; }
    public int Iter { get; set; }
    public string? Check { get; set; }
}

internal sealed class MdrDirectory
{
    public string? Comment { get; set; }
    public long PayloadLength { get; set; }
    public List<MdrEntry> Entries { get; set; } = new();
}

internal sealed class MdrEntry
{
    public string P { get; set; } = "";
    public bool D { get; set; }
    public long S { get; set; }
    public long Z { get; set; }
    public long O { get; set; }
    public int M { get; set; }
    public uint C { get; set; }
    public long T { get; set; }
}

internal readonly record struct MdrFooter(long ArchiveLength, long DirOffset, long DirLength, long RecOffset, long RecLength)
{
    public byte[] ToBytes()
    {
        var b = new byte[MdrFormat.FooterSize];
        MdrFormat.FooterMagic.CopyTo(b, 0);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(8), ArchiveLength);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(16), DirOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(24), (uint)DirLength);
        BinaryPrimitives.WriteInt64LittleEndian(b.AsSpan(28), RecOffset);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(36), (uint)RecLength);
        BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(40), StreamUtil.Crc(b.AsSpan(0, 40)));
        return b;
    }

    public static MdrFooter? TryParse(ReadOnlySpan<byte> b)
    {
        if (b.Length < MdrFormat.FooterSize) return null;
        if (!b[..8].SequenceEqual(MdrFormat.FooterMagic)) return null;
        if (BinaryPrimitives.ReadUInt32LittleEndian(b[40..]) != StreamUtil.Crc(b[..40])) return null;
        return new MdrFooter(
            BinaryPrimitives.ReadInt64LittleEndian(b[8..]),
            BinaryPrimitives.ReadInt64LittleEndian(b[16..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[24..]),
            BinaryPrimitives.ReadInt64LittleEndian(b[28..]),
            BinaryPrimitives.ReadUInt32LittleEndian(b[36..]));
    }

    /// <summary>Lee el pie al final de un flujo; devuelve también dónde empieza el archivo (por si va tras un .exe).</summary>
    public static (MdrFooter Footer, long BaseOffset)? Locate(Stream raw)
    {
        if (raw.Length < MdrFormat.FooterSize + MdrFormat.Magic.Length) return null;
        raw.Seek(-MdrFormat.FooterSize, SeekOrigin.End);
        var buf = new byte[MdrFormat.FooterSize];
        raw.ReadExactly(buf);
        var f = TryParse(buf);
        if (f == null) return null;
        long baseOffset = raw.Length - f.Value.ArchiveLength;
        if (baseOffset < 0) return null;
        return (f.Value, baseOffset);
    }
}
