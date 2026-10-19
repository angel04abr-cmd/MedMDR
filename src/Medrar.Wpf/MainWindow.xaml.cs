using System.Collections.ObjectModel;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Medrar.Core;
using CoreTheme = Medrar.Core.ThemeMode;

namespace Medrar.Wpf;

/// <summary>
/// Ventana principal del diseño Fluent (Fase 1: en construcción). Ya muestra y recorre carpetas del disco con el aspecto nuevo;
/// abrir y editar archivos comprimidos y el resto de órdenes se conectan en las fases siguientes.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly ObservableCollection<FileItem> _items = new();
    private readonly ListCollectionView _view;
    private string _folder = "";
    private int _sortColumn;
    private bool _sortAscending = true;
    private static readonly string[] ColumnTitles = { "Nombre", "Tamaño", "Tipo", "Modificado" };

    public MainWindow(string? path)
    {
        InitializeComponent();
        _view = new ListCollectionView(_items);
        FileList.ItemsSource = _view;
        ApplySort();

        if (App.Args.Theme == null) ApplyTheme(_settings.Theme, save: false); else MarkTheme(App.Args.Theme switch { "light" => CoreTheme.Light, "dark" => CoreTheme.Dark, _ => CoreTheme.System });

        string start = path != null && Directory.Exists(path) ? path
            : path != null && File.Exists(path) ? Path.GetDirectoryName(Path.GetFullPath(path))!
            : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        LoadFolder(start);
        UpdateCommands();
        StatusRight.Text = $"Medrar {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)} · interfaz Fluent en construcción";
    }

    // ------------------------------------------------------------------ carpetas

    private void LoadFolder(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var list = new List<FileItem>();
            if (di.Parent != null) list.Add(FileItem.Up(di.Parent.FullName));
            foreach (var fi in di.EnumerateFileSystemInfos())
            {
                try { if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0) list.Add(FileItem.FromDisk(fi)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _items.Clear();
            foreach (var it in list) _items.Add(it);
            _folder = di.FullName;
            AddressBox.Text = _folder;
            Title = $"{(di.Name.Length > 0 ? di.Name : _folder)} — Medrar";
            UpButton.IsEnabled = di.Parent != null;
            UpdateStatus();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusRight.Text = "No se puede abrir esa carpeta: " + ex.Message;
        }
    }

    private void Open(FileItem item)
    {
        if (item.IsDirectory) LoadFolder(item.FullPath);
        else if (item.IsArchive) Say("Abrir archivos comprimidos se conecta en la Fase 2.");
        else Say("Ver ficheros se conecta en la Fase 2.");
    }

    private void GoUp() { var parent = Directory.GetParent(_folder); if (parent != null) LoadFolder(parent.FullName); }

    private void Say(string text) => StatusRight.Text = text;

    // ------------------------------------------------------------------ eventos

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileList.SelectedItem is FileItem it && e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(FileList, d) != null) Open(it);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (AddressBox.IsKeyboardFocused) return;
        if (e.Key == Key.Back) { GoUp(); e.Handled = true; }
        else if (e.Key == Key.Enter && FileList.SelectedItem is FileItem it) { Open(it); e.Handled = true; }
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string p = AddressBox.Text.Trim().Trim('"');
        if (Directory.Exists(p)) { LoadFolder(p); FileList.Focus(); }
        else if (File.Exists(p)) Say("Abrir archivos comprimidos se conecta en la Fase 2.");
        else Say("No existe esa ruta.");
        e.Handled = true;
    }

    private void Up_Click(object sender, RoutedEventArgs e) => GoUp();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _folder };
        if (dlg.ShowDialog(this) == true) LoadFolder(dlg.FolderName);
    }

    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void Command_Click(object sender, RoutedEventArgs e)
    {
        if (sender is CommandButton b) Say($"«{b.Label}» se conecta en la Fase 2 (esta ventana está en construcción).");
    }

    private void About_Click(object sender, RoutedEventArgs e) =>
        MessageBox.Show(this, $"Medrar {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3)}\n\nInterfaz Fluent en construcción (Fase 1). La aplicación completa sigue siendo Medrar.exe.",
            "Acerca de Medrar", MessageBoxButton.OK, MessageBoxImage.Information);

    // ------------------------------------------------------------------ orden

    private void Header_Click(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not GridViewColumnHeader { Column: { } col }) return;
        int idx = Columns.Columns.IndexOf(col);
        if (idx < 0) return;
        if (_sortColumn == idx) _sortAscending = !_sortAscending; else { _sortColumn = idx; _sortAscending = true; }
        ApplySort();
    }

    private void ApplySort()
    {
        _view.CustomSort = new FileItemComparer(_sortColumn, _sortAscending);
        for (int i = 0; i < Columns.Columns.Count; i++)
            Columns.Columns[i].Header = i == _sortColumn ? ColumnTitles[i] + (_sortAscending ? "  ↑" : "  ↓") : ColumnTitles[i];
    }

    // ------------------------------------------------------------------ selección y estado

    private void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e) { UpdateStatus(); UpdateCommands(); }

    private IEnumerable<FileItem> Selected() => FileList.SelectedItems.Cast<FileItem>().Where(i => !i.IsUp);

    private void UpdateCommands()
    {
        var sel = Selected().ToList();
        bool archive = sel.Any(i => i.IsArchive);
        CmdExtract.IsEnabled = archive;
        CmdTest.IsEnabled = archive;
        CmdInfo.IsEnabled = archive;
        CmdView.IsEnabled = sel.Any(i => !i.IsDirectory);
        CmdDelete.IsEnabled = sel.Count > 0;
    }

    private void UpdateStatus()
    {
        var sel = Selected().ToList();
        int dirs = _items.Count(i => i.IsDirectory && !i.IsUp), files = _items.Count(i => !i.IsDirectory);
        StatusLeft.Text = sel.Count == 0
            ? $"{files} fichero(s) y {dirs} carpeta(s)"
            : $"Seleccionados: {ArchiveReport.FormatSize(sel.Where(i => !i.IsDirectory).Sum(i => i.Size))} en {sel.Count(i => !i.IsDirectory)} fichero(s), {sel.Count(i => i.IsDirectory)} carpeta(s)";
    }

    private void FileList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // La última columna ocupa el ancho que sobra (descontando la barra de desplazamiento).
        double others = 0;
        for (int i = 0; i < Columns.Columns.Count - 1; i++) others += Columns.Columns[i].ActualWidth;
        Columns.Columns[^1].Width = Math.Max(150, FileList.ActualWidth - others - 34);
    }

    // ------------------------------------------------------------------ tema

    private void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem { Tag: string tag } && Enum.TryParse<CoreTheme>(tag, out var t)) ApplyTheme(t, save: true);
    }

    private void ApplyTheme(CoreTheme t, bool save)
    {
        Application.Current.ThemeMode = t switch { CoreTheme.Light => System.Windows.ThemeMode.Light, CoreTheme.Dark => System.Windows.ThemeMode.Dark, _ => System.Windows.ThemeMode.System };
        MarkTheme(t);
        if (!save) return;
        _settings.Theme = t;
        try { SettingsStore.Save(_settings); } catch { /* sin permisos: no es crítico */ }
    }

    private void MarkTheme(CoreTheme t)
    {
        ThemeSystem.IsChecked = t == CoreTheme.System;
        ThemeLight.IsChecked = t == CoreTheme.Light;
        ThemeDark.IsChecked = t == CoreTheme.Dark;
    }

    internal void SelectRowForSnapshot(int index)
    {
        if (index < 0 || index >= _view.Count) return;
        FileList.SelectedIndex = index;
        FileList.ScrollIntoView(FileList.SelectedItem);
    }
}
