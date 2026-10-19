using System.IO;
using System.Windows;

namespace MedMDR.Wpf;

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

        if (Args.Mode != "main")        // «Añadir», «Extraer aquí», «Extraer en…» o «Comprobar» desde el menú de Windows: sin ventana principal
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            int code = new MainWindow(null, quick: true).RunQuick(Args.Mode, Args.Paths);
            Shutdown(code);
            return;
        }

        Window window = Args.Dialog != null ? DevTools.CreateDialog(Args.Dialog) : new MainWindow(Args.Path);
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
    public string? Dialog;
    public string? Auto;                    // solo pruebas: órdenes separadas por comas que se ejecutan solas (select:N,open,test,info,view,extract-here…)
    public string? ShotsDir;                // solo pruebas: carpeta donde se guarda una captura de cada diálogo que se abra
    public string? Password;
    public bool AutoYes;                    // solo pruebas: responde «Sí/Aceptar» a las preguntas sin mostrarlas
    public string? AutoText;                // solo pruebas: texto con el que se responden los cuadros de texto
    public bool Accept;                     // solo pruebas: tras la captura, cada diálogo se acepta con sus valores (en vez de cancelarse)
    public int SelectRow = -1;
    public string Mode = "main";           // main | add | extract-here | extract-to | test (órdenes del menú de Windows)
    public List<string> Paths = new();

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
                case "--auto": l.Auto = args[++i]; break;
                case "--shots-dir": l.ShotsDir = args[++i]; break;
                case "--yes": l.AutoYes = true; break;
                case "--add": l.Mode = "add"; break;
                case "--extract-here": l.Mode = "extract-here"; break;
                case "--extract-to": l.Mode = "extract-to"; break;
                case "--test": l.Mode = "test"; break;
                case "--accept": l.Accept = true; break;
                case "--text": l.AutoText = args[++i]; break;
                case "--password": l.Password = args[++i]; break;
                case "--dialog": l.Dialog = args[++i]; break;
                case "--dump-keys": l.DumpKeys = args[++i]; break;
                case "--select": l.SelectRow = int.Parse(args[++i]); break;
                default:
                    l.Paths.Add(args[i]);
                    if (l.Path == null && (File.Exists(args[i]) || Directory.Exists(args[i]))) l.Path = args[i];
                    break;
            }
        }
        return l;
    }
}
