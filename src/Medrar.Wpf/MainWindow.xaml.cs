using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Medrar.Core;
using CoreTheme = Medrar.Core.ThemeMode;

namespace Medrar.Wpf;

/// <summary>
/// Ventana principal del diseño Fluent: recorre carpetas y archivos comprimidos, con panel lateral (accesos, favoritos y recientes).
/// Las órdenes están en MainWindow.Commands.cs.
/// </summary>
public partial class MainWindow : Window
{
    private readonly AppSettings _settings = SettingsStore.Load();
    private readonly ObservableCollection<FileItem> _items = new();
    private readonly ListCollectionView _view;
    private readonly ObservableCollection<NavItem> _nav = new();
    private ArchiveSession? _session;
    private string _folder = "";
    private string _inside = "";                       // carpeta actual dentro del archivo ("" = raíz)
    private string? _sessionPassword;                  // contraseña de la sesión (solo en memoria)
    private readonly List<string> _tempFiles = new();
    private readonly List<string> _tempDirs = new();
    private int _sortColumn;
    private bool _sortAscending = true;
    private static readonly string[] ColumnTitles = { "Nombre", "Tamaño", "Comprimido", "Tipo", "Modificado", "CRC32" };

    private bool InArchive => _session != null;
    private IArchive Archive => _session!.Archive;

    public MainWindow(string? path)
    {
        InitializeComponent();
        _view = new ListCollectionView(_items);
        FileList.ItemsSource = _view;
        ApplySort();

        var navView = new ListCollectionView(_nav);
        navView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(NavItem.Section)));
        Sidebar.ItemsSource = navView;

        if (App.Args.Theme == null) ApplyTheme(_settings.Theme, save: false);
        else MarkTheme(App.Args.Theme switch { "light" => CoreTheme.Light, "dark" => CoreTheme.Dark, _ => CoreTheme.System });

        _sessionPassword = App.Args.Password;
        BuildSidebar();
        if (path != null && File.Exists(path)) { LoadFolder(Path.GetDirectoryName(Path.GetFullPath(path))!); OpenArchive(Path.GetFullPath(path)); }
        else LoadFolder(path != null && Directory.Exists(path) ? path
            : Directory.Exists(_settings.StartupFolder) ? _settings.StartupFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
        ApplyWindowSettings();
        UpdateCommands();
    }

    // ------------------------------------------------------------------ panel lateral

    private void BuildSidebar()
    {
        _nav.Clear();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        void Quick(string title, string path, IconKind kind) { if (Directory.Exists(path)) _nav.Add(new NavItem { Section = "Accesos", Title = title, Path = path, Kind = kind }); }
        Quick("Escritorio", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), IconKind.Desktop);
        Quick("Descargas", Path.Combine(home, "Downloads"), IconKind.Download);
        Quick("Documentos", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), IconKind.Documents);
        Quick("Imágenes", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), IconKind.Pictures);
        Quick("Música", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic), IconKind.Music);
        Quick("Vídeos", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos), IconKind.Video);
        foreach (var d in DriveInfo.GetDrives())
        {
            try { if (d.IsReady) _nav.Add(new NavItem { Section = "Accesos", Title = $"{(string.IsNullOrEmpty(d.VolumeLabel) ? "Disco local" : d.VolumeLabel)} ({d.Name.TrimEnd('\\')})", Path = d.RootDirectory.FullName, Kind = IconKind.Drive }); }
            catch (IOException) { }
        }
        foreach (var f in _settings.Favorites)
            _nav.Add(new NavItem { Section = "Favoritos", Title = f.Name, Path = f.Path, Kind = IconKind.Star, IsFavorite = true });
        foreach (var r in _settings.RecentArchives.Where(File.Exists).Take(8))
            _nav.Add(new NavItem { Section = "Recientes", Title = Path.GetFileName(r), Path = r, Kind = IconKind.Clock });
    }

    private void Sidebar_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Sidebar.SelectedItem is not NavItem n) return;
        Dispatcher.BeginInvoke(() => { Sidebar.SelectedItem = null; GoToPath(n.Path); });
    }

    private void SidebarRemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        // El elemento sobre el que se abrió el menú contextual es el que está bajo el ratón.
        var element = Mouse.DirectlyOver as DependencyObject;
        var container = element == null ? null : ItemsControl.ContainerFromElement(Sidebar, element) as ListBoxItem;
        if (container?.DataContext is not NavItem { IsFavorite: true } n) { Say("Solo se pueden quitar los favoritos."); return; }
        RemoveFavorite(n.Path);
    }

    private void AddFavorite_Click(object sender, RoutedEventArgs e)
    {
        string path = InArchive ? _session!.Path : _folder;
        if (string.IsNullOrEmpty(path)) return;
        if (_settings.Favorites.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) { Say("Ya está en favoritos."); return; }
        string name = Path.GetFileName(path.TrimEnd('\\'));
        _settings.Favorites.Add(new Favorite { Name = name.Length > 0 ? name : path, Path = path });
        SaveSettings();
        BuildSidebar();
        Say("Añadido a favoritos: " + path);
    }

    private void RemoveFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (_settings.Favorites.Count == 0) { Msg.Info(this, "Todavía no hay favoritos. Usa «Añadir la ubicación actual a favoritos»."); return; }
        string path = InArchive ? _session!.Path : _folder;
        if (_settings.Favorites.Any(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) RemoveFavorite(path);
        else Msg.Info(this, "La ubicación actual no está en favoritos. Para quitar otro favorito, haz clic derecho sobre él en el panel lateral.");
    }

    private void RemoveFavorite(string path)
    {
        _settings.Favorites.RemoveAll(f => f.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
        SaveSettings();
        BuildSidebar();
    }

    private void SaveSettings() { try { SettingsStore.Save(_settings); } catch { /* sin permisos: no es crítico */ } }

    // ------------------------------------------------------------------ carpetas y archivos

    private void Populate(IEnumerable<FileItem> items, bool withUp)
    {
        _items.Clear();
        if (withUp) _items.Add(FileItem.Up());
        foreach (var it in items) _items.Add(it);
        UpdateStatus();
        UpdateCommands();
    }

    public void LoadFolder(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var items = new List<FileItem>();
            foreach (var fi in di.EnumerateFileSystemInfos())
            {
                try { if ((fi.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != (FileAttributes.Hidden | FileAttributes.System)) items.Add(FileItem.FromDisk(fi)); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
            _folder = di.FullName;
            AddressBox.Text = _folder;
            Populate(items, di.Parent != null);
            Title = WindowTitle(_folder, di.Name.Length > 0 ? di.Name : _folder);
            SettingsStore.AddHistory(_settings, _folder);
            ShowComment(null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Msg.Warn(this, "No se puede abrir esa carpeta:\n" + Msg.Describe(ex));
        }
    }

    private void ShowArchiveLevel()
    {
        string prefix = _inside.Length == 0 ? "" : _inside + "/";
        var dirs = new Dictionary<string, FileItem>(StringComparer.OrdinalIgnoreCase);
        var files = new List<FileItem>();
        foreach (var e in Archive.Entries)
        {
            string p = e.Path.TrimEnd('/');
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || p.Length == prefix.Length) continue;
            string rest = p[prefix.Length..];
            int slash = rest.IndexOf('/');
            if (slash >= 0 || e.IsDirectory)
            {
                string name = slash >= 0 ? rest[..slash] : rest;
                if (!dirs.ContainsKey(name) || slash < 0)
                    dirs[name] = FileItem.FromEntryFolder(name, prefix + name, slash < 0 ? e : dirs.GetValueOrDefault(name)?.Entry, slash < 0 ? e.Modified : default);
            }
            else files.Add(FileItem.FromEntry(rest, e));
        }
        AddressBox.Text = Path.Combine(_session!.Path, _inside.Replace('/', '\\'));
        Populate(dirs.Values.Concat(files), true);
        Title = WindowTitle(_session.Path, Path.GetFileName(_session.Path)).Replace(" — " + AppInfo.Name, (Archive.IsEncrypted ? " 🔒" : "") + " — " + AppInfo.Name);
    }

    private void ShowComment(string? text)
    {
        bool show = _settings.ShowArchiveComment && !string.IsNullOrWhiteSpace(text);
        CommentBar.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        CommentText.Text = text ?? "";
    }

    private void GoUp()
    {
        if (InArchive)
        {
            if (_inside.Length > 0)
            {
                int i = _inside.LastIndexOf('/');
                _inside = i < 0 ? "" : _inside[..i];
                ShowArchiveLevel();
            }
            else CloseArchive(true);
        }
        else
        {
            var parent = Directory.GetParent(_folder);
            if (parent != null) LoadFolder(parent.FullName);
        }
    }

    private void GoToPath(string path)
    {
        if (Directory.Exists(path)) { CloseArchive(false); LoadFolder(path); }
        else if (File.Exists(path)) { CloseArchive(false); LoadFolder(Path.GetDirectoryName(path)!); OpenArchive(path); }
        else Msg.Warn(this, "Esa ubicación ya no existe:\n" + path);
    }

    private void ActivateSelected()
    {
        if (FileList.SelectedItem is not FileItem n) return;
        if (n.IsUp) { GoUp(); return; }
        if (InArchive)
        {
            if (n.IsDirectory) { _inside = n.FullPath; ShowArchiveLevel(); }
            else OpenEntryExternally(n);
        }
        else if (n.IsDirectory) LoadFolder(n.FullPath);
        else OpenArchiveOrFile(n.FullPath);
    }

    private void OpenArchiveOrFile(string path)
    {
        bool exe = path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
        if (ArchiveOpener.LooksLikeArchive(path) && (!exe || SfxBuilder.IsSfx(path))) OpenArchive(path);
        else
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (Exception ex) { Msg.Warn(this, "No se puede abrir el fichero:\n" + Msg.Describe(ex)); }
        }
    }

    public void OpenArchive(string path)
    {
        ArchiveSession? s;
        try
        {
            s = null;
            if (_sessionPassword != null)
                try { s = ArchiveSession.Wrap(path, ArchiveOpener.Open(path, _sessionPassword), _sessionPassword); } catch { s = null; }
            s ??= ArchiveSession.Open(this, path);
        }
        catch (Exception ex) { Msg.Error(this, Msg.Describe(ex), "No se puede abrir el archivo"); return; }
        if (s == null) return;
        _session?.Dispose();
        _session = s;
        s.PromptIfEncrypted(this);
        _folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? _folder;
        _inside = "";
        SettingsStore.AddRecent(_settings, path);
        SaveSettings();
        ShowArchiveLevel();
        ShowComment(s.Archive.Comment);
        BuildSidebar();
    }

    private void CloseArchive(bool refresh)
    {
        if (_session == null) return;
        _session.Dispose();
        _session = null;
        _inside = "";
        ShowComment(null);
        if (refresh) LoadFolder(_folder);
        UpdateCommands();
    }

    private void ReloadArchive()
    {
        if (_session == null) return;
        string path = _session.Path, inside = _inside;
        string? pw = _session.Password;
        _session.Dispose();
        _session = null;
        try
        {
            var a = ArchiveOpener.Open(path, pw);
            _session = ArchiveSession.Wrap(path, a, pw);
            _inside = inside;
            if (inside.Length > 0 && !a.Entries.Any(e => e.Path.StartsWith(inside + "/", StringComparison.OrdinalIgnoreCase))) _inside = "";
            ShowArchiveLevel();
            ShowComment(a.Comment);
        }
        catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); LoadFolder(_folder); }
    }

    // ------------------------------------------------------------------ eventos de la lista y la barra

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && ItemsControl.ContainerFromElement(FileList, d) != null) ActivateSelected();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.FocusedElement is TextBox) return;
        var mods = Keyboard.Modifiers;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var args = new RoutedEventArgs();
        bool handled = true;
        if (mods == ModifierKeys.None && key == Key.Back) GoUp();
        else if (mods == ModifierKeys.None && key == Key.Enter) ActivateSelected();
        else if (mods == ModifierKeys.None && key == Key.Delete) Command_Delete(this, args);
        else if (mods == ModifierKeys.None && key == Key.F2) Command_Rename(this, args);
        else if (mods == ModifierKeys.None && key == Key.F3) Command_Find(this, args);
        else if (mods == ModifierKeys.Control && key == Key.O) OpenArchiveDialog_Click(this, args);
        else if (mods == ModifierKeys.Control && key == Key.P) SessionPassword_Click(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.A) Command_Add(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.E) Command_Extract(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.T) Command_Test(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.V) Command_View(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.I) Command_Info(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.R) Command_Repair(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.P) Command_Protect(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.L) Command_Lock(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.M) Command_Comment(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.X) Command_Sfx(this, args);
        else if (mods == ModifierKeys.Alt && key == Key.Q) Command_Convert(this, args);
        else handled = false;
        if (handled) e.Handled = true;
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        string p = AddressBox.Text.Trim().Trim('"');
        if (Directory.Exists(p) || File.Exists(p)) { GoToPath(p); FileList.Focus(); }
        else Msg.Warn(this, "No existe esa ruta.");
        e.Handled = true;
    }

    private void Up_Click(object sender, RoutedEventArgs e) => GoUp();
    private void CloseArchive_Click(object sender, RoutedEventArgs e) => CloseArchive(true);
    private void Exit_Click(object sender, RoutedEventArgs e) => Close();

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { InitialDirectory = _folder };
        if (dlg.ShowDialog(this) == true) { CloseArchive(false); LoadFolder(dlg.FolderName); }
    }

    private void OpenArchiveDialog_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Abrir archivo", InitialDirectory = Directory.Exists(_folder) ? _folder : "",
            Filter = "Archivos comprimidos|*.mdr;*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.lz;*.zst;*.cab;*.iso;*.jar;*.001;*.exe|Todos los ficheros|*.*"
        };
        if (dlg.ShowDialog(this) == true) OpenArchive(dlg.FileName);
    }

    private void SessionPassword_Click(object sender, RoutedEventArgs e)
    {
        string? pw = TextPromptWindow.AskPassword(this, "Se probará automáticamente al abrir archivos cifrados.\nSolo se guarda en memoria y se olvida al cerrar MedMDR.", "Contraseña para esta sesión");
        if (pw != null) _sessionPassword = pw;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) { FileList.SelectAll(); FileList.Focus(); }

    private void CopyNames_Click(object sender, RoutedEventArgs e)
    {
        var names = Selected().Select(n => n.FullPath).ToList();
        if (names.Count == 0) return;
        try { Clipboard.SetText(string.Join(Environment.NewLine, names)); Say($"{names.Count} nombre(s) copiados"); } catch { /* portapapeles ocupado */ }
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = this }.ShowDialog();

    // ------------------------------------------------------------------ orden de columnas

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

    private List<FileItem> Selected() => FileList.SelectedItems.Cast<FileItem>().Where(i => !i.IsUp).ToList();

    private void UpdateCommands()
    {
        if (CmdExtract == null) return;
        var sel = Selected();
        bool archiveSelected = !InArchive && sel.Any(i => i.IsArchive);
        CmdExtract.IsEnabled = InArchive || archiveSelected;
        CmdTest.IsEnabled = InArchive || archiveSelected;
        CmdInfo.IsEnabled = InArchive || archiveSelected;
        CmdView.IsEnabled = sel.Any(i => !i.IsDirectory);
        CmdDelete.IsEnabled = sel.Count > 0;
        MenuClose.IsEnabled = InArchive;
        UpButton.IsEnabled = InArchive || (_folder.Length > 0 && Directory.GetParent(_folder) != null);
    }

    private void UpdateStatus()
    {
        var sel = Selected();
        long selSize = sel.Where(i => !i.IsDirectory).Sum(i => i.Size);
        int selFiles = sel.Count(i => !i.IsDirectory), selDirs = sel.Count(i => i.IsDirectory);
        StatusLeft.Text = sel.Count == 0 ? "" : $"Seleccionados {ArchiveReport.FormatSize(selSize)} en {selFiles} fichero(s)" + (selDirs > 0 ? $", {selDirs} carpeta(s)" : "");
        if (InArchive)
        {
            var a = Archive;
            var flags = new List<string>();
            if (a.IsSolid) flags.Add("sólido");
            if (a.IsEncrypted) flags.Add(a.HasEncryptedNames ? "cifrado (con nombres)" : "cifrado");
            if (a.IsLocked) flags.Add("bloqueado");
            if (a.HasRecovery) flags.Add("con recuperación");
            var totalFiles = a.Files().ToList();
            StatusRight.Text = a.FormatName + (flags.Count > 0 ? " · " + string.Join(", ", flags) : "") +
                               $"   ·   Total {ArchiveReport.FormatSize(totalFiles.Sum(f => f.Size))} en {totalFiles.Count} fichero(s) · archivo {ArchiveReport.FormatSize(a.ArchiveSize)}";
        }
        else
        {
            var files = _items.Where(i => !i.IsUp && !i.IsDirectory).ToList();
            StatusRight.Text = $"Total {ArchiveReport.FormatSize(files.Sum(f => f.Size))} en {files.Count} fichero(s)";
        }
    }

    /// <summary>Mensaje breve en la barra de estado.</summary>
    private void Say(string text) => StatusLeft.Text = text;

    private void FileList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // La columna del nombre ocupa el ancho que sobra (descontando las demás y la barra de desplazamiento).
        double others = 0;
        for (int i = 1; i < Columns.Columns.Count; i++) others += Columns.Columns[i].ActualWidth;
        Columns.Columns[0].Width = Math.Max(160, FileList.ActualWidth - others - 36);
    }

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _session?.Dispose();
        foreach (var f in _tempFiles) { try { File.Delete(f); } catch { } }
        foreach (var d in _tempDirs) { try { Directory.Delete(d, true); } catch { } }
        SaveSettings();
    }

    // ------------------------------------------------------------------ arrastrar y soltar (hacia dentro)

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        Dispatcher.BeginInvoke(() =>
        {
            if (InArchive) AddToCurrentArchive(paths);
            else if (paths.Length == 1 && File.Exists(paths[0]) && ArchiveOpener.LooksLikeArchive(paths[0])) OpenArchive(paths[0]);
            else AddToNewArchive(paths);
        });
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
        SaveSettings();
    }

    private void MarkTheme(CoreTheme t)
    {
        ThemeSystem.IsChecked = t == CoreTheme.System;
        ThemeLight.IsChecked = t == CoreTheme.Light;
        ThemeDark.IsChecked = t == CoreTheme.Dark;
    }

    /// <summary>Solo pruebas: ejecuta órdenes separadas por comas como si el usuario las pulsara.</summary>
    internal void RunAuto(string script)
    {
        foreach (var raw in script.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            string cmd = raw.Trim();
            var noArgs = new RoutedEventArgs();
            if (cmd.StartsWith("select:")) SelectRowForSnapshot(int.Parse(cmd[7..]));
            else if (cmd.StartsWith("add:")) AddToCurrentArchive(cmd[4..].Split('|'));
            else if (cmd.StartsWith("new:")) AddToNewArchive(cmd[4..].Split('|'));
            else switch (cmd)
            {
                case "open": ActivateSelected(); break;
                case "up": GoUp(); break;
                case "test": Command_Test(this, noArgs); break;
                case "info": Command_Info(this, noArgs); break;
                case "view": Command_View(this, noArgs); break;
                case "extract-here": Command_ExtractHere(this, noArgs); break;
                case "extract": Command_Extract(this, noArgs); break;
                case "convert": Command_Convert(this, noArgs); break;
                case "wizard": Command_Wizard(this, noArgs); break;
                case "settings": Command_Settings(this, noArgs); break;
                case "find": Command_Find(this, noArgs); break;
                case "delete": Command_Delete(this, noArgs); break;
                case "report": Command_Report(this, noArgs); break;
                case "rename": Command_Rename(this, noArgs); break;
                case "lock": Command_Lock(this, noArgs); break;
                case "repair": Command_Repair(this, noArgs); break;
                case "protect": Command_Protect(this, noArgs); break;
                case "about": About_Click(this, noArgs); break;
                default: throw new ArgumentException("Orden de prueba desconocida: " + cmd);
            }
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ContextIdle);
        }
    }

    internal void SelectRowForSnapshot(int index)
    {
        if (index < 0 || index >= _view.Count) return;
        FileList.SelectedIndex = index;
        FileList.ScrollIntoView(FileList.SelectedItem);
    }
}
