using System.Windows;

namespace Medrar.Wpf;

/// <summary>Pide una contraseña nueva (la repite para comprobarla) y, si el formato lo permite, si se cifran también los nombres.</summary>
public partial class NewPasswordWindow : Window
{
    public string Password => Show.IsChecked == true ? PwText.Text : Pw.Password;
    public bool EncryptNames => Names.IsChecked == true && Names.Visibility == Visibility.Visible;

    public NewPasswordWindow(bool offerEncryptNames, bool encryptNames)
    {
        InitializeComponent();
        DevTools.Hook(this, () => { Pw.Password = Pw2.Password = "prueba"; Ok_Click(this, new RoutedEventArgs()); });
        Names.Visibility = offerEncryptNames ? Visibility.Visible : Visibility.Collapsed;
        Names.IsChecked = encryptNames && offerEncryptNames;
        Loaded += (_, _) => Pw.Focus();
    }

    private void Show_Changed(object sender, RoutedEventArgs e)
    {
        bool show = Show.IsChecked == true;
        if (show) { PwText.Text = Pw.Password; Pw2Text.Text = Pw2.Password; } else { Pw.Password = PwText.Text; Pw2.Password = Pw2Text.Text; }
        Pw.Visibility = Pw2.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        PwText.Visibility = Pw2Text.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string a = Show.IsChecked == true ? PwText.Text : Pw.Password, b = Show.IsChecked == true ? Pw2Text.Text : Pw2.Password;
        if (a.Length == 0) { Msg.Warn(this, "Escribe una contraseña."); return; }
        if (a != b) { Msg.Warn(this, "Las dos contraseñas no coinciden."); return; }
        DialogResult = true;
    }

    /// <summary>Devuelve null si se cancela.</summary>
    public static (string Password, bool EncryptNames)? Ask(Window? owner, bool offerEncryptNames, bool encryptNames = false)
    {
        var w = new NewPasswordWindow(offerEncryptNames, encryptNames);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? (w.Password, w.EncryptNames) : null;
    }
}
