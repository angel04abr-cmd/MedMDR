using Medrar.Core;

namespace Medrar.App;

/// <summary>Buscar texto (o bytes) en una carpeta, en un archivo abierto y en los archivos comprimidos que contenga.</summary>
internal sealed class FindDialog : DialogBase
{
    private readonly string _folder;
    private readonly IArchive? _archive;
    private readonly string? _password;
    private readonly TextBox _text = new() { Width = 360 };
    private readonly TextBox _mask = new() { Width = 360, Text = "*" };
    private readonly CheckBox _case = UI.Check("Distinguir mayúsculas");
    private readonly CheckBox _hex = UI.Check("Buscar bytes en hexadecimal (ej. 4D 5A)");
    private readonly CheckBox _sub = UI.Check("Incluir subcarpetas", true);
    private readonly CheckBox _inside = UI.Check("Buscar dentro de los archivos comprimidos", true);
    private readonly ListView _results = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    private readonly Button _go;
    private readonly Label _status = UI.Label("", muted: true);
    private CancellationTokenSource? _cts;
    public string? Navigate { get; private set; }

    public FindDialog(string folder, IArchive? archive, string? password) : base(archive == null ? "Buscar ficheros" : "Buscar en el archivo")
    {
        _folder = folder; _archive = archive; _password = password;
        ClientSize = new Size(640, 520);
        FormBorderStyle = FormBorderStyle.Sizable; MaximizeBox = true; MinimumSize = new Size(560, 460);
        _results.Columns.Add("Dónde", 260); _results.Columns.Add("Coincidencia", 330);
        _go = UI.Button("Buscar", Start, 96);
        var cancel = UI.Button("Detener", (_, _) => _cts?.Cancel(), 96);

        var top = UI.Column(
            UI.Label(archive == null ? $"Carpeta: {folder}" : $"Archivo: {archive.Path}", muted: true),
            UI.Label("Texto a buscar:"), _text,
            UI.Label("Nombres de fichero (comodines, separados por ;):"), _mask,
            UI.Row(_case, _hex),
            archive == null ? UI.Row(_sub, _inside) : new Panel { Height = 2 },
            UI.Row(_go, cancel, _status));
        top.Padding = new Padding(12, 8, 12, 4);
        top.Dock = DockStyle.Top;
        Controls.Add(_results);
        Controls.Add(top);
        _results.DoubleClick += (_, _) =>
        {
            if (_results.SelectedItems.Count == 0) return;
            Navigate = (string)_results.SelectedItems[0].Tag!;
            DialogResult = DialogResult.OK;
        };
        AcceptButton = _go;
        Shown += (_, _) => _text.Focus();
    }

    private async void Start(object? s, EventArgs e)
    {
        if (_text.Text.Length == 0) { UI.Warn(this, "Escribe el texto que quieres buscar."); return; }
        _results.Items.Clear();
        _go.Enabled = false;
        _status.Text = "Buscando…";
        _cts = new CancellationTokenSource();
        var q = new SearchQuery
        {
            Text = _text.Text, CaseSensitive = _case.Checked, Hex = _hex.Checked, Mask = _mask.Text == "*" ? "" : _mask.Text,
            IncludeSubfolders = _sub.Checked, InsideArchives = _inside.Checked
        };
        var progress = new ProgressReporter(new Progress<ProgressInfo>(p => _status.Text = "Buscando en " + Path.GetFileName(p.CurrentName)), _cts.Token);
        try
        {
            var hits = await Task.Run(() => _archive != null
                ? Finder.SearchArchive(_archive, q, progress)
                : Finder.SearchFolder(_folder, q, progress, _password));
            foreach (var h in hits)
            {
                var it = new ListViewItem(h.Item) { Tag = h.Container == _folder || _archive != null ? h.Item : h.Container };
                it.SubItems.Add(h.Detail);
                _results.Items.Add(it);
            }
            _status.Text = hits.Count == 0 ? "Sin coincidencias." : $"{hits.Count} coincidencia(s).";
        }
        catch (OperationCanceledException) { _status.Text = "Búsqueda detenida."; }
        catch (MedrarException ex) { _status.Text = ex.Message; }
        catch (Exception ex) { _status.Text = UI.Describe(ex); }
        finally { _go.Enabled = true; }
    }
}
