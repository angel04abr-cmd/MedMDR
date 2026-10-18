using System.Collections;
using Medrar.Core;

namespace Medrar.App;

/// <summary>Elemento de la lista: puede ser un fichero o carpeta del disco, o una entrada dentro de un archivo.</summary>
internal sealed class Node
{
    public string Name = "";
    public string FullPath = "";          // ruta en disco, o ruta dentro del archivo
    public bool IsDir;
    public bool IsUp;
    public long Size;
    public long Packed = -1;
    public DateTime Modified;
    public string? Crc;
    public ArchiveEntry? Entry;           // solo dentro de un archivo (null en carpetas implícitas)
}

internal sealed partial class MainForm : Form
{
    private readonly AppSettings _settings = Program.Settings;
    private ArchiveSession? _session;
    private string _folder = "";
    private string _inside = "";                 // carpeta actual dentro del archivo ("" = raíz)
    private string? _sessionPassword;           // contraseña temporal (solo memoria)
    private readonly List<string> _tempFiles = new();
    private int _sortCol;
    private bool _sortAsc = true;

    private readonly MenuStrip _menu = new();
    private readonly ToolStrip _tools = new();
    private readonly ListView _list = new();
    private readonly TextBox _address = new();
    private readonly ToolStripButton _up = new();
    private readonly StatusStrip _status = new();
    private readonly ToolStripStatusLabel _statusLeft = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusRight = new();
    private readonly ToolStripStatusLabel _statusFlags = new();
    private readonly TextBox _comment = new() { Multiline = true, ReadOnly = true, Dock = DockStyle.Bottom, Height = 70, ScrollBars = ScrollBars.Vertical, Visible = false };
    private readonly ContextMenuStrip _ctx = new();

    private bool InArchive => _session != null;
    private IArchive Archive => _session!.Archive;

    public MainForm(string? openPath)
    {
        Text = "Medrar";
        Icon = AppIcons.App;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(Math.Max(760, _settings.WindowWidth), Math.Max(480, _settings.WindowHeight));
        MinimumSize = new Size(720, 420);
        AllowDrop = true;
        KeyPreview = true;
        TopMost = _settings.AlwaysOnTop;

        BuildMenu();
        BuildToolbar();
        BuildAddressBar();
        BuildList();
        BuildStatus();

        var addressPanel = new Panel { Dock = DockStyle.Top, Height = 34, Padding = new Padding(6, 4, 6, 4) };
        addressPanel.Controls.Add(_address);
        var upBtn = new Button { Image = Icons.Get(IconKind.Up, 16), Dock = DockStyle.Left, Width = 32, FlatStyle = FlatStyle.Flat, TabStop = false };
        upBtn.FlatAppearance.BorderSize = 0;
        upBtn.Click += (_, _) => GoUp();
        new ToolTip().SetToolTip(upBtn, "Subir un nivel (Retroceso)");
        addressPanel.Controls.Add(upBtn);
        addressPanel.Controls.SetChildIndex(_address, 0);
        _address.Dock = DockStyle.Fill;

        Controls.Add(_list);
        Controls.Add(_comment);
        Controls.Add(addressPanel);
        Controls.Add(_tools);
        Controls.Add(_menu);
        Controls.Add(_status);
        MainMenuStrip = _menu;
        _menu.Dock = DockStyle.Top;

        DragEnter += OnDragEnter; DragDrop += OnDragDrop;
        _list.DragEnter += OnDragEnter; _list.DragDrop += OnDragDrop;
        FormClosing += (_, _) => OnClosing();

        string start = !string.IsNullOrWhiteSpace(_settings.StartupFolder) && Directory.Exists(_settings.StartupFolder)
            ? _settings.StartupFolder : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Load += (_, _) =>
        {
            Theme.Style(this);
            Theme.StyleMenu(_menu.Items);
            if (openPath != null && Directory.Exists(openPath)) GoFolder(openPath);
            else if (openPath != null && File.Exists(openPath)) { GoFolder(Path.GetDirectoryName(Path.GetFullPath(openPath))!); OpenArchive(Path.GetFullPath(openPath)); }
            else GoFolder(start);
        };
        Shown += (_, _) => Updates.CheckInBackground(this, _settings);
        if (Program.PendingThemeFlip is { } flip)
        {
            var t = new System.Windows.Forms.Timer { Interval = 2000 };
            t.Tick += (_, _) => { t.Stop(); ChangeTheme(flip); };
            Shown += (_, _) => t.Start();
        }
    }

    // ---------------------------------------------------------------- construcción

    private void BuildAddressBar()
    {
        _address.BorderStyle = BorderStyle.FixedSingle;
        _comment.BorderStyle = BorderStyle.FixedSingle;
        _address.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            string p = _address.Text.Trim().Trim('"');
            if (Directory.Exists(p)) { CloseArchive(false); GoFolder(p); }
            else if (File.Exists(p)) OpenArchiveOrFile(p);
            else UI.Warn(this, "No existe esa ruta.");
        };
    }

    private void BuildList()
    {
        _list.Dock = DockStyle.Fill;
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.HideSelection = false;
        _list.MultiSelect = true;
        _list.SmallImageList = FileIcons.List;
        _list.AllowDrop = true;
        _list.BorderStyle = BorderStyle.None;
        _list.GridLines = false;
        _list.DoubleBuffered(true);
        string[] cols = { "Nombre", "Tamaño", "Comprimido", "Tipo", "Modificado", "CRC32" };
        for (int i = 0; i < cols.Length; i++)
            _list.Columns.Add(new ColumnHeader { Text = cols[i], Width = i < _settings.ColumnWidths.Length ? _settings.ColumnWidths[i] : 100, TextAlign = i is 1 or 2 ? HorizontalAlignment.Right : HorizontalAlignment.Left });
        _list.ColumnClick += (_, e) =>
        {
            if (_sortCol == e.Column) _sortAsc = !_sortAsc; else { _sortCol = e.Column; _sortAsc = true; }
            ApplySort();
        };
        _list.ItemActivate += (_, _) => ActivateSelected();
        _list.ItemDrag += OnItemDrag;
        _list.SelectedIndexChanged += (_, _) => { UpdateStatus(); UpdateToolbarState(); };
        _list.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Back) { GoUp(); e.Handled = true; }
            else if (e.KeyCode == Keys.Delete) { DeleteSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.F2) { RenameSelected(); e.Handled = true; }
            else if (e.KeyCode == Keys.Add && !e.Control) { SelectGroup(true); e.Handled = true; }
            else if (e.KeyCode == Keys.Subtract && !e.Control) { SelectGroup(false); e.Handled = true; }
            else if (e.KeyCode == Keys.Multiply) { InvertSelection(); e.Handled = true; }
        };
        _list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            BuildContextMenu();
            _ctx.Show(_list, e.Location);
        };
        _list.ListViewItemSorter = new NodeSorter(this);
    }

    private void BuildStatus()
    {
        _status.Items.AddRange(new ToolStripItem[] { _statusLeft, _statusFlags, _statusRight });
        _status.SizingGrip = true;
    }

    private void ApplySort() { _list.Sort(); }

    private sealed class NodeSorter : IComparer
    {
        private readonly MainForm _f;
        public NodeSorter(MainForm f) => _f = f;
        public int Compare(object? x, object? y)
        {
            var a = (Node)((ListViewItem)x!).Tag!; var b = (Node)((ListViewItem)y!).Tag!;
            if (a.IsUp != b.IsUp) return a.IsUp ? -1 : 1;
            if (a.IsDir != b.IsDir && !_f._settings.FlatFolderView) return a.IsDir ? -1 : 1;
            int c = _f._sortCol switch
            {
                1 => a.Size.CompareTo(b.Size),
                2 => a.Packed.CompareTo(b.Packed),
                3 => string.Compare(FileIcons.TypeName(a.Name, a.IsDir), FileIcons.TypeName(b.Name, b.IsDir), StringComparison.CurrentCultureIgnoreCase),
                4 => a.Modified.CompareTo(b.Modified),
                5 => string.Compare(a.Crc, b.Crc, StringComparison.Ordinal),
                _ => NaturalCompare(a.Name, b.Name)
            };
            if (c == 0 && _f._sortCol != 0) c = NaturalCompare(a.Name, b.Name);
            return _f._sortAsc ? c : -c;
        }
    }

    [System.Runtime.InteropServices.DllImport("shlwapi.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);
    private static int NaturalCompare(string a, string b) => StrCmpLogicalW(a, b);

    // ---------------------------------------------------------------- navegación y listado

    private IEnumerable<Node> SelectedNodes() =>
        _list.SelectedItems.Cast<ListViewItem>().Select(i => (Node)i.Tag!).Where(n => !n.IsUp);

    private void Populate(IEnumerable<Node> nodes, bool withUp)
    {
        _list.BeginUpdate();
        _list.Items.Clear();
        if (withUp)
            _list.Items.Add(new ListViewItem("..", FileIcons.Up) { Tag = new Node { Name = "..", IsDir = true, IsUp = true } });
        foreach (var n in nodes)
        {
            var it = new ListViewItem(n.Name, FileIcons.For(n.Name, n.IsDir)) { Tag = n };
            it.SubItems.Add(n.IsDir && !InArchive ? "" : n.IsDir ? "" : n.Size.ToString("N0"));
            it.SubItems.Add(!InArchive || n.IsDir || n.Packed < 0 ? "" : n.Packed.ToString("N0"));
            it.SubItems.Add(FileIcons.TypeName(n.Name, n.IsDir));
            it.SubItems.Add(n.Modified == default ? "" : n.Modified.ToString("dd/MM/yyyy HH:mm"));
            it.SubItems.Add(n.Crc ?? "");
            _list.Items.Add(it);
        }
        _list.Sort();
        _list.EndUpdate();
        UpdateStatus();
        UpdateToolbarState();
    }

    public void GoFolder(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            var nodes = new List<Node>();
            foreach (var e in di.EnumerateFileSystemInfos())
            {
                try
                {
                    if ((e.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == (FileAttributes.Hidden | FileAttributes.System)) continue;
                    bool dir = e is DirectoryInfo;
                    nodes.Add(new Node { Name = e.Name, FullPath = e.FullName, IsDir = dir, Size = dir ? 0 : ((FileInfo)e).Length, Modified = e.LastWriteTime });
                }
                catch { }
            }
            _folder = di.FullName;
            _address.Text = _folder;
            Populate(nodes, di.Parent != null);
            Text = (_settings.ShowFullPathInTitle ? _folder : di.Name.Length == 0 ? _folder : di.Name) + " — Medrar";
            SettingsStore.AddHistory(_settings, _folder);
            ShowComment(null);
            _statusFlags.Text = "";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            UI.Warn(this, "No se puede abrir esa carpeta:\n" + UI.Describe(ex));
        }
    }

    private void ShowArchiveLevel()
    {
        var entries = Archive.Entries;
        string prefix = _inside.Length == 0 ? "" : _inside + "/";
        var dirs = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var files = new List<Node>();
        foreach (var e in entries)
        {
            string p = e.Path.TrimEnd('/');
            if (!p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || p.Length == prefix.Length) continue;
            string rest = p[prefix.Length..];
            int slash = rest.IndexOf('/');
            if (slash >= 0 || e.IsDirectory)
            {
                string name = slash >= 0 ? rest[..slash] : rest;
                if (!dirs.ContainsKey(name))
                    dirs[name] = new Node { Name = name, FullPath = prefix + name, IsDir = true, Modified = slash < 0 ? e.Modified : default, Entry = slash < 0 ? e : null };
                else if (slash < 0) { dirs[name].Entry = e; dirs[name].Modified = e.Modified; }
            }
            else
                files.Add(new Node { Name = rest, FullPath = p, Size = e.Size, Packed = e.PackedSize, Modified = e.Modified, Crc = e.Crc?.ToString("X8"), Entry = e });
        }
        _address.Text = Path.Combine(_session!.Path, _inside.Replace('/', '\\'));
        Populate(dirs.Values.Concat(files), true);
        var a = Archive;
        Text = $"{Path.GetFileName(_session.Path)} — Medrar" + (a.IsEncrypted ? " 🔒" : "");
        var flags = new List<string>();
        if (a.IsSolid) flags.Add("sólido");
        if (a.IsEncrypted) flags.Add(a.HasEncryptedNames ? "cifrado (con nombres)" : "cifrado");
        if (a.IsLocked) flags.Add("bloqueado");
        if (a.HasRecovery) flags.Add("con recuperación");
        _statusFlags.Text = a.FormatName + (flags.Count > 0 ? " · " + string.Join(", ", flags) : "");
    }

    private void ShowComment(string? text)
    {
        _comment.Visible = _settings.ShowArchiveComment && !string.IsNullOrWhiteSpace(text);
        _comment.Text = text ?? "";
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
            if (parent != null) GoFolder(parent.FullName);
        }
    }

    private void ActivateSelected()
    {
        var items = _list.SelectedItems.Cast<ListViewItem>().ToList();
        if (items.Count == 0) return;
        var n = (Node)items[0].Tag!;
        if (n.IsUp) { GoUp(); return; }
        if (InArchive)
        {
            if (n.IsDir) { _inside = n.FullPath; ShowArchiveLevel(); }
            else OpenEntryExternally(n);
        }
        else
        {
            if (n.IsDir) GoFolder(n.FullPath);
            else OpenArchiveOrFile(n.FullPath);
        }
    }

    private void OpenArchiveOrFile(string path)
    {
        if (ArchiveOpener.LooksLikeArchive(path) && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) OpenArchive(path);
        else if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && SfxBuilder.IsSfx(path)) OpenArchive(path);
        else try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true }); }
             catch (Exception ex) { UI.Warn(this, "No se puede abrir el fichero:\n" + UI.Describe(ex)); }
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
        catch (Exception ex) { UI.Error(this, UI.Describe(ex), "No se puede abrir el archivo"); return; }
        if (s == null) return;
        _session?.Dispose();
        _session = s;
        s.PromptIfEncrypted(this);
        _folder = Path.GetDirectoryName(Path.GetFullPath(path)) ?? _folder;
        _inside = "";
        SettingsStore.AddRecent(_settings, path);
        Program.SaveSettings();
        ShowArchiveLevel();
        ShowComment(s.Archive.Comment);
        RebuildRecentMenu();
    }

    private void CloseArchive(bool refresh)
    {
        if (_session == null) return;
        _session.Dispose();
        _session = null;
        _inside = "";
        ShowComment(null);
        if (refresh) GoFolder(_folder);
    }

    private void ReloadArchive()
    {
        if (_session == null) return;
        string path = _session.Path;
        string inside = _inside;
        string? pw = _session.Password;
        _session.Dispose();
        _session = null;
        try
        {
            var a = ArchiveOpener.Open(path, pw);
            _session = ArchiveSession.Wrap(path, a, pw);
            _inside = inside;
            if (!Archive.Entries.Any(e => e.Path.StartsWith(inside + "/", StringComparison.OrdinalIgnoreCase)) && inside.Length > 0) _inside = "";
            ShowArchiveLevel();
            ShowComment(a.Comment);
        }
        catch (Exception ex) { UI.Error(this, UI.Describe(ex)); GoFolder(_folder); }
    }

    // ---------------------------------------------------------------- estado

    private void UpdateStatus()
    {
        var sel = SelectedNodes().ToList();
        var all = _list.Items.Cast<ListViewItem>().Select(i => (Node)i.Tag!).Where(n => !n.IsUp).ToList();
        long selSize = sel.Where(n => !n.IsDir).Sum(n => n.Size);
        int nFiles = all.Count(n => !n.IsDir);
        if (InArchive)
        {
            var totalFiles = Archive.Files().ToList();
            _statusLeft.Text = sel.Count > 0 ? $"Seleccionados {UI.Size(selSize)} en {sel.Count(n => !n.IsDir)} fichero(s)" : "";
            _statusRight.Text = $"Total {UI.Size(totalFiles.Sum(f => f.Size))} en {totalFiles.Count} fichero(s) · archivo {UI.Size(Archive.ArchiveSize)}";
        }
        else
        {
            _statusLeft.Text = sel.Count > 0 ? $"Seleccionados {UI.Size(selSize)} en {sel.Count(n => !n.IsDir)} fichero(s)" + (sel.Any(n => n.IsDir) ? $", {sel.Count(n => n.IsDir)} carpeta(s)" : "") : "";
            _statusRight.Text = $"Total {UI.Size(all.Where(n => !n.IsDir).Sum(n => n.Size))} en {nFiles} fichero(s)";
        }
    }

    private void OnClosing()
    {
        _settings.WindowWidth = ClientSize.Width;
        _settings.WindowHeight = ClientSize.Height;
        _settings.ColumnWidths = _list.Columns.Cast<ColumnHeader>().Select(c => c.Width).ToArray();
        Program.SaveSettings();
        _session?.Dispose();
        foreach (var f in _tempFiles) { try { File.Delete(f); } catch { } }
        foreach (var d in _tempDirs) { try { Directory.Delete(d, true); } catch { } }
    }

    // ---------------------------------------------------------------- arrastrar y soltar

    private void OnDragEnter(object? s, DragEventArgs e)
    {
        e.Effect = e.Data?.GetDataPresent(DataFormats.FileDrop) == true ? DragDropEffects.Copy : DragDropEffects.None;
    }

    private void OnDragDrop(object? s, DragEventArgs e)
    {
        if (_draggingOut) return;                                   // lo que se arrastra desde esta misma ventana no se vuelve a añadir
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] paths || paths.Length == 0) return;
        BeginInvoke(() => HandleDropped(paths));
    }

    private void HandleDropped(string[] paths)
    {
        if (InArchive) { AddToCurrentArchive(paths); return; }
        if (paths.Length == 1 && File.Exists(paths[0]) && ArchiveOpener.LooksLikeArchive(paths[0])) { OpenArchive(paths[0]); return; }
        AddToNewArchive(paths);
    }
}

internal static class ListViewExtensions
{
    public static void DoubleBuffered(this ListView lv, bool value)
    {
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(lv, value);
    }
}

