using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Medrar.Core;

/// <summary>
/// Criptografía del formato .mdr: PBKDF2-HMAC-SHA256 para derivar la clave y AES-256-GCM por bloques de 64 KB
/// (cada bloque se autentica, así que cualquier manipulación o truncado se detecta).
/// </summary>
internal static class MdrCrypto
{
    public const int SaltSize = 16;
    public const int KeySize = 32;
    public const int TagSize = 16;
    public const int ChunkSize = 64 * 1024;
    public const int DefaultIterations = 200_000;

    public static byte[] NewSalt() => RandomNumberGenerator.GetBytes(SaltSize);

    /// <summary>Devuelve (clave AES de 32 bytes, valor de comprobación de 32 bytes).</summary>
    public static (byte[] Key, byte[] Check) Derive(string password, byte[] salt, int iterations)
    {
        var pw = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormC));
        var d = Rfc2898DeriveBytes.Pbkdf2(pw, salt, iterations, HashAlgorithmName.SHA256, 64);
        CryptographicOperations.ZeroMemory(pw);
        return (d[..32], d[32..]);
    }

    public static bool CheckMatches(byte[] a, byte[] b) => CryptographicOperations.FixedTimeEquals(a, b);

    internal static void MakeNonce(Span<byte> nonce, uint purpose, ulong index)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(nonce[..4], purpose);
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[4..], index);
    }

    internal static void MakeAad(Span<byte> aad, uint purpose, ulong index, bool last)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(aad[..4], purpose);
        BinaryPrimitives.WriteUInt64LittleEndian(aad[4..12], index);
        aad[12] = last ? (byte)1 : (byte)0;
    }

    /// <summary>Tamaño físico de un texto cifrado de longitud lógica <paramref name="logical"/>.</summary>
    public static long PhysicalLength(long logical)
    {
        long chunks = Math.Max(1, (logical + ChunkSize - 1) / ChunkSize);
        return logical + chunks * TagSize;
    }
}

/// <summary>Flujo de escritura que cifra por bloques. Debe cerrarse (Dispose) para escribir el último bloque.</summary>
internal sealed class ChunkedAesWriteStream : Stream
{
    private readonly Stream _inner;
    private readonly AesGcm _aes;
    private readonly uint _purpose;
    private readonly byte[] _buf = new byte[MdrCrypto.ChunkSize];
    private int _fill;
    private ulong _index;
    private bool _closed;

    public ChunkedAesWriteStream(Stream inner, byte[] key, uint purpose)
    {
        _inner = inner;
        _aes = new AesGcm(key, MdrCrypto.TagSize);
        _purpose = purpose;
    }

    public override void Write(byte[] buffer, int offset, int count) => Write(buffer.AsSpan(offset, count));

    public override void Write(ReadOnlySpan<byte> data)
    {
        while (!data.IsEmpty)
        {
            if (_fill == _buf.Length) EmitChunk(last: false);
            int n = Math.Min(data.Length, _buf.Length - _fill);
            data[..n].CopyTo(_buf.AsSpan(_fill));
            _fill += n;
            data = data[n..];
        }
    }

    private void EmitChunk(bool last)
    {
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[13];
        MdrCrypto.MakeNonce(nonce, _purpose, _index);
        MdrCrypto.MakeAad(aad, _purpose, _index, last);
        var cipher = new byte[_fill];
        Span<byte> tag = stackalloc byte[MdrCrypto.TagSize];
        _aes.Encrypt(nonce, _buf.AsSpan(0, _fill), cipher, tag, aad);
        _inner.Write(cipher);
        _inner.Write(tag);
        _fill = 0;
        _index++;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_closed)
        {
            _closed = true;
            EmitChunk(last: true);
            _aes.Dispose();
        }
        base.Dispose(disposing);
    }

    public override void Flush() { }
    public override bool CanRead => false;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
}

/// <summary>Lectura con acceso aleatorio sobre datos cifrados por bloques.</summary>
internal sealed class ChunkedAesReadStream : Stream
{
    private readonly Stream _inner;
    private readonly long _start;
    private readonly long _logicalLength;
    private readonly AesGcm _aes;
    private readonly uint _purpose;
    private long _pos;
    private long _cachedIndex = -1;
    private byte[] _cache = Array.Empty<byte>();

    public ChunkedAesReadStream(Stream inner, long physicalStart, long logicalLength, byte[] key, uint purpose)
    {
        _inner = inner;
        _start = physicalStart;
        _logicalLength = logicalLength;
        _aes = new AesGcm(key, MdrCrypto.TagSize);
        _purpose = purpose;
    }

    private long ChunkCount => Math.Max(1, (_logicalLength + MdrCrypto.ChunkSize - 1) / MdrCrypto.ChunkSize);

    private void Load(long index)
    {
        if (_cachedIndex == index) return;
        long plainLen = Math.Min(MdrCrypto.ChunkSize, _logicalLength - index * MdrCrypto.ChunkSize);
        if (plainLen < 0) plainLen = 0;
        long phys = _start + index * (MdrCrypto.ChunkSize + MdrCrypto.TagSize);
        _inner.Seek(phys, SeekOrigin.Begin);
        var cipher = new byte[plainLen];
        _inner.ReadExactly(cipher);
        var tag = new byte[MdrCrypto.TagSize];
        _inner.ReadExactly(tag);
        Span<byte> nonce = stackalloc byte[12];
        Span<byte> aad = stackalloc byte[13];
        MdrCrypto.MakeNonce(nonce, _purpose, (ulong)index);
        MdrCrypto.MakeAad(aad, _purpose, (ulong)index, index == ChunkCount - 1);
        var plain = new byte[plainLen];
        try { _aes.Decrypt(nonce, cipher, tag, plain, aad); }
        catch (CryptographicException) { throw new WrongPasswordException(); }
        _cache = plain;
        _cachedIndex = index;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (!buffer.IsEmpty && _pos < _logicalLength)
        {
            long idx = _pos / MdrCrypto.ChunkSize;
            Load(idx);
            int off = (int)(_pos - idx * MdrCrypto.ChunkSize);
            int n = Math.Min(buffer.Length, _cache.Length - off);
            if (n <= 0) break;
            _cache.AsSpan(off, n).CopyTo(buffer);
            buffer = buffer[n..];
            _pos += n;
            total += n;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _pos = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _pos + offset,
            _ => _logicalLength + offset
        };
        return _pos;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Length => _logicalLength;
    public override long Position { get => _pos; set => _pos = value; }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing) { if (disposing) _aes.Dispose(); base.Dispose(disposing); }
}
