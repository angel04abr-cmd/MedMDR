using Medrar.Core;

namespace Medrar.App;

/// <summary>Pide una línea de texto (sustituye a InputBox).</summary>
internal sealed class InputDialog : DialogBase
{
    private readonly TextBox _box = new() { Width = 380 };
    public string Value => _box.Text;

    private InputDialog(string title, string prompt, string def, bool multiline) : base(title)
    {
        ClientSize = new Size(430, multiline ? 260 : 140);
        _box.Text = def;
        if (multiline) { _box.Multiline = true; _box.Height = 150; _box.Width = 400; _box.AcceptsReturn = true; _box.ScrollBars = ScrollBars.Vertical; }
        var col = UI.Column(UI.Label(prompt), _box);
        col.Padding = new Padding(14, 12, 14, 0);
        Controls.Add(col);
        Controls.Add(Footer());
        Shown += (_, _) => { _box.Focus(); _box.SelectAll(); };
    }

    /// <summary>Devuelve null si se cancela.</summary>
    public static string? Ask(string prompt, string title, string def = "", IWin32Window? owner = null, bool multiline = false)
    {
        using var d = new InputDialog(title, prompt, def, multiline);
        return d.ShowDialog(owner) == DialogResult.OK ? d.Value : null;
    }
}

/// <summary>Lista de resultados (errores, ficheros dañados...).</summary>
internal sealed class ResultDialog : DialogBase
{
    public ResultDialog(string title, string headline, IEnumerable<string> lines, bool ok) : base(title)
    {
        ClientSize = new Size(620, 400);
        var head = new Label { Text = headline, Dock = DockStyle.Top, Height = 44, Padding = new Padding(12, 10, 12, 0), Font = new Font(Font, FontStyle.Bold), ForeColor = ok ? Color.SeaGreen : Color.IndianRed };
        var box = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true, IntegralHeight = false };
        box.Items.AddRange(lines.Cast<object>().ToArray());
        Controls.Add(box);
        Controls.Add(head);
        var footer = Footer("Cerrar", "Copiar", null);
        foreach (Control c in footer.Controls) if (c is Button b && b.DialogResult == DialogResult.Cancel)
            { b.DialogResult = DialogResult.None; b.Click += (_, _) => { Clipboard.SetText(string.Join("\r\n", lines)); }; }
        Controls.Add(footer);
        Theme.Style(this);
    }
}

internal sealed class AboutDialog : DialogBase
{
    public AboutDialog() : base("Acerca de Medrar")
    {
        ClientSize = new Size(460, 300);
        var logo = new PictureBox { Image = Icons.Get(IconKind.Logo, 96), SizeMode = PictureBoxSizeMode.AutoSize, Margin = new Padding(0, 8, 16, 0) };
        var text = UI.Column(
            new Label { Text = "Medrar", Font = new Font(Font.FontFamily, 22, FontStyle.Bold), AutoSize = true },
            UI.Label("Versión 1.0"),
            UI.Label("Gestor de archivos comprimidos para Windows."),
            UI.Label("Crea y extrae .mdr, ZIP, TAR y GZ; lee además RAR, 7Z, BZ2, XZ y más.", muted: true),
            UI.Label("Todo el código, iconos y textos son propios."),
            UI.Label("Lectura de RAR/7Z con SharpCompress (licencia MIT).", muted: true));
        var row = UI.Row(logo, text);
        row.Padding = new Padding(18, 14, 8, 0);
        Controls.Add(row);
        var footer = Footer("Cerrar", "", null);
        foreach (Button c in footer.Controls.OfType<Button>().ToList()) if (c.DialogResult == DialogResult.Cancel) footer.Controls.Remove(c);
        Controls.Add(footer);
    }
}

/// <summary>Favoritos: carpetas y archivos a los que se vuelve con un clic.</summary>
internal sealed class FavoritesDialog : DialogBase
{
    private readonly AppSettings _s;
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill, HideSelection = false };
    public string? Go { get; private set; }

    public FavoritesDialog(AppSettings s) : base("Organizar favoritos")
    {
        _s = s;
        ClientSize = new Size(600, 380);
        _list.Columns.Add("Nombre", 180);
        _list.Columns.Add("Ruta", 380);
        Fill();
        var go = UI.Button("Ir", (_, _) => { if (_list.SelectedItems.Count > 0) { Go = (string)_list.SelectedItems[0].Tag!; DialogResult = DialogResult.OK; } }, 80);
        var ren = UI.Button("Renombrar", (_, _) =>
        {
            if (_list.SelectedItems.Count == 0) return;
            var f = (Favorite)_list.SelectedItems[0].SubItems[0].Tag!;
            string? n = InputDialog.Ask("Nuevo nombre:", "Renombrar favorito", f.Name, this);
            if (!string.IsNullOrWhiteSpace(n)) { f.Name = n; Program.SaveSettings(); Fill(); }
        }, 100);
        var del = UI.Button("Quitar", (_, _) =>
        {
            if (_list.SelectedItems.Count == 0) return;
            _s.Favorites.Remove((Favorite)_list.SelectedItems[0].SubItems[0].Tag!); Program.SaveSettings(); Fill();
        }, 80);
        var side = new FlowLayoutPanel { Dock = DockStyle.Right, Width = 112, FlowDirection = FlowDirection.TopDown, Padding = new Padding(4, 8, 4, 0) };
        side.Controls.AddRange(new Control[] { go, ren, del });
        _list.DoubleClick += (_, _) => go.PerformClick();
        Controls.Add(_list);
        Controls.Add(side);
        var footer = Footer("Cerrar", "", null);
        foreach (Button c in footer.Controls.OfType<Button>().ToList()) if (c.DialogResult == DialogResult.Cancel) footer.Controls.Remove(c);
        Controls.Add(footer);
    }

    private void Fill()
    {
        _list.Items.Clear();
        foreach (var f in _s.Favorites)
        {
            var it = new ListViewItem(f.Name) { Tag = f.Path };
            it.SubItems[0].Tag = f;
            it.SubItems.Add(f.Path);
            _list.Items.Add(it);
        }
    }
}

/// <summary>Prueba de velocidad de compresión.</summary>
internal sealed class BenchmarkDialog : DialogBase
{
    private readonly ListView _list = new() { View = View.Details, FullRowSelect = true, Dock = DockStyle.Fill };
    private readonly Label _status = new() { Dock = DockStyle.Top, Height = 30, Padding = new Padding(10, 8, 0, 0), Text = "Midiendo… (unos segundos)" };
    private readonly CancellationTokenSource _cts = new();

    public BenchmarkDialog() : base("Prueba de velocidad")
    {
        ClientSize = new Size(600, 320);
        foreach (var (t, w) in new[] { ("Método", 110), ("Nivel", 70), ("Comprimir (MB/s)", 130), ("Descomprimir (MB/s)", 140), ("Tamaño final", 100) })
            _list.Columns.Add(t, w);
        Controls.Add(_list);
        Controls.Add(_status);
        var footer = Footer("Cerrar", "", null);
        foreach (Button c in footer.Controls.OfType<Button>().ToList()) if (c.DialogResult == DialogResult.Cancel) footer.Controls.Remove(c);
        Controls.Add(footer);
        FormClosing += (_, _) => _cts.Cancel();
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            var log = new Progress<string>(s => _status.Text = "Midiendo: " + s);
            var results = await Task.Run(() => Benchmark.Run(8, log, _cts.Token));
            foreach (var r in results)
            {
                var it = new ListViewItem(r.Method);
                it.SubItems.Add(r.Level.ToString());
                it.SubItems.Add(r.CompressMBps.ToString("0.0"));
                it.SubItems.Add(r.DecompressMBps.ToString("0.0"));
                it.SubItems.Add(r.RatioPercent.ToString("0.0") + " %");
                _list.Items.Add(it);
            }
            _status.Text = "Listo. Datos de prueba de 8 MB parecidos a texto; en tu equipo, con un solo hilo.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _status.Text = "Error: " + ex.Message; }
    }
}
