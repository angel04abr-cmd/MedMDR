using Medrar.Core;

namespace Medrar.App;

/// <summary>Atajos para construir ventanas por código (sin diseñador) con un aspecto uniforme.</summary>
internal static class UI
{
    public static Padding Pad => new(12);

    public static Label Label(string text, bool bold = false, bool muted = false)
    {
        var l = new Label { Text = text, AutoSize = true, Margin = new Padding(0, 6, 6, 2), Anchor = AnchorStyles.Left };
        if (bold) l.Font = new Font(l.Font, FontStyle.Bold);
        if (muted) l.ForeColor = Theme.Muted;
        return l;
    }

    public static Button Button(string text, EventHandler? click = null, int width = 96, DialogResult result = DialogResult.None)
    {
        var b = new FluentButton { Text = text, Width = width, Height = 32, Margin = new Padding(4, 4, 0, 4), DialogResult = result };
        if (click != null) b.Click += click;
        return b;
    }

    public static CheckBox Check(string text, bool value = false) =>
        new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 3, 6, 3) };

    public static RadioButton Radio(string text, bool value = false) =>
        new() { Text = text, Checked = value, AutoSize = true, Margin = new Padding(0, 3, 6, 3) };

    public static TextBox Text(string value = "", int width = 260) =>
        new() { Text = value, Width = width, Margin = new Padding(0, 3, 6, 3) };

    public static ComboBox Combo(string[] items, int selected = 0, int width = 200, bool dropDownList = true)
    {
        var c = new ComboBox { Width = width, DropDownStyle = dropDownList ? ComboBoxStyle.DropDownList : ComboBoxStyle.DropDown, Margin = new Padding(0, 3, 6, 3) };
        c.Items.AddRange(items);
        if (items.Length > 0) c.SelectedIndex = Math.Clamp(selected, 0, items.Length - 1);
        return c;
    }

    public static FlowLayoutPanel Row(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0) };
        f.Controls.AddRange(controls);
        return f;
    }

    public static FlowLayoutPanel Column(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, FlowDirection = FlowDirection.TopDown, Margin = new Padding(0) };
        f.Controls.AddRange(controls);
        return f;
    }

    /// <summary>Sección con título en negrita (más fiable en pantalla que un GroupBox).</summary>
    public static Control Group(string title, Control content)
    {
        var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false, Margin = new Padding(0, 6, 12, 6) };
        f.Controls.Add(Label(title, bold: true));
        content.Margin = new Padding(14, 0, 0, 0);
        f.Controls.Add(content);
        return f;
    }

    public static TabPage Tab(string title, Control content)
    {
        var t = new TabPage(title) { Padding = new Padding(12), UseVisualStyleBackColor = true };
        content.Dock = DockStyle.Fill;
        t.Controls.Add(content);
        return t;
    }

    public static FlowLayoutPanel Page(params Control[] controls)
    {
        var f = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Dock = DockStyle.Fill };
        f.Controls.AddRange(controls);
        return f;
    }

    public static string? PickFolder(IWin32Window owner, string? start = null, string description = "Elige una carpeta")
    {
        using var d = new FolderBrowserDialog { Description = description, UseDescriptionForTitle = true, InitialDirectory = start ?? "", ShowNewFolderButton = true };
        return d.ShowDialog(owner) == DialogResult.OK ? d.SelectedPath : null;
    }

    public static void Info(IWin32Window? owner, string text, string title = "Medrar") =>
        MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Warn(IWin32Window? owner, string text, string title = "Medrar") =>
        MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public static void Error(IWin32Window? owner, string text, string title = "Medrar") =>
        MessageBox.Show(owner, text, title, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static bool Confirm(IWin32Window? owner, string text, string title = "Medrar") =>
        MessageBox.Show(owner, text, title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;

    /// <summary>Mensaje de error entendible para el usuario.</summary>
    public static string Describe(Exception ex) => ex switch
    {
        MedrarException m => m.Message,
        UnauthorizedAccessException => "No hay permiso para acceder a esa ubicación. Prueba con otra carpeta o ejecuta Medrar como administrador.",
        DirectoryNotFoundException or FileNotFoundException => "No se encuentra el fichero o la carpeta: " + ex.Message,
        IOException io when io.Message.Contains("espacio", StringComparison.OrdinalIgnoreCase) || io.Message.Contains("space", StringComparison.OrdinalIgnoreCase)
            => "No hay espacio suficiente en el disco.",
        IOException io => "Error al leer o escribir un fichero: " + io.Message,
        _ => ex.Message
    };

    public static string Size(long bytes) => ArchiveReport.FormatSize(bytes);
}

/// <summary>Base de los cuadros de diálogo: tamaño fijo, tema y botón Aceptar/Cancelar.</summary>
internal class DialogBase : Form
{
    protected DialogBase(string title)
    {
        Text = title;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Icon = AppIcons.App;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = Fluent.UiFont;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.Style(this);
    }

    /// <summary>Pie estándar con Aceptar y Cancelar.</summary>
    protected FlowLayoutPanel Footer(string ok = "Aceptar", string cancel = "Cancelar", EventHandler? okClick = null)
    {
        var okBtn = UI.Button(ok, okClick, 100, okClick == null ? DialogResult.OK : DialogResult.None);
        var cancelBtn = UI.Button(cancel, null, 100, DialogResult.Cancel);
        AcceptButton = okBtn;
        CancelButton = cancelBtn;
        var f = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Bottom, Height = 46, Padding = new Padding(8, 6, 8, 6) };
        f.Controls.Add(cancelBtn);
        f.Controls.Add(okBtn);
        return f;
    }
}

internal static class AppIcons
{
    private static Icon? _app;
    public static Icon App => _app ??= Icons.AppIcon(32);
}
