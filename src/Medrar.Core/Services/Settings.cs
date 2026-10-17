using System.Text.Json;

namespace Medrar.Core;

public sealed class Favorite
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
}

public sealed class CompressionProfile
{
    public string Name { get; set; } = "";
    public ArchiveOptions Options { get; set; } = new();
}

public enum ThemeMode { System, Light, Dark }

public sealed class AppSettings
{
    public ThemeMode Theme { get; set; } = ThemeMode.System;
    public bool KeepHistory { get; set; } = true;
    public bool ShowButtonText { get; set; } = true;
    public bool LargeIcons { get; set; } = true;
    public bool ShowFullPathInTitle { get; set; }
    public bool PlaySound { get; set; }
    public bool ShowArchiveComment { get; set; } = true;
    public bool ReuseWindow { get; set; }
    public bool AlwaysOnTop { get; set; }
    public bool SingleClickOpen { get; set; }
    public bool ShowTaskbarProgress { get; set; } = true;
    public bool FlatFolderView { get; set; }
    public bool DetailsView { get; set; } = true;

    public string DefaultArchiveFolder { get; set; } = "";
    public string DefaultExtractFolder { get; set; } = "";
    public string TempFolder { get; set; } = "";
    public string StartupFolder { get; set; } = "";

    public ArchiveFormat DefaultFormat { get; set; } = ArchiveFormat.Mdr;
    public int DefaultLevel { get; set; } = 3;
    public bool DefaultSolid { get; set; }
    public int DefaultRecoveryPercent { get; set; }
    public bool DefaultTestAfter { get; set; }

    public OverwriteMode ExtractOverwrite { get; set; } = OverwriteMode.Ask;
    public bool KeepBrokenFiles { get; set; }

    public string ViewerFontName { get; set; } = "Consolas";
    public float ViewerFontSize { get; set; } = 10f;
    public bool ViewerWordWrap { get; set; }
    public int ViewerMaxKb { get; set; } = 4096;

    public bool AskBeforeDelete { get; set; } = true;
    public bool ContextMenuCascaded { get; set; } = true;

    public List<Favorite> Favorites { get; set; } = new();
    public List<string> History { get; set; } = new();
    public List<string> RecentArchives { get; set; } = new();
    public List<CompressionProfile> Profiles { get; set; } = new();
    public List<string> SavedPasswordsNote { get; set; } = new(); // reservado; Medrar no guarda contraseñas

    public int WindowWidth { get; set; } = 1000;
    public int WindowHeight { get; set; } = 640;
    public int[] ColumnWidths { get; set; } = { 320, 90, 90, 130, 150, 80 };
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Opt = new() { WriteIndented = true };

    public static string DefaultPath =>
        System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Medrar", "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Opt) ?? new AppSettings();
        }
        catch { /* configuración dañada: se parte de cero */ }
        return new AppSettings();
    }

    public static void Save(AppSettings s, string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(s, Opt));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Exporta la configuración a un fichero (para copiarla a otro equipo).</summary>
    public static void Export(AppSettings s, string path) => Save(s, path);

    public static AppSettings Import(string path)
    {
        var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Opt)
                ?? throw new MedrarException("El fichero de configuración no es válido.");
        return s;
    }

    public static void AddRecent(AppSettings s, string path, int max = 12)
    {
        s.RecentArchives.RemoveAll(p => p.Equals(path, StringComparison.OrdinalIgnoreCase));
        s.RecentArchives.Insert(0, path);
        if (s.RecentArchives.Count > max) s.RecentArchives.RemoveRange(max, s.RecentArchives.Count - max);
    }

    public static void AddHistory(AppSettings s, string folder, int max = 30)
    {
        if (!s.KeepHistory) return;
        s.History.RemoveAll(p => p.Equals(folder, StringComparison.OrdinalIgnoreCase));
        s.History.Insert(0, folder);
        if (s.History.Count > max) s.History.RemoveRange(max, s.History.Count - max);
    }
}
