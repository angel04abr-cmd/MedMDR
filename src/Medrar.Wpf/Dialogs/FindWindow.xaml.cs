using System.Windows;
using System.Windows.Input;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Buscar texto (o bytes) en una carpeta, en un archivo abierto y en los archivos comprimidos que contenga.</summary>
public partial class FindWindow : Window
{
    private sealed record Row(string Item, string Detail, string Target);

    private readonly string _folder;
    private readonly IArchive? _archive;
    private readonly string? _password;
    private CancellationTokenSource? _cts;

    /// <summary>Ruta (o ruta interna del archivo) a la que ir, si el usuario eligió un resultado.</summary>
    public string? Navigate { get; private set; }

    public FindWindow(string folder, IArchive? archive, string? password)
    {
        InitializeComponent();
        DevTools.Hook(this);
        _folder = folder; _archive = archive; _password = password;
        Title = archive == null ? "Buscar ficheros" : "Buscar en el archivo";
        Where.Text = archive == null ? $"Carpeta: {folder}" : $"Archivo: {archive.Path}";
        FolderOptions.Visibility = archive == null ? Visibility.Visible : Visibility.Collapsed;
        Loaded += (_, _) =>
        {
            TextBox.Focus();
            if (App.Args.AutoText != null) { TextBox.Text = App.Args.AutoText; Go_Click(this, new RoutedEventArgs()); }   // solo pruebas
        };
        Closing += (_, _) => _cts?.Cancel();
    }

    private async void Go_Click(object sender, RoutedEventArgs e)
    {
        if (TextBox.Text.Length == 0) { Msg.Warn(this, "Escribe el texto que quieres buscar."); return; }
        Results.Items.Clear();
        GoButton.IsEnabled = false; StopButton.IsEnabled = true;
        StatusText.Text = "Buscando…";
        _cts = new CancellationTokenSource();
        var q = new SearchQuery
        {
            Text = TextBox.Text, CaseSensitive = CaseCheck.IsChecked == true, Hex = HexCheck.IsChecked == true, Mask = MaskBox.Text == "*" ? "" : MaskBox.Text,
            IncludeSubfolders = SubCheck.IsChecked == true, InsideArchives = InsideCheck.IsChecked == true
        };
        var progress = new ProgressReporter(new Progress<ProgressInfo>(p => StatusText.Text = "Buscando en " + Path.GetFileName(p.CurrentName)), _cts.Token);
        try
        {
            var hits = await Task.Run(() => _archive != null
                ? Finder.SearchArchive(_archive, q, progress)
                : Finder.SearchFolder(_folder, q, progress, _password));
            foreach (var h in hits)
                Results.Items.Add(new Row(h.Item, h.Detail, h.Container == _folder || _archive != null ? h.Item : h.Container));
            StatusText.Text = hits.Count == 0 ? "Sin coincidencias." : $"{hits.Count} coincidencia(s).";
        }
        catch (OperationCanceledException) { StatusText.Text = "Búsqueda detenida."; }
        catch (MedrarException ex) { StatusText.Text = ex.Message; }
        catch (Exception ex) { StatusText.Text = Msg.Describe(ex); }
        finally { GoButton.IsEnabled = true; StopButton.IsEnabled = false; }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => _cts?.Cancel();

    private void Results_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Results.SelectedItem is not Row r) return;
        Navigate = r.Target;
        DialogResult = true;
    }

    /// <summary>Devuelve la ruta elegida, o null.</summary>
    public static string? Ask(Window? owner, string folder, IArchive? archive, string? password)
    {
        var w = new FindWindow(folder, archive, password);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.Navigate : null;
    }
}
