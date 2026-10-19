using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Lo que el usuario eligió en «Nombre y parámetros del archivo».</summary>
public sealed class AddRequest
{
    public string ArchivePath { get; set; } = "";
    public ArchiveOptions Options { get; set; } = new();
    public PathMode PathMode { get; set; } = PathMode.Relative;
    /// <summary>Carpeta dentro del archivo donde se colocan los ficheros (al añadir a un archivo abierto).</summary>
    public string? Subfolder { get; set; }
}

/// <summary>«Añadir al archivo»: nombre, formato, método, volúmenes, contraseña, recuperación, comentario…</summary>
public partial class AddWindow : Window
{
    private static readonly string[] Levels = { "Almacenar (sin comprimir)", "Más rápido", "Rápido", "Normal", "Bueno", "El mejor (más lento)", "Ultra (solo .mdr, muy lento)" };
    private static readonly string[] Dicts = { "Automático", "128 KB", "512 KB", "1 MB", "4 MB", "16 MB" };
    private static readonly int[] DictKb = { 0, 128, 512, 1024, 4096, 16384 };
    private static readonly string[] Modes = { "Añadir y reemplazar ficheros", "Añadir y actualizar ficheros", "Solo actualizar los ya existentes", "Sincronizar el contenido" };

    private readonly AppSettings _settings;
    private readonly bool _editingExisting;
    private readonly string _folder;
    private string? _password;
    private bool _encryptNames;
    private bool _ready;

    public AddRequest Result { get; private set; } = new();

    public AddWindow(IReadOnlyList<string> sources, string defaultFolder, AppSettings settings, string? existingArchive = null)
    {
        InitializeComponent();
        DevTools.Hook(this, () => Ok_Click(this, new RoutedEventArgs()));
        _settings = settings;
        _editingExisting = existingArchive != null;
        _folder = defaultFolder;
        Title = existingArchive == null ? "Nombre y parámetros del archivo" : "Añadir ficheros al archivo";

        foreach (var r in settings.RecentArchives.Take(8)) NameBox.Items.Add(r);
        foreach (var m in Modes) ModeBox.Items.Add(m);
        foreach (var l in Levels) LevelBox.Items.Add(l);
        foreach (var d in Dicts) DictBox.Items.Add(d);
        foreach (var u in new[] { "KB", "MB", "GB" }) VolUnit.Items.Add(u);
        ModeBox.SelectedIndex = 0; DictBox.SelectedIndex = 0; VolUnit.SelectedIndex = 1;

        string folder = string.IsNullOrWhiteSpace(settings.DefaultArchiveFolder) ? defaultFolder : settings.DefaultArchiveFolder;
        LevelBox.SelectedIndex = Math.Clamp(settings.DefaultLevel, 0, 6);
        SolidCheck.IsChecked = settings.DefaultSolid;
        TestCheck.IsChecked = settings.DefaultTestAfter;
        if (settings.DefaultRecoveryPercent > 0) { RecCheck.IsChecked = true; RecPct.Text = Math.Clamp(settings.DefaultRecoveryPercent, 1, 50).ToString(); }
        SetFormat(settings.DefaultFormat);
        StoreBox.Text = string.Join("; ", ArchiveOptions.DefaultStorePatterns);

        if (existingArchive != null)
        {
            NameBox.Text = existingArchive;
            NameBox.IsEnabled = false; BrowseButton.IsEnabled = false;
            SetFormat(ArchiveBuilder.FormatFromName(existingArchive) ?? ArchiveFormat.Mdr);
            foreach (var r in new[] { FMdr, FZip, F7z, FTgz, FTar, FGz }) r.IsEnabled = false;
            VolBox.IsEnabled = VolUnit.IsEnabled = SfxCheck.IsEnabled = SfxFullCheck.IsEnabled = false;
        }
        else NameBox.Text = Path.Combine(folder, DefaultName(sources) + ArchiveBuilder.Extension(settings.DefaultFormat));

        _ready = true;
        OnFormatChanged();
    }

    internal static string DefaultName(IReadOnlyList<string> sources)
    {
        if (sources.Count == 1)
        {
            string p = sources[0].TrimEnd('\\', '/');
            return Directory.Exists(p) ? Path.GetFileName(p) : Path.GetFileNameWithoutExtension(p);
        }
        string pn = Path.GetFileName(Path.GetDirectoryName(sources[0].TrimEnd('\\', '/')) ?? "");
        return string.IsNullOrEmpty(pn) ? "Archivo" : pn;
    }

    private ArchiveFormat CurrentFormat =>
        FZip.IsChecked == true ? ArchiveFormat.Zip : F7z.IsChecked == true ? ArchiveFormat.SevenZip : FTgz.IsChecked == true ? ArchiveFormat.TarGz
        : FTar.IsChecked == true ? ArchiveFormat.Tar : FGz.IsChecked == true ? ArchiveFormat.Gz : ArchiveFormat.Mdr;

    private void SetFormat(ArchiveFormat f)
    {
        FMdr.IsChecked = f == ArchiveFormat.Mdr; FZip.IsChecked = f == ArchiveFormat.Zip; FTgz.IsChecked = f == ArchiveFormat.TarGz;
        FTar.IsChecked = f == ArchiveFormat.Tar; FGz.IsChecked = f == ArchiveFormat.Gz; F7z.IsChecked = f == ArchiveFormat.SevenZip;
    }

    private void Format_Changed(object sender, RoutedEventArgs e) { if (_ready) OnFormatChanged(); }

    private void OnFormatChanged()
    {
        var f = CurrentFormat;
        bool mdr = f == ArchiveFormat.Mdr;
        var cap = ArchiveBuilder.Capabilities(f);
        SolidCheck.IsEnabled = cap.Solid;
        DeflateCheck.IsEnabled = mdr; DictBox.IsEnabled = mdr; CommentBox.IsEnabled = cap.Comment;
        RecPct.IsEnabled = RecCheck.IsChecked == true;
        SfxCheck.IsEnabled = !_editingExisting;
        SfxFullCheck.IsEnabled = SfxCheck.IsChecked == true && !_editingExisting;
        if (SfxCheck.IsChecked != true) SfxFullCheck.IsChecked = false;
        if (!cap.Solid) SolidCheck.IsChecked = false;
        PwButton.IsEnabled = cap.Password;
        if (!cap.Password || (!cap.EncryptNames && _encryptNames)) { if (!cap.Password) _password = null; _encryptNames = false; UpdatePwState(); }
        HintText.Text = f switch
        {
            ArchiveFormat.Mdr => $"Formato propio de {AppInfo.Name}: solo lo abre {AppInfo.Name}. Para compartir con otros programas elige ZIP o 7Z.",
            ArchiveFormat.Zip => "Lo abre cualquier programa. Con contraseña usa AES-256 (los nombres de los ficheros no se cifran).",
            ArchiveFormat.SevenZip => "Lo abren 7-Zip y otros programas. Con contraseña usa AES-256 y puedes cifrar también los nombres.",
            ArchiveFormat.TarGz => "Habitual en Linux y macOS. Sin contraseña.",
            ArchiveFormat.Tar => "Sin compresión ni contraseña (empaqueta ficheros).",
            _ => "Comprime un único fichero. Sin contraseña."
        };
        VolBox.IsEnabled = VolUnit.IsEnabled = SfxCheck.IsChecked != true && !_editingExisting;
        if (SfxCheck.IsChecked == true) VolBox.Text = "";
        if (!_editingExisting && NameBox.Text.Length > 0)
        {
            string t = NameBox.Text;
            foreach (var ext in new[] { ".tar.gz", ".mdr", ".zip", ".7z", ".tar", ".gz", ".tgz", ".exe" })
                if (t.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { t = t[..^ext.Length]; break; }
            NameBox.Text = t + (SfxCheck.IsChecked == true ? ".exe" : ArchiveBuilder.Extension(f));
        }
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var s = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Nombre del archivo", OverwritePrompt = false,
            InitialDirectory = Path.GetDirectoryName(NameBox.Text) is { Length: > 0 } d && Directory.Exists(d) ? d : _folder,
            FileName = Path.GetFileName(NameBox.Text),
            Filter = $"{AppInfo.Name} (*.mdr)|*.mdr|ZIP (*.zip)|*.zip|7Z (*.7z)|*.7z|TAR.GZ (*.tar.gz)|*.tar.gz|TAR (*.tar)|*.tar|GZ (*.gz)|*.gz|Todos|*.*"
        };
        if (s.ShowDialog(this) == true) NameBox.Text = s.FileName;
    }

    private void Password_Click(object sender, RoutedEventArgs e)
    {
        var cap = ArchiveBuilder.Capabilities(CurrentFormat);
        if (!cap.Password) { Msg.Info(this, "Este formato no admite contraseña. Usa .mdr o ZIP."); return; }
        var r = NewPasswordWindow.Ask(this, cap.EncryptNames, _encryptNames);
        if (r == null) return;
        _password = r.Value.Password;
        _encryptNames = cap.EncryptNames && r.Value.EncryptNames;
        UpdatePwState();
    }

    private void UpdatePwState() =>
        PwState.Text = _password == null ? "Sin contraseña" : _encryptNames ? "Contraseña establecida (nombres cifrados)" : "Contraseña establecida";

    private void LoadComment_Click(object sender, RoutedEventArgs e)
    {
        var o = new Microsoft.Win32.OpenFileDialog { Filter = "Texto|*.txt|Todos|*.*" };
        if (o.ShowDialog(this) == true) { try { CommentBox.Text = File.ReadAllText(o.FileName); } catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); } }
    }

    private ArchiveOptions BuildOptions()
    {
        int unit = VolUnit.SelectedIndex;
        long mult = unit == 0 ? 1024L : unit == 1 ? 1024L * 1024 : 1024L * 1024 * 1024;
        long vol = double.TryParse(VolBox.Text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) && v > 0 ? (long)(v * mult) : 0;
        int pct = int.TryParse(RecPct.Text, out var p) ? Math.Clamp(p, 1, 50) : 5;
        return new ArchiveOptions
        {
            Format = CurrentFormat,
            Level = LevelBox.SelectedIndex,
            DictionaryKb = DictKb[Math.Max(0, DictBox.SelectedIndex)],
            UseDeflate = DeflateCheck.IsChecked == true,
            Solid = SolidCheck.IsChecked == true,
            Password = _password,
            EncryptNames = _encryptNames && _password != null,
            RecoveryPercent = RecCheck.IsChecked == true ? pct : 0,
            VolumeSizeBytes = vol,
            Comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text,
            Lock = LockCheck.IsChecked == true,
            SelfExtracting = SfxCheck.IsChecked == true,
            SfxIncludeRuntime = SfxFullCheck.IsChecked == true,
            TestAfter = TestCheck.IsChecked == true,
            DeleteSourceAfter = DeleteCheck.IsChecked == true,
            UpdateMode = (UpdateMode)Math.Max(0, ModeBox.SelectedIndex),
            StorePatterns = Split(StoreBox.Text),
            ExcludePatterns = Split(ExcludeBox.Text),
        };
    }

    private static List<string> Split(string s) => s.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void ApplyOptions(ArchiveOptions o)
    {
        _ready = false;
        SetFormat(o.Format);
        LevelBox.SelectedIndex = Math.Clamp(o.Level, 0, 6);
        int di = Array.IndexOf(DictKb, o.DictionaryKb); DictBox.SelectedIndex = di < 0 ? 0 : di;
        DeflateCheck.IsChecked = o.UseDeflate; SolidCheck.IsChecked = o.Solid;
        RecCheck.IsChecked = o.RecoveryPercent > 0; if (o.RecoveryPercent > 0) RecPct.Text = Math.Clamp(o.RecoveryPercent, 1, 50).ToString();
        LockCheck.IsChecked = o.Lock; TestCheck.IsChecked = o.TestAfter; DeleteCheck.IsChecked = o.DeleteSourceAfter;
        SfxCheck.IsChecked = o.SelfExtracting; SfxFullCheck.IsChecked = o.SfxIncludeRuntime;
        ModeBox.SelectedIndex = (int)o.UpdateMode;
        StoreBox.Text = string.Join("; ", o.StorePatterns); ExcludeBox.Text = string.Join("; ", o.ExcludePatterns);
        _ready = true;
        OnFormatChanged();
    }

    private void Profiles_Click(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = (UIElement)sender, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        var save = new MenuItem { Header = "Guardar la configuración actual como perfil…" };
        save.Click += (_, _) =>
        {
            string? n = TextPromptWindow.Ask(this, "Nombre del perfil:", "Guardar perfil", "Mi perfil");
            if (string.IsNullOrWhiteSpace(n)) return;
            var o = BuildOptions(); o.Password = null; o.Comment = null;
            _settings.Profiles.RemoveAll(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            _settings.Profiles.Add(new CompressionProfile { Name = n, Options = o });
            try { SettingsStore.Save(_settings); } catch { /* sin permisos */ }
        };
        menu.Items.Add(save);
        if (_settings.Profiles.Count > 0)
        {
            menu.Items.Add(new Separator());
            foreach (var p in _settings.Profiles.ToList())
            {
                var item = new MenuItem { Header = p.Name };
                item.Click += (_, _) => ApplyOptions(p.Options);
                var del = new MenuItem { Header = "Eliminar este perfil" };
                del.Click += (_, _) => { _settings.Profiles.Remove(p); try { SettingsStore.Save(_settings); } catch { } };
                item.Items.Add(del);
                menu.Items.Add(item);
            }
        }
        menu.IsOpen = true;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string path = NameBox.Text.Trim();
        if (path.Length == 0) { Msg.Warn(this, "Escribe el nombre del archivo."); return; }
        if (!Path.IsPathRooted(path)) path = Path.Combine(_folder, path);
        var o = BuildOptions();
        if (!_editingExisting)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == "") path += o.SelfExtracting ? ".exe" : ArchiveBuilder.Extension(o.Format);
            if (o.SelfExtracting && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, ".exe");
            if (o.SelfExtracting && SfxBuilder.FindStub(o.SfxIncludeRuntime) == null)
            {
                Msg.Warn(this, o.SfxIncludeRuntime ? "No se encuentra el módulo autoextraíble completo (MedMDR.Sfx.Full.exe) junto al programa." : "No se encuentra el módulo autoextraíble (MedMDR.Sfx.exe) junto al programa.");
                return;
            }
            if (File.Exists(path) || (o.VolumeSizeBytes > 0 && File.Exists(path + ".001")))
            {
                var r = Msg.AskYesNoCancel(this, $"«{Path.GetFileName(path)}» ya existe.\n\nSí = añadir/actualizar su contenido\nNo = reemplazarlo\nCancelar = volver");
                if (r == MessageBoxResult.Cancel) return;
                if (r == MessageBoxResult.No)
                {
                    try { VolumeSet.DeleteAll(File.Exists(path) ? path : path + ".001"); } catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); return; }
                }
            }
        }
        if (o.VolumeSizeBytes > 0 && o.VolumeSizeBytes < 1024) { Msg.Warn(this, "El tamaño de volumen mínimo es 1 KB."); return; }
        Result = new AddRequest { ArchivePath = path, Options = o, PathMode = PFull.IsChecked == true ? PathMode.Full : PNone.IsChecked == true ? PathMode.None : PathMode.Relative };
        DialogResult = true;
    }

    /// <summary>Devuelve null si se cancela.</summary>
    public static AddRequest? Ask(Window? owner, IReadOnlyList<string> sources, string defaultFolder, AppSettings settings, string? existingArchive = null)
    {
        var w = new AddWindow(sources, defaultFolder, settings, existingArchive);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.Result : null;
    }
}
