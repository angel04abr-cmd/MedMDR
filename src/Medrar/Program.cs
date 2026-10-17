using Medrar.Core;

namespace Medrar.App;

internal static class Program
{
    public static AppSettings Settings { get; private set; } = new();

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--make-icon")
        {
            Icons.WriteIco(args[1]);
            return 0;
        }

        Settings = SettingsStore.Load();
        var launch = Launch.Parse(args);
        if (launch.ThemeOverride != null) Settings.Theme = launch.ThemeOverride.Value;   // solo para pruebas; no se guarda
        ApplicationConfiguration.Initialize();
        Theme.Apply(Settings.Theme);

        try
        {
            return launch.Run();
        }
        catch (Exception ex)
        {
            try
            {
                string log = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "error.log");
                Directory.CreateDirectory(Path.GetDirectoryName(log)!);
                File.AppendAllText(log, $"[{DateTime.Now:s}] {ex}{Environment.NewLine}{Environment.NewLine}");
            }
            catch { }
            MessageBox.Show("Medrar ha encontrado un error inesperado:\n\n" + ex.Message, "Medrar", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }

    public static void SaveSettings()
    {
        try { SettingsStore.Save(Settings); } catch { /* sin permisos: no es crítico */ }
    }

    public static void ReplaceSettings(AppSettings s) { Settings = s; SaveSettings(); }
}

/// <summary>Interpreta la línea de comandos (la usan el menú de Windows, la asociación de archivos y las pruebas).</summary>
internal sealed class Launch
{
    public string? OpenPath;
    public string Mode = "main";               // main | add | extract-here | extract-to | test | snapshot
    public List<string> Paths = new();
    public string? SnapshotFile;
    public string? SnapshotDialog;
    public string? Password;
    public ThemeMode? ThemeOverride;

    public static Launch Parse(string[] args)
    {
        var l = new Launch();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--add": l.Mode = "add"; break;
                case "--extract-here": l.Mode = "extract-here"; break;
                case "--extract-to": l.Mode = "extract-to"; break;
                case "--test": l.Mode = "test"; break;
                case "--snapshot": l.Mode = "snapshot"; l.SnapshotFile = args[++i]; break;
                case "--dialog": l.SnapshotDialog = args[++i]; break;
                case "--password": l.Password = args[++i]; break;
                case "--theme": l.ThemeOverride = args[++i] == "dark" ? ThemeMode.Dark : ThemeMode.Light; break;
                default: l.Paths.Add(args[i]); break;
            }
        }
        if (l.Mode == "main" && l.Paths.Count > 0) l.OpenPath = l.Paths[0];
        return l;
    }

    public int Run()
    {
        switch (Mode)
        {
            case "add":
                return QuickActions.Add(Paths);
            case "extract-here":
            case "extract-to":
            case "test":
                return QuickActions.OnArchive(Mode, Paths.FirstOrDefault(), Password);
            case "snapshot":
                return SnapshotTool.Run(this);
            default:
                Application.Run(new MainForm(OpenPath));
                return 0;
        }
    }
}
