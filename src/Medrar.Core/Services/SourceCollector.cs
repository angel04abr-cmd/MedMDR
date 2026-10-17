namespace Medrar.Core;

public static class SourceCollector
{
    /// <summary>
    /// Convierte rutas del disco (ficheros y carpetas) en elementos para archivar.
    /// <paramref name="mode"/>: Relative = cada elemento con su carpeta (sin ruta superior); Full = ruta completa sin la unidad;
    /// None = solo nombres de fichero. <paramref name="subfolder"/> añade un prefijo dentro del archivo.
    /// </summary>
    public static List<SourceItem> Collect(IEnumerable<string> paths, ArchiveOptions options, PathMode mode = PathMode.Relative,
                                           string? subfolder = null, CancellationToken token = default)
    {
        var result = new List<SourceItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string prefix = string.IsNullOrWhiteSpace(subfolder) ? "" : subfolder.Replace('\\', '/').Trim('/') + "/";

        foreach (var raw in paths)
        {
            string full = Path.GetFullPath(raw);
            if (Directory.Exists(full))
            {
                string parent = Path.GetDirectoryName(full.TrimEnd('\\', '/')) ?? "";
                AddDirectory(new DirectoryInfo(full), parent, mode, prefix, options, result, seen, token);
            }
            else if (File.Exists(full))
            {
                string parent = Path.GetDirectoryName(full) ?? "";
                AddFile(new FileInfo(full), parent, mode, prefix, options, result, seen);
            }
            else throw new FileNotFoundException("No existe: " + raw, raw);
        }
        return result;
    }

    private static string EntryName(string fullPath, string parent, PathMode mode, bool isDir)
    {
        switch (mode)
        {
            case PathMode.None:
                return Path.GetFileName(fullPath.TrimEnd('\\', '/'));
            case PathMode.Full:
                string root = Path.GetPathRoot(fullPath) ?? "";
                return fullPath[root.Length..].Replace('\\', '/').Trim('/');
            default:
                string rel = fullPath.Length > parent.Length ? fullPath[parent.Length..] : fullPath;
                return rel.Replace('\\', '/').Trim('/');
        }
    }

    private static string Unique(string name, HashSet<string> seen)
    {
        if (seen.Add(name)) return name;
        string dir = name.Contains('/') ? name[..(name.LastIndexOf('/') + 1)] : "";
        string file = name[dir.Length..];
        string stem = Path.GetFileNameWithoutExtension(file), ext = Path.GetExtension(file);
        for (int i = 2; ; i++)
        {
            string cand = $"{dir}{stem} ({i}){ext}";
            if (seen.Add(cand)) return cand;
        }
    }

    private static void AddFile(FileInfo fi, string parent, PathMode mode, string prefix, ArchiveOptions o, List<SourceItem> list, HashSet<string> seen)
    {
        if (Glob.MatchesAny(fi.Name, o.ExcludePatterns)) return;
        string name = Unique(prefix + EntryName(fi.FullName, parent, mode, false), seen);
        string path = fi.FullName;
        list.Add(new SourceItem
        {
            EntryPath = name,
            Size = fi.Length,
            Modified = fi.LastWriteTime,
            FullPath = path,
            Open = () => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024)
        });
    }

    private static void AddDirectory(DirectoryInfo di, string parent, PathMode mode, string prefix, ArchiveOptions o,
                                     List<SourceItem> list, HashSet<string> seen, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (Glob.MatchesAny(di.Name, o.ExcludePatterns)) return;
        if (mode != PathMode.None)
        {
            string name = prefix + EntryName(di.FullName, parent, mode, true);
            if (name.Length > 0 && seen.Add(name + "/"))
                list.Add(new SourceItem { EntryPath = name, IsDirectory = true, Modified = di.LastWriteTime, FullPath = di.FullName });
        }
        IEnumerable<FileSystemInfo> children;
        try { children = di.EnumerateFileSystemInfos().OrderBy(c => c is DirectoryInfo ? 0 : 1).ThenBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList(); }
        catch (UnauthorizedAccessException) { return; }
        foreach (var c in children)
        {
            if (c is DirectoryInfo sub)
            {
                if (sub.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;   // no seguir enlaces (evita bucles)
                AddDirectory(sub, parent, mode, prefix, o, list, seen, token);
            }
            else if (c is FileInfo f)
            {
                if (f.Attributes.HasFlag(FileAttributes.ReparsePoint) && !f.Exists) continue;
                AddFile(f, parent, mode, prefix, o, list, seen);
            }
        }
    }
}
