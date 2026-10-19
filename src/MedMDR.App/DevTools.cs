using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace MedMDR.Wpf;

/// <summary>Herramientas de desarrollo y pruebas (no forman parte de la experiencia normal): capturas de la ventana y volcado del tema.</summary>
internal static class DevTools
{
    [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT r);
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }

    /// <summary>Escribe la lista de claves de color y pincel que define el tema Fluent integrado.</summary>
    public static void DumpFluentKeys(string file)
    {
        var sb = new StringBuilder();
        void Walk(ResourceDictionary d, string prefix)
        {
            foreach (var k in d.Keys) sb.AppendLine($"{prefix}{k}\t{d[k]?.GetType().Name}");
            foreach (var m in d.MergedDictionaries) Walk(m, prefix);
        }
        foreach (var name in new[] { "Fluent", "Fluent.Light", "Fluent.Dark" })
        {
            try
            {
                var dict = new ResourceDictionary { Source = new Uri($"pack://application:,,,/PresentationFramework.Fluent;component/Themes/{name}.xaml") };
                Walk(dict, name + ":");
            }
            catch (Exception ex) { sb.AppendLine($"{name}: ERROR {ex.GetType().Name}: {ex.Message}"); }
        }
        File.WriteAllText(file, sb.ToString());
    }

    private static int _shotCounter;

    /// <summary>Con --shots-dir, cada diálogo que se abre se fotografía a los 1,3 s y se cierra (devuelve «cancelar», o «aceptar» con --accept).</summary>
    public static void Hook(Window dialog, Action? accept = null)
    {
        string? dir = App.Args.ShotsDir;
        if (dir == null) return;
        dialog.Loaded += (_, _) =>
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            t.Tick += (_, _) =>
            {
                t.Stop();
                try
                {
                    Directory.CreateDirectory(dir);
                    Capture(dialog);
                }
                catch (Exception ex) { ErrorLog.Write(ex); }
                if (App.Args.Accept && accept != null)
                {
                    try { accept(); } catch (Exception ex) { ErrorLog.Write(ex); }
                    if (!dialog.IsVisible) return;
                }
                dialog.Close();
            };
            t.Start();
        };
    }

    /// <summary>Solo pruebas: guarda una captura del diálogo en la carpeta de --shots-dir (con una etiqueta opcional en el nombre).</summary>
    public static void Capture(Window dialog, string tag = "")
    {
        string? dir = App.Args.ShotsDir;
        if (dir == null) return;
        Directory.CreateDirectory(dir);
        dialog.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        string name = new string(dialog.Title.Where(char.IsLetterOrDigit).ToArray());
        Save(dialog, Path.Combine(dir, $"{++_shotCounter:00}-{dialog.GetType().Name}-{name}{tag}.png"));
    }

    /// <summary>Crea un diálogo suelto (con datos de ejemplo) para poder capturarlo con --dialog.</summary>
    public static Window CreateDialog(string name) => name switch
    {
        "about" => new AboutWindow(),
        "update" => new UpdateWindow(new MedMDR.Core.UpdateInfo(new Version(9, 9, 9), "v9.9.9", "• Novedad de ejemplo uno\n• Novedad de ejemplo dos\n• Correcciones varias", "https://example.invalid/", "x.msi", "https://example.invalid/x.msi", 1000, null), new Version(1, 5, 0)),
        "add" => new AddWindow(new[] { Environment.CurrentDirectory }, Environment.CurrentDirectory, new MedMDR.Core.AppSettings()),
        "settings" => new SettingsWindow(new MedMDR.Core.AppSettings()),
        "wizard" => new WizardWindow(Environment.CurrentDirectory, new MedMDR.Core.AppSettings()),
        "convert" => new ConvertWindow(Array.Empty<string>(), Environment.CurrentDirectory),
        "find" => new FindWindow(Environment.CurrentDirectory, null, null),
        "password" => new NewPasswordWindow(true, false),
        "benchmark" => new BenchmarkWindow(),
        _ => throw new ArgumentException("Diálogo desconocido: " + name)
    };

    /// <summary>Espera a que la ventana se pinte, guarda una captura (con barra de título) y cierra la aplicación.</summary>
    public static void SnapshotLater(Window window, LaunchArgs args)
    {
        void Finish()
        {
            try
            {
                if (args.Auto != null && window is MainWindow auto) auto.RunAuto(args.Auto);
                if (args.SelectRow >= 0 && window is MainWindow mw) mw.SelectRowForSnapshot(args.SelectRow);
                window.Dispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
                Save(window, args.Snapshot!);
            }
            finally { Application.Current.Shutdown(); }
        }

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2200) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (args.ThenTheme == null) { Finish(); return; }
            // Cambio de tema en caliente: en esta interfaz todo se repinta solo, sin reiniciar la ventana.
            Application.Current.ThemeMode = args.ThenTheme switch { "light" => ThemeMode.Light, "dark" => ThemeMode.Dark, _ => ThemeMode.System };
            var second = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            second.Tick += (_, _) => { second.Stop(); Finish(); };
            second.Start();
        };
        timer.Start();
    }

    private static void Save(Window window, string file)
    {
        IntPtr h = new WindowInteropHelper(window).Handle;
        GetWindowRect(h, out var r);
        int w = r.Right - r.Left, ht = r.Bottom - r.Top;
        using var bmp = new System.Drawing.Bitmap(w, ht);
        using (var g = System.Drawing.Graphics.FromImage(bmp))
        {
            IntPtr dc = g.GetHdc();
            PrintWindow(h, dc, 2);
            g.ReleaseHdc(dc);
        }
        bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
    }
}
