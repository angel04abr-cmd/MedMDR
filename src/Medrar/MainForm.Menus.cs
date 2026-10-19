using Medrar.Core;

namespace Medrar.App;

internal sealed partial class MainForm
{
    private ToolStripMenuItem _recentMenu = null!;
    private ToolStripMenuItem _favMenu = null!;
    private readonly Dictionary<string, ToolStripButton> _btn = new();

    private static ToolStripMenuItem Item(string text, Keys keys, EventHandler click)
    {
        var m = new ToolStripMenuItem(text, null, click);
        if (keys != Keys.None) m.ShortcutKeys = keys;
        return m;
    }

    private void BuildMenu()
    {
        var file = new ToolStripMenuItem("&Archivo");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Abrir archivo…", Keys.Control | Keys.O, (_, _) => OpenArchiveDialog()),
            Item("Cambiar &unidad", Keys.Control | Keys.D, (_, _) => ChangeDrive()),
            Item("Contraseña para esta &sesión…", Keys.Control | Keys.P, (_, _) => SetSessionPassword()),
            new ToolStripSeparator(),
            Item("&Copiar ficheros", Keys.Control | Keys.C, (_, _) => CopyFiles()),
            Item("&Pegar ficheros", Keys.Control | Keys.V, (_, _) => PasteFiles()),
            Item("Copiar nombres co&mpletos", Keys.None, (_, _) => CopyNames()),
            new ToolStripSeparator(),
            Item("Seleccionar &todo", Keys.Control | Keys.A, (_, _) => SelectAll()),
            Item("Seleccionar &grupo…   (tecla +)", Keys.None, (_, _) => SelectGroup(true)),
            Item("&Quitar selección de grupo…   (tecla -)", Keys.None, (_, _) => SelectGroup(false)),
            Item("&Invertir selección   (tecla *)", Keys.None, (_, _) => InvertSelection()),
            new ToolStripSeparator(),
        });
        _recentMenu = new ToolStripMenuItem("Archivos &recientes");
        file.DropDownItems.Add(_recentMenu);
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(Item("&Salir", Keys.Alt | Keys.F4, (_, _) => Close()));
        RebuildRecentMenu();

        var cmd = new ToolStripMenuItem("&Órdenes");
        cmd.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Añadir ficheros al archivo", Keys.Alt | Keys.A, (_, _) => AddCommand()),
            Item("&Extraer en la carpeta especificada", Keys.Alt | Keys.E, (_, _) => ExtractCommand()),
            Item("&Comprobar ficheros comprimidos", Keys.Alt | Keys.T, (_, _) => TestCommand()),
            new ToolStripSeparator(),
            Item("&Ver fichero", Keys.Alt | Keys.V, (_, _) => ViewCommand()),
            Item("E&liminar ficheros", Keys.Delete, (_, _) => DeleteSelected()),
            Item("&Renombrar ficheros", Keys.F2, (_, _) => RenameSelected()),
            new ToolStripSeparator(),
            Item("Extraer sin pedir &confirmación", Keys.Alt | Keys.W, (_, _) => ExtractCommand(ask: false)),
            Item("Agregar co&mentario al archivo", Keys.Alt | Keys.M, (_, _) => CommentCommand()),
            Item("&Proteger el archivo contra daños", Keys.Alt | Keys.P, (_, _) => ProtectCommand()),
            Item("&Bloquear / desbloquear el archivo", Keys.Alt | Keys.L, (_, _) => LockCommand()),
        });

        var tools = new ToolStripMenuItem("&Herramientas");
        tools.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Asistente…", Keys.None, (_, _) => WizardCommand()),
            new ToolStripSeparator(),
            Item("Convertir &archivos…", Keys.Alt | Keys.Q, (_, _) => ConvertCommand()),
            Item("&Reparar archivo…", Keys.Alt | Keys.R, (_, _) => RepairCommand()),
            Item("Convertir en autoe&xtraíble…", Keys.Alt | Keys.X, (_, _) => SfxCommand()),
            Item("&Quitar el módulo autoextraíble…", Keys.None, (_, _) => UnpackSfxCommand()),
            new ToolStripSeparator(),
            Item("&Buscar ficheros…", Keys.F3, (_, _) => FindCommand()),
            Item("Mostrar &información", Keys.Alt | Keys.I, (_, _) => InfoCommand()),
            Item("&Generar informe…", Keys.None, (_, _) => ReportCommand()),
            Item("&Prueba de velocidad…", Keys.Alt | Keys.B, (_, _) => { using var d = new BenchmarkDialog(); d.ShowDialog(this); }),
        });

        _favMenu = new ToolStripMenuItem("&Favoritos");
        RebuildFavorites();

        var options = new ToolStripMenuItem("&Opciones");
        var importExport = new ToolStripMenuItem("&Importar y exportar");
        importExport.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Exportar la configuración a un fichero…", Keys.None, (_, _) => ExportSettings()),
            Item("&Importar la configuración desde un fichero…", Keys.None, (_, _) => ImportSettings()),
            Item("&Restablecer la configuración", Keys.None, (_, _) => ResetSettings()),
        });
        var view = new ToolStripMenuItem("&Lista de ficheros");
        var flat = new ToolStripMenuItem("Carpetas y ficheros mezclados") { CheckOnClick = true, Checked = _settings.FlatFolderView };
        flat.CheckedChanged += (_, _) => { _settings.FlatFolderView = flat.Checked; Program.SaveSettings(); _list.Sort(); };
        view.DropDownItems.Add(flat);
        var themes = new ToolStripMenuItem("&Temas");
        foreach (var (label, mode) in new[] { ("Predeterminado del sistema", ThemeMode.System), ("Claro", ThemeMode.Light), ("Oscuro", ThemeMode.Dark) })
            themes.DropDownItems.Add(label, null, (_, _) => ChangeTheme(mode));
        options.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Configuración…", Keys.Control | Keys.S, (_, _) => SettingsCommand()),
            importExport,
            Item("&Borrar el historial", Keys.None, (_, _) => { _settings.History.Clear(); _settings.RecentArchives.Clear(); Program.SaveSettings(); RebuildRecentMenu(); }),
            new ToolStripSeparator(),
            view, themes,
        });

        var help = new ToolStripMenuItem("Ay&uda");
        help.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&Cómo se usa Medrar", Keys.F1, (_, _) => ViewerForm.ShowText(this, "Ayuda de Medrar", HelpText.Text, _settings)),
            Item("Buscar a&ctualizaciones…", Keys.None, async (_, _) => await Updates.CheckNowAsync(this, _settings)),
            new ToolStripSeparator(),
            Item("&Acerca de Medrar…", Keys.None, (_, _) => { using var d = new AboutDialog(); d.ShowDialog(this); }),
        });

        _menu.Items.AddRange(new ToolStripItem[] { file, cmd, tools, _favMenu, options, help });
    }

    private void RebuildRecentMenu()
    {
        _recentMenu.DropDownItems.Clear();
        foreach (var p in _settings.RecentArchives.Where(File.Exists).Take(10))
            _recentMenu.DropDownItems.Add(p, null, (_, _) => OpenArchive(p));
        _recentMenu.Enabled = _recentMenu.DropDownItems.Count > 0;
        Theme.StyleMenu(_recentMenu.DropDownItems);
    }

    private void RebuildFavorites()
    {
        _favMenu.DropDownItems.Clear();
        _favMenu.DropDownItems.Add(Item("&Añadir a favoritos…", Keys.Control | Keys.F, (_, _) => AddFavorite()));
        _favMenu.DropDownItems.Add(Item("&Organizar favoritos…", Keys.None, (_, _) =>
        {
            using var d = new FavoritesDialog(_settings);
            if (d.ShowDialog(this) == DialogResult.OK && d.Go != null) GoToPath(d.Go);
            RebuildFavorites();
        }));
        if (_settings.Favorites.Count > 0) _favMenu.DropDownItems.Add(new ToolStripSeparator());
        foreach (var f in _settings.Favorites)
        {
            var path = f.Path;
            _favMenu.DropDownItems.Add(f.Name, null, (_, _) => GoToPath(path));
        }
        Theme.StyleMenu(_favMenu.DropDownItems);
    }

    private void BuildToolbar()
    {
        _tools.GripStyle = ToolStripGripStyle.Hidden;
        _tools.ImageScalingSize = _settings.LargeIcons ? new Size(32, 32) : new Size(20, 20);
        _tools.Padding = new Padding(8, 4, 8, 4);
        void Add(string key, string text, IconKind icon, string tip, Action act)
        {
            var b = new ToolStripButton(_settings.ShowButtonText ? text : "", Icons.Get(icon, _settings.LargeIcons ? 32 : 20), (_, _) => act())
            {
                TextImageRelation = TextImageRelation.ImageAboveText, ToolTipText = tip, AutoToolTip = false, Margin = new Padding(2, 2, 2, 2), Padding = new Padding(8, 4, 8, 4),
                ImageScaling = ToolStripItemImageScaling.None
            };
            _btn[key] = b;
            _tools.Items.Add(b);
        }
        Add("add", "Añadir", IconKind.Add, "Añadir ficheros a un archivo (Alt+A)", AddCommand);
        Add("extract", "Extraer en", IconKind.Extract, "Extraer en la carpeta especificada (Alt+E)", () => ExtractCommand());
        Add("test", "Comprobar", IconKind.Test, "Comprobar los ficheros comprimidos (Alt+T)", TestCommand);
        Add("view", "Ver", IconKind.View, "Ver el fichero seleccionado (Alt+V)", ViewCommand);
        Add("delete", "Eliminar", IconKind.Delete, "Eliminar (Supr)", DeleteSelected);
        Add("find", "Buscar", IconKind.Find, "Buscar ficheros (F3)", FindCommand);
        Add("wizard", "Asistente", IconKind.Wizard, "Asistente paso a paso", WizardCommand);
        Add("info", "Información", IconKind.Info, "Mostrar información (Alt+I)", InfoCommand);
        _tools.Items.Add(new ToolStripSeparator());
        Add("repair", "Reparar", IconKind.Repair, "Reparar un archivo dañado (Alt+R)", RepairCommand);
    }

    private void UpdateToolbarState()
    {
        if (_btn.Count == 0) return;
        var sel = SelectedNodes().ToList();
        bool anyArchiveSelected = !InArchive && sel.Any(n => !n.IsDir && ArchiveOpener.LooksLikeArchive(n.FullPath));
        _btn["add"].Enabled = true;
        _btn["extract"].Enabled = InArchive || anyArchiveSelected;
        _btn["test"].Enabled = InArchive || anyArchiveSelected;
        _btn["view"].Enabled = sel.Any(n => !n.IsDir);
        _btn["delete"].Enabled = sel.Count > 0;
        _btn["info"].Enabled = InArchive || anyArchiveSelected;
    }

    private void BuildContextMenu()
    {
        _ctx.Items.Clear();
        var sel = SelectedNodes().ToList();
        bool one = sel.Count == 1;
        if (InArchive)
        {
            if (sel.Count > 0)
            {
                _ctx.Items.Add("Abrir", null, (_, _) => ActivateSelected());
                _ctx.Items.Add("Extraer en una carpeta…", null, (_, _) => ExtractCommand());
                _ctx.Items.Add("Extraer aquí (junto al archivo)", null, (_, _) => ExtractHere());
                _ctx.Items.Add("Ver", null, (_, _) => ViewCommand());
                _ctx.Items.Add(new ToolStripSeparator());
                _ctx.Items.Add("Renombrar", null, (_, _) => RenameSelected());
                _ctx.Items.Add("Eliminar del archivo", null, (_, _) => DeleteSelected());
                _ctx.Items.Add(new ToolStripSeparator());
            }
            _ctx.Items.Add("Añadir ficheros al archivo…", null, (_, _) => AddCommand());
            _ctx.Items.Add("Comprobar el archivo", null, (_, _) => TestCommand());
            _ctx.Items.Add("Información del archivo", null, (_, _) => InfoCommand());
            _ctx.Items.Add(new ToolStripSeparator());
            _ctx.Items.Add("Cerrar el archivo", null, (_, _) => CloseArchive(true));
        }
        else if (sel.Count > 0)
        {
            bool isArc = one && !sel[0].IsDir && ArchiveOpener.LooksLikeArchive(sel[0].FullPath);
            _ctx.Items.Add("Abrir", null, (_, _) => ActivateSelected());
            if (isArc)
            {
                _ctx.Items.Add("Extraer en una carpeta…", null, (_, _) => ExtractCommand());
                _ctx.Items.Add("Extraer aquí", null, (_, _) => ExtractHere());
                _ctx.Items.Add("Comprobar", null, (_, _) => TestCommand());
                _ctx.Items.Add("Reparar…", null, (_, _) => RepairCommand());
            }
            _ctx.Items.Add("Añadir a un archivo…", null, (_, _) => AddCommand());
            _ctx.Items.Add(new ToolStripSeparator());
            _ctx.Items.Add("Copiar", null, (_, _) => CopyFiles());
            _ctx.Items.Add("Renombrar", null, (_, _) => RenameSelected());
            _ctx.Items.Add("Eliminar (a la Papelera)", null, (_, _) => DeleteSelected());
            _ctx.Items.Add(new ToolStripSeparator());
            _ctx.Items.Add("Mostrar en el Explorador", null, (_, _) => Ops.OpenFolderOf(sel[0].FullPath));
        }
        else
        {
            _ctx.Items.Add("Pegar", null, (_, _) => PasteFiles());
            _ctx.Items.Add("Actualizar", null, (_, _) => GoFolder(_folder));
        }
        Theme.StyleMenu(_ctx.Items);
        _ctx.BackColor = Theme.PanelBack; _ctx.ForeColor = Theme.Fore;
    }
}

internal static class HelpText
{
    public const string Text = """
        CÓMO SE USA MEDRAR
        ==================

        EXPLORAR
          La ventana principal es un explorador de archivos. Haz doble clic en una carpeta para entrar y en un
          archivo comprimido para abrirlo como si fuera una carpeta. Retroceso sube un nivel. También puedes
          escribir una ruta en la barra de direcciones.

        CREAR UN ARCHIVO
          1. Selecciona ficheros o carpetas (o arrástralos a la ventana).
          2. Pulsa «Añadir» (Alt+A).
          3. Elige el nombre, el formato y las opciones y pulsa Aceptar.
          Formatos: .mdr (el de Medrar: cifrado AES-256, recuperación, volúmenes, autoextraíble), ZIP, TAR, TAR.GZ y GZ.

        EXTRAER
          Abre el archivo (o selecciónalo) y pulsa «Extraer en» (Alt+E). Elige la carpeta y cómo tratar los ficheros
          que ya existan. Alt+W extrae sin hacer preguntas.

        CONTRASEÑA (solo .mdr)
          En «Añadir» pulsa «Establecer contraseña». Si marcas «Cifrar también los nombres», nadie podrá ver ni
          la lista de ficheros sin la contraseña. Medrar NO guarda contraseñas en el disco.

        PROTEGER Y REPARAR
          «Registro de recuperación» añade datos extra para poder reparar un archivo dañado (Herramientas >
          Reparar archivo). La reparación crea una copia y no toca el original. Si hay demasiado daño, no se
          puede recuperar.

        VOLÚMENES Y AUTOEXTRAÍBLES
          «Dividir en volúmenes» parte el archivo en trozos (.001, .002…). Abre el .001 y Medrar los une solo.
          Un archivo autoextraíble (.exe) se abre sin Medrar en equipos con .NET 10.

        OTROS FORMATOS
          Medrar abre RAR, 7Z, BZ2, XZ, LZ, CAB, ISO y más. Crea .mdr, ZIP (también con contraseña AES-256), 7Z, TAR, TAR.GZ y GZ.
          Medrar no puede CREAR archivos RAR (el formato es propietario): usa 7Z o ZIP para compartir.

        ATAJOS
          Alt+A añadir · Alt+E extraer · Alt+T comprobar · Alt+V ver · Supr eliminar · F2 renombrar
          F3 buscar · Alt+I información · Alt+R reparar · Alt+Q convertir · Ctrl+O abrir · Ctrl+S configuración

        LÍNEA DE COMANDOS
          mdr.exe a|x|t|l|d|c|r|s|i|b …   (escribe «mdr -h» para ver todas las órdenes)
        """;
}
