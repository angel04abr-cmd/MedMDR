using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Ventana de progreso con cancelación, velocidad y tiempo restante. El trabajo corre en otro hilo; la ventana es modal.</summary>
public partial class ProgressWindow : Window
{
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Action<ProgressReporter> _work;
    private readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private ProgressInfo? _last;
    private bool _finished;

    public Exception? Error { get; private set; }
    public bool WasCancelled { get; private set; }

    private ProgressWindow(string title, Action<ProgressReporter> work)
    {
        InitializeComponent();
        _work = work;
        Title = AppInfo.Name + " — " + title;
        TitleText.Text = title;
        Bar.IsIndeterminate = true;           // hasta que se sepa cuánto hay que hacer
        _tick.Tick += (_, _) => Refresh();
        Loaded += (_, _) => Start();
        Closing += (_, e) => { if (!_finished) { e.Cancel = true; Cancel_Click(this, new RoutedEventArgs()); } };
    }

    private void Start()
    {
        var progress = new Progress<ProgressInfo>(p => _last = p);          // se crea en el hilo de la interfaz
        var reporter = new ProgressReporter(progress, _cts.Token);
        _tick.Start();
        Task.Run(() =>
        {
            try { _work(reporter); }
            catch (OperationCanceledException) { WasCancelled = true; }
            catch (Exception ex) { Error = ex; }
            Dispatcher.BeginInvoke(() => { _finished = true; _tick.Stop(); DialogResult = true; });
        });
    }

    private void Refresh()
    {
        var p = _last;
        if (p == null) return;
        FileText.Text = p.CurrentName;
        if (p.BytesTotal > 0)
        {
            Bar.IsIndeterminate = false;
            Bar.Value = Math.Clamp(p.Fraction * 1000, 0, 1000);
        }
        double secs = Math.Max(0.1, _clock.Elapsed.TotalSeconds);
        double speed = p.BytesDone / secs;
        string rest = "";
        if (p.BytesTotal > 0 && p.BytesDone > 0 && p.Fraction < 1)
            rest = $"   ·   quedan {Fmt(TimeSpan.FromSeconds((p.BytesTotal - p.BytesDone) / Math.Max(speed, 1)))}";
        StatsText.Text = $"{(int)(p.Fraction * 100)} %   ·   {p.FilesDone} de {p.FilesTotal} ficheros   ·   {ArchiveReport.FormatSize(p.BytesDone)} de {ArchiveReport.FormatSize(p.BytesTotal)}\n" +
                         $"Transcurrido {Fmt(_clock.Elapsed)}   ·   {ArchiveReport.FormatSize((long)speed)}/s{rest}";
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    private void Cancel_Click(object sender, RoutedEventArgs e) { CancelButton.IsEnabled = false; _cts.Cancel(); }
    private void Background_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    /// <summary>Ejecuta <paramref name="work"/> mostrando el progreso. Devuelve el error, o null si fue bien.</summary>
    public static Exception? Run(Window? owner, string title, Action<ProgressReporter> work, out bool cancelled)
    {
        var w = new ProgressWindow(title, work);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        cancelled = w.WasCancelled;
        return w.Error;
    }
}
