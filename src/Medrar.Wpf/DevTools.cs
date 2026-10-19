using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Medrar.Wpf;

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

    /// <summary>Espera a que la ventana se pinte, guarda una captura (con barra de título) y cierra la aplicación.</summary>
    public static void SnapshotLater(Window window, LaunchArgs args)
    {
        void Finish()
        {
            try
            {
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
