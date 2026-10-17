using Medrar.Core;

namespace Medrar.App;

/// <summary>Asistente paso a paso para crear un archivo o extraer uno.</summary>
internal sealed class WizardForm : DialogBase
{
    private readonly AppSettings _settings;
    private readonly string _folder;
    private int _step;
    private readonly Panel _page = new() { Dock = DockStyle.Fill, Padding = new Padding(16, 12, 16, 0) };
    private readonly Label _title = new() { Dock = DockStyle.Top, Height = 44, Padding = new Padding(16, 14, 0, 0), Font = new Font(SystemFonts.MessageBoxFont!.FontFamily, 12, FontStyle.Bold) };
    private readonly Button _back = UI.Button("< Atrás", null, 96), _next = UI.Button("Siguiente >", null, 110), _cancel = UI.Button("Cancelar", null, 96, DialogResult.Cancel);

    // paso 1
    private readonly RadioButton _opCreate = UI.Radio("Crear un archivo comprimido nuevo", true);
    private readonly RadioButton _opExtract = UI.Radio("Extraer los ficheros de un archivo comprimido");
    // paso 2
    private readonly ListBox _sources = new() { Width = 500, Height = 150, HorizontalScrollbar = true };
    private readonly TextBox _archive = UI.Text("", 380);
    // paso 3
    private readonly TextBox _name = UI.Text("", 380);
    private readonly TextBox _dest = UI.Text("", 380);
    private readonly ComboBox _format = UI.Combo(new[] { "Medrar (.mdr)", "ZIP", "7Z", "TAR.GZ" }, 0, 220);
    private readonly ComboBox _level = UI.Combo(new[] { "Más rápido", "Normal", "El mejor" }, 1, 220);
    private readonly CheckBox _pwCheck = UI.Check("Proteger con contraseña");
    private string? _password; private bool _names;

    public bool Create => _opCreate.Checked;
    public AddRequest Request { get; private set; } = new();
    public List<string> Sources { get; private set; } = new();
    public string ArchivePath => _archive.Text.Trim();
    public string Destination => _dest.Text.Trim();

    public WizardForm(string folder, AppSettings settings) : base("Asistente")
    {
        _settings = settings; _folder = folder;
        ClientSize = new Size(560, 400);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6) };
        buttons.Controls.Add(_cancel); buttons.Controls.Add(_next); buttons.Controls.Add(_back);
        Controls.Add(_page); Controls.Add(_title); Controls.Add(buttons);
        _back.Click += (_, _) => { if (_step > 0) { _step--; Show_(); } };
        _next.Click += (_, _) => Next();
        CancelButton = _cancel;
        _dest.Text = folder;
        Show_();
    }

    private void Show_()
    {
        _page.Controls.Clear();
        _back.Enabled = _step > 0;
        _next.Text = _step == 2 ? "Terminar" : "Siguiente >";
        _title.Text = $"Asistente: paso {_step + 1} de 3";
        Control content;
        switch (_step)
        {
            case 0:
                _title.Text += " — ¿Qué quieres hacer?";
                content = UI.Column(UI.Label("Elige la operación:"), _opCreate, _opExtract);
                break;
            case 1:
                if (Create)
                {
                    _title.Text += " — Elige qué comprimir";
                    var add = UI.Button("Añadir ficheros…", (_, _) => { using var d = new OpenFileDialog { Multiselect = true }; if (d.ShowDialog(this) == DialogResult.OK) foreach (var f in d.FileNames) if (!_sources.Items.Contains(f)) _sources.Items.Add(f); }, 140);
                    var addDir = UI.Button("Añadir carpeta…", (_, _) => { var p = UI.PickFolder(this, _folder); if (p != null && !_sources.Items.Contains(p)) _sources.Items.Add(p); }, 140);
                    var rem = UI.Button("Quitar", (_, _) => { if (_sources.SelectedItem != null) _sources.Items.Remove(_sources.SelectedItem); }, 80);
                    content = UI.Column(UI.Label("Ficheros y carpetas que se incluirán:"), _sources, UI.Row(add, addDir, rem));
                }
                else
                {
                    _title.Text += " — Elige el archivo";
                    var browse = UI.Button("Examinar…", (_, _) => { using var d = new OpenFileDialog { Filter = "Archivos comprimidos|*.mdr;*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.lz;*.zst;*.cab;*.iso;*.jar;*.001;*.exe|Todos|*.*" }; if (d.ShowDialog(this) == DialogResult.OK) { _archive.Text = d.FileName; _dest.Text = Path.Combine(_settings.DefaultExtractFolder.Length > 0 ? _settings.DefaultExtractFolder : Path.GetDirectoryName(d.FileName)!, Path.GetFileNameWithoutExtension(d.FileName)); } }, 96);
                    content = UI.Column(UI.Label("Archivo que se va a extraer:"), UI.Row(_archive, browse));
                }
                break;
            default:
                if (Create)
                {
                    _title.Text += " — Opciones del archivo";
                    if (_name.Text.Length == 0)
                    {
                        string first = Sources.FirstOrDefault() ?? _sources.Items.Cast<string>().FirstOrDefault() ?? "Archivo";
                        _name.Text = Path.GetFileNameWithoutExtension(Directory.Exists(first) ? first + "\\x" : first) is { Length: > 0 } nn && !Directory.Exists(first) ? nn : Path.GetFileName(first.TrimEnd('\\'));
                    }
                    var browse = UI.Button("Examinar…", (_, _) => { var p = UI.PickFolder(this, _dest.Text); if (p != null) _dest.Text = p; }, 96);
                    var pwBtn = UI.Button("Contraseña…", (_, _) =>
                    {
                        using var d = new PasswordDialog("Contraseña", "Escribe la contraseña:", true, _names);
                        if (d.ShowDialog(this) == DialogResult.OK) { _password = d.Password; _names = d.EncryptNames; _pwCheck.Checked = true; }
                        else if (_password == null) _pwCheck.Checked = false;
                    }, 110);
                    _pwCheck.CheckedChanged += (_, _) => { if (!_pwCheck.Checked) _password = null; };
                    content = UI.Column(UI.Label("Nombre del archivo (sin extensión):"), _name, UI.Label("Carpeta donde se guardará:"), UI.Row(_dest, browse),
                        UI.Label("Formato:"), _format, UI.Label("Nivel de compresión:"), _level, UI.Row(_pwCheck, pwBtn));
                }
                else
                {
                    _title.Text += " — Carpeta de destino";
                    var browse = UI.Button("Examinar…", (_, _) => { var p = UI.PickFolder(this, _dest.Text); if (p != null) _dest.Text = p; }, 96);
                    content = UI.Column(UI.Label("Los ficheros se extraerán en:"), UI.Row(_dest, browse));
                }
                break;
        }
        content.Dock = DockStyle.Top;
        _page.Controls.Add(content);
        Theme.Style(this);
    }

    private void Next()
    {
        if (_step == 0) { _step = 1; Show_(); return; }
        if (_step == 1)
        {
            if (Create && _sources.Items.Count == 0) { UI.Warn(this, "Añade al menos un fichero o carpeta."); return; }
            if (!Create && !File.Exists(ArchivePath) && !VolumeSet.IsVolumeName(ArchivePath)) { UI.Warn(this, "Elige un archivo existente."); return; }
            Sources = _sources.Items.Cast<string>().ToList();
            _step = 2; Show_(); return;
        }
        if (string.IsNullOrWhiteSpace(Destination) || !Path.IsPathRooted(Destination)) { UI.Warn(this, "Indica una carpeta de destino válida."); return; }
        if (Create)
        {
            if (string.IsNullOrWhiteSpace(_name.Text)) { UI.Warn(this, "Escribe el nombre del archivo."); return; }
            var fmt = _format.SelectedIndex switch { 1 => ArchiveFormat.Zip, 2 => ArchiveFormat.SevenZip, 3 => ArchiveFormat.TarGz, _ => ArchiveFormat.Mdr };
            if (_pwCheck.Checked && _password == null) { UI.Warn(this, "Pulsa «Contraseña…» para establecerla o desmarca la casilla."); return; }
            if (_password != null && fmt is not (ArchiveFormat.Mdr or ArchiveFormat.Zip or ArchiveFormat.SevenZip)) { UI.Warn(this, "La contraseña solo está disponible en los formatos .mdr, ZIP y 7Z."); return; }
            string path = Path.Combine(Destination, _name.Text.Trim() + ArchiveBuilder.Extension(fmt));
            if (File.Exists(path) && !UI.Confirm(this, $"«{Path.GetFileName(path)}» ya existe. ¿Reemplazarlo?")) return;
            try { Directory.CreateDirectory(Destination); if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { UI.Error(this, UI.Describe(ex)); return; }
            Request = new AddRequest
            {
                ArchivePath = path, PathMode = PathMode.Relative,
                Options = new ArchiveOptions { Format = fmt, Level = _level.SelectedIndex switch { 0 => 1, 2 => 5, _ => 3 }, Password = _password, EncryptNames = _names && _password != null }
            };
        }
        DialogResult = DialogResult.OK;
    }
}
