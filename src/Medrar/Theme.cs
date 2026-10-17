using System.Runtime.InteropServices;
using Medrar.Core;
using Microsoft.Win32;

namespace Medrar.App;

/// <summary>Tema claro/oscuro: colores propios y renderizador de barras para que todo se vea coherente.</summary>
internal static class Theme
{
    public static bool IsDark { get; private set; }
    public static Color Back = SystemColors.Control;
    public static Color PanelBack = SystemColors.Control;
    public static Color Fore = SystemColors.ControlText;
    public static Color Muted = SystemColors.GrayText;
    public static Color Border = SystemColors.ControlDark;
    public static Color ListBack = SystemColors.Window;
    public static Color Accent = Color.FromArgb(0x13, 0x8D, 0x90);
    public static Color Selection = SystemColors.Highlight;

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subApp, string? subIdList);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static bool SystemUsesDark()
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return k?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    public static void Apply(ThemeMode mode)
    {
        IsDark = mode == ThemeMode.Dark || (mode == ThemeMode.System && SystemUsesDark());
        Application.SetColorMode(IsDark ? SystemColorMode.Dark : SystemColorMode.Classic);
        if (IsDark)
        {
            Back = Color.FromArgb(0x20, 0x20, 0x20);
            PanelBack = Color.FromArgb(0x2B, 0x2B, 0x2B);
            Fore = Color.FromArgb(0xEE, 0xEE, 0xEE);
            Muted = Color.FromArgb(0xA0, 0xA0, 0xA0);
            Border = Color.FromArgb(0x4A, 0x4A, 0x4A);
            ListBack = Color.FromArgb(0x1B, 0x1B, 0x1B);
            Selection = Color.FromArgb(0x1F, 0x5F, 0x73);
        }
        else
        {
            Back = Color.FromArgb(0xF3, 0xF3, 0xF3);
            PanelBack = Color.White;
            Fore = Color.FromArgb(0x1B, 0x1B, 0x1B);
            Muted = Color.FromArgb(0x66, 0x66, 0x66);
            Border = Color.FromArgb(0xD0, 0xD0, 0xD0);
            ListBack = Color.White;
            Selection = Color.FromArgb(0xCC, 0xE8, 0xEA);
        }
    }

    /// <summary>Aplica el tema a un formulario y a todos sus controles.</summary>
    public static void Style(Control root)
    {
        if (root is Form f)
        {
            int dark = IsDark ? 1 : 0;
            try { DwmSetWindowAttribute(f.Handle, 20, ref dark, 4); } catch { }
        }
        StyleRec(root);
    }

    private static void StyleRec(Control c)
    {
        switch (c)
        {
            case Form:
                c.BackColor = Back; c.ForeColor = Fore; break;
            case ListView lv:
                lv.BackColor = ListBack; lv.ForeColor = Fore;
                if (lv.IsHandleCreated) SetWindowTheme(lv.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
                break;
            case TreeView tv:
                tv.BackColor = ListBack; tv.ForeColor = Fore;
                if (tv.IsHandleCreated) SetWindowTheme(tv.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
                break;
            case TextBox or RichTextBox or ComboBox or NumericUpDown or ListBox:
                c.BackColor = ListBack; c.ForeColor = Fore; break;
            case ToolStrip ts:
                ts.Renderer = new ThemedRenderer();
                ts.BackColor = PanelBack; ts.ForeColor = Fore;
                foreach (ToolStripItem it in ts.Items) it.ForeColor = Fore;
                break;
            case TabControl or TabPage or Panel or GroupBox or CheckBox or RadioButton or Label or TableLayoutPanel or FlowLayoutPanel or SplitContainer:
                c.ForeColor = Fore;
                if (c is TabPage || c is Panel || c is TableLayoutPanel || c is FlowLayoutPanel || c is GroupBox) c.BackColor = Color.Transparent;
                break;
        }
        foreach (Control child in c.Controls) StyleRec(child);
    }

    public static void StyleMenu(ToolStripItemCollection items)
    {
        foreach (ToolStripItem i in items)
        {
            i.ForeColor = Fore;
            if (i is ToolStripMenuItem m) StyleMenu(m.DropDownItems);
        }
    }

    private sealed class ThemedRenderer : ToolStripProfessionalRenderer
    {
        public ThemedRenderer() : base(new Colors()) { RoundedEdges = false; }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }
    }

    private sealed class Colors : ProfessionalColorTable
    {
        public Colors() { UseSystemColors = false; }
        public override Color ToolStripGradientBegin => PanelBack;
        public override Color ToolStripGradientMiddle => PanelBack;
        public override Color ToolStripGradientEnd => PanelBack;
        public override Color MenuStripGradientBegin => PanelBack;
        public override Color MenuStripGradientEnd => PanelBack;
        public override Color ToolStripDropDownBackground => IsDark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : Color.White;
        public override Color ImageMarginGradientBegin => ToolStripDropDownBackground;
        public override Color ImageMarginGradientMiddle => ToolStripDropDownBackground;
        public override Color ImageMarginGradientEnd => ToolStripDropDownBackground;
        public override Color MenuItemSelected => Selection;
        public override Color MenuItemSelectedGradientBegin => Selection;
        public override Color MenuItemSelectedGradientEnd => Selection;
        public override Color MenuItemPressedGradientBegin => Selection;
        public override Color MenuItemPressedGradientEnd => Selection;
        public override Color MenuItemBorder => Selection;
        public override Color MenuBorder => Border;
        public override Color ButtonSelectedHighlight => Selection;
        public override Color ButtonSelectedGradientBegin => Selection;
        public override Color ButtonSelectedGradientMiddle => Selection;
        public override Color ButtonSelectedGradientEnd => Selection;
        public override Color ButtonPressedGradientBegin => Accent;
        public override Color ButtonPressedGradientMiddle => Accent;
        public override Color ButtonPressedGradientEnd => Accent;
        public override Color ButtonSelectedBorder => Accent;
        public override Color ButtonPressedBorder => Accent;
        public override Color SeparatorDark => Border;
        public override Color SeparatorLight => Border;
        public override Color StatusStripGradientBegin => PanelBack;
        public override Color StatusStripGradientEnd => PanelBack;
        public override Color GripDark => Border;
        public override Color GripLight => Border;
    }
}
