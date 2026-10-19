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
        if (root is Form f) Fluent.ApplyWindow(f);
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
                // La lista Fluent dibuja ella misma las filas; el tema «Explorer» pintaría encima su propia selección azul.
                if (lv.IsHandleCreated) SetWindowTheme(lv.Handle, lv is FluentListView ? (IsDark ? "DarkMode_Explorer" : "") : (IsDark ? "DarkMode_Explorer" : "Explorer"), null);
                break;
            case TreeView tv:
                tv.BackColor = ListBack; tv.ForeColor = Fore;
                if (tv.IsHandleCreated) SetWindowTheme(tv.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
                break;
            case ComboBox cb:
                cb.FlatStyle = FlatStyle.Flat; cb.BackColor = ListBack; cb.ForeColor = Fore; break;
            case TextBox or RichTextBox or NumericUpDown or ListBox:
                c.BackColor = ListBack; c.ForeColor = Fore; break;
            case ToolStrip ts:
                ts.Renderer = new FluentRenderer();
                ts.BackColor = ts is ToolStripDropDown ? PanelBack : Back; ts.ForeColor = Fore;
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
            if (i.Owner is ToolStripDropDown owner)
            {
                owner.Renderer = new FluentRenderer();
                owner.BackColor = PanelBack; owner.ForeColor = Fore;
                if (owner.Tag as string != "redondo") { owner.Tag = "redondo"; Fluent.RoundPopup(owner); }
            }
            i.ForeColor = Fore;
            if (i is ToolStripMenuItem m) StyleMenu(m.DropDownItems);
        }
    }
}

