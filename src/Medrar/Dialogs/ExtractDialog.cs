using Medrar.Core;

namespace Medrar.App;

internal sealed class ExtractRequest
{
    public string Destination { get; set; } = "";
    public ExtractOptions Options { get; set; } = new();
    public bool ShowInExplorer { get; set; }
}

/// <summary>«Carpeta y opciones de extracción».</summary>
internal sealed class ExtractDialog : DialogBase
{
    private readonly AppSettings _settings;
    private readonly ComboBox _dest = new() { Width = 420, DropDownStyle = ComboBoxStyle.DropDown };
    private readonly RadioButton _uReplace = UI.Radio("Extraer y reemplazar ficheros", true);
    private readonly RadioButton _uUpdate = UI.Radio("Extraer y actualizar ficheros");
    private readonly RadioButton _uFresh = UI.Radio("Solo actualizar los ya existentes");
    private readonly RadioButton _oAsk = UI.Radio("Confirmar la sobrescritura", true);
    private readonly RadioButton _oYes = UI.Radio("Sobrescribir sin confirmación");
    private readonly RadioButton _oSkip = UI.Radio("Omitir los ficheros existentes");
    private readonly RadioButton _oRename = UI.Radio("Renombrar automáticamente");
    private readonly RadioButton _pFull = UI.Radio("Extraer con las rutas completas", true);
    private readonly RadioButton _pNone = UI.Radio("Extraer sin rutas (todo en una carpeta)");
    private readonly CheckBox _broken = UI.Check("Conservar los ficheros dañados");
    private readonly CheckBox _show = UI.Check("Mostrar los ficheros en el Explorador");
    private readonly CheckBox _sub = UI.Check("Crear una carpeta con el nombre del archivo");
    private readonly string _archiveName;

    public ExtractRequest Result { get; private set; } = new();

    public ExtractDialog(string archivePath, string defaultDest, AppSettings settings, bool showSubfolderOption = true)
        : base("Carpeta y opciones de extracción")
    {
        _settings = settings;
        _archiveName = Path.GetFileNameWithoutExtension(archivePath);
        if (_archiveName.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) _archiveName = Path.GetFileNameWithoutExtension(_archiveName);
        ClientSize = new Size(600, 520);

        _dest.Items.AddRange(settings.History.Take(10).ToArray());
        _dest.Text = defaultDest;
        var browse = UI.Button("Examinar…", (_, _) => { var p = UI.PickFolder(this, _dest.Text, "Carpeta de destino"); if (p != null) _dest.Text = p; }, 96);
        var newFolder = UI.Button("Nueva carpeta…", (_, _) =>
        {
            string? n = InputDialog.Ask("Nombre de la carpeta nueva:", "Nueva carpeta", _archiveName);
            if (!string.IsNullOrWhiteSpace(n)) _dest.Text = Path.Combine(Directory.Exists(_dest.Text) ? _dest.Text : defaultDest, n);
        }, 120);

        var main = UI.Column(
            UI.Label("Carpeta de destino (si no existe, se creará):"),
            UI.Row(_dest), UI.Row(browse, newFolder),
            UI.Row(
                UI.Group("Modo de actualización", UI.Column(_uReplace, _uUpdate, _uFresh)),
                UI.Group("Modo de sobrescritura", UI.Column(_oAsk, _oYes, _oSkip, _oRename))),
            UI.Group("Rutas", UI.Column(_pFull, _pNone)),
            UI.Group("Varios", UI.Column(_sub, _broken, _show)));
        main.Padding = new Padding(14, 10, 0, 0);
        _sub.Visible = showSubfolderOption;
        _oAsk.Checked = settings.ExtractOverwrite == OverwriteMode.Ask;
        _oYes.Checked = settings.ExtractOverwrite == OverwriteMode.Overwrite;
        _oSkip.Checked = settings.ExtractOverwrite == OverwriteMode.Skip;
        _oRename.Checked = settings.ExtractOverwrite == OverwriteMode.Rename;
        _broken.Checked = settings.KeepBrokenFiles;
        Controls.Add(main);
        var save = UI.Button("Guardar opciones", (_, _) =>
        {
            settings.ExtractOverwrite = Overwrite; settings.KeepBrokenFiles = _broken.Checked; Program.SaveSettings();
            UI.Info(this, "Opciones guardadas como predeterminadas.");
        }, 130);
        var footer = Footer(okClick: Accept);
        footer.Controls.Add(save);
        Controls.Add(footer);
    }

    private OverwriteMode Overwrite => _oYes.Checked ? OverwriteMode.Overwrite : _oSkip.Checked ? OverwriteMode.Skip : _oRename.Checked ? OverwriteMode.Rename : OverwriteMode.Ask;

    private void Accept(object? s, EventArgs e)
    {
        string dest = _dest.Text.Trim();
        if (dest.Length == 0) { UI.Warn(this, "Indica la carpeta de destino."); return; }
        if (!Path.IsPathRooted(dest)) { UI.Warn(this, "Escribe una ruta completa (por ejemplo C:\\Carpeta)."); return; }
        if (_sub.Visible && _sub.Checked) dest = Path.Combine(dest, _archiveName);
        Result = new ExtractRequest
        {
            Destination = dest,
            ShowInExplorer = _show.Checked,
            Options = new ExtractOptions
            {
                DestinationDir = dest,
                PathMode = _pNone.Checked ? PathMode.None : PathMode.Full,
                Update = _uUpdate.Checked ? ExtractUpdateMode.ExtractUpdate : _uFresh.Checked ? ExtractUpdateMode.FreshenOnly : ExtractUpdateMode.ExtractReplace,
                Overwrite = Overwrite,
                KeepBrokenFiles = _broken.Checked,
            }
        };
        SettingsStore.AddHistory(_settings, _dest.Text.Trim());
        DialogResult = DialogResult.OK;
    }
}
