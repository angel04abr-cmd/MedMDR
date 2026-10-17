using System.Text;
using Medrar.Core;

namespace Medrar.App;

/// <summary>Visor interno: texto con varias codificaciones, vista hexadecimal e imágenes.</summary>
internal sealed class ViewerForm : Form
{
    private readonly byte[] _data;
    private readonly string _name;
    private readonly AppSettings _settings;
    private readonly TextBox _text = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, WordWrap = false, BorderStyle = BorderStyle.None };
    private readonly PictureBox? _pic;
    private Encoding _encoding = new UTF8Encoding(false);
    private bool _hex;
    private readonly ToolStripLabel _status = new();
    private readonly bool _truncated;

    public ViewerForm(string name, byte[] data, AppSettings settings, bool truncated = false)
    {
        _name = name; _data = data; _settings = settings; _truncated = truncated;
        Text = $"{name} — Visor de Medrar";
        Icon = AppIcons.App;
        Size = new Size(860, 620);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        var ext = Path.GetExtension(name).ToLowerInvariant();
        bool isImage = ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp";
        _text.Font = new Font(settings.ViewerFontName, settings.ViewerFontSize);
        _text.WordWrap = settings.ViewerWordWrap;
        _text.ScrollBars = settings.ViewerWordWrap ? ScrollBars.Vertical : ScrollBars.Both;

        var tools = new ToolStrip { GripStyle = ToolStripGripStyle.Hidden };
        var enc = new ToolStripDropDownButton("Codificación");
        foreach (var (label, e) in new (string, Func<Encoding>)[]
        {
            ("UTF-8", () => new UTF8Encoding(false)), ("Windows (ANSI)", () => Encoding.Latin1),
            ("Unicode (UTF-16)", () => Encoding.Unicode), ("DOS (CP437/850)", () => { try { return Encoding.GetEncoding(850); } catch { return Encoding.Latin1; } })
        })
            enc.DropDownItems.Add(label, null, (_, _) => { _encoding = e(); _hex = false; Render(); });
        var hex = new ToolStripButton("Hexadecimal") { CheckOnClick = true };
        hex.CheckedChanged += (_, _) => { _hex = hex.Checked; Render(); };
        var wrap = new ToolStripButton("Ajustar líneas") { CheckOnClick = true, Checked = settings.ViewerWordWrap };
        wrap.CheckedChanged += (_, _) => { _text.WordWrap = wrap.Checked; _text.ScrollBars = wrap.Checked ? ScrollBars.Vertical : ScrollBars.Both; };
        var find = new ToolStripButton("Buscar (Ctrl+F)") ;
        find.Click += (_, _) => FindText();
        var copy = new ToolStripButton("Copiar");
        copy.Click += (_, _) => { if (_text.SelectionLength > 0) Clipboard.SetText(_text.SelectedText); else if (_text.Text.Length > 0) Clipboard.SetText(_text.Text); };
        tools.Items.AddRange(new ToolStripItem[] { enc, hex, wrap, find, copy, new ToolStripSeparator(), _status });

        if (isImage)
        {
            try
            {
                using var ms = new MemoryStream(data);
                _pic = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, Image = Image.FromStream(ms) };
                Controls.Add(_pic);
                _status.Text = $"Imagen {_pic.Image.Width}×{_pic.Image.Height}";
                Controls.Add(tools);
                tools.Items.Clear();
                tools.Items.Add(_status);
                return;
            }
            catch { _pic?.Dispose(); _pic = null; }
        }
        Controls.Add(_text);
        Controls.Add(tools);
        Render();
        KeyDown += (_, e) => { if (e.Control && e.KeyCode == Keys.F) { FindText(); e.Handled = true; } if (e.KeyCode == Keys.Escape) Close(); };
    }

    protected override void OnShown(EventArgs e) { base.OnShown(e); Theme.Style(this); }

    private void Render()
    {
        int limit = Math.Max(16, _settings.ViewerMaxKb) * 1024;
        var slice = _data.Length > limit ? _data[..limit] : _data;
        _text.Text = _hex ? HexDump(slice) : _encoding.GetString(slice).Replace("\r\n", "\n").Replace("\n", "\r\n");
        _status.Text = $"{UI.Size(_data.Length)}" + (_data.Length > limit || _truncated ? $" · mostrando los primeros {UI.Size(slice.Length)}" : "") + (_hex ? " · hex" : $" · {_encoding.EncodingName}");
        _text.SelectionStart = 0;
        _text.SelectionLength = 0;
    }

    public static string HexDump(byte[] d)
    {
        var sb = new StringBuilder(d.Length * 4);
        for (int i = 0; i < d.Length; i += 16)
        {
            sb.Append(i.ToString("X8")).Append("  ");
            for (int j = 0; j < 16; j++)
            {
                if (i + j < d.Length) sb.Append(d[i + j].ToString("X2")).Append(' '); else sb.Append("   ");
                if (j == 7) sb.Append(' ');
            }
            sb.Append(' ');
            for (int j = 0; j < 16 && i + j < d.Length; j++) { byte b = d[i + j]; sb.Append(b is >= 32 and < 127 ? (char)b : '·'); }
            sb.Append("\r\n");
        }
        return sb.ToString();
    }

    private int _findPos;
    private void FindText()
    {
        string? q = InputDialog.Ask("Texto a buscar:", "Buscar", "");
        if (string.IsNullOrEmpty(q)) return;
        int i = _text.Text.IndexOf(q, Math.Min(_findPos, _text.Text.Length), StringComparison.OrdinalIgnoreCase);
        if (i < 0) i = _text.Text.IndexOf(q, 0, StringComparison.OrdinalIgnoreCase);
        if (i < 0) { UI.Info(this, "No se encontró el texto."); return; }
        _text.Focus();
        _text.Select(i, q.Length);
        _text.ScrollToCaret();
        _findPos = i + q.Length;
    }

    /// <summary>Muestra un texto largo (informes, resultados) en una ventana del visor.</summary>
    public static void ShowText(IWin32Window? owner, string title, string text, AppSettings settings)
    {
        using var f = new ViewerForm(title, new UTF8Encoding(false).GetBytes(text), settings);
        f.ShowDialog(owner);
    }
}
