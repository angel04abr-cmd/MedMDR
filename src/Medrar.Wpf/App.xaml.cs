using System.IO;
using System.Windows;

namespace Medrar.Wpf;

public partial class App : Application
{
    public static LaunchArgs Args { get; private set; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ev) => ErrorLog.Write(ev.Exception);
        Args = LaunchArgs.Parse(e.Args);
        if (Args.Theme != null) ThemeMode = Args.Theme switch { "light" => ThemeMode.Light, "dark" => ThemeMode.Dark, _ => ThemeMode.System };

        if (Args.DumpKeys != null)      // herramienta de desarrollo: lista los recursos del tema Fluent
        {
            DevTools.DumpFluentKeys(Args.DumpKeys);
            Shutdown();
            return;
        }

        var window = new MainWindow(Args.Path);
        window.Show();
        if (Args.Snapshot != null) DevTools.SnapshotLater(window, Args);
    }
}

/// <summary>Opciones de la línea de comandos: carpeta o archivo a abrir y opciones solo para pruebas.</summary>
public sealed class LaunchArgs
{
    public string? Path;
    public string? Theme;
    public string? ThenTheme;               // solo pruebas: cambia de tema con la ventana abierta, antes de la captura
    public string? Snapshot;
    public string? DumpKeys;
    public int SelectRow = -1;

    public static LaunchArgs Parse(string[] args)
    {
        var l = new LaunchArgs();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--theme": l.Theme = args[++i]; break;
                case "--then-theme": l.ThenTheme = args[++i]; break;
                case "--snapshot": l.Snapshot = args[++i]; break;
                case "--dump-keys": l.DumpKeys = args[++i]; break;
                case "--select": l.SelectRow = int.Parse(args[++i]); break;
                default: if (File.Exists(args[i]) || Directory.Exists(args[i])) l.Path = args[i]; break;
            }
        }
        return l;
    }
}
