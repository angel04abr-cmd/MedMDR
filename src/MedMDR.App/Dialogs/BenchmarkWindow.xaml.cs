using System.Windows;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Prueba de velocidad de compresión: mide cada método con datos parecidos a texto.</summary>
public partial class BenchmarkWindow : Window
{
    private sealed record Row(string Method, int Level, string Compress, string Decompress, string Ratio);

    private readonly CancellationTokenSource _cts = new();

    public BenchmarkWindow()
    {
        InitializeComponent();
        Closing += (_, _) => _cts.Cancel();
        ContentRendered += async (_, _) => await RunAsync();
    }

    private async Task RunAsync()
    {
        try
        {
            var log = new Progress<string>(s => StatusText.Text = "Midiendo: " + s);
            var results = await Task.Run(() => Benchmark.Run(8, log, _cts.Token));
            foreach (var r in results)
                Results.Items.Add(new Row(r.Method, r.Level, r.CompressMBps.ToString("0.0"), r.DecompressMBps.ToString("0.0"), r.RatioPercent.ToString("0.0") + " %"));
            StatusText.Text = "Listo. Datos de prueba de 8 MB parecidos a texto; en tu equipo, con un solo hilo.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { StatusText.Text = "Error: " + ex.Message; }
        finally
        {
            Busy.IsIndeterminate = false;
            Busy.Visibility = Visibility.Collapsed;
        }
        if (App.Args.ShotsDir != null) { DevTools.Capture(this); Close(); }      // solo pruebas
    }

    public static void Open(Window? owner)
    {
        var w = new BenchmarkWindow();
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
    }
}
