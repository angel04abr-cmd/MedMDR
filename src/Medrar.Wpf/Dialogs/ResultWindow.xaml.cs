using System.Windows;

namespace Medrar.Wpf;

/// <summary>Lista de resultados (errores de una extracción, resultado de una comprobación…).</summary>
public partial class ResultWindow : Window
{
    private readonly string[] _lines;

    public ResultWindow(string title, string headline, IEnumerable<string> lines, bool ok)
    {
        InitializeComponent();
        DevTools.Hook(this);
        Title = title;
        Head.Text = headline;
        Glyph.Kind = ok ? IconKind.Success : IconKind.Warning;
        _lines = lines.ToArray();
        Lines.ItemsSource = _lines;
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Head.Text + Environment.NewLine + string.Join(Environment.NewLine, _lines)); } catch { /* portapapeles ocupado */ }
    }

    public static void Show(Window? owner, string title, string headline, IEnumerable<string> lines, bool ok)
    {
        var w = new ResultWindow(title, headline, lines, ok);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
    }
}
