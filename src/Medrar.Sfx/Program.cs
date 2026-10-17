using Medrar.Core;

namespace Medrar.Sfx;

/// <summary>
/// Módulo autoextraíble: este mismo .exe lleva pegado al final un archivo (.mdr, ZIP, 7Z, TAR...) y un pie que lo localiza.
/// Uso silencioso:  archivo.exe -y [-d carpeta] [-pCONTRASEÑA]
/// </summary>
static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string self = Environment.ProcessPath ?? Application.ExecutablePath;
        string? dest = null, pw = null;
        bool silent = false;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "-y") silent = true;
            else if (args[i] == "-d" && i + 1 < args.Length) dest = args[++i];
            else if (args[i].StartsWith("-p")) pw = args[i][2..];
        }
        dest ??= Path.Combine(Path.GetDirectoryName(self)!, Path.GetFileNameWithoutExtension(self));

        if (silent)
        {
            try
            {
                using var a = ArchiveOpener.Open(self, pw);
                var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Overwrite });
                return r.Ok ? 0 : 3;
            }
            catch (Exception) { return 2; }
        }
        Application.Run(new SfxForm(self, dest, pw));
        return 0;
    }
}

sealed class SfxForm : Form
{
    private readonly string _self;
    private string? _pw;
    private readonly TextBox _dest = new() { Width = 330 };
    private readonly Label _info = new() { AutoSize = true, MaximumSize = new Size(440, 0) };
    private readonly ProgressBar _bar = new() { Width = 440, Height = 20, Maximum = 1000 };
    private readonly Label _file = new() { AutoSize = false, Width = 440, Height = 18, AutoEllipsis = true };
    private readonly Button _go = new() { Text = "Extraer", Width = 100, Height = 30 };
    private readonly Button _cancel = new() { Text = "Cerrar", Width = 100, Height = 30 };
    private IArchive? _archive;
    private CancellationTokenSource? _cts;

    public SfxForm(string self, string dest, string? pw)
    {
        _self = self; _pw = pw;
        Text = "Archivo autoextraíble — Medrar";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(500, 270);
        Font = SystemFonts.MessageBoxFont!;
        try { Icon = Icon.ExtractAssociatedIcon(self); } catch { }
        _dest.Text = dest;
        var browse = new Button { Text = "Examinar…", Width = 96, Height = 26 };
        browse.Click += (_, _) =>
        {
            using var d = new FolderBrowserDialog { Description = "Carpeta de destino", UseDescriptionForTitle = true };
            if (d.ShowDialog(this) == DialogResult.OK) _dest.Text = d.SelectedPath;
        };
        var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, Padding = new Padding(16, 12, 0, 0) };
        var row = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        row.Controls.AddRange(new Control[] { _dest, browse });
        panel.Controls.AddRange(new Control[] { _info, new Label { Text = "Extraer en la carpeta:", AutoSize = true, Margin = new Padding(0, 10, 0, 2) }, row, _bar, _file });
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 46, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8, 6, 8, 6) };
        buttons.Controls.Add(_cancel); buttons.Controls.Add(_go);
        Controls.Add(panel); Controls.Add(buttons);
        _cancel.Click += (_, _) => { if (_cts != null) _cts.Cancel(); else Close(); };
        _go.Click += async (_, _) => await Extract();
        Shown += (_, _) => LoadInfo();
    }

    private void LoadInfo()
    {
        try
        {
            _archive?.Dispose();
            _archive = ArchiveOpener.Open(_self, _pw);
            if (_archive.IsEncrypted && string.IsNullOrEmpty(_pw)) { AskPassword(false); return; }
            var files = _archive.Files().ToList();
            _info.Text = $"Este archivo contiene {files.Count} fichero(s), {ArchiveReport.FormatSize(files.Sum(f => f.Size))} sin comprimir." +
                         (string.IsNullOrWhiteSpace(_archive.Comment) ? "" : "\n\n" + _archive.Comment);
        }
        catch (PasswordRequiredException) { AskPassword(false); }
        catch (WrongPasswordException) { AskPassword(true); }
        catch (Exception ex) { _info.Text = "No se puede leer el archivo: " + ex.Message; _go.Enabled = false; }
    }

    private void AskPassword(bool wrong)
    {
        using var f = new Form { Text = "Contraseña", ClientSize = new Size(330, 120), FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterParent, MaximizeBox = false, MinimizeBox = false, Font = Font };
        var tb = new TextBox { UseSystemPasswordChar = true, Left = 16, Top = 38, Width = 296 };
        var ok = new Button { Text = "Aceptar", DialogResult = DialogResult.OK, Left = 130, Top = 76, Width = 90 };
        var cancel = new Button { Text = "Cancelar", DialogResult = DialogResult.Cancel, Left = 226, Top = 76, Width = 90 };
        f.Controls.AddRange(new Control[] { new Label { Text = (wrong ? "Contraseña incorrecta. " : "") + "Escribe la contraseña:", Left = 16, Top = 12, AutoSize = true }, tb, ok, cancel });
        f.AcceptButton = ok; f.CancelButton = cancel;
        if (f.ShowDialog(this) == DialogResult.OK) { _pw = tb.Text; LoadInfo(); }
        else { _info.Text = "El archivo está cifrado y hace falta la contraseña."; _go.Enabled = false; }
    }

    private async Task Extract()
    {
        if (_archive == null) return;
        string dest = _dest.Text.Trim();
        if (!Path.IsPathRooted(dest)) { MessageBox.Show(this, "Indica una carpeta de destino válida.", Text); return; }
        _go.Enabled = false; _cancel.Text = "Cancelar";
        _cts = new CancellationTokenSource();
        _file.Text = "";
        var progress = new ProgressReporter(new Progress<ProgressInfo>(p => { _bar.Value = (int)(p.Fraction * 1000); _file.Text = p.CurrentName; }), _cts.Token);
        try
        {
            var res = await Task.Run(() => Extractor.Extract(_archive, _archive.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Overwrite }, progress));
            _bar.Value = 1000;
            if (res.Ok)
            {
                MessageBox.Show(this, $"Se extrajeron {res.Extracted} fichero(s) en:\n{dest}", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
                try { System.Diagnostics.Process.Start("explorer.exe", $"\"{dest}\""); } catch { }
                Close();
            }
            else MessageBox.Show(this, "Hubo errores:\n" + string.Join("\n", res.Errors.Take(10).Select(e => e.Path + ": " + e.Error)), Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        catch (OperationCanceledException) { _file.Text = "Cancelado."; }
        catch (PasswordRequiredException) { _cts = null; _go.Enabled = true; AskPassword(false); return; }
        catch (WrongPasswordException) { _cts = null; _go.Enabled = true; AskPassword(true); return; }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { _cts = null; _go.Enabled = true; _cancel.Text = "Cerrar"; }
    }

    protected override void Dispose(bool disposing) { if (disposing) _archive?.Dispose(); base.Dispose(disposing); }
}
