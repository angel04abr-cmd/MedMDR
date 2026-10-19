using System.IO;
using System.Windows;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Pregunta qué hacer cuando al extraer ya existe el fichero de destino.</summary>
public partial class OverwriteWindow : Window
{
    public OverwriteDecision Decision { get; private set; } = OverwriteDecision.Cancel;
    public bool ApplyToAll => ApplyAll.IsChecked == true;

    public OverwriteWindow(string path)
    {
        InitializeComponent();
        DevTools.Hook(this);
        PathText.Text = path;
        try { var fi = new FileInfo(path); InfoText.Text = $"{ArchiveReport.FormatSize(fi.Length)} · modificado {fi.LastWriteTime:dd/MM/yyyy HH:mm}"; }
        catch { InfoText.Text = ""; }
    }

    private void Choose(OverwriteDecision d) { Decision = d; DialogResult = true; }
    private void Overwrite_Click(object sender, RoutedEventArgs e) => Choose(OverwriteDecision.Overwrite);
    private void Skip_Click(object sender, RoutedEventArgs e) => Choose(OverwriteDecision.Skip);
    private void Rename_Click(object sender, RoutedEventArgs e) => Choose(OverwriteDecision.Rename);

    /// <summary>Se llama desde el hilo que extrae: la ventana se muestra en el hilo de la interfaz.</summary>
    public static Func<string, OverwriteDecision> Prompt(Window owner, ExtractOptions o) => path =>
    {
        var result = OverwriteDecision.Cancel;
        owner.Dispatcher.Invoke(() =>
        {
            var w = new OverwriteWindow(path);
            // sobre la ventana de progreso si está abierta; si no, sobre la principal; sin ninguna visible (órdenes del menú de Windows), centrada
            Window? top = owner.OwnedWindows.Cast<Window>().FirstOrDefault(x => x.IsVisible) ?? (owner.IsVisible ? owner : null);
            if (top != null) w.Owner = top; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            w.ShowDialog();
            result = w.Decision;
            if (w.ApplyToAll && result != OverwriteDecision.Cancel)
                o.Overwrite = result == OverwriteDecision.Overwrite ? OverwriteMode.Overwrite : result == OverwriteDecision.Skip ? OverwriteMode.Skip : OverwriteMode.Rename;
        });
        return result;
    };
}
