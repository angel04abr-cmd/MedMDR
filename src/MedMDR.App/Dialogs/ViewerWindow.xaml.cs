using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Visor interno: texto con varias codificaciones, vista hexadecimal e imágenes.</summary>
public partial class ViewerWindow : Window
{
    private readonly byte[] _data;
    private readonly AppSettings _settings;
    private readonly bool _truncated;
    private Encoding _encoding = new UTF8Encoding(false);
    private bool _hex;
    private int _findPos;

    public ViewerWindow(string name, byte[] data, AppSettings settings, bool truncated = false)
    {
        InitializeComponent();
        DevTools.Hook(this);
        _data = data; _settings = settings; _truncated = truncated;
        Title = $"{name} — Visor de {AppInfo.Name}";
        Text.FontFamily = new FontFamily(settings.ViewerFontName + ", Consolas");
        Text.FontSize = Math.Max(8, settings.ViewerFontSize * 96 / 72);
        WrapToggle.IsChecked = settings.ViewerWordWrap;
        Text.TextWrapping = settings.ViewerWordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;

        foreach (var (label, make) in new (string, Func<Encoding>)[]
        {
            ("UTF-8", () => new UTF8Encoding(false)), ("Windows (ANSI)", () => Encoding.Latin1), ("Unicode (UTF-16)", () => Encoding.Unicode),
            ("DOS (CP437/850)", () => { try { return Encoding.GetEncoding(850); } catch { return Encoding.Latin1; } })
        })
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => { _encoding = make(); _hex = false; HexToggle.IsChecked = false; Render(); };
            EncodingMenu.Items.Add(item);
        }

        string ext = Path.GetExtension(name).ToLowerInvariant();
        if (ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".ico" or ".webp")
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = new MemoryStream(data);
                bmp.EndInit();
                bmp.Freeze();
                Picture.Source = bmp;
                Picture.Visibility = Visibility.Visible;
                Text.Visibility = Visibility.Collapsed;
                Tools.Visibility = Visibility.Collapsed;
                Status.Text = $"Imagen {bmp.PixelWidth}×{bmp.PixelHeight} · {ArchiveReport.FormatSize(data.Length)}";
                return;
            }
            catch { /* no es una imagen válida: se muestra como texto */ }
        }
        Render();
    }

    private void Render()
    {
        int limit = Math.Max(16, _settings.ViewerMaxKb) * 1024;
        var slice = _data.Length > limit ? _data[..limit] : _data;
        Text.Text = _hex ? HexDump(slice) : _encoding.GetString(slice).Replace("\r\n", "\n").Replace("\n", "\r\n");
        Status.Text = ArchiveReport.FormatSize(_data.Length)
            + (_data.Length > limit || _truncated ? $" · mostrando los primeros {ArchiveReport.FormatSize(slice.Length)}" : "")
            + (_hex ? " · hexadecimal" : " · " + _encoding.EncodingName);
        Text.Select(0, 0);
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

    private void Encoding_Click(object sender, RoutedEventArgs e) { EncodingMenu.PlacementTarget = EncodingButton; EncodingMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom; EncodingMenu.IsOpen = true; }
    private void Hex_Click(object sender, RoutedEventArgs e) { _hex = HexToggle.IsChecked == true; Render(); }
    private void Wrap_Click(object sender, RoutedEventArgs e) => Text.TextWrapping = WrapToggle.IsChecked == true ? TextWrapping.Wrap : TextWrapping.NoWrap;

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Text.SelectionLength > 0 ? Text.SelectedText : Text.Text); } catch { /* portapapeles ocupado */ }
    }

    private void Find_Click(object sender, RoutedEventArgs e)
    {
        string? q = TextPromptWindow.Ask(this, "Texto a buscar:", "Buscar");
        if (string.IsNullOrEmpty(q)) return;
        string all = Text.Text;
        int i = all.IndexOf(q, Math.Min(_findPos, all.Length), StringComparison.OrdinalIgnoreCase);
        if (i < 0) i = all.IndexOf(q, 0, StringComparison.OrdinalIgnoreCase);
        if (i < 0) { Msg.Info(this, "No se encontró el texto."); return; }
        Text.Focus();
        Text.Select(i, q.Length);
        Text.ScrollToLine(Text.GetLineIndexFromCharacterIndex(i));
        _findPos = i + q.Length;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control && Text.Visibility == Visibility.Visible) { Find_Click(this, new RoutedEventArgs()); e.Handled = true; }
        else if (e.Key == Key.Escape) { Close(); e.Handled = true; }
    }

    /// <summary>Muestra un texto largo (información, informes) en una ventana del visor.</summary>
    public static void ShowText(Window? owner, string title, string text, AppSettings settings)
    {
        var w = new ViewerWindow(title, new UTF8Encoding(false).GetBytes(text), settings);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
    }

    public static void Show(Window? owner, string name, byte[] data, AppSettings settings, bool truncated)
    {
        var w = new ViewerWindow(name, data, settings, truncated);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
    }
}
