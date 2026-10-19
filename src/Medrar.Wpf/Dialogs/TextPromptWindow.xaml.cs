using System.Windows;

namespace Medrar.Wpf;

/// <summary>Pide una línea de texto, un texto largo o una contraseña (sustituye al InputBox y al diálogo de contraseña antiguos).</summary>
public partial class TextPromptWindow : Window
{
    private readonly bool _secret;

    private TextPromptWindow(string prompt, string title, string initial, bool multiline, bool secret)
    {
        InitializeComponent();
        DevTools.Hook(this);
        Title = title;
        Prompt.Text = prompt;
        _secret = secret;
        TextBox.Text = initial;
        if (multiline)
        {
            TextBox.AcceptsReturn = true; TextBox.TextWrapping = TextWrapping.Wrap; TextBox.Height = 130;
            TextBox.VerticalContentAlignment = VerticalAlignment.Top; TextBox.Padding = new Thickness(10, 8, 10, 8);
            TextBox.VerticalScrollBarVisibility = System.Windows.Controls.ScrollBarVisibility.Auto;
        }
        if (secret)
        {
            TextBox.Visibility = Visibility.Collapsed;
            Secret.Visibility = Visibility.Visible;
            ShowSecret.Visibility = Visibility.Visible;
        }
        Loaded += (_, _) => { if (secret) Secret.Focus(); else { TextBox.Focus(); TextBox.SelectAll(); } };
    }

    private string Value => _secret ? (ShowSecret.IsChecked == true ? TextBox.Text : Secret.Password) : TextBox.Text;

    private void ShowSecret_Changed(object sender, RoutedEventArgs e)
    {
        if (ShowSecret.IsChecked == true) { TextBox.Text = Secret.Password; TextBox.Visibility = Visibility.Visible; Secret.Visibility = Visibility.Collapsed; TextBox.Focus(); }
        else { Secret.Password = TextBox.Text; Secret.Visibility = Visibility.Visible; TextBox.Visibility = Visibility.Collapsed; Secret.Focus(); }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (_secret && Value.Length == 0) { Msg.Warn(this, "Escribe una contraseña."); return; }
        DialogResult = true;
    }

    /// <summary>Devuelve null si se cancela.</summary>
    public static string? Ask(Window? owner, string prompt, string title, string initial = "", bool multiline = false)
    {
        if (App.Args.AutoText != null) return App.Args.AutoText;
        var w = new TextPromptWindow(prompt, title, initial, multiline, secret: false);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.Value : null;
    }

    /// <summary>Pide la contraseña de un archivo. Devuelve null si se cancela.</summary>
    public static string? AskPassword(Window? owner, string prompt, string title = "Contraseña")
    {
        var w = new TextPromptWindow(prompt, title, "", false, secret: true);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w.Value : null;
    }
}
