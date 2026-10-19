using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Medrar.Core;
using CoreTheme = Medrar.Core.ThemeMode;

namespace Medrar.Wpf;

/// <summary>Configuración: interfaz, compresión, carpetas, visor, seguridad, actualizaciones e integración con Windows.</summary>
public partial class SettingsWindow : Window
{
    private readonly AppSettings _s;
    private bool _clearToken;

    public SettingsWindow(AppSettings s)
    {
        InitializeComponent();
        DevTools.Hook(this, () => Ok_Click(this, new RoutedEventArgs()));
        _s = s;

        foreach (var f in new[] { "Medrar (.mdr)", "ZIP", "7Z", "TAR.GZ", "TAR" }) FormatBox.Items.Add(f);
        foreach (var l in new[] { "Almacenar", "Más rápido", "Rápido", "Normal", "Bueno", "El mejor", "Ultra (solo .mdr, muy lento)" }) LevelBox.Items.Add(l);
        foreach (var o in new[] { "Preguntar", "Sobrescribir sin preguntar", "Omitir los existentes", "Renombrar automáticamente" }) OverwriteBox.Items.Add(o);
        foreach (var f in Fonts.SystemFontFamilies.Select(f => f.Source).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)) FontBox.Items.Add(f);

        TSys.IsChecked = s.Theme == CoreTheme.System; TLight.IsChecked = s.Theme == CoreTheme.Light; TDark.IsChecked = s.Theme == CoreTheme.Dark;
        HistoryCheck.IsChecked = s.KeepHistory; BtnTextCheck.IsChecked = s.ShowButtonText; BigCheck.IsChecked = s.LargeIcons;
        FullTitleCheck.IsChecked = s.ShowFullPathInTitle; CommentCheck.IsChecked = s.ShowArchiveComment; TopCheck.IsChecked = s.AlwaysOnTop;

        FormatBox.SelectedIndex = s.DefaultFormat switch { ArchiveFormat.Zip => 1, ArchiveFormat.SevenZip => 2, ArchiveFormat.TarGz => 3, ArchiveFormat.Tar => 4, _ => 0 };
        LevelBox.SelectedIndex = Math.Clamp(s.DefaultLevel, 0, 6);
        SolidCheck.IsChecked = s.DefaultSolid; TestCheck.IsChecked = s.DefaultTestAfter;
        RecBox.Text = Math.Clamp(s.DefaultRecoveryPercent, 0, 50).ToString();

        ArcFolder.Text = s.DefaultArchiveFolder; ExtFolder.Text = s.DefaultExtractFolder; StartFolder.Text = s.StartupFolder;

        int fi = FontBox.Items.IndexOf(s.ViewerFontName);
        FontBox.SelectedIndex = fi < 0 ? Math.Max(0, FontBox.Items.IndexOf("Consolas")) : fi;
        FontSizeBox.Text = Math.Clamp(s.ViewerFontSize, 6, 32).ToString("0.#");
        WrapCheck.IsChecked = s.ViewerWordWrap; MaxKbBox.Text = Math.Clamp(s.ViewerMaxKb, 16, 65536).ToString();

        AskDeleteCheck.IsChecked = s.AskBeforeDelete; BrokenCheck.IsChecked = s.KeepBrokenFiles;
        OverwriteBox.SelectedIndex = s.ExtractOverwrite switch { OverwriteMode.Overwrite => 1, OverwriteMode.Skip => 2, OverwriteMode.Rename => 3, _ => 0 };
        PrivacyText.Text = $"{AppInfo.Name} nunca guarda contraseñas en el disco ni las envía a ningún sitio. La única conexión a Internet es la búsqueda de actualizaciones (pestaña «Actualizaciones»), que no envía ningún dato de tus archivos.";

        AutoUpdateCheck.Content = $"Buscar actualizaciones al abrir {AppInfo.Name} (como mucho una vez al día)";
        AutoUpdateCheck.IsChecked = s.AutoCheckUpdates;
        HowText.Text = UpdateInstaller.IsMsiInstall()
            ? "✔ Esta copia se instaló con el instalador (.msi): puede actualizarse sola."
            : "Esta copia no se instaló con el .msi (es portable): avisará de las versiones nuevas, pero tendrás que instalarlas tú.";
        UpdateTokenState();
        TokenHelp.Text = $"Normalmente no hace falta ningún token: el repositorio de {AppInfo.Name} es público. Solo sirve si GitHub limita las consultas desde tu red. Créalo en GitHub > Settings > Developer settings > Fine-grained tokens, con el permiso «Contents: Read-only». Se guarda cifrado con tu cuenta de Windows (DPAPI) y solo se envía a api.github.com.";

        AssocCheck.Content = $"Asociar los archivos .mdr con {AppInfo.Name}";
        MenuCheck.Content = $"Añadir «Añadir a un archivo con {AppInfo.Name}» al menú del botón derecho";
        IntegrateButton.Content = $"Integrar {AppInfo.Name}";
        UpdateIntegrationState();
    }

    private void UpdateTokenState() =>
        TokenState.Text = string.IsNullOrEmpty(_s.UpdateTokenProtected) ? "No hay ningún token guardado." : "✔ Hay un token guardado. Escribe otro arriba solo si quieres cambiarlo.";

    private void ClearToken_Click(object sender, RoutedEventArgs e)
    {
        _clearToken = true; TokenBox.Password = "";
        TokenState.Text = "Se quitará el token al aceptar.";
    }

    private void BrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name } || FindName(name) is not TextBox box) return;
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta", InitialDirectory = Directory.Exists(box.Text) ? box.Text : "" };
        if (d.ShowDialog(this) == true) box.Text = d.FolderName;
    }

    private static string ExePath => Environment.ProcessPath ?? "";

    private void UpdateIntegrationState()
    {
        var sh = new ShellIntegration();
        IntState.Text = (sh.IsAssociated() ? $"✔ Los archivos .mdr están asociados con {AppInfo.Name}.\n" : "Los archivos .mdr no están asociados.\n") +
                        (sh.IsContextMenuInstalled() ? "✔ El menú del botón derecho está instalado." : "El menú del botón derecho no está instalado.");
    }

    private void Integrate_Click(object sender, RoutedEventArgs e)
    {
        if (AssocCheck.IsChecked != true && MenuCheck.IsChecked != true) { Msg.Info(this, "Marca al menos una de las dos opciones."); return; }
        try { new ShellIntegration().Install(ExePath, AssocCheck.IsChecked == true, MenuCheck.IsChecked == true); UpdateIntegrationState(); Msg.Info(this, "Integración realizada."); }
        catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); }
    }

    private void RemoveIntegration_Click(object sender, RoutedEventArgs e)
    {
        try { new ShellIntegration().Uninstall(); UpdateIntegrationState(); Msg.Info(this, "Se quitó la integración con el Explorador."); }
        catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); }
    }

    private static int Num(string text, int min, int max, int fallback) => int.TryParse(text.Trim(), out var v) ? Math.Clamp(v, min, max) : fallback;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        foreach (var box in new[] { ArcFolder, ExtFolder, StartFolder })
            if (box.Text.Trim().Length > 0 && !Directory.Exists(box.Text.Trim())) { Msg.Warn(this, $"La carpeta «{box.Text}» no existe."); return; }
        _s.Theme = TDark.IsChecked == true ? CoreTheme.Dark : TLight.IsChecked == true ? CoreTheme.Light : CoreTheme.System;
        _s.KeepHistory = HistoryCheck.IsChecked == true; _s.ShowButtonText = BtnTextCheck.IsChecked == true; _s.LargeIcons = BigCheck.IsChecked == true;
        _s.ShowFullPathInTitle = FullTitleCheck.IsChecked == true; _s.ShowArchiveComment = CommentCheck.IsChecked == true; _s.AlwaysOnTop = TopCheck.IsChecked == true;
        _s.DefaultFormat = FormatBox.SelectedIndex switch { 1 => ArchiveFormat.Zip, 2 => ArchiveFormat.SevenZip, 3 => ArchiveFormat.TarGz, 4 => ArchiveFormat.Tar, _ => ArchiveFormat.Mdr };
        _s.DefaultLevel = LevelBox.SelectedIndex; _s.DefaultSolid = SolidCheck.IsChecked == true && FormatBox.SelectedIndex == 0; _s.DefaultTestAfter = TestCheck.IsChecked == true;
        _s.DefaultRecoveryPercent = Num(RecBox.Text, 0, 50, 0);
        _s.DefaultArchiveFolder = ArcFolder.Text.Trim(); _s.DefaultExtractFolder = ExtFolder.Text.Trim(); _s.StartupFolder = StartFolder.Text.Trim();
        _s.ViewerFontName = FontBox.SelectedItem as string ?? "Consolas";
        _s.ViewerFontSize = float.TryParse(FontSizeBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var fs) ? Math.Clamp(fs, 6, 32) : 10;
        _s.ViewerWordWrap = WrapCheck.IsChecked == true; _s.ViewerMaxKb = Num(MaxKbBox.Text, 16, 65536, 1024);
        _s.AskBeforeDelete = AskDeleteCheck.IsChecked == true; _s.KeepBrokenFiles = BrokenCheck.IsChecked == true;
        _s.ExtractOverwrite = OverwriteBox.SelectedIndex switch { 1 => OverwriteMode.Overwrite, 2 => OverwriteMode.Skip, 3 => OverwriteMode.Rename, _ => OverwriteMode.Ask };
        _s.AutoCheckUpdates = AutoUpdateCheck.IsChecked == true;
        string tk = TokenBox.Password.Trim();
        if (tk.Length > 0) { _s.UpdateTokenProtected = TokenProtector.Protect(tk); _s.LastUpdateCheckUtc = default; }
        else if (_clearToken) { _s.UpdateTokenProtected = ""; _s.LastUpdateCheckUtc = default; }
        DialogResult = true;
    }

    /// <summary>Muestra la ventana; true si el usuario aceptó (y los valores ya están en <paramref name="settings"/>).</summary>
    public static bool Edit(Window? owner, AppSettings settings)
    {
        var w = new SettingsWindow(settings);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true;
    }
}
