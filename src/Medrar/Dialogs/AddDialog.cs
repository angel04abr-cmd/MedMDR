using Medrar.Core;

namespace Medrar.App;

internal sealed class AddRequest
{
    public string ArchivePath { get; set; } = "";
    public ArchiveOptions Options { get; set; } = new();
    public PathMode PathMode { get; set; } = PathMode.Relative;
    /// <summary>Carpeta dentro del archivo donde se colocan los ficheros (al añadir a un archivo abierto).</summary>
    public string? Subfolder { get; set; }
}

/// <summary>«Añadir al archivo»: nombre, formato, método, volúmenes, contraseña, recuperación, comentario...</summary>
internal sealed class AddDialog : DialogBase
{
    private static readonly string[] Levels = { "Almacenar (sin comprimir)", "Más rápido", "Rápido", "Normal", "Bueno", "El mejor (más lento)", "Ultra (solo .mdr, muy lento)" };
    private static readonly string[] Dicts = { "Automático", "128 KB", "512 KB", "1 MB", "4 MB", "16 MB" };
    private static readonly int[] DictKb = { 0, 128, 512, 1024, 4096, 16384 };
    private static readonly string[] Modes = { "Añadir y reemplazar ficheros", "Añadir y actualizar ficheros", "Solo actualizar los ya existentes", "Sincronizar el contenido" };

    private readonly AppSettings _settings;
    private readonly ComboBox _name = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDown, Margin = new Padding(0, 3, 6, 3) };
    private readonly RadioButton _fMdr = UI.Radio("Medrar (.mdr) — cifrado, recuperación y más", true);
    private readonly RadioButton _fZip = UI.Radio("ZIP");
    private readonly RadioButton _fTgz = UI.Radio("TAR.GZ");
    private readonly RadioButton _fTar = UI.Radio("TAR");
    private readonly RadioButton _f7z = UI.Radio("7Z");
    private readonly RadioButton _fGz = UI.Radio("GZ (1 fichero)");
    private readonly Label _hint = new() { AutoSize = false, Width = 320, Height = 52, ForeColor = Color.Gray, Margin = new Padding(0, 4, 0, 4) };
    private readonly ComboBox _level = UI.Combo(Levels, 3, 220);
    private readonly ComboBox _dict = UI.Combo(Dicts, 0, 220);
    private readonly ComboBox _mode = UI.Combo(Modes, 0, 280);
    private readonly TextBox _vol = UI.Text("", 70);
    private readonly ComboBox _volUnit = UI.Combo(new[] { "KB", "MB", "GB" }, 1, 60);
    private readonly CheckBox _delete = UI.Check("Eliminar los ficheros originales al terminar");
    private readonly CheckBox _sfx = UI.Check("Crear archivo autoextraíble (.exe)");
    private readonly CheckBox _sfxFull = UI.Check("Incluir .NET (funciona sin nada instalado; pesa más)");
    private Button _pwButton = null!;
    private readonly CheckBox _solid = UI.Check("Crear archivo sólido");
    private readonly CheckBox _rec = UI.Check("Añadir registro de recuperación");
    private readonly NumericUpDown _recPct = new() { Minimum = 1, Maximum = 50, Value = 5, Width = 56 };
    private readonly CheckBox _test = UI.Check("Verificar los ficheros comprimidos");
    private readonly CheckBox _lock = UI.Check("Bloquear el archivo (impide modificarlo)");
    private readonly Label _pwState = UI.Label("Sin contraseña", muted: true);
    private readonly CheckBox _deflate = UI.Check("Usar Deflate en lugar de Brotli (más compatible, menos compresión)");
    private readonly RadioButton _pRel = UI.Radio("Rutas relativas (la carpeta y su contenido)", true);
    private readonly RadioButton _pFull = UI.Radio("Rutas completas (sin la letra de unidad)");
    private readonly RadioButton _pNone = UI.Radio("Sin rutas (solo los nombres de fichero)");
    private readonly TextBox _store = new() { Multiline = true, Height = 70, Width = 420, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _exclude = new() { Multiline = true, Height = 70, Width = 420, ScrollBars = ScrollBars.Vertical };
    private readonly TextBox _comment = new() { Multiline = true, Width = 520, Height = 220, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
    private string? _password;
    private bool _encryptNames;
    private readonly bool _editingExisting;
    private readonly string _folder;

    public AddRequest Result { get; private set; } = new();

    public AddDialog(IReadOnlyList<string> sources, string defaultFolder, AppSettings settings, string? existingArchive = null)
        : base(existingArchive == null ? "Nombre y parámetros del archivo" : "Añadir ficheros al archivo")
    {
        _settings = settings;
        _editingExisting = existingArchive != null;
        _folder = defaultFolder;
        ClientSize = new Size(720, 600);

        // --- pestaña General ---
        _name.Items.AddRange(settings.RecentArchives.Take(8).ToArray());
        string baseName = DefaultName(sources);
        var browse = UI.Button("Examinar…", (_, _) => BrowseName(), 96);
        var profiles = UI.Button("Perfiles…", ShowProfiles, 96);

        var formats = UI.Column(_fMdr, UI.Row(_fZip, _f7z, _fTgz, _fTar, _fGz), _hint);
        var volRow = UI.Row(_vol, _volUnit, UI.Label("(vacío = un solo fichero)", muted: true));
        var recRow = UI.Row(_rec, _recPct, UI.Label("%"));
        _pwButton = UI.Button("Establecer contraseña…", SetPassword, 190);
        var pwRow = UI.Column(_pwButton, _pwState);

        var optBox = UI.Column(_delete, _sfx, _sfxFull, _solid, recRow, _test, _lock);
        var top = UI.Column(UI.Label("Nombre del archivo:"), UI.Row(_name, browse, profiles));
        var left = UI.Column(
            UI.Label("Modo de actualización:"), _mode,
            UI.Label("Formato:", true), formats,
            UI.Label("Método de compresión:"), _level,
            UI.Label("Tamaño del diccionario:"), _dict,
            UI.Label("Dividir en volúmenes de:"), volRow);
        left.Margin = new Padding(0, 0, 18, 0);
        var right = UI.Column(UI.Group("Opciones", optBox), pwRow);
        var general = UI.Page(top, UI.Row(left, right));

        // --- pestaña Avanzado ---
        var advanced = UI.Page(
            UI.Label("Compresión", true), _deflate,
            UI.Label("Rutas dentro del archivo", true), _pRel, _pFull, _pNone,
            UI.Label("Guardar sin comprimir (comodines separados por ; )", true), _store,
            UI.Label("Excluir del archivo (comodines separados por ; )", true), _exclude);

        // --- pestaña Comentario ---
        var loadComment = UI.Button("Cargar desde un fichero…", (_, _) =>
        {
            using var o = new OpenFileDialog { Filter = "Texto|*.txt|Todos|*.*" };
            if (o.ShowDialog(this) == DialogResult.OK) _comment.Text = File.ReadAllText(o.FileName);
        }, 190);
        var commentPage = UI.Page(UI.Label("Texto que se guardará dentro del archivo:"), _comment, loadComment);

        var tabs = new FluentTabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(UI.Tab("General", general));
        tabs.TabPages.Add(UI.Tab("Avanzado", advanced));
        tabs.TabPages.Add(UI.Tab("Comentario", commentPage));
        Controls.Add(tabs);
        Controls.Add(Footer(okClick: Accept));

        // valores por defecto
        string folder = string.IsNullOrWhiteSpace(settings.DefaultArchiveFolder) ? defaultFolder : settings.DefaultArchiveFolder;
        _level.SelectedIndex = Math.Clamp(settings.DefaultLevel, 0, 6);
        _solid.Checked = settings.DefaultSolid;
        _test.Checked = settings.DefaultTestAfter;
        if (settings.DefaultRecoveryPercent > 0) { _rec.Checked = true; _recPct.Value = Math.Clamp(settings.DefaultRecoveryPercent, 1, 50); }
        SetFormat(settings.DefaultFormat);
        _store.Text = string.Join("; ", ArchiveOptions.DefaultStorePatterns);

        if (existingArchive != null)
        {
            _name.Text = existingArchive;
            _name.Enabled = false; browse.Enabled = false;
            var fmt = ArchiveBuilder.FormatFromName(existingArchive) ?? ArchiveFormat.Mdr;
            SetFormat(fmt);
            foreach (var r in new[] { _fMdr, _fZip, _f7z, _fTgz, _fTar, _fGz }) r.Enabled = false;
            _vol.Enabled = _volUnit.Enabled = _sfx.Enabled = _sfxFull.Enabled = false;
        }
        else _name.Text = Path.Combine(folder, baseName + ArchiveBuilder.Extension(settings.DefaultFormat));

        foreach (var r in new[] { _fMdr, _fZip, _f7z, _fTgz, _fTar, _fGz }) r.CheckedChanged += (_, _) => OnFormatChanged();
        _sfx.CheckedChanged += (_, _) => OnFormatChanged();
        _rec.CheckedChanged += (_, _) => _recPct.Enabled = _rec.Checked;
        _recPct.Enabled = _rec.Checked;
        OnFormatChanged();
    }

    private static string DefaultName(IReadOnlyList<string> sources)
    {
        if (sources.Count == 1)
        {
            string p = sources[0].TrimEnd('\\', '/');
            return Path.GetFileNameWithoutExtension(Directory.Exists(p) ? p + "\\x" : p) is { Length: > 0 } n && !Directory.Exists(p) ? n : Path.GetFileName(p);
        }
        string parent = Path.GetDirectoryName(sources[0].TrimEnd('\\', '/')) ?? "";
        string pn = Path.GetFileName(parent);
        return string.IsNullOrEmpty(pn) ? "Archivo" : pn;
    }

    private ArchiveFormat CurrentFormat =>
        _fZip.Checked ? ArchiveFormat.Zip : _f7z.Checked ? ArchiveFormat.SevenZip : _fTgz.Checked ? ArchiveFormat.TarGz : _fTar.Checked ? ArchiveFormat.Tar : _fGz.Checked ? ArchiveFormat.Gz : ArchiveFormat.Mdr;

    private void SetFormat(ArchiveFormat f)
    {
        _fMdr.Checked = f == ArchiveFormat.Mdr; _fZip.Checked = f == ArchiveFormat.Zip; _fTgz.Checked = f == ArchiveFormat.TarGz;
        _fTar.Checked = f == ArchiveFormat.Tar; _fGz.Checked = f == ArchiveFormat.Gz; _f7z.Checked = f == ArchiveFormat.SevenZip;
    }

    private void OnFormatChanged()
    {
        var f = CurrentFormat;
        bool mdr = f == ArchiveFormat.Mdr;
        var cap = ArchiveBuilder.Capabilities(f);
        _solid.Enabled = cap.Solid;
        _deflate.Enabled = mdr; _dict.Enabled = mdr; _comment.Enabled = cap.Comment;
        _rec.Enabled = true; _lock.Enabled = true;                    // recuperación y bloqueo valen para todos los formatos
        _recPct.Enabled = _rec.Checked;
        _sfx.Enabled = !_editingExisting;
        _sfxFull.Enabled = _sfx.Checked && !_editingExisting;
        if (!_sfx.Checked) _sfxFull.Checked = false;
        if (!cap.Solid) _solid.Checked = false;
        _pwButton.Enabled = cap.Password;
        if (!cap.Password || (!cap.EncryptNames && _encryptNames)) { if (!cap.Password) _password = null; _encryptNames = false; UpdatePwState(); }
        _hint.Text = f switch
        {
            ArchiveFormat.Mdr => "Formato propio de Medrar: solo lo abre Medrar. Para compartir con otros programas elige ZIP o 7Z.",
            ArchiveFormat.Zip => "Lo abre cualquier programa. Con contraseña usa AES-256 (los nombres de los ficheros no se cifran).",
            ArchiveFormat.SevenZip => "Lo abren 7-Zip, WinRAR y otros. Con contraseña usa AES-256 y puedes cifrar también los nombres.",
            ArchiveFormat.TarGz => "Habitual en Linux y macOS. Sin contraseña.",
            ArchiveFormat.Tar => "Sin compresión ni contraseña (empaqueta ficheros).",
            _ => "Comprime un único fichero. Sin contraseña."
        };
        _vol.Enabled = _volUnit.Enabled = !_sfx.Checked && !_editingExisting;
        if (_sfx.Checked) _vol.Text = "";
        if (!_editingExisting && _name.Text.Length > 0)
        {
            string t = _name.Text;
            foreach (var ext in new[] { ".tar.gz", ".mdr", ".zip", ".7z", ".tar", ".gz", ".tgz", ".exe" })
                if (t.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) { t = t[..^ext.Length]; break; }
            _name.Text = t + (_sfx.Checked ? ".exe" : ArchiveBuilder.Extension(f));
        }
    }

    private void BrowseName()
    {
        using var s = new SaveFileDialog
        {
            Title = "Nombre del archivo", OverwritePrompt = false,
            InitialDirectory = Path.GetDirectoryName(_name.Text) is { Length: > 0 } d && Directory.Exists(d) ? d : _folder,
            FileName = Path.GetFileName(_name.Text),
            Filter = "Medrar (*.mdr)|*.mdr|ZIP (*.zip)|*.zip|7Z (*.7z)|*.7z|TAR.GZ (*.tar.gz)|*.tar.gz|TAR (*.tar)|*.tar|GZ (*.gz)|*.gz|Todos|*.*"
        };
        if (s.ShowDialog(this) == DialogResult.OK) _name.Text = s.FileName;
    }

    private void SetPassword(object? s, EventArgs e)
    {
        if (!ArchiveBuilder.Capabilities(CurrentFormat).Password) { UI.Info(this, "Este formato no admite contraseña. Usa .mdr o ZIP."); return; }
        bool names = ArchiveBuilder.Capabilities(CurrentFormat).EncryptNames;
        using var d = new PasswordDialog("Establecer contraseña", "Escribe la contraseña del archivo:", true, _encryptNames, offerEncryptNames: names);
        if (d.ShowDialog(this) != DialogResult.OK) return;
        _password = d.Password;
        _encryptNames = names && d.EncryptNames;
        UpdatePwState();
    }

    private void UpdatePwState() =>
        _pwState.Text = _password == null ? "Sin contraseña" : _encryptNames ? "Contraseña establecida (nombres cifrados)" : "Contraseña establecida";

    private ArchiveOptions BuildOptions()
    {
        int unit = _volUnit.SelectedIndex;
        long mult = unit == 0 ? 1024L : unit == 1 ? 1024L * 1024 : 1024L * 1024 * 1024;
        long vol = double.TryParse(_vol.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) && v > 0 ? (long)(v * mult) : 0;
        return new ArchiveOptions
        {
            Format = CurrentFormat,
            Level = _level.SelectedIndex,
            DictionaryKb = DictKb[Math.Max(0, _dict.SelectedIndex)],
            UseDeflate = _deflate.Checked,
            Solid = _solid.Checked,
            Password = _password,
            EncryptNames = _encryptNames && _password != null,
            RecoveryPercent = _rec.Checked ? (int)_recPct.Value : 0,
            VolumeSizeBytes = vol,
            Comment = string.IsNullOrWhiteSpace(_comment.Text) ? null : _comment.Text,
            Lock = _lock.Checked,
            SelfExtracting = _sfx.Checked,
            SfxIncludeRuntime = _sfxFull.Checked,
            TestAfter = _test.Checked,
            DeleteSourceAfter = _delete.Checked,
            UpdateMode = (UpdateMode)Math.Max(0, _mode.SelectedIndex),
            StorePatterns = Split(_store.Text),
            ExcludePatterns = Split(_exclude.Text),
        };
    }

    private static List<string> Split(string s) => s.Split(new[] { ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private void ApplyOptions(ArchiveOptions o, bool keepName = true)
    {
        SetFormat(o.Format);
        _level.SelectedIndex = Math.Clamp(o.Level, 0, 6);
        int di = Array.IndexOf(DictKb, o.DictionaryKb); _dict.SelectedIndex = di < 0 ? 0 : di;
        _deflate.Checked = o.UseDeflate; _solid.Checked = o.Solid;
        _rec.Checked = o.RecoveryPercent > 0; if (o.RecoveryPercent > 0) _recPct.Value = Math.Clamp(o.RecoveryPercent, 1, 50);
        _lock.Checked = o.Lock; _test.Checked = o.TestAfter; _delete.Checked = o.DeleteSourceAfter; _sfx.Checked = o.SelfExtracting; _sfxFull.Checked = o.SfxIncludeRuntime;
        _mode.SelectedIndex = (int)o.UpdateMode;
        _store.Text = string.Join("; ", o.StorePatterns); _exclude.Text = string.Join("; ", o.ExcludePatterns);
        OnFormatChanged();
    }

    private void ShowProfiles(object? sender, EventArgs e)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Guardar la configuración actual como perfil…", null, (_, _) =>
        {
            string? n = InputDialog.Ask("Nombre del perfil:", "Guardar perfil", "Mi perfil");
            if (string.IsNullOrWhiteSpace(n)) return;
            var o = BuildOptions(); o.Password = null; o.Comment = null;
            _settings.Profiles.RemoveAll(p => p.Name.Equals(n, StringComparison.OrdinalIgnoreCase));
            _settings.Profiles.Add(new CompressionProfile { Name = n, Options = o });
            Program.SaveSettings();
        });
        if (_settings.Profiles.Count > 0)
        {
            menu.Items.Add(new ToolStripSeparator());
            foreach (var p in _settings.Profiles.ToList())
            {
                var item = new ToolStripMenuItem(p.Name, null, (_, _) => ApplyOptions(p.Options));
                item.DropDownItems.Add("Eliminar este perfil", null, (_, _) => { _settings.Profiles.Remove(p); Program.SaveSettings(); });
                menu.Items.Add(item);
            }
        }
        Theme.StyleMenu(menu.Items);
        menu.Show((Control)sender!, new Point(0, ((Control)sender!).Height));
    }

    private void Accept(object? s, EventArgs e)
    {
        string path = _name.Text.Trim();
        if (path.Length == 0) { UI.Warn(this, "Escribe el nombre del archivo."); return; }
        if (!Path.IsPathRooted(path)) path = Path.Combine(_folder, path);
        var o = BuildOptions();
        if (!_editingExisting)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext == "") path += o.SelfExtracting ? ".exe" : ArchiveBuilder.Extension(o.Format);
            if (o.SelfExtracting && !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) path = Path.ChangeExtension(path, ".exe");
            if (o.SelfExtracting && SfxBuilder.FindStub(o.SfxIncludeRuntime) == null)
            { UI.Warn(this, o.SfxIncludeRuntime ? "No se encuentra el módulo autoextraíble completo (Medrar.Sfx.Full.exe) junto al programa." : "No se encuentra el módulo autoextraíble (Medrar.Sfx.exe) junto al programa."); return; }
            if (File.Exists(path) || (o.VolumeSizeBytes > 0 && File.Exists(path + ".001")))
            {
                var r = MessageBox.Show(this, $"«{Path.GetFileName(path)}» ya existe.\n\nSí = añadir/actualizar su contenido\nNo = reemplazarlo\nCancelar = volver", "Medrar",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) return;
                if (r == DialogResult.No)
                {
                    try { VolumeSet.DeleteAll(File.Exists(path) ? path : path + ".001"); } catch (Exception ex) { UI.Error(this, UI.Describe(ex)); return; }
                }
            }
        }
        if (o.VolumeSizeBytes > 0 && o.VolumeSizeBytes < 1024) { UI.Warn(this, "El tamaño de volumen mínimo es 1 KB."); return; }
        Result = new AddRequest { ArchivePath = path, Options = o, PathMode = _pFull.Checked ? PathMode.Full : _pNone.Checked ? PathMode.None : PathMode.Relative };
        DialogResult = DialogResult.OK;
    }
}
