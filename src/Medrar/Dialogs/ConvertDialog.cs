using Medrar.Core;

namespace Medrar.App;

/// <summary>Convertir archivos de cualquier formato legible a .mdr, ZIP o TAR.GZ.</summary>
internal sealed class ConvertDialog : DialogBase
{
    private readonly ListBox _files = new() { Width = 520, Height = 110, SelectionMode = SelectionMode.MultiExtended, HorizontalScrollbar = true };
    private readonly ComboBox _format = UI.Combo(new[] { "Medrar (.mdr)", "ZIP (.zip)", "7Z (.7z)", "TAR.GZ (.tar.gz)" }, 0, 200);
    private readonly ComboBox _level = UI.Combo(new[] { "Almacenar", "Más rápido", "Rápido", "Normal", "Bueno", "El mejor" }, 3, 200);
    private readonly CheckBox _solid = UI.Check("Crear archivos sólidos");
    private readonly CheckBox _rec = UI.Check("Añadir registro de recuperación (5 %; en .mdr dentro, en los demás en un fichero .mdrrec)");
    private readonly CheckBox _delete = UI.Check("Eliminar los archivos originales al terminar");
    private readonly Button _pw;
    private readonly Label _pwState = UI.Label("Sin contraseña", muted: true);
    private readonly TextBox _dest = UI.Text("", 400);
    private string? _password;
    private bool _names;

    public List<string> Sources => _files.Items.Cast<string>().ToList();
    public string Destination => _dest.Text.Trim();
    public bool DeleteOriginals => _delete.Checked;
    public ArchiveOptions Options { get; private set; } = new();

    public ConvertDialog(IEnumerable<string> initial, string folder, AppSettings settings) : base("Convertir archivos")
    {
        ClientSize = new Size(580, 570);
        foreach (var p in initial) _files.Items.Add(p);
        _dest.Text = folder;
        var add = UI.Button("Añadir…", (_, _) =>
        {
            using var d = new OpenFileDialog { Multiselect = true, Title = "Archivos que se van a convertir", Filter = "Archivos comprimidos|*.mdr;*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.lz;*.zst;*.cab;*.iso;*.jar;*.001|Todos|*.*" };
            if (d.ShowDialog(this) == DialogResult.OK) foreach (var f in d.FileNames) if (!_files.Items.Contains(f)) _files.Items.Add(f);
        }, 96);
        var remove = UI.Button("Quitar", (_, _) => { foreach (var i in _files.SelectedItems.Cast<object>().ToList()) _files.Items.Remove(i); }, 80);
        _pw = UI.Button("Contraseña…", (_, _) =>
        {
            if (_format.SelectedIndex > 2) { UI.Info(this, "La contraseña solo está disponible en los formatos .mdr, ZIP y 7Z."); return; }
            bool canNames = _format.SelectedIndex is 0 or 2;
            using var d = new PasswordDialog("Contraseña del archivo nuevo", "Escribe la contraseña:", true, _names, offerEncryptNames: canNames);
            if (d.ShowDialog(this) != DialogResult.OK) return;
            _password = d.Password; _names = d.EncryptNames && canNames;
            _pwState.Text = _names ? "Contraseña establecida (nombres cifrados)" : "Contraseña establecida";
        }, 120);
        var browse = UI.Button("Examinar…", (_, _) => { var p = UI.PickFolder(this, _dest.Text, "Carpeta donde se guardarán los archivos convertidos"); if (p != null) _dest.Text = p; }, 96);
        _format.SelectedIndexChanged += (_, _) =>
        {
            bool mdr = _format.SelectedIndex == 0;
            _solid.Enabled = mdr;
            if (!mdr) _solid.Checked = false;
            if (_format.SelectedIndex > 2) { _password = null; _names = false; _pwState.Text = "Sin contraseña"; }
        };
        var col = UI.Column(
            UI.Label("Archivos que se convertirán:"), UI.Row(_files), UI.Row(add, remove),
            UI.Label("Convertir a:", true), _format,
            UI.Label("Nivel de compresión:"), _level,
            UI.Row(_solid), UI.Row(_rec), UI.Row(_pw, _pwState),
            UI.Label("Carpeta de destino:"), UI.Row(_dest, browse), _delete);
        col.Padding = new Padding(14, 10, 0, 0);
        Controls.Add(col);
        Controls.Add(Footer(okClick: Accept));
    }

    private void Accept(object? s, EventArgs e)
    {
        if (_files.Items.Count == 0) { UI.Warn(this, "Añade al menos un archivo."); return; }
        if (!Path.IsPathRooted(Destination)) { UI.Warn(this, "Indica una carpeta de destino válida."); return; }
        try { Directory.CreateDirectory(Destination); } catch (Exception ex) { UI.Error(this, UI.Describe(ex)); return; }
        Options = new ArchiveOptions
        {
            Format = _format.SelectedIndex switch { 1 => ArchiveFormat.Zip, 2 => ArchiveFormat.SevenZip, 3 => ArchiveFormat.TarGz, _ => ArchiveFormat.Mdr },
            Level = _level.SelectedIndex, Solid = _solid.Checked, Password = _password, EncryptNames = _names && _password != null,
            RecoveryPercent = _rec.Checked ? 5 : 0
        };
        DialogResult = DialogResult.OK;
    }
}
