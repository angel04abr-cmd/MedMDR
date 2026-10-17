namespace Medrar.App;

internal sealed class PasswordDialog : DialogBase
{
    private readonly TextBox _pw = new() { UseSystemPasswordChar = true, Width = 300 };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true, Width = 300 };
    private readonly CheckBox _show = UI.Check("Mostrar la contraseña");
    private readonly CheckBox _names = UI.Check("Cifrar también los nombres de los ficheros");
    private readonly bool _needConfirm;

    public string Password => _pw.Text;
    public bool EncryptNames => _names.Checked;

    /// <param name="forNewArchive">true al crear (pide repetirla y ofrece cifrar nombres).</param>
    public PasswordDialog(string title, string prompt, bool forNewArchive, bool encryptNames = false, bool offerEncryptNames = true)
        : base(title)
    {
        _needConfirm = forNewArchive;
        ClientSize = new Size(380, forNewArchive ? (offerEncryptNames ? 250 : 220) : (prompt.Length > 70 ? 215 : 170));
        var promptLabel = UI.Label(prompt);
        promptLabel.MaximumSize = new Size(350, 0);                 // que el texto largo se ajuste en vez de cortarse
        var col = UI.Column(promptLabel, _pw);
        if (forNewArchive)
        {
            col.Controls.Add(UI.Label("Repite la contraseña para comprobarla:"));
            col.Controls.Add(_confirm);
            _names.Checked = encryptNames && offerEncryptNames;
            if (offerEncryptNames) col.Controls.Add(_names);
        }
        col.Controls.Add(_show);
        col.Padding = new Padding(14, 12, 14, 0);
        _show.CheckedChanged += (_, _) => { _pw.UseSystemPasswordChar = _confirm.UseSystemPasswordChar = !_show.Checked; };
        Controls.Add(col);
        Controls.Add(Footer(okClick: Accept));
    }

    private void Accept(object? s, EventArgs e)
    {
        if (_pw.Text.Length == 0) { UI.Warn(this, "Escribe una contraseña."); return; }
        if (_needConfirm && _pw.Text != _confirm.Text) { UI.Warn(this, "Las dos contraseñas no coinciden."); return; }
        DialogResult = DialogResult.OK;
    }

    /// <summary>Pide la contraseña de un archivo existente. Devuelve null si se cancela.</summary>
    public static string? Ask(IWin32Window? owner, string fileName, bool wrong = false)
    {
        using var d = new PasswordDialog("Contraseña", (wrong ? "Contraseña incorrecta. " : "") + $"Escribe la contraseña de «{fileName}»:", false);
        return d.ShowDialog(owner) == DialogResult.OK ? d.Password : null;
    }
}
