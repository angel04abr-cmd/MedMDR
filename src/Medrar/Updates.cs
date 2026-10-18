using System.Diagnostics;
using System.Reflection;
using Medrar.Core;

namespace Medrar.App;

/// <summary>Comprobación y aplicación de actualizaciones desde GitHub Releases (ver <see cref="UpdateService"/>).</summary>
internal static class Updates
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static int _busy;

    public static Version CurrentVersion =>
        UpdateService.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0));

    private static UpdateService NewService(AppSettings s) => UpdateService.FromEnvironment(TokenProtector.Unprotect(s.UpdateTokenProtected));

    /// <summary>Al abrir Medrar: si toca (como mucho una vez cada 24 h) busca en segundo plano y avisa solo si hay algo nuevo.</summary>
    public static void CheckInBackground(Form owner, AppSettings s)
    {
        if (!s.AutoCheckUpdates || DateTime.UtcNow - s.LastUpdateCheckUtc < Interval) return;
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var svc = NewService(s);
                var info = await svc.CheckAsync(CurrentVersion).ConfigureAwait(false);
                s.LastUpdateCheckUtc = DateTime.UtcNow;
                Program.SaveSettings();
                if (info != null && info.Version.ToString() != s.SkippedVersion && !owner.IsDisposed)
                    owner.BeginInvoke(() => Offer(owner, s, info));
            }
            catch
            {
                // En silencio: sin red, sin token o GitHub caído no deben molestar al abrir el programa.
                s.LastUpdateCheckUtc = DateTime.UtcNow;
                Program.SaveSettings();
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    /// <summary>Ayuda > Buscar actualizaciones: siempre da una respuesta (nueva versión, al día o el motivo del fallo).</summary>
    public static async Task CheckNowAsync(Form owner, AppSettings s)
    {
        UpdateInfo? info;
        owner.Cursor = Cursors.WaitCursor;
        try
        {
            using var svc = NewService(s);
            info = await svc.CheckAsync(CurrentVersion);
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            Program.SaveSettings();
        }
        catch (Exception ex) { owner.Cursor = Cursors.Default; UI.Warn(owner, "No se pudo buscar actualizaciones.\n\n" + UI.Describe(ex)); return; }
        finally { owner.Cursor = Cursors.Default; }

        if (info == null) UI.Info(owner, $"Medrar está al día: tienes la versión {CurrentVersion}, que es la más reciente.");
        else Offer(owner, s, info, manual: true);
    }

    private static void Offer(Form owner, AppSettings s, UpdateInfo info, bool manual = false)
    {
        using var d = new UpdateDialog(info, CurrentVersion);
        var r = d.ShowDialog(owner);
        if (r == DialogResult.OK) Install(owner, s, info);
        else if (r == DialogResult.Ignore && !manual) { s.SkippedVersion = info.Version.ToString(); Program.SaveSettings(); }
    }

    private static void Install(Form owner, AppSettings s, UpdateInfo info)
    {
        if (!UpdateInstaller.IsMsiInstall())
        {
            UI.Info(owner, UpdateInstaller.IsLegacyPerUserInstall()
                ? "Esta copia de Medrar está instalada solo para tu usuario (versiones 1.2.x). La versión nueva se instala para todo el equipo " +
                  "y pide permisos de administrador, así que no se puede poner encima.\n\n" +
                  "Desinstala la actual (Configuración de Windows, Aplicaciones, Medrar) y ejecuta el instalador nuevo; se abrirá su página ahora."
                : "Esta copia de Medrar no se instaló con el instalador (.msi), así que no puede actualizarse sola.\n\n" +
                  "Se abrirá la página de la versión para que descargues el instalador nuevo.");
            if (info.PageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), "MedrarUpdate", info.Version.ToString());
        string? msi = null;
        using var svc = NewService(s);
        var err = ProgressForm.Run(owner, $"Descargando Medrar {info.Version}", rep =>
        {
            rep.FilesTotal = 1;
            msi = svc.DownloadAsync(info, dir, rep, rep.Token).GetAwaiter().GetResult();
        }, out bool cancelled);
        if (cancelled) return;
        if (err != null) { UI.Error(owner, "No se pudo descargar la actualización.\n\n" + UI.Describe(err)); return; }

        try { UpdateInstaller.LaunchApply(msi!, Environment.ProcessPath ?? Application.ExecutablePath, Environment.ProcessId); }
        catch (Exception ex) { UI.Error(owner, "No se pudo iniciar la instalación.\n\n" + UI.Describe(ex)); return; }
        owner.Close();   // el script espera a que se cierre, instala y vuelve a abrir Medrar
    }
}

internal sealed class UpdateDialog : DialogBase
{
    public UpdateDialog(UpdateInfo info, Version current) : base("Actualización de Medrar")
    {
        ClientSize = new Size(520, 360);
        var notes = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.FixedSingle,
            Text = string.IsNullOrWhiteSpace(info.Notes) ? "(Esta versión no incluye notas.)" : info.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n")
        };
        var head = UI.Column(
            UI.Label($"Hay una versión nueva de Medrar: {info.Version}", bold: true),
            UI.Label($"Tienes la {current}. Al actualizar, Medrar se cierra, se instala la nueva y se vuelve a abrir.", muted: true),
            UI.Label("Novedades:"));
        head.Dock = DockStyle.Top;
        head.Padding = new Padding(12, 8, 12, 0);
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 4, 12, 4) };
        body.Controls.Add(notes);
        Controls.Add(body);
        Controls.Add(head);
        var footer = Footer("Actualizar", "Más tarde");
        var skip = UI.Button("Omitir esta versión", null, 150, DialogResult.Ignore);
        footer.Controls.Add(skip);
        Controls.Add(footer);
    }
}
