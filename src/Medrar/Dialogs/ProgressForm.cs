using System.Diagnostics;
using Medrar.Core;

namespace Medrar.App;

/// <summary>Ventana de progreso con cancelación, tiempo transcurrido, velocidad y tiempo restante.</summary>
internal sealed class ProgressForm : Form
{
    private readonly Label _title = new() { AutoSize = true, Font = new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold) };
    private readonly Label _file = new() { AutoSize = false, Height = 20, Dock = DockStyle.Top, AutoEllipsis = true };
    private readonly ProgressBar _bar = new() { Dock = DockStyle.Top, Height = 22, Maximum = 1000 };
    private readonly Label _stats = new() { AutoSize = false, Height = 40, Dock = DockStyle.Top };
    private readonly Button _cancel = UI.Button("Cancelar", null, 100);
    private readonly Button _background = UI.Button("En segundo plano", null, 130);
    private readonly CancellationTokenSource _cts = new();
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly Action<ProgressReporter> _work;
    private ProgressInfo? _last;
    private Exception? _error;
    private bool _finished;
    private readonly System.Windows.Forms.Timer _tick = new() { Interval = 250 };

    public Exception? Error => _error;
    public bool WasCancelled { get; private set; }

    private ProgressForm(string title, Action<ProgressReporter> work)
    {
        _work = work;
        Text = "Medrar — " + title;
        _title.Text = title;
        Icon = AppIcons.App;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = true;
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(520, 190);
        Padding = new Padding(14);
        AutoScaleMode = AutoScaleMode.Dpi;

        var top = new Panel { Dock = DockStyle.Top, Height = 28 };
        top.Controls.Add(_title);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 42, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(_cancel);
        buttons.Controls.Add(_background);
        Controls.Add(buttons);
        Controls.Add(_stats);
        Controls.Add(_bar);
        Controls.Add(_file);
        Controls.Add(top);

        _cancel.Click += (_, _) => { _cancel.Enabled = false; _cts.Cancel(); };
        _background.Click += (_, _) => WindowState = FormWindowState.Minimized;
        FormClosing += (_, e) => { if (!_finished) { e.Cancel = true; _cts.Cancel(); _cancel.Enabled = false; } };
        _tick.Tick += (_, _) => Refresh_();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Theme.Style(this);
        var progress = new Progress<ProgressInfo>(p => _last = p);
        var reporter = new ProgressReporter(progress, _cts.Token);
        _tick.Start();
        Task.Run(() =>
        {
            try { _work(reporter); }
            catch (OperationCanceledException) { WasCancelled = true; }
            catch (Exception ex) { _error = ex; }
            BeginInvoke(() => { _finished = true; _tick.Stop(); DialogResult = DialogResult.OK; Close(); });
        });
    }

    private void Refresh_()
    {
        var p = _last;
        if (p == null) return;
        _file.Text = p.CurrentName;
        _bar.Value = (int)Math.Clamp(p.Fraction * 1000, 0, 1000);
        double secs = Math.Max(0.1, _clock.Elapsed.TotalSeconds);
        double speed = p.BytesDone / secs;
        string rest = "";
        if (p.BytesTotal > 0 && p.BytesDone > 0 && p.Fraction < 1)
        {
            var left = TimeSpan.FromSeconds((p.BytesTotal - p.BytesDone) / Math.Max(speed, 1));
            rest = $"   ·   quedan {Fmt(left)}";
        }
        _stats.Text = $"{(int)(p.Fraction * 100)} %   ·   {p.FilesDone} de {p.FilesTotal} ficheros   ·   {UI.Size(p.BytesDone)} de {UI.Size(p.BytesTotal)}\n" +
                      $"Transcurrido {Fmt(_clock.Elapsed)}   ·   {UI.Size((long)speed)}/s{rest}";
        Text = $"{(int)(p.Fraction * 100)} % — {_title.Text}";
    }

    private static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";

    /// <summary>Ejecuta <paramref name="work"/> mostrando el progreso. Devuelve el error, o null si fue bien; <paramref name="cancelled"/> indica cancelación.</summary>
    public static Exception? Run(IWin32Window? owner, string title, Action<ProgressReporter> work, out bool cancelled)
    {
        using var f = new ProgressForm(title, work);
        f.ShowDialog(owner);
        cancelled = f.WasCancelled;
        return f.Error;
    }
}
