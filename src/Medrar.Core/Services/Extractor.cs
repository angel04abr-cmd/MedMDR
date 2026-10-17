using System.Text;

namespace Medrar.Core;

public sealed class ExtractOptions
{
    public string DestinationDir { get; set; } = "";
    public PathMode PathMode { get; set; } = PathMode.Full;
    /// <summary>Con PathMode.Relative, carpeta del archivo que se toma como raíz.</summary>
    public string BaseFolder { get; set; } = "";
    public ExtractUpdateMode Update { get; set; } = ExtractUpdateMode.ExtractReplace;
    public OverwriteMode Overwrite { get; set; } = OverwriteMode.Ask;
    public bool KeepBrokenFiles { get; set; }
    public bool RestoreTimes { get; set; } = true;
    /// <summary>Se llama cuando el destino ya existe y Overwrite == Ask.</summary>
    public Func<string, OverwriteDecision>? AskOverwrite { get; set; }
}

public sealed class ExtractResult
{
    public int Extracted { get; set; }
    public int Skipped { get; set; }
    public List<string> Files { get; } = new();
    public List<(string Path, string Error)> Errors { get; } = new();
    public bool Ok => Errors.Count == 0;
}

public static class SafePath
{
    private static readonly HashSet<string> Reserved = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    };

    /// <summary>Limpia un nombre de entrada. Lanza si intenta salir de la carpeta de destino (ataque «zip slip»).</summary>
    public static string Resolve(string rootDir, string entryPath)
    {
        string root = Path.GetFullPath(rootDir);
        var segs = new List<string>();
        foreach (var raw in entryPath.Replace('\\', '/').Split('/'))
        {
            if (raw.Length == 0 || raw == ".") continue;
            if (raw == "..") throw new MedrarException($"Ruta peligrosa en el archivo: «{entryPath}».");
            var sb = new StringBuilder(raw.Length);
            foreach (char c in raw) sb.Append(Path.GetInvalidFileNameChars().Contains(c) ? '_' : c);
            string s = sb.ToString().TrimEnd(' ', '.');
            if (s.Length == 0) s = "_";
            string stem = s.Contains('.') ? s[..s.IndexOf('.')] : s;
            if (Reserved.Contains(stem)) s = "_" + s;
            segs.Add(s);
        }
        if (segs.Count == 0) throw new MedrarException("Entrada sin nombre.");
        string full = Path.GetFullPath(Path.Combine(new[] { root }.Concat(segs).ToArray()));
        string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase))
            throw new MedrarException($"Ruta peligrosa en el archivo: «{entryPath}».");
        return full;
    }
}

public static class Extractor
{
    public static ExtractResult Extract(IArchive archive, IEnumerable<ArchiveEntry> selection, ExtractOptions o, ProgressReporter? progress = null)
    {
        progress ??= ProgressReporter.None;
        var result = new ExtractResult();
        Directory.CreateDirectory(o.DestinationDir);

        var all = selection.ToList();
        // al elegir una carpeta se extrae todo su contenido
        var chosenDirs = all.Where(e => e.IsDirectory).Select(e => e.Path.TrimEnd('/') + "/").ToList();
        // Se identifica cada entrada por su número dentro del archivo (no por referencia): así no se duplican
        // aunque la selección venga de una copia anterior del mismo archivo (por ejemplo, tras pedir la contraseña).
        var seen = new HashSet<int>();
        var files = new List<ArchiveEntry>();
        foreach (var e in all.Where(e => !e.IsDirectory)) if (seen.Add(e.Index)) files.Add(e);
        foreach (var d in chosenDirs)
            foreach (var e in archive.Entries.Where(x => !x.IsDirectory && x.Path.StartsWith(d, StringComparison.OrdinalIgnoreCase)))
                if (seen.Add(e.Index)) files.Add(e);
        var dirsToCreate = new List<(ArchiveEntry Entry, string Path)>();

        progress.Total = files.Sum(f => f.Size);
        progress.FilesTotal = files.Count;

        var targets = new Dictionary<int, string>();
        string RelativeName(ArchiveEntry e)
        {
            switch (o.PathMode)
            {
                case PathMode.None: return e.Name;
                case PathMode.Relative:
                    string b = o.BaseFolder.Trim('/');
                    if (b.Length > 0 && e.Path.StartsWith(b + "/", StringComparison.OrdinalIgnoreCase)) return e.Path[(b.Length + 1)..];
                    return e.Path;
                default: return e.Path;
            }
        }

        foreach (var e in all.Where(x => x.IsDirectory).Concat(archive.Entries.Where(x => x.IsDirectory && chosenDirs.Any(d => (x.Path.TrimEnd('/') + "/").StartsWith(d, StringComparison.OrdinalIgnoreCase)))).Distinct())
        {
            if (o.PathMode == PathMode.None) break;
            try
            {
                string dp = SafePath.Resolve(o.DestinationDir, RelativeName(e));
                Directory.CreateDirectory(dp);
                dirsToCreate.Add((e, dp));
            }
            catch (MedrarException) { }
        }

        Func<ArchiveEntry, Stream?> openSink = e =>
        {
            string target;
            try { target = SafePath.Resolve(o.DestinationDir, RelativeName(e)); }
            catch (MedrarException ex) { result.Errors.Add((e.Path, ex.Message)); return null; }

            if (File.Exists(target))
            {
                bool isNewer = e.Modified > File.GetLastWriteTime(target).AddSeconds(1);
                if (o.Update == ExtractUpdateMode.ExtractUpdate && !isNewer) { result.Skipped++; return null; }
                if (o.Update == ExtractUpdateMode.FreshenOnly && !isNewer) { result.Skipped++; return null; }
                var decision = o.Overwrite switch
                {
                    OverwriteMode.Overwrite => OverwriteDecision.Overwrite,
                    OverwriteMode.Skip => OverwriteDecision.Skip,
                    OverwriteMode.Rename => OverwriteDecision.Rename,
                    _ => o.AskOverwrite?.Invoke(target) ?? OverwriteDecision.Overwrite
                };
                switch (decision)
                {
                    case OverwriteDecision.Cancel: throw new OperationCanceledException();
                    case OverwriteDecision.Skip: result.Skipped++; return null;
                    case OverwriteDecision.Rename: target = UniqueName(target); break;
                }
            }
            else if (o.Update == ExtractUpdateMode.FreshenOnly) { result.Skipped++; return null; }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            targets[e.Index] = target;
            return new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024);
        };

        Action<ArchiveEntry, Exception?> done = (e, err) =>
        {
            if (!targets.TryGetValue(e.Index, out var target)) return;
            if (err != null)
            {
                result.Errors.Add((e.Path, err.Message));
                if (!o.KeepBrokenFiles) { try { File.Delete(target); } catch { } }
                return;
            }
            if (o.RestoreTimes)
            {
                try { File.SetLastWriteTime(target, e.Modified); } catch { }
            }
            result.Extracted++;
            result.Files.Add(target);
        };

        archive.ExtractEntries(files, openSink, done, progress);

        if (o.RestoreTimes)
            foreach (var (e, p) in dirsToCreate) { try { Directory.SetLastWriteTime(p, e.Modified); } catch { } }
        return result;
    }

    public static string UniqueName(string path)
    {
        string dir = Path.GetDirectoryName(path)!, stem = Path.GetFileNameWithoutExtension(path), ext = Path.GetExtension(path);
        for (int i = 1; ; i++)
        {
            string c = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(c) && !Directory.Exists(c)) return c;
        }
    }

    /// <summary>Verifica todas las sumas de control sin escribir nada en disco.</summary>
    public static ExtractResult Test(IArchive archive, ProgressReporter? progress = null)
    {
        progress ??= ProgressReporter.None;
        var result = new ExtractResult();
        var files = archive.Entries.Where(e => !e.IsDirectory).ToList();
        progress.Total = files.Sum(f => f.Size);
        progress.FilesTotal = files.Count;
        archive.ExtractEntries(files, _ => Stream.Null, (e, err) =>
        {
            if (err != null) result.Errors.Add((e.Path, err.Message)); else result.Extracted++;
        }, progress);
        return result;
    }
}
