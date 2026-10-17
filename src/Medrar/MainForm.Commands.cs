using System.Diagnostics;
using Medrar.Core;

namespace Medrar.App;

internal sealed partial class MainForm
{
    // ---------------------------------------------------------------- utilidades

    private string DefaultExtractDir(string archivePath)
    {
        string name = Path.GetFileNameWithoutExtension(archivePath);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
        string baseDir = !string.IsNullOrWhiteSpace(_settings.DefaultExtractFolder) ? _settings.DefaultExtractFolder : Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
        return Path.Combine(baseDir, name);
    }

    private string? SelectedArchivePath()
    {
        if (InArchive) return _session!.Path;
        var n = SelectedNodes().FirstOrDefault(x => !x.IsDir && ArchiveOpener.LooksLikeArchive(x.FullPath));
        return n?.FullPath;
    }

    /// <summary>Entradas a procesar: la selección actual o, si no hay, todo el archivo.</summary>
    private List<ArchiveEntry> EntriesForSelection(bool allIfNone)
    {
        var sel = SelectedNodes().ToList();
        if (sel.Count == 0) return allIfNone ? Archive.Entries.ToList() : new List<ArchiveEntry>();
        var list = new List<ArchiveEntry>();
        foreach (var n in sel)
        {
            if (!n.IsDir) { if (n.Entry != null) list.Add(n.Entry); }
            else list.Add(new ArchiveEntry { Path = n.FullPath, IsDirectory = true, Modified = n.Modified });
        }
        return list;
    }

    private ArchiveSession? OpenSessionFor(string path)
    {
        if (InArchive && string.Equals(_session!.Path, path, StringComparison.OrdinalIgnoreCase)) return _session;
        return ArchiveSession.Open(this, path);
    }

    private void GoToPath(string path)
    {
        if (Directory.Exists(path)) { CloseArchive(false); GoFolder(path); }
        else if (File.Exists(path)) { CloseArchive(false); GoFolder(Path.GetDirectoryName(path)!); OpenArchive(path); }
        else UI.Warn(this, "Esa ubicación ya no existe:\n" + path);
    }

    // ---------------------------------------------------------------- Archivo

    private void OpenArchiveDialog()
    {
        using var d = new OpenFileDialog
        {
            Title = "Abrir archivo", InitialDirectory = Directory.Exists(_folder) ? _folder : "",
            Filter = "Archivos comprimidos|*.mdr;*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.lz;*.zst;*.cab;*.iso;*.jar;*.001;*.exe|Todos los ficheros|*.*"
        };
        if (d.ShowDialog(this) == DialogResult.OK) OpenArchive(d.FileName);
    }

    private void ChangeDrive()
    {
        var m = new ContextMenuStrip();
        foreach (var di in DriveInfo.GetDrives().Where(x => x.IsReady))
        {
            var root = di.RootDirectory.FullName;
            string label = string.IsNullOrEmpty(di.VolumeLabel) ? "Disco local" : di.VolumeLabel;
            m.Items.Add($"{root}  {label}", null, (_, _) => { CloseArchive(false); GoFolder(root); });
        }
        Theme.StyleMenu(m.Items);
        m.Show(_list, new Point(20, 20));
    }

    private void SetSessionPassword()
    {
        using var d = new PasswordDialog("Contraseña para esta sesión", "Se probará automáticamente al abrir archivos cifrados.\nSolo se guarda en memoria y se olvida al cerrar Medrar.", false);
        if (d.ShowDialog(this) == DialogResult.OK) _sessionPassword = d.Password;
    }

    private void CopyFiles()
    {
        if (InArchive) { CopyNames(); return; }
        var paths = SelectedNodes().Select(n => n.FullPath).Where(p => p.Length > 0).ToArray();
        if (paths.Length == 0) return;
        var c = new System.Collections.Specialized.StringCollection();
        c.AddRange(paths);
        Clipboard.SetFileDropList(c);
        _statusLeft.Text = $"{paths.Length} elemento(s) copiados al portapapeles";
    }

    private void CopyNames()
    {
        var names = SelectedNodes().Select(n => InArchive ? n.FullPath : n.FullPath).ToList();
        if (names.Count > 0) Clipboard.SetText(string.Join(Environment.NewLine, names));
    }

    private void PasteFiles()
    {
        if (InArchive)
        {
            if (Clipboard.ContainsFileDropList()) AddToCurrentArchive(Clipboard.GetFileDropList().Cast<string>().ToArray());
            return;
        }
        if (!Clipboard.ContainsFileDropList()) return;
        try
        {
            foreach (string src in Clipboard.GetFileDropList())
            {
                string dest = Path.Combine(_folder, Path.GetFileName(src.TrimEnd('\\')));
                if (Directory.Exists(src)) CopyDir(src, Extractor.UniqueName(dest));
                else File.Copy(src, File.Exists(dest) ? Extractor.UniqueName(dest) : dest);
            }
            GoFolder(_folder);
        }
        catch (Exception ex) { UI.Error(this, UI.Describe(ex)); }
    }

    private static void CopyDir(string src, string dest)
    {
        Directory.CreateDirectory(dest);
        foreach (var f in Directory.GetFiles(src)) File.Copy(f, Path.Combine(dest, Path.GetFileName(f)));
        foreach (var d in Directory.GetDirectories(src)) CopyDir(d, Path.Combine(dest, Path.GetFileName(d)));
    }

    private void SelectAll() { foreach (ListViewItem i in _list.Items) i.Selected = ((Node)i.Tag!).IsUp ? false : true; }

    private void SelectGroup(bool select)
    {
        string? mask = InputDialog.Ask("Máscara de nombres (por ejemplo *.txt):", select ? "Seleccionar grupo" : "Quitar selección de grupo", "*.*", this);
        if (string.IsNullOrWhiteSpace(mask)) return;
        var pats = mask.Replace("*.*", "*").Split(';', ',');
        foreach (ListViewItem i in _list.Items)
        {
            var n = (Node)i.Tag!;
            if (n.IsUp || n.IsDir) continue;
            if (Glob.MatchesAny(n.Name, pats)) i.Selected = select;
        }
    }

    private void InvertSelection() { foreach (ListViewItem i in _list.Items) if (!((Node)i.Tag!).IsUp) i.Selected = !i.Selected; }

    // ---------------------------------------------------------------- Añadir

    private void AddCommand()
    {
        if (InArchive)
        {
            var m = new ContextMenuStrip();
            m.Items.Add("Añadir ficheros…", null, (_, _) =>
            {
                using var d = new OpenFileDialog { Multiselect = true, Title = "Ficheros que se añadirán al archivo" };
                if (d.ShowDialog(this) == DialogResult.OK) AddToCurrentArchive(d.FileNames);
            });
            m.Items.Add("Añadir una carpeta…", null, (_, _) =>
            {
                var p = UI.PickFolder(this, _folder, "Carpeta que se añadirá al archivo");
                if (p != null) AddToCurrentArchive(new[] { p });
            });
            Theme.StyleMenu(m.Items);
            m.Show(_tools, new Point(8, _tools.Height));
            return;
        }
        var paths = SelectedNodes().Select(n => n.FullPath).ToArray();
        if (paths.Length == 0)
        {
            UI.Info(this, "Selecciona primero los ficheros o carpetas que quieres comprimir (o arrástralos a la ventana).");
            return;
        }
        AddToNewArchive(paths);
    }

    private void AddToNewArchive(IReadOnlyList<string> paths)
    {
        using var d = new AddDialog(paths.ToArray(), _folder, _settings);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var created = Ops.Create(this, d.Result, paths);
        if (created == null) return;
        if (!InArchive) { GoFolder(Path.GetDirectoryName(created)!); SelectByName(Path.GetFileName(created)); }
    }

    private void AddToCurrentArchive(string[] paths)
    {
        var s = _session!;
        if (!Archive.CanModify)
        {
            UI.Warn(this, NotModifiableMessage());
            return;
        }
        if (Archive.IsLocked) { UI.Warn(this, "El archivo está bloqueado: no se puede modificar."); return; }
        using var d = new AddDialog(paths, _folder, _settings, s.Path);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        d.Result.Subfolder = _inside;
        d.Result.ArchivePath = s.Path;
        d.Result.Options.Password = s.Password;
        s.Archive.Dispose();
        var created = Ops.Create(this, d.Result, paths, s);
        ReloadArchive();
        if (created != null) _statusLeft.Text = "Archivo actualizado";
    }

    private void SelectByName(string name)
    {
        foreach (ListViewItem i in _list.Items)
            if (((Node)i.Tag!).Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { i.Selected = true; i.EnsureVisible(); break; }
    }

    // ---------------------------------------------------------------- Extraer / comprobar / ver

    private void ExtractHere() => ExtractCommand(ask: false, here: true);

    private void ExtractCommand(bool ask = true, bool here = false)
    {
        string? archivePath = SelectedArchivePath();
        if (archivePath == null) { UI.Info(this, "Abre o selecciona un archivo comprimido para extraer."); return; }
        bool multiple = !InArchive && SelectedNodes().Count(x => !x.IsDir && ArchiveOpener.LooksLikeArchive(x.FullPath)) > 1;

        var paths = InArchive ? new List<string> { archivePath } : SelectedNodes().Where(x => !x.IsDir && ArchiveOpener.LooksLikeArchive(x.FullPath)).Select(x => x.FullPath).ToList();
        ExtractRequest? req = null;
        string defaultDest = here ? Path.GetDirectoryName(Path.GetFullPath(archivePath))! : DefaultExtractDir(archivePath);
        if (ask)
        {
            using var d = new ExtractDialog(archivePath, defaultDest, _settings, showSubfolderOption: multiple);
            if (d.ShowDialog(this) != DialogResult.OK) return;
            req = d.Result;
        }
        else
        {
            req = new ExtractRequest
            {
                Destination = defaultDest,
                Options = new ExtractOptions { DestinationDir = defaultDest, Overwrite = here ? OverwriteMode.Ask : _settings.ExtractOverwrite, KeepBrokenFiles = _settings.KeepBrokenFiles }
            };
        }

        foreach (var path in paths)
        {
            var s = OpenSessionFor(path);
            if (s == null) continue;
            try
            {
                var o = new ExtractOptions
                {
                    DestinationDir = multiple ? Path.Combine(req.Destination, Path.GetFileNameWithoutExtension(path)) : req.Destination,
                    PathMode = req.Options.PathMode, Update = req.Options.Update, Overwrite = req.Options.Overwrite,
                    KeepBrokenFiles = req.Options.KeepBrokenFiles, RestoreTimes = true
                };
                var entries = InArchive && ReferenceEquals(s, _session) ? EntriesForSelection(true) : s.Archive.Entries.ToList();
                var res = Ops.Extract(this, s, entries, o);
                if (res != null)
                {
                    SettingsStore.AddHistory(_settings, o.DestinationDir);
                    _statusLeft.Text = $"Extraídos {res.Extracted} fichero(s) en {o.DestinationDir}";
                    if (req.ShowInExplorer && Directory.Exists(o.DestinationDir)) Process.Start("explorer.exe", $"\"{o.DestinationDir}\"");
                }
            }
            finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
        }
        Program.SaveSettings();
        if (!InArchive) GoFolder(_folder);
    }

    private void TestCommand()
    {
        string? p = SelectedArchivePath();
        if (p == null) { UI.Info(this, "Abre o selecciona un archivo comprimido para comprobarlo."); return; }
        var s = OpenSessionFor(p);
        if (s == null) return;
        try { Ops.Test(this, s); }
        finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
    }

    private void ViewCommand()
    {
        var n = SelectedNodes().FirstOrDefault(x => !x.IsDir);
        if (n == null) { UI.Info(this, "Selecciona un fichero para verlo."); return; }
        long limit = Math.Max(16, _settings.ViewerMaxKb) * 1024L;
        byte[]? data = null;
        bool truncated = false;
        try
        {
            if (InArchive)
            {
                var entry = n.Entry!;
                truncated = entry.Size > limit;
                var s = _session!;
                for (;;)
                {
                    var err = ProgressForm.Run(this, "Leyendo " + n.Name, p => data = Archive.ReadAll(entry, limit), out bool cancelled);
                    if (cancelled) return;
                    if (err is PasswordRequiredException or WrongPasswordException) { if (!s.Unlock(this, err is WrongPasswordException)) return; continue; }
                    if (err != null) { UI.Error(this, UI.Describe(err)); return; }
                    break;
                }
            }
            else
            {
                using var fs = File.OpenRead(n.FullPath);
                truncated = fs.Length > limit;
                data = new byte[Math.Min(fs.Length, limit)];
                fs.ReadExactly(data);
            }
        }
        catch (Exception ex) { UI.Error(this, UI.Describe(ex)); return; }
        using var v = new ViewerForm(n.Name, data!, _settings, truncated);
        v.ShowDialog(this);
    }

    private void OpenEntryExternally(Node n)
    {
        if (n.Entry == null) return;
        string dir = Path.Combine(Path.GetTempPath(), "Medrar", Guid.NewGuid().ToString("N")[..8]);
        var o = new ExtractOptions { DestinationDir = dir, PathMode = PathMode.None, Overwrite = OverwriteMode.Overwrite };
        var res = Ops.Extract(this, _session!, new[] { n.Entry }, o);
        if (res == null || res.Files.Count == 0) return;
        _tempFiles.AddRange(res.Files);
        try { Process.Start(new ProcessStartInfo(res.Files[0]) { UseShellExecute = true }); }
        catch (Exception ex) { UI.Warn(this, "No hay ningún programa asociado para abrir este fichero.\n" + ex.Message); }
    }

    // ---------------------------------------------------------------- Editar el archivo

    private bool ApplyEdit(string title, Action<EditPlan> build)
    {
        var s = _session!;
        if (!Archive.CanModify)
        {
            UI.Warn(this, NotModifiableMessage());
            return false;
        }
        var plan = new EditPlan();
        build(plan);
        string path = s.Path;
        for (;;)
        {
            string? pw = s.Password;
            s.Archive.Dispose();
            var err = ProgressForm.Run(this, title, p => ArchiveEditor.Apply(path, pw, plan, p), out bool cancelled);
            if (!cancelled && err is PasswordRequiredException or WrongPasswordException)
            {
                // falta la contraseña (o no sirve): se pide, se reabre el archivo con ella y se repite el cambio
                if (s.Unlock(this, err is WrongPasswordException)) continue;
                ReloadArchive();
                return false;
            }
            ReloadArchive();
            if (cancelled) return false;
            if (err != null) { UI.Error(this, UI.Describe(err), title); return false; }
            return true;
        }
    }

    private void DeleteSelected()
    {
        var sel = SelectedNodes().ToList();
        if (sel.Count == 0) return;
        if (InArchive)
        {
            if (Archive.IsLocked) { UI.Warn(this, "El archivo está bloqueado: no se puede modificar."); return; }
            if (_settings.AskBeforeDelete && !UI.Confirm(this, $"¿Eliminar {sel.Count} elemento(s) del archivo?\nEsta acción reescribe el archivo y no se puede deshacer.", "Eliminar")) return;
            ApplyEdit("Eliminando del archivo", plan => { foreach (var n in sel) plan.Delete.Add(n.FullPath); });
        }
        else
        {
            if (_settings.AskBeforeDelete && !UI.Confirm(this, $"¿Enviar {sel.Count} elemento(s) a la Papelera de reciclaje?", "Eliminar")) return;
            if (!Recycle.Delete(Handle, sel.Select(n => n.FullPath))) UI.Warn(this, "No se pudieron eliminar todos los elementos.");
            GoFolder(_folder);
        }
    }

    private void RenameSelected()
    {
        var n = SelectedNodes().FirstOrDefault();
        if (n == null) return;
        string? newName = InputDialog.Ask("Nuevo nombre:", "Renombrar", n.Name, this);
        if (string.IsNullOrWhiteSpace(newName) || newName == n.Name) return;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { UI.Warn(this, "El nombre contiene caracteres no permitidos."); return; }
        if (InArchive)
        {
            string parent = n.FullPath.Contains('/') ? n.FullPath[..n.FullPath.LastIndexOf('/')] + "/" : "";
            ApplyEdit("Renombrando", plan => plan.Rename[n.FullPath] = parent + newName);
        }
        else
        {
            try
            {
                string dest = Path.Combine(Path.GetDirectoryName(n.FullPath)!, newName);
                if (n.IsDir) Directory.Move(n.FullPath, dest); else File.Move(n.FullPath, dest);
                GoFolder(_folder);
                SelectByName(newName);
            }
            catch (Exception ex) { UI.Error(this, UI.Describe(ex)); }
        }
    }

    private string NotModifiableMessage() =>
        SfxBuilder.IsSfx(_session!.Path)
            ? "Un autoextraíble es de solo lectura. Usa Herramientas > «Quitar el módulo autoextraíble» para recuperar el archivo y poder modificarlo."
            : $"Medrar no puede modificar archivos {Archive.FormatName}. Conviértelo antes a .mdr, ZIP o 7Z (Herramientas > Convertir archivos).";

    private void CommentCommand()
    {
        if (!InArchive) { UI.Info(this, "Abre primero un archivo .mdr para agregar un comentario."); return; }
        if (Archive is not MdrArchive || !Archive.CanModify) { UI.Info(this, "Los comentarios solo se pueden guardar dentro de archivos .mdr (no autoextraíbles)."); return; }
        string? text = InputDialog.Ask("Comentario del archivo (vacío = quitarlo):", "Comentario", Archive.Comment ?? "", this, multiline: true);
        if (text == null) return;
        ApplyEdit("Guardando el comentario", plan => { plan.SetComment = true; plan.NewComment = string.IsNullOrWhiteSpace(text) ? null : text; });
        ShowComment(Archive.Comment);
    }

    /// <summary>Aplica cambios a un archivo que puede estar abierto en la ventana o no (pide la contraseña si hace falta).</summary>
    private bool EditFile(string path, string title, Action<EditPlan> build)
    {
        if (InArchive && string.Equals(_session!.Path, path, StringComparison.OrdinalIgnoreCase)) return ApplyEdit(title, build);
        string? pw;
        using (var probe = ArchiveSession.Open(this, path)) { if (probe == null) return false; pw = probe.Password; }
        var plan = new EditPlan();
        build(plan);
        var err = ProgressForm.Run(this, title, p => ArchiveEditor.Apply(path, pw, plan, p), out bool cancelled);
        if (cancelled) return false;
        if (err != null) { UI.Error(this, UI.Describe(err), title); return false; }
        return true;
    }

    private void ProtectCommand()
    {
        string? p = SelectedArchivePath();
        if (p == null) { UI.Info(this, "Abre o selecciona un archivo comprimido para protegerlo contra daños."); return; }
        string? v = InputDialog.Ask("Porcentaje de datos de recuperación (1-50 %):", "Proteger el archivo contra daños", "5", this);
        if (v == null) return;
        if (!int.TryParse(v.Trim().TrimEnd('%'), out int pct) || pct is < 1 or > 50) { UI.Warn(this, "Escribe un número entre 1 y 50."); return; }
        bool embedded = MdrArchive.LooksLikeMdr(p) && !SfxBuilder.IsSfx(p) && !VolumeSet.IsVolumeName(p);
        bool ok;
        if (embedded) ok = EditFile(p, "Añadiendo el registro de recuperación", plan => plan.RecoveryPercent = pct);
        else ok = Ops.CreateRecoveryFile(this, p, pct);        // ZIP, 7Z, TAR, RAR, autoextraíbles...: va en un fichero aparte
        if (!ok) return;
        string where = embedded ? "dentro del archivo" : "en el fichero «" + Path.GetFileName(RecoveryRecord.SidecarPathFor(p)) + "» (guárdalo junto al archivo)";
        UI.Info(this, $"Listo: registro de recuperación del {pct} % {where}.");
        if (!InArchive) GoFolder(_folder);
    }

    private void LockCommand()
    {
        string? p = SelectedArchivePath();
        if (p == null) { UI.Info(this, "Abre o selecciona un archivo comprimido para bloquearlo o desbloquearlo."); return; }
        bool isMdr = MdrArchive.LooksLikeMdr(p) && !SfxBuilder.IsSfx(p);
        bool locked = ArchiveEditor.IsReadOnlyFile(p);
        if (isMdr && !locked)
        {
            var s = OpenSessionFor(p);
            if (s == null) return;
            locked = s.Archive.IsLocked;
            if (!ReferenceEquals(s, _session)) s.Dispose();
        }
        if (locked)
        {
            if (!UI.Confirm(this, "El archivo está bloqueado.\n¿Desbloquearlo para poder modificarlo?")) return;
            if (isMdr) { if (!EditFile(p, "Desbloqueando el archivo", plan => plan.Lock = false)) return; }
            if (ArchiveEditor.IsReadOnlyFile(p)) { try { ArchiveBuilder.UnlockFile(p); } catch (Exception ex) { UI.Error(this, UI.Describe(ex)); return; } }
        }
        else
        {
            if (!UI.Confirm(this, "Un archivo bloqueado ya no se puede modificar con Medrar (añadir, eliminar, renombrar…).\nEs una protección contra accidentes, no una medida de seguridad.\n¿Bloquearlo ahora?")) return;
            if (isMdr) { if (!EditFile(p, "Bloqueando el archivo", plan => plan.Lock = true)) return; }
            else { try { ArchiveBuilder.LockFile(p); } catch (Exception ex) { UI.Error(this, UI.Describe(ex)); return; } }
        }
        if (InArchive) ReloadArchive(); else GoFolder(_folder);
        UpdateStatus();
    }

    // ---------------------------------------------------------------- Herramientas

    private void InfoCommand()
    {
        string? p = SelectedArchivePath();
        if (p == null) { UI.Info(this, "Abre o selecciona un archivo comprimido para ver su información."); return; }
        var s = OpenSessionFor(p);
        if (s == null) return;
        try
        {
            string text = ArchiveReport.Summary(s.Archive);
            if (s.Archive.HasRecovery)
            {
                var r = RecoveryRecord.Test(p);
                text += "\nEstado del registro de recuperación: " + r.Message + "\n";
                if (s.Archive is not MdrArchive || SfxBuilder.IsSfx(p)) text += "(va en el fichero " + Path.GetFileName(RecoveryRecord.SidecarPathFor(p)) + ")\n";
            }
            ViewerForm.ShowText(this, "Información — " + Path.GetFileName(p), text, _settings);
        }
        finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
    }

    private void ReportCommand()
    {
        string? p = SelectedArchivePath();
        if (p == null) { UI.Info(this, "Abre o selecciona un archivo comprimido para generar su informe."); return; }
        using var d = new SaveFileDialog { Title = "Guardar informe", FileName = Path.GetFileNameWithoutExtension(p) + "-informe", Filter = "Página web (*.html)|*.html|Texto (*.txt)|*.txt", DefaultExt = "html" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        var s = OpenSessionFor(p);
        if (s == null) return;
        try
        {
            string content = d.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? ArchiveReport.Text(s.Archive) : ArchiveReport.Html(s.Archive);
            File.WriteAllText(d.FileName, content, new System.Text.UTF8Encoding(true));
            if (UI.Confirm(this, "Informe guardado.\n¿Abrirlo ahora?")) Process.Start(new ProcessStartInfo(d.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { UI.Error(this, UI.Describe(ex)); }
        finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
    }

    private void RepairCommand()
    {
        string? p = SelectedArchivePath();
        if (p == null)
        {
            using var d = new OpenFileDialog { Title = "Archivo que se va a reparar", Filter = "Todos los ficheros|*.*" };
            if (d.ShowDialog(this) != DialogResult.OK) return;
            p = d.FileName;
        }
        if (!File.Exists(RecoveryRecord.SidecarPathFor(p)) && !MdrArchive.LooksLikeMdr(p))
        {
            UI.Info(this, "Este archivo no tiene registro de recuperación, así que no se puede reparar.\n\nSi lo creas de nuevo, usa Órdenes > Proteger el archivo contra daños: genera un registro (dentro del .mdr, o en un fichero .mdrrec junto a los demás formatos). Con ese fichero se puede reparar incluso un archivo truncado.");
            return;
        }
        Ops.Repair(this, p);
        if (!InArchive) GoFolder(_folder);
    }

    private void SfxCommand()
    {
        string? p = SelectedArchivePath();
        if (p == null || p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) || VolumeSet.IsVolumeName(p))
        {
            UI.Info(this, "Selecciona o abre un archivo .mdr, ZIP, 7Z, TAR, TAR.GZ o GZ para convertirlo en autoextraíble.");
            return;
        }
        if (SfxBuilder.IsSfx(p)) { UI.Info(this, "Ese fichero ya es un autoextraíble."); return; }
        bool full = false;
        if (SfxBuilder.FindStub(true) != null)
        {
            var r = MessageBox.Show(this, "¿Incluir .NET dentro del autoextraíble?\n\nSí: funciona en cualquier equipo con Windows, sin instalar nada (pesa bastante más).\nNo: ligero, pero necesita .NET 10 en el equipo donde se abra.",
                "Autoextraíble", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;
            full = r == DialogResult.Yes;
        }
        Ops.ConvertToSfx(this, p, full);
        if (!InArchive) GoFolder(_folder);
    }

    private void UnpackSfxCommand()
    {
        string? p = !InArchive ? SelectedNodes().FirstOrDefault(n => !n.IsDir && n.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.FullPath : _session!.Path;
        if (p == null || !SfxBuilder.IsSfx(p)) { UI.Info(this, "Selecciona o abre un archivo autoextraíble (.exe de Medrar)."); return; }
        string? result = Ops.UnpackSfx(this, p);
        if (result == null) return;
        if (InArchive) CloseArchive(true); else GoFolder(_folder);
        UI.Info(this, "Se guardó el archivo original (sin el módulo autoextraíble):\n" + result);
    }

    private void ConvertCommand()
    {
        var initial = InArchive ? new List<string> { _session!.Path }
            : SelectedNodes().Where(n => !n.IsDir && ArchiveOpener.LooksLikeArchive(n.FullPath)).Select(n => n.FullPath).ToList();
        using var d = new ConvertDialog(initial, _folder, _settings);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        int ok = 0;
        var failures = new List<string>();
        foreach (var src in d.Sources)
        {
            string name = Path.GetFileNameWithoutExtension(src);
            if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            string dest = Path.Combine(d.Destination, name + ArchiveBuilder.Extension(d.Options.Format));
            if (string.Equals(Path.GetFullPath(dest), Path.GetFullPath(src), StringComparison.OrdinalIgnoreCase)) dest = Extractor.UniqueName(dest);
            else if (File.Exists(dest)) dest = Extractor.UniqueName(dest);
            string? pw = null;
            using (var probe = ArchiveSession.Open(this, src)) { if (probe == null) continue; pw = probe.Password; }
            var err = ProgressForm.Run(this, "Convirtiendo " + Path.GetFileName(src), p => ArchiveEditor.Convert(src, pw, dest, d.Options, p), out bool cancelled);
            if (cancelled) break;
            if (err != null) failures.Add($"{Path.GetFileName(src)}: {UI.Describe(err)}");
            else
            {
                ok++;
                if (d.DeleteOriginals) { try { VolumeSet.DeleteAll(src); } catch { } }
            }
        }
        if (failures.Count > 0)
        {
            using var r = new ResultDialog("Convertir archivos", $"Convertidos {ok}; fallaron {failures.Count}:", failures, false);
            r.ShowDialog(this);
        }
        else if (ok > 0) UI.Info(this, $"Se convirtieron {ok} archivo(s) en:\n{d.Destination}");
        if (InArchive) CloseArchive(true); else GoFolder(_folder);
    }

    private void FindCommand()
    {
        using var d = new FindDialog(_folder, InArchive ? Archive : null, _session?.Password);
        if (d.ShowDialog(this) != DialogResult.OK || d.Navigate == null) return;
        if (InArchive)
        {
            string p = d.Navigate;
            int i = p.LastIndexOf('/');
            _inside = i < 0 ? "" : p[..i];
            ShowArchiveLevel();
            SelectByName(i < 0 ? p : p[(i + 1)..]);
        }
        else
        {
            string p = d.Navigate;
            if (Directory.Exists(p)) GoFolder(p);
            else if (File.Exists(p)) { GoFolder(Path.GetDirectoryName(p)!); if (ArchiveOpener.LooksLikeArchive(p)) OpenArchive(p); else SelectByName(Path.GetFileName(p)); }
        }
    }

    private void WizardCommand()
    {
        using var w = new WizardForm(_folder, _settings);
        if (w.ShowDialog(this) != DialogResult.OK) return;
        if (w.Create)
        {
            var created = Ops.Create(this, w.Request, w.Sources);
            if (created != null) { CloseArchive(false); GoFolder(Path.GetDirectoryName(created)!); SelectByName(Path.GetFileName(created)); }
        }
        else
        {
            var s = ArchiveSession.Open(this, w.ArchivePath);
            if (s == null) return;
            try
            {
                var o = new ExtractOptions { DestinationDir = w.Destination, Overwrite = _settings.ExtractOverwrite, KeepBrokenFiles = _settings.KeepBrokenFiles };
                var res = Ops.Extract(this, s, s.Archive.Entries.ToList(), o);
                if (res != null && Directory.Exists(w.Destination))
                {
                    UI.Info(this, $"Se extrajeron {res.Extracted} fichero(s) en:\n{w.Destination}");
                    CloseArchive(false); GoFolder(w.Destination);
                }
            }
            finally { s.Dispose(); }
        }
    }

    // ---------------------------------------------------------------- Favoritos y opciones

    private void AddFavorite()
    {
        string path = InArchive ? _session!.Path : _folder;
        string? name = InputDialog.Ask("Nombre del favorito:", "Añadir a favoritos", Path.GetFileName(path.TrimEnd('\\')) is { Length: > 0 } n ? n : path, this);
        if (string.IsNullOrWhiteSpace(name)) return;
        _settings.Favorites.Add(new Favorite { Name = name, Path = path });
        Program.SaveSettings();
        RebuildFavorites();
    }

    private void SettingsCommand()
    {
        var before = _settings.Theme;
        using var d = new SettingsDialog(_settings);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        Program.SaveSettings();
        TopMost = _settings.AlwaysOnTop;
        if (_settings.Theme != before) ChangeTheme(_settings.Theme);
        ApplyToolbarSettings();
        if (InArchive) ShowArchiveLevel(); else GoFolder(_folder);
    }

    private void ApplyToolbarSettings()
    {
        _tools.ImageScalingSize = _settings.LargeIcons ? new Size(32, 32) : new Size(20, 20);
        var icons = new Dictionary<string, IconKind> { ["add"] = IconKind.Add, ["extract"] = IconKind.Extract, ["test"] = IconKind.Test, ["view"] = IconKind.View, ["delete"] = IconKind.Delete, ["find"] = IconKind.Find, ["wizard"] = IconKind.Wizard, ["info"] = IconKind.Info, ["repair"] = IconKind.Repair };
        var labels = new Dictionary<string, string> { ["add"] = "Añadir", ["extract"] = "Extraer en", ["test"] = "Comprobar", ["view"] = "Ver", ["delete"] = "Eliminar", ["find"] = "Buscar", ["wizard"] = "Asistente", ["info"] = "Información", ["repair"] = "Reparar" };
        foreach (var kv in _btn)
        {
            kv.Value.Image = Icons.Get(icons[kv.Key], _settings.LargeIcons ? 32 : 20);
            kv.Value.Text = _settings.ShowButtonText ? labels[kv.Key] : "";
        }
    }

    private void ChangeTheme(ThemeMode mode)
    {
        _settings.Theme = mode;
        Program.SaveSettings();
        Theme.Apply(mode);
        Theme.Style(this);
        Theme.StyleMenu(_menu.Items);
        _tools.Renderer = new ToolStripProfessionalRenderer();
        Theme.Style(this);
        Invalidate(true);
    }

    private void ExportSettings()
    {
        using var d = new SaveFileDialog { Title = "Exportar configuración", FileName = "medrar-configuracion.json", Filter = "Configuración de Medrar (*.json)|*.json" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try { SettingsStore.Export(_settings, d.FileName); UI.Info(this, "Configuración exportada."); }
        catch (Exception ex) { UI.Error(this, UI.Describe(ex)); }
    }

    private void ImportSettings()
    {
        using var d = new OpenFileDialog { Title = "Importar configuración", Filter = "Configuración de Medrar (*.json)|*.json" };
        if (d.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var s = SettingsStore.Import(d.FileName);
            Program.ReplaceSettings(s);
            UI.Info(this, "Configuración importada. Se aplicará por completo al reiniciar Medrar.");
        }
        catch (Exception ex) { UI.Error(this, "No se pudo importar: " + UI.Describe(ex)); }
    }

    private void ResetSettings()
    {
        if (!UI.Confirm(this, "¿Restablecer toda la configuración (incluidos favoritos y perfiles)?")) return;
        Program.ReplaceSettings(new AppSettings());
        UI.Info(this, "Configuración restablecida. Reinicia Medrar para aplicarla.");
    }
}

internal sealed partial class MainForm
{
    private readonly List<string> _tempDirs = new();
    private readonly Dictionary<string, string> _dragCache = new();
    private bool _draggingOut;

    /// <summary>
    /// Arrastrar fuera de la ventana: desde un archivo, los ficheros se extraen a una carpeta temporal y se ofrecen al
    /// Explorador (u otro programa) como ficheros normales. Desde una carpeta, se ofrecen los ficheros reales.
    /// </summary>
    private void OnItemDrag(object? sender, ItemDragEventArgs e)
    {
        var sel = SelectedNodes().ToList();
        if (sel.Count == 0) return;
        string[] paths;
        if (!InArchive)
        {
            paths = sel.Select(n => n.FullPath).Where(p => p.Length > 0).ToArray();
        }
        else
        {
            var entries = EntriesForSelection(false);
            string key = _session!.Path + "|" + _inside + "|" + string.Join("|", entries.Select(x => x.Path).OrderBy(x => x));
            if (!_dragCache.TryGetValue(key, out var dir) || !Directory.Exists(dir))
            {
                dir = Path.Combine(Path.GetTempPath(), "Medrar", "drag-" + Guid.NewGuid().ToString("N")[..8]);
                var o = new ExtractOptions { DestinationDir = dir, PathMode = PathMode.Relative, BaseFolder = _inside, Overwrite = OverwriteMode.Overwrite };
                long total = entries.Where(x => !x.IsDirectory).Sum(x => x.Size) + entries.Where(x => x.IsDirectory).Sum(d => Archive.Entries.Where(f => !f.IsDirectory && f.Path.StartsWith(d.Path.TrimEnd('/') + "/")).Sum(f => f.Size));
                bool done = false;
                if (total <= 64L * 1024 * 1024)
                {
                    // poco contenido: se extrae al instante, sin ventanas, para que el arrastre siga activo
                    try { Extractor.Extract(Archive, entries, o); done = true; }
                    catch (PasswordRequiredException) { }
                    catch (WrongPasswordException) { }
                    catch (Exception ex) { UI.Error(this, UI.Describe(ex)); return; }
                }
                if (!done)
                {
                    // mucho contenido (o falta la contraseña): se extrae con progreso y se vuelve a intentar el arrastre
                    var res = Ops.Extract(this, _session!, entries, o);
                    if (res != null && res.Files.Count > 0)
                    {
                        _tempDirs.Add(dir); _dragCache[key] = dir;
                        _statusLeft.Text = "Listo: ya se puede arrastrar la selección al Explorador (vuelve a arrastrarla).";
                    }
                    return;
                }
                _tempDirs.Add(dir); _dragCache[key] = dir;
            }
            paths = sel.Select(n => Path.Combine(dir, n.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        }
        if (paths.Length == 0) return;
        _draggingOut = true;
        try { DoDragDrop(new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy); }
        finally { _draggingOut = false; }
    }
}
