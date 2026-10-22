using System.Diagnostics;
using System.Reflection;
using System.Windows;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Comprobación y aplicación de actualizaciones desde GitHub Releases (ver <see cref="UpdateService"/>).</summary>
internal static class Updates
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(24);
    private static int _busy;

    public static Version CurrentVersion =>
        UpdateService.Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0));

    private static UpdateService NewService(AppSettings s) => UpdateService.FromEnvironment(TokenProtector.Unprotect(s.UpdateTokenProtected));

    private static void Save(AppSettings s) { try { SettingsStore.Save(s); } catch { /* sin permisos: no es crítico */ } }

    /// <summary>Al abrir: si toca (como mucho una vez cada 24 h) busca en segundo plano y avisa solo si hay algo nuevo.</summary>
    public static void CheckInBackground(Window owner, AppSettings s)
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
                Save(s);
                if (info != null && info.Version.ToString() != s.SkippedVersion)
                    _ = owner.Dispatcher.BeginInvoke(() => Offer(owner, s, info));
            }
            catch
            {
                // En silencio: sin red, sin token o GitHub caído no deben molestar al abrir el programa.
                s.LastUpdateCheckUtc = DateTime.UtcNow;
                Save(s);
            }
            finally { Interlocked.Exchange(ref _busy, 0); }
        });
    }

    /// <summary>Ayuda > Buscar actualizaciones: siempre da una respuesta (nueva versión, al día o el motivo del fallo).</summary>
    public static async Task CheckNowAsync(Window owner, AppSettings s)
    {
        UpdateInfo? info;
        var previous = owner.Cursor;
        owner.Cursor = System.Windows.Input.Cursors.Wait;
        try
        {
            using var svc = NewService(s);
            info = await svc.CheckAsync(CurrentVersion);
            s.LastUpdateCheckUtc = DateTime.UtcNow;
            Save(s);
        }
        catch (Exception ex) { owner.Cursor = previous; Msg.Warn(owner, "No se pudo buscar actualizaciones.\n\n" + Msg.Describe(ex)); return; }
        finally { owner.Cursor = previous; }

        if (info == null) Msg.Info(owner, $"{AppInfo.Name} está al día: tienes la versión {CurrentVersion}, que es la más reciente.");
        else Offer(owner, s, info, manual: true);
    }

    private static void Offer(Window owner, AppSettings s, UpdateInfo info, bool manual = false)
    {
        switch (UpdateWindow.Ask(owner, info, CurrentVersion))
        {
            case UpdateChoice.Install: Install(owner, s, info); break;
            case UpdateChoice.Skip when !manual: s.SkippedVersion = info.Version.ToString(); Save(s); break;
        }
    }

    private static void Install(Window owner, AppSettings s, UpdateInfo info)
    {
        if (!UpdateInstaller.IsMsiInstall())
        {
            Msg.Info(owner, $"Esta copia de {AppInfo.Name} no se instaló con el instalador (.msi), así que no puede actualizarse sola.\n\n" +
                  "Se abrirá la página de la versión para que descargues el instalador nuevo.");
            if (info.PageUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                Process.Start(new ProcessStartInfo(info.PageUrl) { UseShellExecute = true });
            return;
        }

        string dir = Path.Combine(Path.GetTempPath(), "MedMDRUpdate", info.Version.ToString());
        string? msi = null;
        using var svc = NewService(s);
        var err = ProgressWindow.Run(owner, $"Descargando {AppInfo.Name} {info.Version}", rep =>
        {
            rep.FilesTotal = 1;
            msi = svc.DownloadAsync(info, dir, rep, rep.Token).GetAwaiter().GetResult();
        }, out bool cancelled);
        if (cancelled) return;
        if (err != null) { Msg.Error(owner, "No se pudo descargar la actualización.\n\n" + Msg.Describe(err)); return; }

        try { UpdateInstaller.LaunchApply(msi!, Environment.ProcessPath ?? "", Environment.ProcessId); }
        catch (Exception ex) { Msg.Error(owner, "No se pudo iniciar la instalación.\n\n" + Msg.Describe(ex)); return; }
        owner.Close();   // el script espera a que se cierre, instala y vuelve a abrir el programa
    }
}
