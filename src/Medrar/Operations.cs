using Medrar.Core;

namespace Medrar.App;

/// <summary>Un archivo abierto junto con su contraseña (se pide una sola vez y se recuerda mientras esté abierto).</summary>
internal sealed class ArchiveSession : IDisposable
{
    public string Path { get; }
    public string? Password { get; private set; }
    public IArchive Archive { get; private set; }

    private ArchiveSession(string path, IArchive archive, string? pw) { Path = path; Archive = archive; Password = pw; }

    public static ArchiveSession Wrap(string path, IArchive archive, string? pw) => new(path, archive, pw);

    public static ArchiveSession? Open(IWin32Window? owner, string path)
    {
        string? pw = null;
        bool wrong = false;
        for (;;)
        {
            try { return new ArchiveSession(path, ArchiveOpener.Open(path, pw), pw); }
            catch (PasswordRequiredException) { }
            catch (WrongPasswordException) { wrong = true; }
            pw = PasswordDialog.Ask(owner, System.IO.Path.GetFileName(path), wrong);
            if (pw == null) return null;
        }
    }

    /// <summary>
    /// Si el contenido está cifrado pero el listado se ve (ZIP con contraseña, .mdr sin nombres cifrados, RAR solo con contenido
    /// cifrado), ofrece escribir la contraseña ya, para poder modificar y extraer sin más preguntas. Cancelar solo deja ver la lista.
    /// </summary>
    public void PromptIfEncrypted(IWin32Window? owner)
    {
        if (!Archive.IsEncrypted || !string.IsNullOrEmpty(Password)) return;
        string name = System.IO.Path.GetFileName(Path);
        for (;;)
        {
            using var d = new PasswordDialog("Contraseña", $"El contenido de «{name}» está cifrado.\nEscribe la contraseña para poder extraerlo o modificarlo (puedes cancelar y ver solo la lista):", false);
            if (d.ShowDialog(owner) != DialogResult.OK) return;
            try
            {
                var fresh = ArchiveOpener.Open(Path, d.Password);
                // comprobar de verdad que la clave sirve (algunos formatos solo lo notan al descifrar)
                var first = fresh.Entries.Where(e => !e.IsDirectory && e.Size > 0).OrderBy(e => e.Size).FirstOrDefault();   // el más pequeño: la comprobación es rápida
                if (first != null)
                {
                    try { fresh.ReadAll(first, 64); }
                    catch (WrongPasswordException) { fresh.Dispose(); UI.Warn(owner, "Contraseña incorrecta."); continue; }
                }
                Archive.Dispose();
                Archive = fresh;
                Password = d.Password;
                return;
            }
            catch (WrongPasswordException) { UI.Warn(owner, "Contraseña incorrecta."); }
            catch (Exception ex) { UI.Error(owner, UI.Describe(ex)); return; }
        }
    }

    /// <summary>Pide la contraseña y reabre el archivo con ella. Devuelve false si se cancela.</summary>
    public bool Unlock(IWin32Window? owner, bool wrong)
    {
        string? pw = PasswordDialog.Ask(owner, System.IO.Path.GetFileName(Path), wrong);
        if (pw == null) return false;
        try
        {
            var fresh = ArchiveOpener.Open(Path, pw);
            Archive.Dispose();
            Archive = fresh;
            Password = pw;
            return true;
        }
        catch (WrongPasswordException) { return Unlock(owner, true); }
        catch (Exception ex) { UI.Error(owner, UI.Describe(ex)); return false; }
    }

    public void Dispose() => Archive.Dispose();
}

internal static class Ops
{
    private sealed class OverwriteDialog : DialogBase
    {
        public OverwriteDecision Decision { get; private set; } = OverwriteDecision.Cancel;
        public bool ApplyToAll => _all.Checked;
        private readonly CheckBox _all = UI.Check("Aplicar a todos los ficheros siguientes");

        public OverwriteDialog(string path) : base("El fichero ya existe")
        {
            ClientSize = new Size(480, 190);
            var info = new FileInfo(path);
            var col = UI.Column(
                UI.Label("Ya existe este fichero en la carpeta de destino:"),
                UI.Label(path, bold: true),
                UI.Label($"{UI.Size(info.Length)} · modificado {info.LastWriteTime:g}", muted: true),
                _all);
            col.Padding = new Padding(14, 12, 14, 0);
            Controls.Add(col);
            var f = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 6, 8, 6) };
            void Add(string t, OverwriteDecision d, int w = 100)
            {
                var b = UI.Button(t, (_, _) => { Decision = d; DialogResult = DialogResult.OK; }, w);
                f.Controls.Add(b);
            }
            Add("Cancelar", OverwriteDecision.Cancel);
            Add("Renombrar", OverwriteDecision.Rename);
            Add("Omitir", OverwriteDecision.Skip, 80);
            Add("Sobrescribir", OverwriteDecision.Overwrite);
            Controls.Add(f);
        }
    }

    public static Func<string, OverwriteDecision> OverwritePrompt(Control owner, ExtractOptions o) => path =>
    {
        OverwriteDecision result = OverwriteDecision.Cancel;
        owner.Invoke(() =>
        {
            using var d = new OverwriteDialog(path);
            d.ShowDialog(owner);
            result = d.Decision;
            if (d.ApplyToAll && result != OverwriteDecision.Cancel)
                o.Overwrite = result == OverwriteDecision.Overwrite ? OverwriteMode.Overwrite : result == OverwriteDecision.Skip ? OverwriteMode.Skip : OverwriteMode.Rename;
        });
        return result;
    };

    /// <summary>Repite la operación pidiendo contraseña si hace falta. Devuelve false si se canceló o falló.</summary>
    private static bool RunWithPassword(Form owner, ArchiveSession s, string title, Action<ProgressReporter> work)
    {
        for (;;)
        {
            var err = ProgressForm.Run(owner, title, work, out bool cancelled);
            if (cancelled) return false;
            if (err is PasswordRequiredException or WrongPasswordException)
            {
                if (!s.Unlock(owner, err is WrongPasswordException)) return false;
                continue;
            }
            if (err != null) { UI.Error(owner, UI.Describe(err), title); return false; }
            return true;
        }
    }

    public static ExtractResult? Extract(Form owner, ArchiveSession s, IEnumerable<ArchiveEntry> selection, ExtractOptions o)
    {
        ExtractResult? result = null;
        var sel = selection.ToList();
        o.AskOverwrite = OverwritePrompt(owner, o);
        if (!RunWithPassword(owner, s, "Extrayendo ficheros", p => result = Extractor.Extract(s.Archive, sel, o, p))) return null;
        if (result != null && !result.Ok)
            using (var d = new ResultDialog("Resultado de la extracción", $"Se extrajeron {result.Extracted} fichero(s), pero hubo {result.Errors.Count} error(es):",
                       result.Errors.Select(e => $"{e.Path}: {e.Error}"), false)) d.ShowDialog(owner);
        return result;
    }

    public static bool Test(Form owner, ArchiveSession s)
    {
        ExtractResult? result = null;
        if (!RunWithPassword(owner, s, "Comprobando el archivo", p => result = Extractor.Test(s.Archive, p))) return false;
        var lines = new List<string>();
        bool ok = result!.Ok;
        string head = ok ? $"Todo correcto: {result.Extracted} fichero(s) verificados." : $"Se encontraron {result.Errors.Count} error(es).";
        lines.AddRange(result.Errors.Select(e => $"{e.Path}: {e.Error}"));
        if (s.Archive.HasRecovery)
        {
            var rr = RecoveryRecord.Test(s.Path);
            lines.Add("Registro de recuperación: " + rr.Message);
            if (!rr.Success && !ok) head += " Puedes intentar «Reparar archivo».";
        }
        if (lines.Count == 0) lines.Add("No se encontraron errores en el archivo.");
        using var d = new ResultDialog("Comprobación", head, lines, ok);
        d.ShowDialog(owner);
        return ok;
    }

    /// <summary>Crea un archivo nuevo (o actualiza uno existente) desde los ficheros indicados. Devuelve la ruta creada.</summary>
    public static string? Create(Form owner, AddRequest req, IReadOnlyList<string> sources, ArchiveSession? existing = null)
    {
        string? created = null;
        var o = req.Options;
        bool updating = existing != null || (File.Exists(req.ArchivePath) && o.VolumeSizeBytes == 0 && !o.SelfExtracting);
        string title = updating ? "Actualizando el archivo" : "Creando el archivo";

        bool Work(string? pw)
        {
            var err = ProgressForm.Run(owner, title, p =>
            {
                var items = SourceCollector.Collect(sources, o, req.PathMode, req.Subfolder, p.Token);
                if (updating)
                {
                    existing?.Archive.Dispose();
                    var plan = new EditPlan { Mode = o.UpdateMode };
                    plan.Add.AddRange(items);
                    if (o.Lock) plan.Lock = true;
                    if (o.RecoveryPercent > 0) plan.RecoveryPercent = o.RecoveryPercent;
                    if (o.Comment != null) { plan.SetComment = true; plan.NewComment = o.Comment; }
                    ArchiveEditor.Apply(req.ArchivePath, pw ?? existing?.Password ?? o.Password, plan, p);
                    created = req.ArchivePath;
                }
                else created = ArchiveBuilder.Create(req.ArchivePath, items, o, p);
                if (o.TestAfter && created != null)
                {
                    using var a = ArchiveOpener.Open(created, o.Password ?? pw ?? existing?.Password);
                    var t = Extractor.Test(a, p);
                    if (!t.Ok) throw new MedrarException("La verificación encontró errores: " + string.Join("; ", t.Errors.Select(e => e.Path)));
                }
                if (o.DeleteSourceAfter && created != null)
                    foreach (var s in sources) { try { if (Directory.Exists(s)) Directory.Delete(s, true); else File.Delete(s); } catch { } }
            }, out bool cancelled);
            if (cancelled) { created = null; return false; }
            if (err is PasswordRequiredException or WrongPasswordException && existing != null)
            {
                if (!existing.Unlock(owner, err is WrongPasswordException)) { created = null; return false; }
                existing.Archive.Dispose();                          // se vuelve a cerrar para poder reescribirlo
                return Work(existing.Password);
            }
            if (err != null) { UI.Error(owner, UI.Describe(err), title); created = null; return false; }
            return true;
        }

        if (updating && existing != null)
        {
            // si el archivo está cifrado, ya tenemos la contraseña de la sesión
            if (!Work(existing.Password)) return null;
        }
        else if (updating)
        {
            // actualizar un archivo que no estaba abierto: se pide la contraseña si la necesita
            string? pw;
            using (var probe = ArchiveSession.Open(owner, req.ArchivePath)) { if (probe == null) return null; pw = probe.Password; }
            if (!Work(pw)) return null;
        }
        else if (!Work(null)) return null;

        if (created != null)
        {
            SettingsStore.AddRecent(Program.Settings, created);
            Program.SaveSettings();
        }
        return created;
    }

    public static void Repair(Form owner, string archivePath)
    {
        string dest = Path.Combine(Path.GetDirectoryName(archivePath)!, Path.GetFileNameWithoutExtension(archivePath) + ".reparado" + Path.GetExtension(archivePath));
        if (File.Exists(dest) && !UI.Confirm(owner, $"«{Path.GetFileName(dest)}» ya existe. ¿Reemplazarlo?")) return;
        RepairResult? r = null;
        var err = ProgressForm.Run(owner, "Reparando el archivo", p => r = RecoveryRecord.Repair(archivePath, dest, null, p), out bool cancelled);
        if (cancelled) return;
        if (err != null) { UI.Error(owner, UI.Describe(err)); return; }
        string msg = r!.Message + (r.OutputPath != null && r.BlocksDamaged > 0 ? $"\n\nSe guardó una copia reparada en:\n{r.OutputPath}\n(el original no se ha modificado)" : r.OutputPath != null ? $"\n\nCopia: {r.OutputPath}" : "");
        if (r.Success) UI.Info(owner, msg, "Reparar archivo"); else UI.Warn(owner, msg, "Reparar archivo");
    }

    public static void ConvertToSfx(Form owner, string archivePath, bool includeRuntime)
    {
        try
        {
            string? exe = null;
            var err = ProgressForm.Run(owner, "Creando el autoextraíble", p => exe = SfxBuilder.ConvertToSfx(archivePath, null, includeRuntime), out bool cancelled);
            if (cancelled) return;
            if (err != null) { UI.Error(owner, UI.Describe(err)); return; }
            UI.Info(owner, $"Se creó el archivo autoextraíble:\n{exe}\n({UI.Size(new FileInfo(exe!).Length)})");
        }
        catch (Exception ex) { UI.Error(owner, UI.Describe(ex)); }
    }

    /// <summary>Quita el módulo autoextraíble de un .exe y deja el archivo original a su lado.</summary>
    public static string? UnpackSfx(Form owner, string exePath)
    {
        try
        {
            string? result = null;
            var err = ProgressForm.Run(owner, "Quitando el módulo autoextraíble", p => result = SfxBuilder.Unpack(exePath), out bool cancelled);
            if (cancelled) return null;
            if (err != null) { UI.Error(owner, UI.Describe(err)); return null; }
            return result;
        }
        catch (Exception ex) { UI.Error(owner, UI.Describe(ex)); return null; }
    }

    /// <summary>Crea el fichero de recuperación (.mdrrec) de cualquier archivo.</summary>
    public static bool CreateRecoveryFile(Form owner, string archivePath, int percent)
    {
        var err = ProgressForm.Run(owner, "Calculando el registro de recuperación", p => RecoveryRecord.CreateSidecar(archivePath, percent, p), out bool cancelled);
        if (cancelled) return false;
        if (err != null) { UI.Error(owner, UI.Describe(err)); return false; }
        return true;
    }

    public static void OpenFolderOf(string path)
    {
        try { System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{path}\""); } catch { }
    }
}
