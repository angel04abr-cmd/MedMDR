using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Medrar.Core;

public sealed record SearchHit(string Container, string Item, string Detail);

public sealed class SearchQuery
{
    public string Text { get; set; } = "";
    public bool CaseSensitive { get; set; }
    public bool Hex { get; set; }
    /// <summary>Máscara de nombres para la búsqueda en carpetas (ej. *.txt). Vacío = todos.</summary>
    public string Mask { get; set; } = "";
    public bool IncludeSubfolders { get; set; } = true;
    public bool InsideArchives { get; set; } = true;
    public long MaxFileBytes { get; set; } = 64L * 1024 * 1024;
}

public static class Finder
{
    private static byte[]? ParseHex(string s)
    {
        var clean = new string(s.Where(Uri.IsHexDigit).ToArray());
        if (clean.Length == 0 || clean.Length % 2 != 0) return null;
        return Convert.FromHexString(clean);
    }

    /// <summary>Busca texto o bytes en los datos. Devuelve una descripción de la primera coincidencia o null.</summary>
    public static string? SearchBytes(byte[] data, SearchQuery q)
    {
        if (q.Hex)
        {
            var pat = ParseHex(q.Text) ?? throw new MedrarException("Patrón hexadecimal no válido (usa pares de dígitos, ej. 4D 5A).");
            int i = data.AsSpan().IndexOf(pat);
            return i < 0 ? null : $"desplazamiento 0x{i:X}";
        }
        var cmp = q.CaseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        string utf8 = Encoding.UTF8.GetString(data);
        int idx = utf8.IndexOf(q.Text, cmp);
        if (idx >= 0) return Context(utf8, idx, q.Text.Length);
        // UTF-16 (los .txt guardados desde algunos programas de Windows)
        string utf16 = Encoding.Unicode.GetString(data);
        idx = utf16.IndexOf(q.Text, cmp);
        if (idx >= 0) return Context(utf16, idx, q.Text.Length);
        string ansi = Encoding.Latin1.GetString(data);
        idx = ansi.IndexOf(q.Text, cmp);
        return idx >= 0 ? Context(ansi, idx, q.Text.Length) : null;
    }

    private static string Context(string s, int idx, int len)
    {
        int a = Math.Max(0, idx - 24), b = Math.Min(s.Length, idx + len + 24);
        var line = s[a..b].Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        int lineNo = s.AsSpan(0, idx).Count('\n') + 1;
        return $"línea {lineNo}: …{line}…";
    }

    public static List<SearchHit> SearchArchive(IArchive archive, SearchQuery q, ProgressReporter? progress = null)
    {
        var hits = new List<SearchHit>();
        var files = archive.Entries.Where(e => !e.IsDirectory).ToList();
        progress ??= ProgressReporter.None;
        progress.FilesTotal = files.Count;
        progress.Total = files.Sum(f => f.Size);
        foreach (var e in files)
        {
            progress.Token.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(q.Mask) && !Glob.MatchesAny(e.Path, q.Mask.Split(';', ',')))
            { progress.Add(e.Size); continue; }
            if (e.Size > q.MaxFileBytes) { progress.Add(e.Size); continue; }
            try
            {
                var data = archive.ReadAll(e);
                var r = SearchBytes(data, q);
                if (r != null) hits.Add(new SearchHit(archive.Path, e.Path, r));
            }
            catch (MedrarException) { }
            progress.Add(0);
        }
        return hits;
    }

    /// <summary>Busca dentro de ficheros de una carpeta (y de los archivos comprimidos que contenga).</summary>
    public static List<SearchHit> SearchFolder(string folder, SearchQuery q, ProgressReporter? progress = null, string? password = null)
    {
        var hits = new List<SearchHit>();
        progress ??= ProgressReporter.None;
        var opt = new EnumerationOptions { RecurseSubdirectories = q.IncludeSubfolders, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var masks = string.IsNullOrWhiteSpace(q.Mask) ? null : q.Mask.Split(';', ',');
        foreach (var f in Directory.EnumerateFiles(folder, "*", opt))
        {
            progress.Token.ThrowIfCancellationRequested();
            progress.Begin(f);
            try
            {
                var fi = new FileInfo(f);
                if (masks != null && !Glob.MatchesAny(fi.Name, masks)) continue;
                if (q.InsideArchives && ArchiveOpener.LooksLikeArchive(f))
                {
                    try
                    {
                        using var a = ArchiveOpener.Open(f, password);
                        hits.AddRange(SearchArchive(a, new SearchQuery { Text = q.Text, CaseSensitive = q.CaseSensitive, Hex = q.Hex, MaxFileBytes = q.MaxFileBytes }));
                    }
                    catch (MedrarException) { }
                    catch (Exception) { }
                    continue;
                }
                if (fi.Length > q.MaxFileBytes) continue;
                var data = File.ReadAllBytes(f);
                var r = SearchBytes(data, q);
                if (r != null) hits.Add(new SearchHit(folder, f, r));
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return hits;
    }
}

public static class ArchiveReport
{
    public static string FormatSize(long bytes)
    {
        string[] u = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes; int i = 0;
        while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : v.ToString(v >= 100 ? "0" : "0.#", CultureInfo.CurrentCulture) + " " + u[i];
    }

    public static string Summary(IArchive a)
    {
        var files = a.Entries.Where(e => !e.IsDirectory).ToList();
        long size = files.Sum(f => f.Size), packed = a.ArchiveSize;
        var sb = new StringBuilder();
        sb.AppendLine($"Archivo:       {a.Path}");
        sb.AppendLine($"Formato:       {a.FormatName}");
        sb.AppendLine($"Tamaño:        {FormatSize(a.ArchiveSize)} ({a.ArchiveSize:N0} bytes)");
        sb.AppendLine($"Ficheros:      {files.Count:N0}    Carpetas: {a.Entries.Count(e => e.IsDirectory):N0}");
        sb.AppendLine($"Tamaño total:  {FormatSize(size)} ({size:N0} bytes)");
        sb.AppendLine($"Ratio:         {(size > 0 ? (100.0 * packed / size).ToString("0.0", CultureInfo.CurrentCulture) : "0")} %");
        sb.AppendLine($"Sólido:        {(a.IsSolid ? "Sí" : "No")}");
        sb.AppendLine($"Cifrado:       {(a.IsEncrypted ? (a.HasEncryptedNames ? "Sí (también los nombres)" : "Sí") : "No")}");
        sb.AppendLine($"Bloqueado:     {(a.IsLocked ? "Sí" : "No")}");
        sb.AppendLine($"Recuperación:  {(a.HasRecovery ? "Sí" : "No")}");
        if (a is MdrArchive m)
        {
            sb.AppendLine($"Método:        {m.MethodName}");
            sb.AppendLine($"Creado:        {m.Created:g}");
        }
        if (!string.IsNullOrWhiteSpace(a.Comment)) { sb.AppendLine(); sb.AppendLine("Comentario:"); sb.AppendLine(a.Comment); }
        return sb.ToString();
    }

    public static string Text(IArchive a)
    {
        var sb = new StringBuilder(Summary(a));
        sb.AppendLine();
        sb.AppendLine("Nombre\tTamaño\tComprimido\tModificado\tCRC32");
        foreach (var e in a.Entries.Where(x => !x.IsDirectory))
            sb.AppendLine($"{e.Path}\t{e.Size}\t{e.PackedSize}\t{e.Modified:yyyy-MM-dd HH:mm}\t{(e.Crc.HasValue ? e.Crc.Value.ToString("X8") : "")}");
        return sb.ToString();
    }

    public static string Html(IArchive a)
    {
        string E(string s) => System.Net.WebUtility.HtmlEncode(s);
        var sb = new StringBuilder();
        sb.AppendLine("<!doctype html><html lang=\"es\"><meta charset=\"utf-8\"><title>Informe de archivo</title>");
        sb.AppendLine("<style>body{font-family:Segoe UI,sans-serif;margin:2rem;color:#222}table{border-collapse:collapse;width:100%}td,th{border-bottom:1px solid #ddd;padding:4px 8px;text-align:left}th{background:#f3f3f3}td.n{text-align:right;font-variant-numeric:tabular-nums}pre{background:#f7f7f7;padding:1rem}</style>");
        sb.AppendLine($"<h1>Informe de {E(System.IO.Path.GetFileName(a.Path))}</h1><pre>{E(Summary(a))}</pre>");
        sb.AppendLine("<table><tr><th>Nombre</th><th>Tamaño</th><th>Comprimido</th><th>Modificado</th><th>CRC32</th></tr>");
        foreach (var e in a.Entries.Where(x => !x.IsDirectory))
            sb.AppendLine($"<tr><td>{E(e.Path)}</td><td class=n>{e.Size:N0}</td><td class=n>{e.PackedSize:N0}</td><td>{e.Modified:yyyy-MM-dd HH:mm}</td><td>{(e.Crc.HasValue ? e.Crc.Value.ToString("X8") : "")}</td></tr>");
        sb.AppendLine("</table></html>");
        return sb.ToString();
    }
}

public sealed record BenchmarkResult(string Method, int Level, double CompressMBps, double DecompressMBps, double RatioPercent);

public static class Benchmark
{
    /// <summary>Mide velocidad y ratio con datos sintéticos parecidos a texto.</summary>
    public static List<BenchmarkResult> Run(int megabytes = 8, IProgress<string>? log = null, CancellationToken token = default)
    {
        var data = MakeData(megabytes * 1024 * 1024);
        var results = new List<BenchmarkResult>();
        foreach (var (method, name) in new[] { (Codec.Deflate, "Deflate"), (Codec.Brotli, "Brotli") })
        {
            foreach (int level in new[] { 1, 3, 5 })
            {
                token.ThrowIfCancellationRequested();
                log?.Report($"{name}, nivel {level}…");
                var ms = new MemoryStream();
                var sw = Stopwatch.StartNew();
                using (var c = Codec.CreateCompressor(method, ms, level, 0)) c.Write(data);
                sw.Stop();
                double cs = megabytes / Math.Max(sw.Elapsed.TotalSeconds, 1e-6);
                ms.Position = 0;
                sw.Restart();
                using (var d = Codec.CreateDecompressor(method, ms))
                {
                    var buf = new byte[256 * 1024];
                    while (d.Read(buf, 0, buf.Length) > 0) { }
                }
                sw.Stop();
                double ds = megabytes / Math.Max(sw.Elapsed.TotalSeconds, 1e-6);
                results.Add(new BenchmarkResult(name, level, cs, ds, 100.0 * ms.Length / data.Length));
            }
        }
        return results;
    }

    private static byte[] MakeData(int size)
    {
        var rnd = new Random(12345);
        string[] words = { "archivo", "compresión", "datos", "memoria", "sistema", "usuario", "carpeta", "ventana", "programa", "guardar",
                           "abrir", "extraer", "contraseña", "volumen", "registro", "recuperación", "formato", "bloque", "cifrado", "texto" };
        var sb = new StringBuilder(size + 64);
        while (sb.Length < size)
        {
            sb.Append(words[rnd.Next(words.Length)]);
            sb.Append(rnd.Next(12) == 0 ? '\n' : ' ');
            if (rnd.Next(40) == 0) sb.Append(rnd.Next(100000));
        }
        return Encoding.UTF8.GetBytes(sb.ToString(0, size));
    }
}
