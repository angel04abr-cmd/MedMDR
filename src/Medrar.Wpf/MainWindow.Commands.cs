using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Órdenes de la ventana principal (extraer, comprobar, ver, editar el archivo, proteger, reparar…).</summary>
public partial class MainWindow
{
    // ------------------------------------------------------------------ utilidades

    private string DefaultExtractDir(string archivePath)
    {
        string name = Path.GetFileNameWithoutExtension(archivePath);
        if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
        string baseDir = !string.IsNullOrWhiteSpace(_settings.DefaultExtractFolder) ? _settings.DefaultExtractFolder : Path.GetDirectoryName(Path.GetFullPath(archivePath))!;
        return Path.Combine(baseDir, name);
    }

    private string? SelectedArchivePath() => InArchive ? _session!.Path : Selected().FirstOrDefault(x => x.IsArchive)?.FullPath;

    /// <summary>Entradas a procesar: la selección actual o, si no hay, todo el archivo.</summary>
    private List<ArchiveEntry> EntriesForSelection(bool allIfNone)
    {
        var sel = Selected();
        if (sel.Count == 0) return allIfNone ? Archive.Entries.ToList() : new List<ArchiveEntry>();
        var list = new List<ArchiveEntry>();
        foreach (var n in sel)
        {
            if (!n.IsDirectory) { if (n.Entry != null) list.Add(n.Entry); }
            else list.Add(new ArchiveEntry { Path = n.FullPath, IsDirectory = true, Modified = n.Modified });
        }
        return list;
    }

    private ArchiveSession? OpenSessionFor(string path)
    {
        if (InArchive && string.Equals(_session!.Path, path, StringComparison.OrdinalIgnoreCase)) return _session;
        return ArchiveSession.Open(this, path);
    }

    private string NotModifiableMessage() =>
        SfxBuilder.IsSfx(_session!.Path)
            ? "Un autoextraíble es de solo lectura. Usa Herramientas > «Quitar el módulo autoextraíble» para recuperar el archivo y poder modificarlo."
            : $"{AppInfo.Name} no puede modificar archivos {Archive.FormatName}. Conviértelo antes a .mdr, ZIP o 7Z (Herramientas > Convertir archivos).";

    /// <summary>Repite la operación pidiendo contraseña si hace falta. Devuelve false si se canceló o falló.</summary>
    private bool RunWithPassword(ArchiveSession s, string title, Action<ProgressReporter> work)
    {
        for (;;)
        {
            var err = ProgressWindow.Run(this, title, work, out bool cancelled);
            if (cancelled) return false;
            if (err is PasswordRequiredException or WrongPasswordException)
            {
                if (!s.Unlock(this, err is WrongPasswordException)) return false;
                continue;
            }
            if (err != null) { Msg.Error(this, Msg.Describe(err), title); return false; }
            return true;
        }
    }

    // ------------------------------------------------------------------ extraer

    private ExtractResult? ExtractWith(ArchiveSession s, IEnumerable<ArchiveEntry> selection, ExtractOptions o)
    {
        ExtractResult? result = null;
        var sel = selection.ToList();
        o.AskOverwrite = OverwriteWindow.Prompt(this, o);
        if (!RunWithPassword(s, "Extrayendo ficheros", p => result = Extractor.Extract(s.Archive, sel, o, p))) return null;
        if (result != null && !result.Ok)
            ResultWindow.Show(this, "Resultado de la extracción", $"Se extrajeron {result.Extracted} fichero(s), pero hubo {result.Errors.Count} error(es):",
                result.Errors.Select(e => $"{e.Path}: {e.Error}"), false);
        return result;
    }

    private void Command_ExtractHere(object sender, RoutedEventArgs e) => ExtractCommand(ask: false, here: true);
    private void Command_Extract(object sender, RoutedEventArgs e) => ExtractCommand(ask: true, here: false);

    private void ExtractCommand(bool ask, bool here)
    {
        string? archivePath = SelectedArchivePath();
        if (archivePath == null) { Msg.Info(this, "Abre o selecciona un archivo comprimido para extraer."); return; }
        var paths = InArchive ? new List<string> { archivePath } : Selected().Where(x => x.IsArchive).Select(x => x.FullPath).ToList();
        bool multiple = paths.Count > 1;

        string dest = here ? Path.GetDirectoryName(Path.GetFullPath(archivePath))! : DefaultExtractDir(archivePath);
        ExtractRequest? req = null;
        if (ask)
        {
            req = ExtractWindow.Ask(this, archivePath, dest, _settings, showSubfolderOption: !multiple);
            if (req == null) return;
            dest = req.Destination;
        }

        foreach (var path in paths)
        {
            var s = OpenSessionFor(path);
            if (s == null) continue;
            try
            {
                var o = req?.Options ?? new ExtractOptions
                {
                    Overwrite = here ? OverwriteMode.Ask : _settings.ExtractOverwrite,
                    KeepBrokenFiles = _settings.KeepBrokenFiles
                };
                o.DestinationDir = multiple ? Path.Combine(dest, Path.GetFileNameWithoutExtension(path)) : dest;
                o.RestoreTimes = true;
                var entries = InArchive && ReferenceEquals(s, _session) ? EntriesForSelection(true) : s.Archive.Entries.ToList();
                var res = ExtractWith(s, entries, o);
                if (res != null)
                {
                    SettingsStore.AddHistory(_settings, o.DestinationDir);
                    Say($"Extraídos {res.Extracted} fichero(s) en {o.DestinationDir}");
                    if (req?.ShowInExplorer == true && Directory.Exists(o.DestinationDir))
                        try { Process.Start(new ProcessStartInfo("explorer.exe", $"\"{o.DestinationDir}\"") { UseShellExecute = true }); } catch { /* sin Explorador */ }
                }
            }
            finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
        }
        SaveSettings();
        if (!InArchive) LoadFolder(_folder);
    }

    // ------------------------------------------------------------------ comprobar, ver, información

    private void Command_Test(object sender, RoutedEventArgs e)
    {
        string? p = SelectedArchivePath();
        if (p == null) { Msg.Info(this, "Abre o selecciona un archivo comprimido para comprobarlo."); return; }
        var s = OpenSessionFor(p);
        if (s == null) return;
        try
        {
            ExtractResult? result = null;
            if (!RunWithPassword(s, "Comprobando el archivo", prog => result = Extractor.Test(s.Archive, prog))) return;
            bool ok = result!.Ok;
            string head = ok ? $"Todo correcto: {result.Extracted} fichero(s) verificados." : $"Se encontraron {result.Errors.Count} error(es).";
            var lines = result.Errors.Select(x => $"{x.Path}: {x.Error}").ToList();
            if (s.Archive.HasRecovery)
            {
                var rr = RecoveryRecord.Test(s.Path);
                lines.Add("Registro de recuperación: " + rr.Message);
                if (!rr.Success && !ok) head += " Puedes intentar «Reparar archivo».";
            }
            if (lines.Count == 0) lines.Add("No se encontraron errores en el archivo.");
            ResultWindow.Show(this, "Comprobación", head, lines, ok);
        }
        finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
    }

    private void Command_View(object sender, RoutedEventArgs e)
    {
        var n = Selected().FirstOrDefault(x => !x.IsDirectory);
        if (n == null) { Msg.Info(this, "Selecciona un fichero para verlo."); return; }
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
                if (!RunWithPassword(s, "Leyendo " + n.Name, _ => data = s.Archive.ReadAll(entry, limit))) return;
            }
            else
            {
                using var fs = File.OpenRead(n.FullPath);
                truncated = fs.Length > limit;
                data = new byte[Math.Min(fs.Length, limit)];
                fs.ReadExactly(data);
            }
        }
        catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); return; }
        ViewerWindow.Show(this, n.Name, data!, _settings, truncated);
    }

    private void OpenEntryExternally(FileItem n)
    {
        if (n.Entry == null) return;
        string dir = Path.Combine(Path.GetTempPath(), AppInfo.Name, Guid.NewGuid().ToString("N")[..8]);
        _tempDirs.Add(dir);
        var o = new ExtractOptions { DestinationDir = dir, PathMode = PathMode.None, Overwrite = OverwriteMode.Overwrite };
        var res = ExtractWith(_session!, new[] { n.Entry }, o);
        if (res == null || res.Files.Count == 0) return;
        _tempFiles.AddRange(res.Files);
        try { Process.Start(new ProcessStartInfo(res.Files[0]) { UseShellExecute = true }); }
        catch (Exception ex) { Msg.Warn(this, "No hay ningún programa asociado para abrir este fichero.\n" + ex.Message); }
    }

    private void Command_Info(object sender, RoutedEventArgs e)
    {
        string? p = SelectedArchivePath();
        if (p == null) { Msg.Info(this, "Abre o selecciona un archivo comprimido para ver su información."); return; }
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
            ViewerWindow.ShowText(this, "Información — " + Path.GetFileName(p), text, _settings);
        }
        finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
    }

    private void Command_Report(object sender, RoutedEventArgs e)
    {
        string? p = SelectedArchivePath();
        if (p == null) { Msg.Info(this, "Abre o selecciona un archivo comprimido para generar su informe."); return; }
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Guardar informe", FileName = Path.GetFileNameWithoutExtension(p) + "-informe", Filter = "Página web (*.html)|*.html|Texto (*.txt)|*.txt", DefaultExt = "html"
        };
        if (dlg.ShowDialog(this) != true) return;
        var s = OpenSessionFor(p);
        if (s == null) return;
        try
        {
            string content = dlg.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ? ArchiveReport.Text(s.Archive) : ArchiveReport.Html(s.Archive);
            File.WriteAllText(dlg.FileName, content, new System.Text.UTF8Encoding(true));
            if (Msg.Confirm(this, "Informe guardado.\n¿Abrirlo ahora?")) Process.Start(new ProcessStartInfo(dlg.FileName) { UseShellExecute = true });
        }
        catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); }
        finally { if (!ReferenceEquals(s, _session)) s.Dispose(); }
    }

    // ------------------------------------------------------------------ editar el archivo

    private bool ApplyEdit(string title, Action<EditPlan> build)
    {
        var s = _session!;
        if (!Archive.CanModify) { Msg.Warn(this, NotModifiableMessage()); return false; }
        var plan = new EditPlan();
        build(plan);
        string path = s.Path;
        for (;;)
        {
            string? pw = s.Password;
            s.Archive.Dispose();
            var err = ProgressWindow.Run(this, title, p => ArchiveEditor.Apply(path, pw, plan, p), out bool cancelled);
            if (!cancelled && err is PasswordRequiredException or WrongPasswordException)
            {
                // falta la contraseña (o no sirve): se pide, se reabre el archivo con ella y se repite el cambio
                if (s.Unlock(this, err is WrongPasswordException)) continue;
                ReloadArchive();
                return false;
            }
            ReloadArchive();
            if (cancelled) return false;
            if (err != null) { Msg.Error(this, Msg.Describe(err), title); return false; }
            return true;
        }
    }

    /// <summary>Aplica cambios a un archivo que puede estar abierto en la ventana o no (pide la contraseña si hace falta).</summary>
    private bool EditFile(string path, string title, Action<EditPlan> build)
    {
        if (InArchive && string.Equals(_session!.Path, path, StringComparison.OrdinalIgnoreCase)) return ApplyEdit(title, build);
        string? pw;
        using (var probe = ArchiveSession.Open(this, path)) { if (probe == null) return false; pw = probe.Password; }
        var plan = new EditPlan();
        build(plan);
        var err = ProgressWindow.Run(this, title, p => ArchiveEditor.Apply(path, pw, plan, p), out bool cancelled);
        if (cancelled) return false;
        if (err != null) { Msg.Error(this, Msg.Describe(err), title); return false; }
        return true;
    }

    private void Command_Add(object sender, RoutedEventArgs e)
    {
        if (!InArchive)
        {
            var paths = Selected().Where(n => !n.IsUp).Select(n => n.FullPath).ToArray();
            if (paths.Length == 0) { Msg.Info(this, "Selecciona primero los ficheros o carpetas que quieres comprimir (o arrástralos a la ventana)."); return; }
            AddToNewArchive(paths);
            return;
        }
        var menu = new ContextMenu { PlacementTarget = sender as UIElement ?? FileList, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var files = new MenuItem { Header = "Añadir ficheros…" };
        files.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Ficheros que se añadirán al archivo" };
            if (dlg.ShowDialog(this) == true) AddToCurrentArchive(dlg.FileNames);
        };
        var folder = new MenuItem { Header = "Añadir una carpeta…" };
        folder.Click += (_, _) =>
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta que se añadirá al archivo" };
            if (dlg.ShowDialog(this) == true) AddToCurrentArchive(new[] { dlg.FolderName });
        };
        menu.Items.Add(files);
        menu.Items.Add(folder);
        menu.IsOpen = true;
    }

    private void AddToCurrentArchive(string[] paths)
    {
        if (!InArchive) return;
        if (!Archive.CanModify) { Msg.Warn(this, NotModifiableMessage()); return; }
        if (Archive.IsLocked) { Msg.Warn(this, "El archivo está bloqueado: no se puede modificar."); return; }
        var s = _session!;
        var req = AddWindow.Ask(this, paths, _folder, _settings, s.Path);
        if (req == null) return;
        req.Subfolder = _inside;
        req.ArchivePath = s.Path;
        req.Options.Password = s.Password;
        s.Archive.Dispose();
        var created = CreateArchive(req, paths, s);
        ReloadArchive();
        if (created != null) Say("Archivo actualizado");
    }

    private void Command_Delete(object sender, RoutedEventArgs e)
    {
        var sel = Selected();
        if (sel.Count == 0) return;
        if (InArchive)
        {
            if (Archive.IsLocked) { Msg.Warn(this, "El archivo está bloqueado: no se puede modificar."); return; }
            if (_settings.AskBeforeDelete && !Msg.Confirm(this, $"¿Eliminar {sel.Count} elemento(s) del archivo?\nEsta acción reescribe el archivo y no se puede deshacer.", "Eliminar")) return;
            ApplyEdit("Eliminando del archivo", plan => { foreach (var n in sel) plan.Delete.Add(n.FullPath); });
        }
        else
        {
            if (_settings.AskBeforeDelete && !Msg.Confirm(this, $"¿Enviar {sel.Count} elemento(s) a la Papelera de reciclaje?", "Eliminar")) return;
            if (!Recycle.Delete(new WindowInteropHelper(this).Handle, sel.Select(n => n.FullPath))) Msg.Warn(this, "No se pudieron eliminar todos los elementos.");
            LoadFolder(_folder);
        }
    }

    private void Command_Rename(object sender, RoutedEventArgs e)
    {
        var n = Selected().FirstOrDefault();
        if (n == null) return;
        string? newName = TextPromptWindow.Ask(this, "Nuevo nombre:", "Renombrar", n.Name);
        if (string.IsNullOrWhiteSpace(newName) || newName == n.Name) return;
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { Msg.Warn(this, "El nombre contiene caracteres no permitidos."); return; }
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
                if (n.IsDirectory) Directory.Move(n.FullPath, dest); else File.Move(n.FullPath, dest);
                LoadFolder(_folder);
            }
            catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); }
        }
    }

    private void Command_Comment(object sender, RoutedEventArgs e)
    {
        if (!InArchive) { Msg.Info(this, "Abre primero un archivo .mdr para agregar un comentario."); return; }
        if (Archive is not MdrArchive || !Archive.CanModify) { Msg.Info(this, "Los comentarios solo se pueden guardar dentro de archivos .mdr (no autoextraíbles)."); return; }
        string? text = TextPromptWindow.Ask(this, "Comentario del archivo (vacío = quitarlo):", "Comentario", Archive.Comment ?? "", multiline: true);
        if (text == null) return;
        ApplyEdit("Guardando el comentario", plan => { plan.SetComment = true; plan.NewComment = string.IsNullOrWhiteSpace(text) ? null : text; });
        ShowComment(Archive.Comment);
    }

    // ------------------------------------------------------------------ proteger, bloquear, reparar, autoextraíble

    private void Command_Protect(object sender, RoutedEventArgs e)
    {
        string? p = SelectedArchivePath();
        if (p == null) { Msg.Info(this, "Abre o selecciona un archivo comprimido para protegerlo contra daños."); return; }
        string? v = TextPromptWindow.Ask(this, "Porcentaje de datos de recuperación (1-50 %):", "Proteger el archivo contra daños", "5");
        if (v == null) return;
        if (!int.TryParse(v.Trim().TrimEnd('%'), out int pct) || pct is < 1 or > 50) { Msg.Warn(this, "Escribe un número entre 1 y 50."); return; }
        bool embedded = MdrArchive.LooksLikeMdr(p) && !SfxBuilder.IsSfx(p) && !VolumeSet.IsVolumeName(p);
        bool ok;
        if (embedded) ok = EditFile(p, "Añadiendo el registro de recuperación", plan => plan.RecoveryPercent = pct);
        else
        {
            // ZIP, 7Z, TAR, RAR, autoextraíbles...: el registro va en un fichero aparte
            var err = ProgressWindow.Run(this, "Calculando el registro de recuperación", prog => RecoveryRecord.CreateSidecar(p, pct, prog), out bool cancelled);
            if (err != null && !cancelled) Msg.Error(this, Msg.Describe(err));
            ok = err == null && !cancelled;
        }
        if (!ok) return;
        string where = embedded ? "dentro del archivo" : "en el fichero «" + Path.GetFileName(RecoveryRecord.SidecarPathFor(p)) + "» (guárdalo junto al archivo)";
        Msg.Info(this, $"Listo: registro de recuperación del {pct} % {where}.");
        if (!InArchive) LoadFolder(_folder);
    }

    private void Command_Lock(object sender, RoutedEventArgs e)
    {
        string? p = SelectedArchivePath();
        if (p == null) { Msg.Info(this, "Abre o selecciona un archivo comprimido para bloquearlo o desbloquearlo."); return; }
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
            if (!Msg.Confirm(this, "El archivo está bloqueado.\n¿Desbloquearlo para poder modificarlo?")) return;
            if (isMdr && !EditFile(p, "Desbloqueando el archivo", plan => plan.Lock = false)) return;
            if (ArchiveEditor.IsReadOnlyFile(p)) { try { ArchiveBuilder.UnlockFile(p); } catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); return; } }
        }
        else
        {
            if (!Msg.Confirm(this, $"Un archivo bloqueado ya no se puede modificar con {AppInfo.Name} (añadir, eliminar, renombrar…).\nEs una protección contra accidentes, no una medida de seguridad.\n¿Bloquearlo ahora?")) return;
            if (isMdr) { if (!EditFile(p, "Bloqueando el archivo", plan => plan.Lock = true)) return; }
            else { try { ArchiveBuilder.LockFile(p); } catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); return; } }
        }
        if (InArchive) ReloadArchive(); else LoadFolder(_folder);
        UpdateStatus();
    }

    private void Command_Repair(object sender, RoutedEventArgs e)
    {
        string? p = SelectedArchivePath();
        if (p == null)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Archivo que se va a reparar", Filter = "Todos los ficheros|*.*" };
            if (dlg.ShowDialog(this) != true) return;
            p = dlg.FileName;
        }
        if (!File.Exists(RecoveryRecord.SidecarPathFor(p)) && !MdrArchive.LooksLikeMdr(p))
        {
            Msg.Info(this, "Este archivo no tiene registro de recuperación, así que no se puede reparar.\n\nSi lo creas de nuevo, usa Órdenes > Proteger contra daños: genera un registro (dentro del .mdr, o en un fichero .mdrrec junto a los demás formatos). Con ese fichero se puede reparar incluso un archivo truncado.");
            return;
        }
        string dest = Path.Combine(Path.GetDirectoryName(p)!, Path.GetFileNameWithoutExtension(p) + ".reparado" + Path.GetExtension(p));
        if (File.Exists(dest) && !Msg.Confirm(this, $"«{Path.GetFileName(dest)}» ya existe. ¿Reemplazarlo?")) return;
        RepairResult? r = null;
        string src = p;
        var err = ProgressWindow.Run(this, "Reparando el archivo", prog => r = RecoveryRecord.Repair(src, dest, null, prog), out bool cancelled);
        if (cancelled) return;
        if (err != null) { Msg.Error(this, Msg.Describe(err)); return; }
        string msg = r!.Message + (r.OutputPath != null && r.BlocksDamaged > 0 ? $"\n\nSe guardó una copia reparada en:\n{r.OutputPath}\n(el original no se ha modificado)" : r.OutputPath != null ? $"\n\nCopia: {r.OutputPath}" : "");
        if (r.Success) Msg.Info(this, msg, "Reparar archivo"); else Msg.Warn(this, msg, "Reparar archivo");
        if (!InArchive) LoadFolder(_folder);
    }

    private void Command_Sfx(object sender, RoutedEventArgs e)
    {
        string? p = SelectedArchivePath();
        if (p == null || p.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) || VolumeSet.IsVolumeName(p))
        {
            Msg.Info(this, "Selecciona o abre un archivo .mdr, ZIP, 7Z, TAR, TAR.GZ o GZ para convertirlo en autoextraíble.");
            return;
        }
        if (SfxBuilder.IsSfx(p)) { Msg.Info(this, "Ese fichero ya es un autoextraíble."); return; }
        bool full = false;
        if (SfxBuilder.FindStub(true) != null)
        {
            var r = Msg.AskYesNoCancel(this, "¿Incluir .NET dentro del autoextraíble?\n\nSí: funciona en cualquier equipo con Windows, sin instalar nada (pesa bastante más).\nNo: ligero, pero necesita .NET 10 en el equipo donde se abra.", "Autoextraíble");
            if (r == MessageBoxResult.Cancel) return;
            full = r == MessageBoxResult.Yes;
        }
        string? exe = null;
        string src = p;
        var err = ProgressWindow.Run(this, "Creando el autoextraíble", _ => exe = SfxBuilder.ConvertToSfx(src, null, full), out bool cancelled);
        if (cancelled) return;
        if (err != null) { Msg.Error(this, Msg.Describe(err)); return; }
        Msg.Info(this, $"Se creó el archivo autoextraíble:\n{exe}\n({ArchiveReport.FormatSize(new FileInfo(exe!).Length)})");
        if (!InArchive) LoadFolder(_folder);
    }

    private void Command_UnpackSfx(object sender, RoutedEventArgs e)
    {
        string? p = !InArchive ? Selected().FirstOrDefault(n => !n.IsDirectory && n.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))?.FullPath : _session!.Path;
        if (p == null || !SfxBuilder.IsSfx(p)) { Msg.Info(this, $"Selecciona o abre un archivo autoextraíble (.exe de {AppInfo.Name})."); return; }
        string? result = null;
        string src = p;
        var err = ProgressWindow.Run(this, "Quitando el módulo autoextraíble", _ => result = SfxBuilder.Unpack(src), out bool cancelled);
        if (cancelled) return;
        if (err != null) { Msg.Error(this, Msg.Describe(err)); return; }
        if (InArchive) CloseArchive(true); else LoadFolder(_folder);
        Msg.Info(this, "Se guardó el archivo original (sin el módulo autoextraíble):\n" + result);
    }

    // ------------------------------------------------------------------ pendientes de la Fase 3 (necesitan sus diálogos)

    private void Command_Wizard(object sender, RoutedEventArgs e)
    {
        var w = WizardWindow.Ask(this, _folder, _settings);
        if (w == null) return;
        if (w.Create)
        {
            var created = CreateArchive(w.Request, w.Sources);
            if (created == null) return;
            if (InArchive) CloseArchive(false);
            LoadFolder(Path.GetDirectoryName(created)!);
            SelectByName(Path.GetFileName(created));
            Say("Archivo creado: " + created);
        }
        else
        {
            var s = ArchiveSession.Open(this, w.ArchivePath);
            if (s == null) return;
            try
            {
                var o = new ExtractOptions { DestinationDir = w.Destination, Overwrite = _settings.ExtractOverwrite, KeepBrokenFiles = _settings.KeepBrokenFiles, RestoreTimes = true };
                var res = ExtractWith(s, s.Archive.Entries.ToList(), o);
                if (res != null && Directory.Exists(w.Destination))
                {
                    Msg.Info(this, $"Se extrajeron {res.Extracted} fichero(s) en:\n{w.Destination}");
                    if (InArchive) CloseArchive(false);
                    LoadFolder(w.Destination);
                }
            }
            finally { s.Dispose(); }
        }
    }

    private void Command_Convert(object sender, RoutedEventArgs e)
    {
        var initial = InArchive ? new List<string> { _session!.Path }
            : Selected().Where(n => !n.IsDirectory && ArchiveOpener.LooksLikeArchive(n.FullPath)).Select(n => n.FullPath).ToList();
        var d = ConvertWindow.Ask(this, initial, _folder);
        if (d == null) return;
        int ok = 0;
        var failures = new List<string>();
        foreach (var src in d.Sources)
        {
            string name = Path.GetFileNameWithoutExtension(src);
            if (name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) name = Path.GetFileNameWithoutExtension(name);
            string dest = Path.Combine(d.Destination, name + ArchiveBuilder.Extension(d.Options.Format));
            if (File.Exists(dest)) dest = Extractor.UniqueName(dest);
            string? pw = null;
            using (var probe = ArchiveSession.Open(this, src)) { if (probe == null) continue; pw = probe.Password; }
            var err = ProgressWindow.Run(this, "Convirtiendo " + Path.GetFileName(src), p => ArchiveEditor.Convert(src, pw, dest, d.Options, p), out bool cancelled);
            if (cancelled) break;
            if (err != null) failures.Add($"{Path.GetFileName(src)}: {Msg.Describe(err)}");
            else
            {
                ok++;
                if (d.DeleteOriginals) { try { VolumeSet.DeleteAll(src); } catch { /* en uso */ } }
            }
        }
        if (failures.Count > 0) ResultWindow.Show(this, "Convertir archivos", $"Convertidos {ok}; fallaron {failures.Count}:", failures, false);
        else if (ok > 0) Msg.Info(this, $"Se convirtieron {ok} archivo(s) en:\n{d.Destination}");
        if (InArchive) CloseArchive(true); else LoadFolder(_folder);
    }

    private void Command_Find(object sender, RoutedEventArgs e)
    {
        string? p = FindWindow.Ask(this, _folder, InArchive ? Archive : null, _session?.Password);
        if (p == null) return;
        if (InArchive)
        {
            int i = p.LastIndexOf('/');
            _inside = i < 0 ? "" : p[..i];
            ShowArchiveLevel();
            SelectByName(i < 0 ? p : p[(i + 1)..]);
        }
        else if (Directory.Exists(p)) LoadFolder(p);
        else if (File.Exists(p))
        {
            LoadFolder(Path.GetDirectoryName(p)!);
            if (ArchiveOpener.LooksLikeArchive(p)) OpenArchive(p); else SelectByName(Path.GetFileName(p));
        }
    }

    private void SelectByName(string name)
    {
        for (int i = 0; i < _view.Count; i++)
            if (((FileItem)_view.GetItemAt(i)).Name.Equals(name, StringComparison.OrdinalIgnoreCase)) { SelectRowForSnapshot(i); break; }
    }

    // ------------------------------------------------------------------ menú contextual de la lista

    private void FileList_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        RowMenu.Items.Clear();
        var sel = Selected();
        bool one = sel.Count == 1;
        void Add(string header, RoutedEventHandler click) { var mi = new MenuItem { Header = header }; mi.Click += click; RowMenu.Items.Add(mi); }
        void Sep() => RowMenu.Items.Add(new Separator());

        if (InArchive)
        {
            if (sel.Count > 0)
            {
                Add("Abrir", (_, _) => ActivateSelected());
                Add("Extraer en una carpeta…", Command_Extract);
                Add("Extraer aquí (junto al archivo)", Command_ExtractHere);
                Add("Ver", Command_View);
                Sep();
                Add("Renombrar", Command_Rename);
                Add("Eliminar del archivo", Command_Delete);
                Sep();
            }
            Add("Añadir ficheros al archivo…", Command_Add);
            Add("Comprobar el archivo", Command_Test);
            Add("Información del archivo", Command_Info);
            Sep();
            Add("Cerrar el archivo", CloseArchive_Click);
        }
        else if (sel.Count > 0)
        {
            bool isArc = one && sel[0].IsArchive;
            Add("Abrir", (_, _) => ActivateSelected());
            if (isArc)
            {
                Add("Extraer en una carpeta…", Command_Extract);
                Add("Extraer aquí", Command_ExtractHere);
                Add("Comprobar", Command_Test);
                Add("Reparar…", Command_Repair);
            }
            Add("Añadir a un archivo…", Command_Add);
            Sep();
            Add("Renombrar", Command_Rename);
            Add("Eliminar (a la Papelera)", Command_Delete);
            Sep();
            Add("Mostrar en el Explorador", (_, _) => { try { Process.Start("explorer.exe", $"/select,\"{sel[0].FullPath}\""); } catch { } });
        }
        else Add("Actualizar", (_, _) => LoadFolder(_folder));
    }
}
