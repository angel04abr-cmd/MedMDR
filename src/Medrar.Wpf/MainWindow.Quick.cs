using System.Diagnostics;
using System.Windows;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Órdenes que llegan desde el menú del botón derecho de Windows o la línea de comandos (sin abrir la ventana principal).</summary>
public partial class MainWindow
{
    /// <summary>Ejecuta «--add», «--extract-here», «--extract-to» o «--test». Devuelve el código de salida (0 = bien).</summary>
    internal int RunQuick(string mode, List<string> paths)
    {
        try { return mode == "add" ? QuickAdd(paths) : QuickArchive(mode, paths.FirstOrDefault()); }
        finally { SaveSettings(); }
    }

    private int QuickAdd(List<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToList();
        if (existing.Count == 0) return 1;
        var req = AddWindow.Ask(this, existing, Path.GetDirectoryName(existing[0].TrimEnd('\\')) ?? Environment.CurrentDirectory, _settings);
        if (req == null) return 0;
        return CreateArchive(req, existing) != null ? 0 : 3;
    }

    private int QuickArchive(string mode, string? path)
    {
        if (path == null || !File.Exists(path)) { Msg.Warn(this, "No se encuentra el archivo indicado."); return 1; }
        path = Path.GetFullPath(path);
        using var s = ArchiveSession.Open(this, path);
        if (s == null) return 1;
        if (mode == "test") return TestSession(s) ? 0 : 3;

        string name = Path.GetFileNameWithoutExtension(path);
        string dest = Path.GetDirectoryName(path)!;
        var o = new ExtractOptions { DestinationDir = dest, Overwrite = _settings.ExtractOverwrite, KeepBrokenFiles = _settings.KeepBrokenFiles, RestoreTimes = true };
        if (mode == "extract-to")
        {
            var req = ExtractWindow.Ask(this, path, Path.Combine(dest, name), _settings, false);
            if (req == null) return 0;
            o = req.Options;
            o.RestoreTimes = true;
        }
        var res = ExtractWith(s, s.Archive.Entries.ToList(), o);
        if (res == null) return 3;
        if (res.Ok && Directory.Exists(o.DestinationDir)) OpenInExplorer(res.Files.FirstOrDefault() ?? o.DestinationDir);
        return res.Ok ? 0 : 3;
    }

    /// <summary>Abre el Explorador de Windows en esa carpeta o con ese fichero seleccionado.</summary>
    private static void OpenInExplorer(string path)
    {
        try
        {
            var psi = File.Exists(path) ? new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") : new ProcessStartInfo("explorer.exe", $"\"{path}\"");
            psi.UseShellExecute = true;
            Process.Start(psi);
        }
        catch { /* sin Explorador */ }
    }
}
