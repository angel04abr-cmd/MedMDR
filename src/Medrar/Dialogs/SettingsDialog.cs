using Medrar.Core;

namespace Medrar.App;

/// <summary>Configuración: interfaz, compresión, carpetas, visor, seguridad e integración con Windows.</summary>
internal sealed class SettingsDialog : DialogBase
{
    private readonly AppSettings _s;
    // Interfaz
    private readonly RadioButton _tSys = UI.Radio("Predeterminado del sistema"), _tLight = UI.Radio("Claro"), _tDark = UI.Radio("Oscuro");
    private readonly CheckBox _history = UI.Check("Conservar el historial de carpetas");
    private readonly CheckBox _btnText = UI.Check("Mostrar el texto de los botones");
    private readonly CheckBox _big = UI.Check("Iconos grandes en la barra de herramientas");
    private readonly CheckBox _fullTitle = UI.Check("Mostrar la ruta completa en la barra de título");
    private readonly CheckBox _comment = UI.Check("Mostrar el comentario del archivo al abrirlo");
    private readonly CheckBox _top = UI.Check("Mantener la ventana siempre visible");
    // Compresión
    private readonly ComboBox _format = UI.Combo(new[] { "Medrar (.mdr)", "ZIP", "7Z", "TAR.GZ", "TAR" }, 0, 180);
    private readonly ComboBox _level = UI.Combo(new[] { "Almacenar", "Más rápido", "Rápido", "Normal", "Bueno", "El mejor" }, 3, 180);
    private readonly CheckBox _solid = UI.Check("Crear archivos sólidos por defecto");
    private readonly CheckBox _testAfter = UI.Check("Verificar siempre al terminar de comprimir");
    private readonly NumericUpDown _rec = new() { Minimum = 0, Maximum = 50, Width = 60 };
    // Carpetas
    private readonly TextBox _arcFolder = UI.Text("", 380), _extFolder = UI.Text("", 380), _startFolder = UI.Text("", 380);
    // Visor
    private readonly ComboBox _font = new() { Width = 200, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly NumericUpDown _fontSize = new() { Minimum = 6, Maximum = 32, DecimalPlaces = 0, Width = 60 };
    private readonly CheckBox _wrap = UI.Check("Ajustar las líneas largas");
    private readonly NumericUpDown _maxKb = new() { Minimum = 16, Maximum = 65536, Width = 90 };
    // Seguridad
    private readonly CheckBox _askDelete = UI.Check("Pedir confirmación antes de eliminar");
    private readonly CheckBox _broken = UI.Check("Conservar los ficheros dañados al extraer");
    private readonly ComboBox _overwrite = UI.Combo(new[] { "Preguntar", "Sobrescribir sin preguntar", "Omitir los existentes", "Renombrar automáticamente" }, 0, 240);
    // Actualizaciones
    private readonly CheckBox _autoUpdate = UI.Check("Buscar actualizaciones al abrir Medrar (como mucho una vez al día)");
    private readonly TextBox _token = new() { Width = 420, UseSystemPasswordChar = true, Margin = new Padding(0, 3, 6, 3) };
    private readonly Label _tokenState = UI.Label("", muted: true);
    private bool _clearToken;
    // Integración
    private readonly Label _intState = UI.Label("", muted: true);
    private readonly CheckBox _assoc = UI.Check("Asociar los archivos .mdr con Medrar", true);
    private readonly CheckBox _menu = UI.Check("Añadir «Añadir a un archivo con Medrar» al menú del botón derecho", true);

    public SettingsDialog(AppSettings s) : base("Configuración")
    {
        _s = s;
        ClientSize = new Size(620, 520);

        foreach (var f in new System.Drawing.Text.InstalledFontCollection().Families.Where(f => f.IsStyleAvailable(FontStyle.Regular)).Select(f => f.Name)) _font.Items.Add(f);
        _tSys.Checked = s.Theme == ThemeMode.System; _tLight.Checked = s.Theme == ThemeMode.Light; _tDark.Checked = s.Theme == ThemeMode.Dark;
        _history.Checked = s.KeepHistory; _btnText.Checked = s.ShowButtonText; _big.Checked = s.LargeIcons;
        _fullTitle.Checked = s.ShowFullPathInTitle; _comment.Checked = s.ShowArchiveComment; _top.Checked = s.AlwaysOnTop;
        _format.SelectedIndex = s.DefaultFormat switch { ArchiveFormat.Zip => 1, ArchiveFormat.SevenZip => 2, ArchiveFormat.TarGz => 3, ArchiveFormat.Tar => 4, _ => 0 };
        _level.SelectedIndex = Math.Clamp(s.DefaultLevel, 0, 5); _solid.Checked = s.DefaultSolid; _testAfter.Checked = s.DefaultTestAfter; _rec.Value = Math.Clamp(s.DefaultRecoveryPercent, 0, 50);
        _arcFolder.Text = s.DefaultArchiveFolder; _extFolder.Text = s.DefaultExtractFolder; _startFolder.Text = s.StartupFolder;
        int fi = _font.Items.IndexOf(s.ViewerFontName); _font.SelectedIndex = fi < 0 ? Math.Max(0, _font.Items.IndexOf("Consolas")) : fi;
        _fontSize.Value = (decimal)Math.Clamp(s.ViewerFontSize, 6, 32); _wrap.Checked = s.ViewerWordWrap; _maxKb.Value = Math.Clamp(s.ViewerMaxKb, 16, 65536);
        _askDelete.Checked = s.AskBeforeDelete; _broken.Checked = s.KeepBrokenFiles;
        _overwrite.SelectedIndex = s.ExtractOverwrite switch { OverwriteMode.Overwrite => 1, OverwriteMode.Skip => 2, OverwriteMode.Rename => 3, _ => 0 };

        Control FolderRow(string label, TextBox box) =>
            UI.Column(UI.Label(label), UI.Row(box, UI.Button("Examinar…", (_, _) => { var p = UI.PickFolder(this, box.Text); if (p != null) box.Text = p; }, 96)));

        var ui = UI.Page(UI.Group("Tema", UI.Column(_tSys, _tLight, _tDark)), _history, _btnText, _big, _fullTitle, _comment, _top);
        var comp = UI.Page(UI.Label("Formato predeterminado:"), _format, UI.Label("Nivel predeterminado:"), _level, _solid, _testAfter,
            UI.Row(UI.Label("Registro de recuperación por defecto (0 = ninguno):"), _rec, UI.Label("%")),
            UI.Label("Los perfiles de compresión se guardan desde el diálogo «Añadir» (botón Perfiles).", muted: true));
        var folders = UI.Page(FolderRow("Carpeta predeterminada para crear archivos (vacío = la actual):", _arcFolder),
            FolderRow("Carpeta predeterminada para extraer (vacío = junto al archivo):", _extFolder),
            FolderRow("Carpeta con la que se abre Medrar (vacío = Documentos):", _startFolder));
        var viewer = UI.Page(UI.Label("Fuente del visor:"), UI.Row(_font, UI.Label("Tamaño"), _fontSize), _wrap,
            UI.Row(UI.Label("Máximo que se muestra de cada fichero:"), _maxKb, UI.Label("KB")));
        var sec = UI.Page(_askDelete, _broken, UI.Label("Si el fichero ya existe al extraer:"), _overwrite,
            UI.Label("Medrar nunca guarda contraseñas en el disco ni las envía a ningún sitio. La única conexión a Internet es la búsqueda de\nactualizaciones (pestaña «Actualizaciones»), que no envía ningún dato de tus archivos.", muted: true));

        _autoUpdate.Checked = s.AutoCheckUpdates;
        UpdateTokenState();
        string how = UpdateInstaller.IsMsiInstall()
            ? "✔ Esta copia se instaló con el instalador (.msi): puede actualizarse sola."
            : "Esta copia no se instaló con el .msi (es portable): avisará de las versiones nuevas, pero tendrás que instalarlas tú.";
        var upd = UI.Page(
            UI.Label("Actualizaciones automáticas", true),
            UI.Label(how, muted: true),
            _autoUpdate,
            UI.Label("Token de acceso de GitHub (solo lectura):"),
            UI.Row(_token, UI.Button("Quitar", (_, _) => { _clearToken = true; _token.Text = ""; _tokenState.Text = "Se quitará el token al aceptar."; }, 80)),
            _tokenState,
            UI.Label("El repositorio de Medrar es privado: sin un token, GitHub no deja ver las versiones. Créalo en GitHub > Settings >\nDeveloper settings > Fine-grained tokens, solo para el repositorio Medrar, con el permiso «Contents: Read-only».\nSe guarda cifrado con tu cuenta de Windows (DPAPI) y solo se envía a api.github.com.", muted: true));

        var integ = UI.Page(
            UI.Label("Integración con el Explorador de Windows", true),
            UI.Label("Solo modifica tu usuario (HKEY_CURRENT_USER): no necesita permisos de administrador y se puede quitar por completo.", muted: true),
            _assoc, _menu,
            UI.Row(UI.Button("Integrar Medrar", Integrate, 140), UI.Button("Quitar la integración", Remove, 160)), _intState);
        UpdateIntegrationState();

        var tabs = new TabControl { Dock = DockStyle.Fill };
        tabs.TabPages.Add(UI.Tab("Interfaz", ui));
        tabs.TabPages.Add(UI.Tab("Compresión", comp));
        tabs.TabPages.Add(UI.Tab("Carpetas", folders));
        tabs.TabPages.Add(UI.Tab("Visor", viewer));
        tabs.TabPages.Add(UI.Tab("Seguridad", sec));
        tabs.TabPages.Add(UI.Tab("Actualizaciones", upd));
        tabs.TabPages.Add(UI.Tab("Integración", integ));
        Controls.Add(tabs);
        Controls.Add(Footer(okClick: Accept));
    }

    private void UpdateTokenState() =>
        _tokenState.Text = string.IsNullOrEmpty(_s.UpdateTokenProtected) ? "No hay ningún token guardado." : "✔ Hay un token guardado. Escribe otro arriba solo si quieres cambiarlo.";

    private static string ExePath => Environment.ProcessPath ?? Application.ExecutablePath;

    private void UpdateIntegrationState()
    {
        var sh = new ShellIntegration();
        _intState.Text = (sh.IsAssociated() ? "✔ Los archivos .mdr están asociados con Medrar. " : "Los archivos .mdr no están asociados. ") +
                         (sh.IsContextMenuInstalled() ? "\n✔ El menú del botón derecho está instalado." : "\nEl menú del botón derecho no está instalado.");
    }

    private void Integrate(object? s, EventArgs e)
    {
        if (!_assoc.Checked && !_menu.Checked) { UI.Info(this, "Marca al menos una de las dos opciones."); return; }
        try { new ShellIntegration().Install(ExePath, _assoc.Checked, _menu.Checked); UpdateIntegrationState(); UI.Info(this, "Integración realizada."); }
        catch (Exception ex) { UI.Error(this, UI.Describe(ex)); }
    }

    private void Remove(object? s, EventArgs e)
    {
        try { new ShellIntegration().Uninstall(); UpdateIntegrationState(); UI.Info(this, "Se quitó la integración con el Explorador."); }
        catch (Exception ex) { UI.Error(this, UI.Describe(ex)); }
    }

    private void Accept(object? s, EventArgs e)
    {
        foreach (var box in new[] { _arcFolder, _extFolder, _startFolder })
            if (box.Text.Trim().Length > 0 && !Directory.Exists(box.Text.Trim())) { UI.Warn(this, $"La carpeta «{box.Text}» no existe."); return; }
        _s.Theme = _tDark.Checked ? ThemeMode.Dark : _tLight.Checked ? ThemeMode.Light : ThemeMode.System;
        _s.KeepHistory = _history.Checked; _s.ShowButtonText = _btnText.Checked; _s.LargeIcons = _big.Checked;
        _s.ShowFullPathInTitle = _fullTitle.Checked; _s.ShowArchiveComment = _comment.Checked; _s.AlwaysOnTop = _top.Checked;
        _s.DefaultFormat = _format.SelectedIndex switch { 1 => ArchiveFormat.Zip, 2 => ArchiveFormat.SevenZip, 3 => ArchiveFormat.TarGz, 4 => ArchiveFormat.Tar, _ => ArchiveFormat.Mdr };
        _s.DefaultLevel = _level.SelectedIndex; _s.DefaultSolid = _solid.Checked && _format.SelectedIndex == 0; _s.DefaultTestAfter = _testAfter.Checked;
        _s.DefaultRecoveryPercent = (int)_rec.Value;
        _s.DefaultArchiveFolder = _arcFolder.Text.Trim(); _s.DefaultExtractFolder = _extFolder.Text.Trim(); _s.StartupFolder = _startFolder.Text.Trim();
        _s.ViewerFontName = (string)_font.SelectedItem!; _s.ViewerFontSize = (float)_fontSize.Value; _s.ViewerWordWrap = _wrap.Checked; _s.ViewerMaxKb = (int)_maxKb.Value;
        _s.AskBeforeDelete = _askDelete.Checked; _s.KeepBrokenFiles = _broken.Checked;
        _s.ExtractOverwrite = _overwrite.SelectedIndex switch { 1 => OverwriteMode.Overwrite, 2 => OverwriteMode.Skip, 3 => OverwriteMode.Rename, _ => OverwriteMode.Ask };
        _s.AutoCheckUpdates = _autoUpdate.Checked;
        string tk = _token.Text.Trim();
        if (tk.Length > 0) { _s.UpdateTokenProtected = TokenProtector.Protect(tk); _s.LastUpdateCheckUtc = default; }
        else if (_clearToken) { _s.UpdateTokenProtected = ""; _s.LastUpdateCheckUtc = default; }
        DialogResult = DialogResult.OK;
    }
}
