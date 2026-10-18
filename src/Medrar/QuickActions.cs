using System.Runtime.InteropServices;
using Medrar.Core;

namespace Medrar.App;

/// <summary>Ventana invisible que hace de «propietaria» de los diálogos cuando no se abre la ventana principal.</summary>
internal sealed class HiddenOwner : Form
{
    public HiddenOwner()
    {
        ShowInTaskbar = false; FormBorderStyle = FormBorderStyle.None; Opacity = 0; Size = new Size(1, 1);
        StartPosition = FormStartPosition.CenterScreen; Icon = AppIcons.App;
    }
}

/// <summary>Órdenes que llegan desde el menú del botón derecho de Windows o la línea de comandos.</summary>
internal static class QuickActions
{
    public static int Add(List<string> paths)
    {
        var existing = paths.Where(p => File.Exists(p) || Directory.Exists(p)).Select(Path.GetFullPath).ToList();
        if (existing.Count == 0) return 1;
        using var owner = new HiddenOwner();
        owner.Show();
        using var d = new AddDialog(existing, Path.GetDirectoryName(existing[0].TrimEnd('\\')) ?? Environment.CurrentDirectory, Program.Settings);
        if (d.ShowDialog(owner) != DialogResult.OK) return 0;
        return Ops.Create(owner, d.Result, existing) != null ? 0 : 3;
    }

    public static int OnArchive(string mode, string? path, string? password)
    {
        if (path == null || !File.Exists(path)) { MessageBox.Show("No se encuentra el archivo indicado.", "Medrar", MessageBoxButtons.OK, MessageBoxIcon.Warning); return 1; }
        path = Path.GetFullPath(path);
        using var owner = new HiddenOwner();
        owner.Show();
        var s = ArchiveSession.Open(owner, path);
        if (s == null) return 1;
        using (s)
        {
            if (mode == "test") return Ops.Test(owner, s) ? 0 : 3;
            string name = Path.GetFileNameWithoutExtension(path);
            string dest = Path.GetDirectoryName(path)!;
            var o = new ExtractOptions { DestinationDir = dest, Overwrite = Program.Settings.ExtractOverwrite, KeepBrokenFiles = Program.Settings.KeepBrokenFiles };
            if (mode == "extract-to")
            {
                using var d = new ExtractDialog(path, Path.Combine(dest, name), Program.Settings, false);
                if (d.ShowDialog(owner) != DialogResult.OK) return 0;
                o = d.Result.Options;
            }
            var res = Ops.Extract(owner, s, s.Archive.Entries.ToList(), o);
            if (res == null) return 3;
            Program.SaveSettings();
            if (res.Ok && Directory.Exists(o.DestinationDir)) Ops.OpenFolderOf(res.Files.FirstOrDefault() ?? o.DestinationDir);
            return res.Ok ? 0 : 3;
        }
    }
}

/// <summary>Herramienta interna para las pruebas: abre una ventana o diálogo y guarda una captura (--snapshot).</summary>
internal static class SnapshotTool
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int L, T, R, B; }

    private static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(15); }
    }

    private static void Save(Form f, string file)
    {
        GetWindowRect(f.Handle, out var r);
        int w = r.R - r.L, h = r.B - r.T;
        using var bmp = new Bitmap(w, h);
        using (var g = Graphics.FromImage(bmp))
        {
            IntPtr dc = g.GetHdc();
            PrintWindow(f.Handle, dc, 2);
            g.ReleaseHdc(dc);
        }
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }

    public static int Run(Launch l)
    {
        Form form;
        string folder = l.Paths.FirstOrDefault(Directory.Exists) ?? Environment.CurrentDirectory;
        string? file = l.Paths.FirstOrDefault(File.Exists);
        var s = Program.Settings;
        switch (l.SnapshotDialog)
        {
            case "add": form = new AddDialog(new[] { file ?? folder }, folder, s); break;
            case "extract": form = new ExtractDialog(file ?? "archivo.mdr", folder, s); break;
            case "settings": form = new SettingsDialog(s); break;
            case "wizard": form = new WizardForm(folder, s); break;
            case "convert": form = new ConvertDialog(file != null ? new[] { file } : Array.Empty<string>(), folder, s); break;
            case "find": form = new FindDialog(folder, null, null); break;
            case "password": form = new PasswordDialog("Establecer contraseña", "Escribe la contraseña del archivo:", true); break;
            case "about": form = new AboutDialog(); break;
            case "favorites": form = new FavoritesDialog(s); break;
            case "benchmark": form = new BenchmarkDialog(); break;
            case "viewer": form = new ViewerForm(Path.GetFileName(file ?? "demo.txt"), file != null ? File.ReadAllBytes(file) : System.Text.Encoding.UTF8.GetBytes("Hola"), s); break;
            default: form = new MainForm(file ?? folder); break;
        }
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(40, 40);
        form.Show();
        Pump(l.SnapshotDialog == "benchmark" ? 9000 : 1800);
        if (l.ThenTheme != null && form is MainForm main) { main.ChangeTheme(l.ThenTheme.Value); Pump(1200); }
        Save(form, l.SnapshotFile!);
        form.Close();
        return 0;
    }
}
