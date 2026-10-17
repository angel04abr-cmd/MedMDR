using System.Text.RegularExpressions;

namespace Medrar.Core;

/// <summary>Varios ficheros (.001, .002, ...) leídos como si fueran uno solo.</summary>
internal sealed class JoinedStream : Stream
{
    private readonly FileStream[] _parts;
    private readonly long[] _starts;
    private long _pos;
    public override long Length { get; }

    public JoinedStream(IReadOnlyList<string> paths)
    {
        _parts = paths.Select(p => new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete)).ToArray();
        _starts = new long[_parts.Length];
        long acc = 0;
        for (int i = 0; i < _parts.Length; i++) { _starts[i] = acc; acc += _parts[i].Length; }
        Length = acc;
    }

    public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        int total = 0;
        while (!buffer.IsEmpty && _pos < Length)
        {
            int i = Array.FindLastIndex(_starts, s => s <= _pos);
            var part = _parts[i];
            part.Seek(_pos - _starts[i], SeekOrigin.Begin);
            int n = part.Read(buffer);
            if (n <= 0) break;
            buffer = buffer[n..];
            _pos += n;
            total += n;
        }
        return total;
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        _pos = origin switch { SeekOrigin.Begin => offset, SeekOrigin.Current => _pos + offset, _ => Length + offset };
        return _pos;
    }

    public override bool CanRead => true;
    public override bool CanSeek => true;
    public override bool CanWrite => false;
    public override long Position { get => _pos; set => _pos = value; }
    public override void Flush() { }
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    protected override void Dispose(bool disposing)
    {
        if (disposing) foreach (var p in _parts) p.Dispose();
        base.Dispose(disposing);
    }
}

public static class VolumeSet
{
    private static readonly Regex Pattern = new(@"^(?<base>.+)\.(?<n>\d{3})$", RegexOptions.Compiled);

    public static bool IsVolumeName(string path) => Pattern.IsMatch(path);

    /// <summary>Si <paramref name="path"/> es un volumen (.001, .002...) devuelve todos los volúmenes en orden.</summary>
    public static IReadOnlyList<string>? FindParts(string path)
    {
        var m = Pattern.Match(path);
        if (!m.Success) return null;
        string b = m.Groups["base"].Value;
        var list = new List<string>();
        for (int i = 1; ; i++)
        {
            string p = $"{b}.{i:000}";
            if (!File.Exists(p)) break;
            list.Add(p);
        }
        return list.Count == 0 ? null : list;
    }

    /// <summary>Parte <paramref name="archivePath"/> en volúmenes de <paramref name="size"/> bytes y borra el original.
    /// Devuelve la lista de volúmenes creados.</summary>
    public static IReadOnlyList<string> Split(string archivePath, long size, ProgressReporter? progress = null)
    {
        if (size < 1024) throw new MedrarException("El tamaño de volumen mínimo es 1 KB.");
        var created = new List<string>();
        using (var src = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            long left = src.Length;
            int idx = 1;
            while (left > 0)
            {
                string part = $"{archivePath}.{idx:000}";
                using (var dst = new FileStream(part, FileMode.Create, FileAccess.Write))
                {
                    long n = Math.Min(size, left);
                    StreamUtil.CopyExactly(src, dst, n);
                    left -= n;
                }
                created.Add(part);
                idx++;
                progress?.Token.ThrowIfCancellationRequested();
            }
            if (created.Count == 0) created.Add(archivePath + ".001");   // archivo vacío: 1 volumen vacío
        }
        File.Delete(archivePath);
        return created;
    }

    public static void DeleteAll(string anyPart)
    {
        var parts = FindParts(anyPart);
        if (parts == null) { if (File.Exists(anyPart)) File.Delete(anyPart); return; }
        foreach (var p in parts) File.Delete(p);
    }
}
