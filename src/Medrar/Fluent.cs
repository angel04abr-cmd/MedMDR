using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Medrar.App;

/// <summary>Piezas de estilo «Fluent» (Windows 11) para Windows Forms: colores, ventana, botones, lista y menús propios.</summary>
internal static class Fluent
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33,
                      DWMWA_BORDER_COLOR = 34, DWMWA_CAPTION_COLOR = 35, DWMWA_TEXT_COLOR = 36;

    public static bool IsWindows11 => Environment.OSVersion.Version.Build >= 22000;

    // ------------------------------------------------------------------ tipografía y medidas

    private static Font? _font;
    /// <summary>Segoe UI Variable (Windows 11) o, si no está, la fuente de mensajes del sistema.</summary>
    public static Font UiFont => _font ??= MakeFont();

    private static Font MakeFont()
    {
        try
        {
            using var fam = new FontFamily("Segoe UI Variable Text");
            return new Font(fam, 9f);
        }
        catch { return SystemFonts.MessageBoxFont ?? new Font("Segoe UI", 9f); }
    }

    public static int Px(Control c, int logical) => (int)Math.Round(logical * c.DeviceDpi / 96.0);

    // ------------------------------------------------------------------ colores (dependen del tema claro/oscuro actual)

    private static Color? _accent;
    /// <summary>Color de énfasis de Windows (el que eligió el usuario); un verde azulado si no se puede leer.</summary>
    public static Color Accent => _accent ??= ReadAccent();

    private static Color ReadAccent()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
            if (k?.GetValue("AccentColor") is int v)       // 0xAABBGGRR
                return Color.FromArgb(v & 0xFF, (v >> 8) & 0xFF, (v >> 16) & 0xFF);
        }
        catch { }
        return Color.FromArgb(0x13, 0x8D, 0x90);
    }

    public static Color Blend(Color top, Color bottom, double amount) => Color.FromArgb(
        (int)(bottom.R + (top.R - bottom.R) * amount), (int)(bottom.G + (top.G - bottom.G) * amount), (int)(bottom.B + (top.B - bottom.B) * amount));

    public static Color Hover => Blend(Theme.Fore, Theme.ListBack, Theme.IsDark ? 0.09 : 0.06);
    public static Color Pressed => Blend(Theme.Fore, Theme.ListBack, Theme.IsDark ? 0.14 : 0.10);
    public static Color SelectionFill => Blend(Accent, Theme.ListBack, Theme.IsDark ? 0.34 : 0.18);
    public static Color SelectionInactive => Blend(Theme.Fore, Theme.ListBack, Theme.IsDark ? 0.14 : 0.09);
    public static Color ControlFill => Theme.IsDark ? Color.FromArgb(0x3A, 0x3A, 0x3A) : Color.White;
    public static Color Stroke => Theme.IsDark ? Color.FromArgb(0x55, 0x55, 0x55) : Color.FromArgb(0xD6, 0xD6, 0xD6);

    public static Color OnAccent(Color accent) => (0.299 * accent.R + 0.587 * accent.G + 0.114 * accent.B) > 150 ? Color.Black : Color.White;

    // ------------------------------------------------------------------ ventana

    /// <summary>Barra de título del color del tema, bordes redondeados y modo oscuro de la ventana (Windows 11).</summary>
    public static void ApplyWindow(Form f)
    {
        if (!f.IsHandleCreated) return;
        IntPtr h = f.Handle;
        int dark = Theme.IsDark ? 1 : 0;
        Set(h, DWMWA_USE_IMMERSIVE_DARK_MODE, dark);
        if (!IsWindows11) return;
        Set(h, DWMWA_WINDOW_CORNER_PREFERENCE, 2);                 // redondeadas
        Set(h, DWMWA_CAPTION_COLOR, Colorref(Theme.Back));
        Set(h, DWMWA_TEXT_COLOR, Colorref(Theme.Fore));
        Set(h, DWMWA_BORDER_COLOR, Colorref(Theme.Border));
    }

    /// <summary>Esquinas redondeadas para los menús desplegables (ventanas emergentes).</summary>
    public static void RoundPopup(ToolStripDropDown dd)
    {
        if (!IsWindows11) return;
        void Apply() { if (dd.IsHandleCreated) { Set(dd.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, 2); } }
        dd.HandleCreated += (_, _) => Apply();
        Apply();
    }

    private static void Set(IntPtr h, int attr, int value) { try { DwmSetWindowAttribute(h, attr, ref value, sizeof(int)); } catch { } }
    private static int Colorref(Color c) => c.R | (c.G << 8) | (c.B << 16);

    // ------------------------------------------------------------------ dibujo

    public static GraphicsPath Rounded(Rectangle r, int radius)
    {
        int d = Math.Max(1, radius * 2);
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRounded(Graphics g, Rectangle r, int radius, Color fill)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(r, radius);
        using var b = new SolidBrush(fill);
        g.FillPath(b, path);
        g.SmoothingMode = old;
    }

    public static void StrokeRounded(Graphics g, Rectangle r, int radius, Color color, float width = 1f)
    {
        var old = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = Rounded(r, radius);
        using var p = new Pen(color, width);
        g.DrawPath(p, path);
        g.SmoothingMode = old;
    }
}

/// <summary>Botón Fluent: esquinas redondeadas, estados al pasar y pulsar, y el predeterminado con el color de énfasis.</summary>
internal class FluentButton : Button
{
    private bool _hover, _down;

    public FluentButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        Cursor = Cursors.Default;
    }

    private bool IsAccent => FindForm()?.AcceptButton == this && Enabled;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    private Color Behind()
    {
        for (Control? c = Parent; c != null; c = c.Parent) if (c.BackColor.A == 255) return c.BackColor;
        return Theme.Back;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Behind());
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Fluent.Px(this, 4);
        bool accent = IsAccent;
        Color fill, text;
        if (!Enabled) { fill = Fluent.Blend(Theme.Fore, Theme.Back, 0.05); text = Theme.Muted; }
        else if (accent)
        {
            var a = Fluent.Accent;
            fill = _down ? Fluent.Blend(Color.Black, a, 0.18) : _hover ? Fluent.Blend(Color.White, a, 0.12) : a;
            text = Fluent.OnAccent(a);
        }
        else
        {
            fill = _down ? Fluent.Blend(Theme.Fore, Fluent.ControlFill, 0.10) : _hover ? Fluent.Blend(Theme.Fore, Fluent.ControlFill, 0.05) : Fluent.ControlFill;
            text = Theme.Fore;
        }
        Fluent.FillRounded(g, r, radius, fill);
        if (!accent) Fluent.StrokeRounded(g, r, radius, Fluent.Stroke);
        if (Focused && ShowFocusCues) Fluent.StrokeRounded(g, Rectangle.Inflate(r, -2, -2), Math.Max(1, radius - 1), accent ? text : Fluent.Accent, 1.5f);

        var content = new Rectangle(4, 0, Width - 8, Height);
        if (Image != null && string.IsNullOrEmpty(Text))
            g.DrawImage(Image, (Width - Image.Width) / 2, (Height - Image.Height) / 2, Image.Width, Image.Height);
        else
            TextRenderer.DrawText(g, Text.Replace("&", ""), Font, content, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}

/// <summary>Panel con borde redondeado para alojar un cuadro de texto (estilo Fluent: la línea inferior toma el color de énfasis al escribir).</summary>
internal sealed class RoundedPanel : Panel
{
    public int Radius { get; set; } = 6;

    public RoundedPanel() => SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

    protected override void OnControlAdded(ControlEventArgs e)
    {
        base.OnControlAdded(e);
        e.Control!.GotFocus += (_, _) => Invalidate();
        e.Control.LostFocus += (_, _) => Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        Color behind = Theme.Back;
        for (Control? c = Parent; c != null; c = c.Parent) if (c.BackColor.A == 255) { behind = c.BackColor; break; }
        g.Clear(behind);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        int radius = Fluent.Px(this, Radius);
        Fluent.FillRounded(g, r, radius, Theme.ListBack);
        Fluent.StrokeRounded(g, r, radius, Fluent.Stroke);
        if (ContainsFocus)
        {
            var old = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var p = new Pen(Fluent.Accent, 2f);
            g.DrawLine(p, radius, Height - 2, Width - radius - 1, Height - 2);
            g.SmoothingMode = old;
        }
    }
}

/// <summary>Elemento de lista que recuerda qué icono de <see cref="FluentListView.IconSource"/> le toca.</summary>
internal sealed class IconItem : ListViewItem
{
    public int Icon { get; }
    public IconItem(string text, int icon) : base(text) => Icon = icon;
}

/// <summary>
/// Lista Fluent: filas altas con la selección como «píldora» redondeada, resaltado al pasar el ratón, cabecera plana con
/// flecha de orden, y la última columna ocupa el ancho sobrante. Los iconos se toman de <see cref="IconSource"/>.
/// </summary>
internal sealed class FluentListView : ListView
{
    [DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr hwnd, int bar, bool show);

    private int _hot = -1;
    private bool _fitting;
    private readonly ImageList _spacer = new() { ColorDepth = ColorDepth.Depth32Bit };

    public ImageList? IconSource { get; set; }
    public int SortColumn { get; set; } = -1;
    public bool SortAscending { get; set; } = true;

    public FluentListView()
    {
        OwnerDraw = true;
        DoubleBuffered = true;
        View = View.Details;
        FullRowSelect = true;
        SmallImageList = _spacer;      // solo fija el alto de las filas; los iconos los dibujamos nosotros
        DrawColumnHeader += OnDrawHeader;
        DrawItem += OnDrawItem;
        DrawSubItem += OnDrawSubItem;
        Resize += (_, _) => FitLastColumn();
        ClientSizeChanged += (_, _) => FitLastColumn();
        ColumnWidthChanged += (_, _) => FitLastColumn();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        _spacer.ImageSize = new Size(1, Fluent.Px(this, 30));
        base.OnHandleCreated(e);
        BeginInvoke(FitLastColumn);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        _spacer.ImageSize = new Size(1, Fluent.Px(this, 30));
    }

    /// <summary>Hace que la última columna ocupe el ancho que sobra (llamar tras rellenar la lista: puede aparecer la barra vertical).</summary>
    public void FitColumns() => FitLastColumn();

    private void FitLastColumn()
    {
        if (_fitting || Columns.Count == 0 || !IsHandleCreated) return;
        _fitting = true;
        try
        {
            int others = 0;
            for (int i = 0; i < Columns.Count - 1; i++) others += Columns[i].Width;
            int w = Math.Max(Fluent.Px(this, 70), ClientSize.Width - others - 2);   // 2 px de margen: evita la barra horizontal por redondeos
            if (Columns[^1].Width != w) Columns[^1].Width = w;
        }
        finally { _fitting = false; }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int idx = HitTest(e.Location).Item?.Index ?? -1;
        if (idx == _hot) return;
        int old = _hot; _hot = idx;
        if (old >= 0 && old < Items.Count) RedrawItems(old, old, false);
        if (_hot >= 0) RedrawItems(_hot, _hot, false);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        int old = _hot; _hot = -1;
        if (old >= 0 && old < Items.Count) RedrawItems(old, old, false);
    }

    // ------------------------------------------------------------------ dibujo

    private void OnDrawHeader(object? sender, DrawListViewColumnHeaderEventArgs e)
    {
        var g = e.Graphics;
        using (var b = new SolidBrush(Theme.ListBack)) g.FillRectangle(b, e.Bounds);
        using (var p = new Pen(Theme.Border)) g.DrawLine(p, e.Bounds.Left, e.Bounds.Bottom - 1, e.Bounds.Right, e.Bounds.Bottom - 1);
        var text = new Rectangle(e.Bounds.X + Fluent.Px(this, 10), e.Bounds.Y, e.Bounds.Width - Fluent.Px(this, 20), e.Bounds.Height);
        var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix |
                    (e.Header!.TextAlign == HorizontalAlignment.Right ? TextFormatFlags.Right : TextFormatFlags.Left);
        if (e.ColumnIndex == SortColumn)
        {
            // flecha de orden (chevrón) al lado del título
            int cx = e.Header.TextAlign == HorizontalAlignment.Right ? text.X + Fluent.Px(this, 6) : text.Right - Fluent.Px(this, 8);
            int cy = e.Bounds.Y + e.Bounds.Height / 2, s = Fluent.Px(this, 3);
            var old = g.SmoothingMode; g.SmoothingMode = SmoothingMode.AntiAlias;
            using var pen = new Pen(Theme.Muted, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            if (SortAscending) g.DrawLines(pen, new[] { new Point(cx - s, cy + s / 2), new Point(cx, cy - s / 2), new Point(cx + s, cy + s / 2) });
            else g.DrawLines(pen, new[] { new Point(cx - s, cy - s / 2), new Point(cx, cy + s / 2), new Point(cx + s, cy - s / 2) });
            g.SmoothingMode = old;
            if (e.Header.TextAlign == HorizontalAlignment.Right) text.X += Fluent.Px(this, 12); else text.Width -= Fluent.Px(this, 14);
        }
        TextRenderer.DrawText(g, e.Header.Text, Font, text, Theme.Muted, flags);
    }

    private void OnDrawItem(object? sender, DrawListViewItemEventArgs e)
    {
        e.DrawDefault = false;
        PaintRow(e.Graphics, e.Item);
    }

    // Las celdas se pintan todas juntas en DrawItem (la fila entera); DrawSubItem no hace nada más.
    private void OnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e) => e.DrawDefault = false;

    private void PaintRow(Graphics g, ListViewItem item)
    {
        var entire = item.GetBounds(ItemBoundsPortion.Entire);
        var row = new Rectangle(0, entire.Y, ClientSize.Width, entire.Height);
        using (var bg = new SolidBrush(Theme.ListBack)) g.FillRectangle(bg, row);
        var pill = Rectangle.Inflate(row, -Fluent.Px(this, 4), -Fluent.Px(this, 2));
        if (item.Selected) Fluent.FillRounded(g, pill, Fluent.Px(this, 5), Focused ? Fluent.SelectionFill : Fluent.SelectionInactive);
        else if (item.Index == _hot) Fluent.FillRounded(g, pill, Fluent.Px(this, 5), Fluent.Hover);

        int pad = Fluent.Px(this, 10);
        for (int col = 0; col < item.SubItems.Count && col < Columns.Count; col++)
        {
            var bounds = col == 0 ? new Rectangle(entire.X, entire.Y, Columns[0].Width, entire.Height) : item.SubItems[col].Bounds;
            int textX = bounds.X + pad;
            if (col == 0)
            {
                int idx = (item as IconItem)?.Icon ?? -1;
                int size = Fluent.Px(this, 16);
                textX = bounds.X + Fluent.Px(this, 14);
                if (IconSource != null && idx >= 0 && idx < IconSource.Images.Count)
                {
                    g.DrawImage(IconSource.Images[idx], new Rectangle(textX, bounds.Y + (bounds.Height - size) / 2, size, size));
                    textX += size + Fluent.Px(this, 8);
                }
            }
            bool right = Columns[col].TextAlign == HorizontalAlignment.Right;
            var rect = new Rectangle(textX, bounds.Y, bounds.Right - textX - (right ? pad : Fluent.Px(this, 4)), bounds.Height);
            TextRenderer.DrawText(g, item.SubItems[col].Text, Font, rect, col == 0 || item.Selected ? Theme.Fore : Theme.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | (right ? TextFormatFlags.Right : TextFormatFlags.Left));
        }
    }

    // En tema oscuro Windows pinta encima su propia selección (rectángulo azul con borde): se repinta la fila seleccionada a continuación.
    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg == 0x0005 /* WM_SIZE */ && IsHandleCreated) ShowScrollBar(Handle, 0, false);   // la última columna siempre cabe: sin barra horizontal
        if (m.Msg == 0x000F /* WM_PAINT */ && Theme.IsDark && IsHandleCreated && SelectedItems.Count > 0)
        {
            using var g = Graphics.FromHwnd(Handle);
            foreach (ListViewItem it in SelectedItems)
                if (it.GetBounds(ItemBoundsPortion.Entire).IntersectsWith(ClientRectangle)) PaintRow(g, it);
        }
    }
}

/// <summary>Pestañas Fluent: títulos planos, con resalte al pasar el ratón y una línea de énfasis bajo la pestaña activa.</summary>
internal sealed class FluentTabControl : TabControl
{
    private int _hot = -1;

    public FluentTabControl()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        Padding = new Point(9, 6);
        ItemSize = new Size(0, 34);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hot = -1;
        for (int i = 0; i < TabCount; i++) if (GetTabRect(i).Contains(e.Location)) { hot = i; break; }
        if (hot != _hot) { _hot = hot; Invalidate(); }
    }

    protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); if (_hot != -1) { _hot = -1; Invalidate(); } }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Theme.Back);
        if (TabCount == 0) return;
        int bottom = GetTabRect(0).Bottom;
        using (var p = new Pen(Theme.Border)) g.DrawLine(p, 0, bottom + 1, Width, bottom + 1);
        for (int i = 0; i < TabCount; i++)
        {
            var r = GetTabRect(i);
            bool selected = i == SelectedIndex;
            var pill = new Rectangle(r.X + 2, r.Y + 3, r.Width - 4, r.Height - 6);
            if (!selected && i == _hot) Fluent.FillRounded(g, pill, Fluent.Px(this, 5), Fluent.Hover);
            TextRenderer.DrawText(g, TabPages[i].Text, Font, r, selected ? Theme.Fore : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            if (selected)
            {
                int w = Math.Min(r.Width - Fluent.Px(this, 24), Fluent.Px(this, 36));
                Fluent.FillRounded(g, new Rectangle(r.X + (r.Width - w) / 2, bottom - Fluent.Px(this, 3), w, Fluent.Px(this, 3)), 1, Fluent.Accent);
            }
        }
    }
}

/// <summary>Barras y menús: fondo liso, botones y opciones con resalte redondeado, sin margen de iconos ni degradados.</summary>
internal sealed class FluentRenderer : ToolStripProfessionalRenderer
{
    public FluentRenderer() : base(new FluentColors()) { RoundedEdges = false; }

    private static Color BackFor(ToolStrip ts) => ts is ToolStripDropDown ? Theme.PanelBack : Theme.Back;

    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
    {
        using var b = new SolidBrush(BackFor(e.ToolStrip));
        e.Graphics.FillRectangle(b, e.AffectedBounds);
    }

    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }
    protected override void OnRenderStatusStripSizingGrip(ToolStripRenderEventArgs e) { }

    protected override void OnRenderButtonBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Enabled) return;
        bool on = item.Selected || item.Pressed || (item is ToolStripButton { Checked: true });
        if (!on) return;
        var r = new Rectangle(1, 1, item.Width - 3, item.Height - 3);
        Fluent.FillRounded(e.Graphics, r, 6, item.Pressed ? Fluent.Pressed : Fluent.Hover);
    }

    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        var item = e.Item;
        if (!item.Enabled || !(item.Selected || item.Pressed)) return;
        bool top = !item.IsOnDropDown;
        var r = top ? new Rectangle(1, 2, item.Width - 3, item.Height - 5) : new Rectangle(4, 1, item.Width - 8, item.Height - 3);
        Fluent.FillRounded(e.Graphics, r, top ? 5 : 4, item.Pressed && top ? Fluent.Pressed : Fluent.Hover);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var p = new Pen(Theme.Border);
        if (e.Vertical) { int x = e.Item.Width / 2; e.Graphics.DrawLine(p, x, 4, x, e.Item.Height - 4); }
        else { int y = e.Item.Height / 2; e.Graphics.DrawLine(p, 10, y, e.Item.Width - 10, y); }
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Theme.Fore : Theme.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = Theme.Fore;
        base.OnRenderArrow(e);
    }

    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        var r = e.ImageRectangle;
        using var p = new Pen(Theme.Fore, 1.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        var old = e.Graphics.SmoothingMode; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.DrawLines(p, new[] { new Point(r.X + 3, r.Y + r.Height / 2), new Point(r.X + r.Width / 2 - 1, r.Bottom - 5), new Point(r.Right - 3, r.Y + 5) });
        e.Graphics.SmoothingMode = old;
    }

    private sealed class FluentColors : ProfessionalColorTable
    {
        public FluentColors() { UseSystemColors = false; }
        public override Color ToolStripDropDownBackground => Theme.PanelBack;
        public override Color ImageMarginGradientBegin => Theme.PanelBack;
        public override Color ImageMarginGradientMiddle => Theme.PanelBack;
        public override Color ImageMarginGradientEnd => Theme.PanelBack;
        public override Color MenuBorder => Theme.Border;
        public override Color MenuItemBorder => Color.Transparent;
        public override Color SeparatorDark => Theme.Border;
        public override Color SeparatorLight => Theme.Border;
    }
}
