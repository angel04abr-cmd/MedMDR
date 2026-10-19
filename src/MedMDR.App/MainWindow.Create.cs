using System.Windows;
using MedMDR.Core;

namespace MedMDR.Wpf;

public partial class MainWindow
{
    /// <summary>Crea un archivo nuevo (o actualiza uno existente) desde los ficheros indicados. Devuelve la ruta creada.</summary>
    private string? CreateArchive(AddRequest req, IReadOnlyList<string> sources, ArchiveSession? existing = null)
    {
        string? created = null;
        var o = req.Options;
        bool updating = existing != null || (File.Exists(req.ArchivePath) && o.VolumeSizeBytes == 0 && !o.SelfExtracting);
        string title = updating ? "Actualizando el archivo" : "Creando el archivo";

        bool Work(string? pw)
        {
            var err = ProgressWindow.Run(this, title, p =>
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
                    if (!t.Ok) throw new MedMDRException("La verificación encontró errores: " + string.Join("; ", t.Errors.Select(e => e.Path)));
                }
                if (o.DeleteSourceAfter && created != null)
                    foreach (var s in sources) { try { if (Directory.Exists(s)) Directory.Delete(s, true); else File.Delete(s); } catch { /* en uso */ } }
            }, out bool cancelled);
            if (cancelled) { created = null; return false; }
            if (err is PasswordRequiredException or WrongPasswordException && existing != null)
            {
                if (!existing.Unlock(this, err is WrongPasswordException)) { created = null; return false; }
                existing.Archive.Dispose();                          // se vuelve a cerrar para poder reescribirlo
                return Work(existing.Password);
            }
            if (err != null) { Msg.Error(this, Msg.Describe(err), title); created = null; return false; }
            return true;
        }

        if (updating && existing != null)
        {
            if (!Work(existing.Password)) return null;               // si está cifrado, la sesión ya tiene la contraseña
        }
        else if (updating)
        {
            string? pw;                                              // actualizar uno que no estaba abierto: se pide la contraseña si la necesita
            using (var probe = ArchiveSession.Open(this, req.ArchivePath)) { if (probe == null) return null; pw = probe.Password; }
            if (!Work(pw)) return null;
        }
        else if (!Work(null)) return null;

        if (created != null)
        {
            SettingsStore.AddRecent(_settings, created);
            SaveSettings();
        }
        return created;
    }

    /// <summary>Muestra «Añadir al archivo» y crea el archivo nuevo con los ficheros indicados.</summary>
    private void AddToNewArchive(IReadOnlyList<string> paths)
    {
        var req = AddWindow.Ask(this, paths, _folder, _settings);
        if (req == null) return;
        var created = CreateArchive(req, paths);
        if (created == null) return;
        Say("Archivo creado: " + created);
        if (!InArchive)
        {
            LoadFolder(Path.GetDirectoryName(created)!);
            SelectByName(Path.GetFileName(created));
        }
    }
}
