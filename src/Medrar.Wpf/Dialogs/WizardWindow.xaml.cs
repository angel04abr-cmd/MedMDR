using System.Windows;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Asistente paso a paso para crear un archivo o extraer uno.</summary>
public partial class WizardWindow : Window
{
    private readonly AppSettings _settings;
    private readonly string _folder;
    private int _step;
    private string? _password;
    private bool _names;

    public bool Create => OpCreate.IsChecked == true;
    public AddRequest Request { get; private set; } = new();
    public List<string> Sources { get; private set; } = new();
    public string ArchivePath => ArchiveBox.Text.Trim();
    public string Destination => Create ? DestBox.Text.Trim() : ExtractDestBox.Text.Trim();

    public WizardWindow(string folder, AppSettings settings)
    {
        InitializeComponent();
        DevTools.Hook(this, () =>        // solo pruebas (--accept): recorre los tres pasos con un ejemplo y los fotografía
        {
            if (App.Args.AutoText is { } src) { if (Directory.Exists(src) || File.Exists(src)) SourcesList.Items.Add(src); }
            for (int i = 0; i < 3; i++) { Next_Click(this, new RoutedEventArgs()); if (!IsVisible) return; DevTools.Capture(this, "-paso" + (i + 2)); }
        });
        _settings = settings; _folder = folder;
        foreach (var f in new[] { $"{AppInfo.Name} (.mdr)", "ZIP", "7Z", "TAR.GZ" }) FormatBox.Items.Add(f);
        foreach (var l in new[] { "Más rápido", "Normal", "El mejor" }) LevelBox.Items.Add(l);
        FormatBox.SelectedIndex = 0; LevelBox.SelectedIndex = 1;
        DestBox.Text = ExtractDestBox.Text = folder;
        PwButton.IsEnabled = false;
        ShowStep();
    }

    private void ShowStep()
    {
        BackButton.Visibility = _step > 0 ? Visibility.Visible : Visibility.Hidden;
        NextButton.Content = _step == 2 ? "Terminar" : "Siguiente";
        StepText.Text = $"Paso {_step + 1} de 3";
        StepBar.Value = _step + 1;
        Page0.Visibility = _step == 0 ? Visibility.Visible : Visibility.Collapsed;
        Page1Create.Visibility = _step == 1 && Create ? Visibility.Visible : Visibility.Collapsed;
        Page1Extract.Visibility = _step == 1 && !Create ? Visibility.Visible : Visibility.Collapsed;
        Page2Create.Visibility = _step == 2 && Create ? Visibility.Visible : Visibility.Collapsed;
        Page2Extract.Visibility = _step == 2 && !Create ? Visibility.Visible : Visibility.Collapsed;
        TitleText.Text = _step switch
        {
            0 => "¿Qué quieres hacer?",
            1 => Create ? "Elige qué comprimir" : "Elige el archivo",
            _ => Create ? "Opciones del archivo" : "Carpeta de destino"
        };
        if (_step == 2 && Create && NameBox.Text.Length == 0)
        {
            string first = SourcesList.Items.Cast<string>().FirstOrDefault() ?? "Archivo";
            NameBox.Text = Directory.Exists(first) ? Path.GetFileName(first.TrimEnd('\\')) : Path.GetFileNameWithoutExtension(first);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) { if (_step > 0) { _step--; ShowStep(); } }

    private void AddFiles_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Ficheros que se comprimirán" };
        if (d.ShowDialog(this) == true) foreach (var f in d.FileNames) if (!SourcesList.Items.Contains(f)) SourcesList.Items.Add(f);
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta que se comprimirá", InitialDirectory = Directory.Exists(_folder) ? _folder : "" };
        if (d.ShowDialog(this) == true && !SourcesList.Items.Contains(d.FolderName)) SourcesList.Items.Add(d.FolderName);
    }

    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        foreach (var i in SourcesList.SelectedItems.Cast<object>().ToList()) SourcesList.Items.Remove(i);
    }

    private void BrowseArchive_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Archivo que se va a extraer",
            Filter = "Archivos comprimidos|*.mdr;*.zip;*.rar;*.7z;*.tar;*.gz;*.tgz;*.bz2;*.xz;*.lz;*.zst;*.cab;*.iso;*.jar;*.001;*.exe|Todos|*.*"
        };
        if (d.ShowDialog(this) != true) return;
        ArchiveBox.Text = d.FileName;
        ExtractDestBox.Text = Path.Combine(_settings.DefaultExtractFolder.Length > 0 ? _settings.DefaultExtractFolder : Path.GetDirectoryName(d.FileName)!, Path.GetFileNameWithoutExtension(d.FileName));
    }

    private void BrowseDest_Click(object sender, RoutedEventArgs e) => BrowseInto(DestBox);
    private void BrowseExtractDest_Click(object sender, RoutedEventArgs e) => BrowseInto(ExtractDestBox);

    private void BrowseInto(System.Windows.Controls.TextBox box)
    {
        var d = new Microsoft.Win32.OpenFolderDialog { Title = "Carpeta", InitialDirectory = Directory.Exists(box.Text) ? box.Text : "" };
        if (d.ShowDialog(this) == true) box.Text = d.FolderName;
    }

    private void PwCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (PwButton == null) return;
        PwButton.IsEnabled = PwCheck.IsChecked == true;
        if (PwCheck.IsChecked != true) _password = null;
        else if (_password == null) Password_Click(this, e);
    }

    private void Password_Click(object sender, RoutedEventArgs e)
    {
        bool canNames = FormatBox.SelectedIndex is 0 or 2;
        var r = NewPasswordWindow.Ask(this, canNames, _names);
        if (r != null) { _password = r.Value.Password; _names = r.Value.EncryptNames && canNames; }
        else if (_password == null) PwCheck.IsChecked = false;
    }

    private void Next_Click(object sender, RoutedEventArgs e)
    {
        if (_step == 0) { _step = 1; ShowStep(); return; }
        if (_step == 1)
        {
            if (Create && SourcesList.Items.Count == 0) { Msg.Warn(this, "Añade al menos un fichero o carpeta."); return; }
            if (!Create && !File.Exists(ArchivePath) && !VolumeSet.IsVolumeName(ArchivePath)) { Msg.Warn(this, "Elige un archivo existente."); return; }
            Sources = SourcesList.Items.Cast<string>().ToList();
            _step = 2; ShowStep(); return;
        }
        if (string.IsNullOrWhiteSpace(Destination) || !Path.IsPathRooted(Destination)) { Msg.Warn(this, "Indica una carpeta de destino válida."); return; }
        if (Create)
        {
            if (string.IsNullOrWhiteSpace(NameBox.Text)) { Msg.Warn(this, "Escribe el nombre del archivo."); return; }
            var fmt = FormatBox.SelectedIndex switch { 1 => ArchiveFormat.Zip, 2 => ArchiveFormat.SevenZip, 3 => ArchiveFormat.TarGz, _ => ArchiveFormat.Mdr };
            if (PwCheck.IsChecked == true && _password == null) { Msg.Warn(this, "Pulsa «Contraseña…» para establecerla o desmarca la casilla."); return; }
            if (_password != null && fmt is not (ArchiveFormat.Mdr or ArchiveFormat.Zip or ArchiveFormat.SevenZip)) { Msg.Warn(this, "La contraseña solo está disponible en los formatos .mdr, ZIP y 7Z."); return; }
            string path = Path.Combine(Destination, NameBox.Text.Trim() + ArchiveBuilder.Extension(fmt));
            if (File.Exists(path) && !Msg.Confirm(this, $"«{Path.GetFileName(path)}» ya existe. ¿Reemplazarlo?")) return;
            try { Directory.CreateDirectory(Destination); if (File.Exists(path)) File.Delete(path); } catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); return; }
            Request = new AddRequest
            {
                ArchivePath = path, PathMode = PathMode.Relative,
                Options = new ArchiveOptions { Format = fmt, Level = LevelBox.SelectedIndex switch { 0 => 1, 2 => 5, _ => 3 }, Password = _password, EncryptNames = _names && _password != null }
            };
        }
        DialogResult = true;
    }

    /// <summary>Devuelve la ventana ya aceptada, o null si se cancela.</summary>
    public static WizardWindow? Ask(Window? owner, string folder, AppSettings settings)
    {
        var w = new WizardWindow(folder, settings);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return w.ShowDialog() == true ? w : null;
    }
}
