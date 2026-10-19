using System.Windows;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Lo que el usuario eligió en «Carpeta y opciones de extracción».</summary>
public sealed class ExtractRequest
{
    public string Destination { get; set; } = "";
    public ExtractOptions Options { get; set; } = new();
    public bool ShowInExplorer { get; set; }
}

/// <summary>«Carpeta y opciones de extracción».</summary>
public partial class ExtractWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string _archiveName;
    private readonly string _defaultDest;

    public ExtractRequest Result { get; private set; } = new();

    public ExtractWindow(string archivePath, string defaultDest, AppSettings settings, bool showSubfolderOption)
    {
        InitializeComponent();
        DevTools.Hook(this, () => Ok_Click(this, new RoutedEventArgs()));
        _settings = settings;
        _defaultDest = defaultDest;
        _archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (_archiveName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) _archiveName = Path.GetFileNameWithoutExtension(_archiveName);

        foreach (var h in settings.History.Take(10)) Dest.Items.Add(h);
        Dest.Text = defaultDest;
        Sub.Visibility = showSubfolderOption ? Visibility.Visible : Visibility.Collapsed;
        OAsk.IsChecked = settings.ExtractOverwrite == OverwriteMode.Ask;
        OYes.IsChecked = settings.ExtractOverwrite == OverwriteMode.Overwrite;
        OSkip.IsChecked = settings.ExtractOverwrite == OverwriteMode.Skip;
        ORename.IsChecked = settings.ExtractOverwrite == OverwriteMode.Rename;
        Broken.IsChecked = settings.KeepBrokenFiles;
    }

    private OverwriteMode Overwrite => OYes.IsChecked == true ? OverwriteMode.Overwrite : OSkip.IsChecked == true ? OverwriteMode.Skip : ORename.IsChecked == true ? OverwriteMode.Rename : OverwriteMode.Ask;

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta de destino", InitialDirectory = Directory.Exists(Dest.Text) ? Dest.Text : "" };
        if (dlg.ShowDialog(this) == true) Dest.Text = dlg.FolderName;
    }

    private void NewFolder_Click(object sender, RoutedEventArgs e)
    {
        string? n = TextPromptWindow.Ask(this, "Nombre de la carpeta nueva:", "Nueva carpeta", _archiveName);
        if (!string.IsNullOrWhiteSpace(n)) Dest.Text = Path.Combine(Directory.Exists(Dest.Text) ? Dest.Text : _defaultDest, n);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.ExtractOverwrite = Overwrite;
        _settings.KeepBrokenFiles = Broken.IsChecked == true;
        try { SettingsStore.Save(_settings); } catch { /* sin permisos */ }
        Msg.Info(this, "Opciones guardadas como predeterminadas.");
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string dest = Dest.Text.Trim();
        if (dest.Length == 0) { Msg.Warn(this, "Indica la carpeta de destino."); return; }
        if (!Path.IsPathRooted(dest)) { Msg.Warn(this, "Escribe una ruta completa (por ejemplo C:\\Carpeta)."); return; }
        string typed = dest;
        if (Sub.Visibility == Visibility.Visible && Sub.IsChecked == true) dest = Path.Combine(dest, _archiveName);
        Result = new ExtractRequest
        {
            Destination = dest,
            ShowInExplorer = ShowExplorer.IsChecked == true,
            Options = new ExtractOptions
            {
                DestinationDir = dest,
                PathMode = PNone.IsChecked == true ? PathMode.None : PathMode.Full,
                Update = UUpdate.IsChecked == true ? ExtractUpdateMode.ExtractUpdate : UFresh.IsChecked == true ? ExtractUpdateMode.FreshenOnly : ExtractUpdateMode.ExtractReplace,
                Overwrite = Overwrite,
                KeepBrokenFiles = Broken.IsChecked == true,
            }
        };
        SettingsStore.AddHistory(_settings, typed);
        DialogResult = true;
    }

    /// <summary>Devuelve null si se cancela.</summary>
    public static ExtractRequest? Ask(Window? owner, string archivePath, string defaultDest, AppSettings settings, bool showSubfolderOption)
    {
        var w = new ExtractWindow(archivePath, defaultDest, settings, showSubfolderOption);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.Result : null;
    }
}
