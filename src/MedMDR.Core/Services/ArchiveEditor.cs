namespace MedMDR.Core;

/// <summary>Cambios que se pueden aplicar a un archivo existente (se reescribe completo en un temporal y se sustituye).</summary>
public sealed class EditPlan
{
    public HashSet<string> Delete { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> Rename { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<SourceItem> Add { get; } = new();
    public UpdateMode Mode { get; set; } = UpdateMode.AddReplace;
    public string? NewComment { get; set; }
    public bool SetComment { get; set; }
    public bool? Lock { get; set; }
    public int? RecoveryPercent { get; set; }
    public string? NewPassword { get; set; }
    public bool ChangePassword { get; set; }
    public bool? EncryptNames { get; set; }
}

public static class ArchiveEditor
{
    /// <summary>¿El fichero tiene la marca de solo lectura? Es la forma de «bloquear» los archivos que no son .mdr.</summary>
    public static bool IsReadOnlyFile(string path)
    {
        try { return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) != 0; }
        catch { return false; }
    }

    /// <summary>Opciones equivalentes a las del archivo existente, para reescribirlo igual.</summary>
    public static ArchiveOptions OptionsFrom(IArchive a, string? password)
    {
        var o = new ArchiveOptions
        {
            Format = ArchiveBuilder.FormatFromName(a.Path) ?? ArchiveFormat.Mdr,
            Password = password,
            Solid = a.IsSolid,
            EncryptNames = a.HasEncryptedNames,
            Comment = a.Comment,
            Lock = (a as MdrArchive)?.HeaderLocked ?? false,                      // fuera de .mdr el bloqueo es un atributo del fichero (se trata aparte)
            Level = 3,
            RecoveryPercent = (a as MdrArchive)?.RecoveryPercent ?? 0,       // fuera de .mdr la recuperación va en un fichero aparte
        };
        return o;
    }

    public static void Apply(string archivePath, string? password, EditPlan plan, ProgressReporter? progress = null)
    {
        progress ??= ProgressReporter.None;
        var archive = ArchiveOpener.Open(archivePath, password);
        string? builtPath = null;
        bool lockAfter = false;
        int sidecarPercent = 0;
        try
        {
        if (!archive.CanModify)
            throw new UnsupportedFormatException("Este formato es de solo lectura en MedMDR. Conviértelo primero a .mdr o .zip.");
        if (archive.IsLocked && plan.Lock != false)
            throw new LockedArchiveException();

        var o = OptionsFrom(archive, password);
        if (plan.SetComment) o.Comment = plan.NewComment;
        if (plan.Lock.HasValue) o.Lock = plan.Lock.Value;
        if (plan.RecoveryPercent.HasValue) o.RecoveryPercent = plan.RecoveryPercent.Value;
        if (plan.ChangePassword) o.Password = string.IsNullOrEmpty(plan.NewPassword) ? null : plan.NewPassword;
        if (plan.EncryptNames.HasValue) o.EncryptNames = plan.EncryptNames.Value;
        if (o.Format != ArchiveFormat.Mdr)
        {
            // el bloqueo y la recuperación de ZIP/7Z/TAR se aplican al terminar (atributo de solo lectura y fichero .mdrrec)
            lockAfter = plan.Lock ?? false;
            sidecarPercent = plan.RecoveryPercent ?? RecoveryRecord.SidecarPercent(archivePath);
            o.Lock = false;
            o.RecoveryPercent = 0;
        }
        if (plan.Lock == false && o.Format == ArchiveFormat.Mdr) o.Lock = false;

        // ¿qué entradas se conservan?
        var existing = archive.Entries.ToList();
        var addByPath = plan.Add.ToDictionary(a => a.EntryPath.Replace('\\', '/').Trim('/'), StringComparer.OrdinalIgnoreCase);
        var drop = new HashSet<string>(plan.Delete.Select(p => p.Replace('\\', '/').Trim('/')), StringComparer.OrdinalIgnoreCase);
        bool IsDeleted(string path)
        {
            foreach (var d in drop)
                if (path.Equals(d, StringComparison.OrdinalIgnoreCase) || path.StartsWith(d + "/", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        var acceptedAdds = new Dictionary<string, SourceItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in addByPath)
        {
            var old = existing.FirstOrDefault(e => e.Path.TrimEnd('/').Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
            bool newer = old == null || kv.Value.Modified.AddSeconds(-1) > old.Modified;
            switch (plan.Mode)
            {
                case UpdateMode.AddReplace: acceptedAdds[kv.Key] = kv.Value; break;
                case UpdateMode.AddUpdate: if (newer) acceptedAdds[kv.Key] = kv.Value; break;
                case UpdateMode.FreshenOnly: if (old != null && newer) acceptedAdds[kv.Key] = kv.Value; break;
                case UpdateMode.Synchronize: if (newer) acceptedAdds[kv.Key] = kv.Value; break;
            }
        }
        if (plan.Mode == UpdateMode.Synchronize && plan.Add.Count > 0)
        {
            var roots = plan.Add.Select(a => a.EntryPath.Replace('\\', '/').Split('/')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var e in existing)
            {
                string p = e.Path.TrimEnd('/');
                bool inRoot = roots.Any(r => p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase));
                if (inRoot && !addByPath.ContainsKey(p)) drop.Add(p);
            }
        }

        string tempDir = Path.Combine(Path.GetTempPath(), "MedMDR-edit-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var items = new List<SourceItem>();
            var keep = new List<ArchiveEntry>();
            foreach (var e in existing)
            {
                string p = e.Path.TrimEnd('/');
                if (IsDeleted(p)) continue;
                if (acceptedAdds.ContainsKey(p)) continue;               // lo sustituye el nuevo
                keep.Add(e);
            }

            // extraer lo que se conserva a una carpeta temporal para volver a empaquetarlo
            var keepFiles = keep.Where(e => !e.IsDirectory).ToList();
            var tempMap = new Dictionary<int, string>();
            progress.Total = keepFiles.Sum(f => f.Size) * 2 + acceptedAdds.Values.Sum(a => a.Size);
            progress.FilesTotal = keepFiles.Count * 2 + acceptedAdds.Count;
            archive.ExtractEntries(keepFiles, e =>
            {
                string tp = Path.Combine(tempDir, e.Index.ToString());
                tempMap[e.Index] = tp;
                return new FileStream(tp, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024);
            }, (e, err) => { if (err != null) throw err; }, progress);

            foreach (var e in keep)
            {
                string newPath = Renamed(e.Path.TrimEnd('/'), plan.Rename);
                if (e.IsDirectory)
                    items.Add(new SourceItem { EntryPath = newPath, IsDirectory = true, Modified = e.Modified });
                else
                {
                    string tp = tempMap[e.Index];
                    items.Add(new SourceItem
                    {
                        EntryPath = newPath, Size = e.Size, Modified = e.Modified,
                        Open = () => new FileStream(tp, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024)
                    });
                }
            }
            items.AddRange(acceptedAdds.Values);

            string tmpOut = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(archivePath))!, "." + Path.GetFileName(archivePath) + ".edit");
            var oo = o.Clone();
            oo.VolumeSizeBytes = 0; oo.SelfExtracting = false;
            builtPath = ArchiveBuilder.Create(tmpOut, items, oo, progress);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }

        // el archivo original debe estar cerrado antes de sustituirlo
        archive.Dispose();
        archive = null!;
        if (IsReadOnlyFile(archivePath)) ArchiveBuilder.UnlockFile(archivePath);   // llegados aquí el bloqueo ya se comprobó
        File.Move(builtPath!, archivePath, overwrite: true);
        builtPath = null;
        if (lockAfter) ArchiveBuilder.LockFile(archivePath);
        // el fichero de recuperación de un ZIP/7Z/TAR ya no coincide con el archivo nuevo: se rehace (o se borra si ya no se quiere)
        if (ArchiveBuilder.FormatFromName(archivePath) is { } f2 && f2 != ArchiveFormat.Mdr)
        {
            if (sidecarPercent > 0) RecoveryRecord.CreateSidecar(archivePath, sidecarPercent, progress);
            else RecoveryRecord.DeleteSidecar(archivePath);
        }
        }
        finally
        {
            archive?.Dispose();
            try { if (builtPath != null && File.Exists(builtPath)) File.Delete(builtPath); } catch { }
        }
    }

    private static string Renamed(string path, Dictionary<string, string> map)
    {
        foreach (var kv in map)
        {
            string from = kv.Key.Replace('\\', '/').Trim('/');
            string to = kv.Value.Replace('\\', '/').Trim('/');
            if (path.Equals(from, StringComparison.OrdinalIgnoreCase)) return to;
            if (path.StartsWith(from + "/", StringComparison.OrdinalIgnoreCase)) return to + path[from.Length..];
        }
        return path;
    }

    /// <summary>Convierte un archivo (cualquier formato legible) a otro formato creando un archivo nuevo.</summary>
    public static string Convert(string sourcePath, string? sourcePassword, string destPath, ArchiveOptions options, ProgressReporter? progress = null)
    {
        progress ??= ProgressReporter.None;
        using var src = ArchiveOpener.Open(sourcePath, sourcePassword);
        string tempDir = Path.Combine(Path.GetTempPath(), "MedMDR-conv-" + Guid.NewGuid().ToString("N")[..10]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var files = src.Entries.Where(e => !e.IsDirectory).ToList();
            progress.Total = files.Sum(f => f.Size) * 2;
            progress.FilesTotal = files.Count * 2;
            var map = new Dictionary<int, string>();
            src.ExtractEntries(files, e =>
            {
                string tp = Path.Combine(tempDir, e.Index.ToString());
                map[e.Index] = tp;
                return new FileStream(tp, FileMode.Create, FileAccess.Write, FileShare.None, 128 * 1024);
            }, (e, err) => { if (err != null) throw err; }, progress);

            var items = new List<SourceItem>();
            foreach (var e in src.Entries)
            {
                if (e.IsDirectory) { items.Add(new SourceItem { EntryPath = e.Path.TrimEnd('/'), IsDirectory = true, Modified = e.Modified }); continue; }
                string tp = map[e.Index];
                items.Add(new SourceItem
                {
                    EntryPath = e.Path, Size = e.Size, Modified = e.Modified,
                    Open = () => new FileStream(tp, FileMode.Open, FileAccess.Read, FileShare.Read, 128 * 1024)
                });
            }
            return ArchiveBuilder.Create(destPath, items, options, progress);
        }
        finally { try { Directory.Delete(tempDir, true); } catch { } }
    }
}
