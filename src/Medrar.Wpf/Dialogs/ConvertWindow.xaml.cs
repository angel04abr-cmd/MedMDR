using System.Windows;
using System.Windows.Controls;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Convertir archivos de cualquier formato legible a .mdr, ZIP, 7Z o TAR.GZ.</summary>
public partial class ConvertWindow : Window
{
    private string? _password;
    private bool _names;

    public List<string> Sources => Files.Items.Cast<string>().ToList();
    public string Destination => DestBox.Text.Trim();
    public bool DeleteOriginals => DeleteCheck.IsChecked == true;
    public ArchiveOptions Options { get; private set; } = new();

    public ConvertWindow(IEnumerable<string> initial, string folder)
    {
        InitializeComponent();
        DevTools.Hook(this, () => Ok_Click(this, new RoutedEventArgs()));
        foreach (var p in initial) Files.Items.Add(p);
        DestBox.Text = folder;
        foreach (var f in new[] { "Medrar (.mdr)", "ZIP (.zip)", "7Z (.7z)", "TAR.GZ (.tar.gz)" }) FormatBox.Items.Add(f);
        foreach (var l in new[] { "Almacenar", "Más rápido", "Rápido", "Normal", "Bueno", "El mejor", "Ultra (solo .mdr, muy lento)" }) LevelBox.Items.Add(l);
        FormatBox.SelectedIndex = 0; LevelBox.SelectedIndex = 3;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Multiselect = true, Title = "Archivos que se van a convertir",
            Filter = "Archivos comprimidos|*.mdr;*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.lz;*.zst;*.cab;*.iso;*.jar;*.001|Todos|*.*"
        };
        if (d.ShowDialog(this) == true) foreach (var f in d.FileNames) if (!Files.Items.Contains(f)) Files.Items.Add(f);
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in Files.SelectedItems.Cast<object>().ToList()) Files.Items.Remove(i);
    }

    private void Format_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (SolidCheck == null) return;
        bool mdr = FormatBox.SelectedIndex == 0;
        SolidCheck.IsEnabled = mdr;
        if (!mdr) SolidCheck.IsChecked = false;
        if (FormatBox.SelectedIndex > 2) { _password = null; _names = false; PwState.Text = "Sin contraseña"; }
    }

    private void Password_Click(object sender, RoutedEventArgs e)
    {
        if (FormatBox.SelectedIndex > 2) { Msg.Info(this, "La contraseña solo está disponible en los formatos .mdr, ZIP y 7Z."); return; }
        bool canNames = FormatBox.SelectedIndex is 0 or 2;
        var r = NewPasswordWindow.Ask(this, canNames, _names);
        if (r == null) return;
        _password = r.Value.Password; _names = r.Value.EncryptNames && canNames;
        PwState.Text = _names ? "Contraseña establecida (nombres cifrados)" : "Contraseña establecida";
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta donde se guardarán los archivos convertidos", InitialDirectory = Directory.Exists(DestBox.Text) ? DestBox.Text : "" };
        if (d.ShowDialog(this) == true) DestBox.Text = d.FolderName;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Files.Items.Count == 0) { Msg.Warn(this, "Añade al menos un archivo."); return; }
        if (!Path.IsPathRooted(Destination)) { Msg.Warn(this, "Indica una carpeta de destino válida."); return; }
        try { Directory.CreateDirectory(Destination); } catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); return; }
        Options = new ArchiveOptions
        {
            Format = FormatBox.SelectedIndex switch { 1 => ArchiveFormat.Zip, 2 => ArchiveFormat.SevenZip, 3 => ArchiveFormat.TarGz, _ => ArchiveFormat.Mdr },
            Level = LevelBox.SelectedIndex, Solid = SolidCheck.IsChecked == true, Password = _password, EncryptNames = _names && _password != null,
            RecoveryPercent = RecCheck.IsChecked == true ? 5 : 0
        };
        DialogResult = true;
    }

    /// <summary>Devuelve null si se cancela.</summary>
    public static ConvertWindow? Ask(Window? owner, IEnumerable<string> initial, string folder)
    {
        var w = new ConvertWindow(initial, folder);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w : null;
    }
}
