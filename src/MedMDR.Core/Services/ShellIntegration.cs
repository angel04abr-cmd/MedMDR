using Microsoft.Win32;

namespace MedMDR.Core;

/// <summary>
/// Integración con el Explorador de Windows (menú de clic derecho y asociación de .mdr).
/// Solo escribe en HKEY_CURRENT_USER, así que no necesita permisos de administrador y se deshace por completo.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ShellIntegration
{
    private const string Verb = "MedMDR";

    /// <summary>Formatos para los que MedMDR se ofrece en «Abrir con».</summary>
    public static readonly string[] OpenWithExtensions = { ".mdr", ".zip", ".7z", ".rar", ".tar", ".gz", ".tgz", ".bz2", ".xz", ".lz", ".zst", ".cab", ".iso", ".jar", ".001" };
    private readonly string _classes;

    /// <param name="classesRoot">Ruta bajo HKCU (por defecto Software\Classes). Se cambia en las pruebas.</param>
    public ShellIntegration(string classesRoot = @"Software\Classes") => _classes = classesRoot;

    private RegistryKey Root => Registry.CurrentUser;

    private void SetCommand(string subKey, string title, string command, string icon)
    {
        using var k = Root.CreateSubKey($@"{_classes}\{subKey}");
        k.SetValue("", title);
        k.SetValue("MUIVerb", title);
        k.SetValue("Icon", icon);
        using var c = k.CreateSubKey("command");
        c.SetValue("", command);
    }

    public void Install(string exePath, bool associateMdr = true, bool contextMenu = true)
    {
        string icon = $"\"{exePath}\",0";
        if (contextMenu)
        {
            SetCommand($@"*\shell\{Verb}.Add", "Añadir a un archivo con MedMDR…", $"\"{exePath}\" --add \"%1\"", icon);
            SetCommand($@"Directory\shell\{Verb}.Add", "Añadir a un archivo con MedMDR…", $"\"{exePath}\" --add \"%1\"", icon);
            SetCommand($@"Directory\Background\shell\{Verb}.Open", "Abrir esta carpeta en MedMDR", $"\"{exePath}\" \"%V\"", icon);
        }
        // MedMDR como programa disponible en «Abrir con» para los formatos habituales (no cambia el programa predeterminado)
        using (var prog = Root.CreateSubKey($@"{_classes}\MedMDR.Archive"))
        {
            prog.SetValue("", "Archivo MedMDR");
            using var di = prog.CreateSubKey("DefaultIcon"); di.SetValue("", icon);
        }
        SetCommand(@"MedMDR.Archive\shell\open", "Abrir con MedMDR", $"\"{exePath}\" \"%1\"", icon);
        SetCommand(@"MedMDR.Archive\shell\extracthere", "Extraer aquí", $"\"{exePath}\" --extract-here \"%1\"", icon);
        SetCommand(@"MedMDR.Archive\shell\extractnamed", "Extraer en carpeta con el nombre del archivo", $"\"{exePath}\" --extract-named \"%1\"", icon);
        SetCommand(@"MedMDR.Archive\shell\extractto", "Extraer en una carpeta…", $"\"{exePath}\" --extract-to \"%1\"", icon);
        SetCommand(@"MedMDR.Archive\shell\test", "Comprobar el archivo", $"\"{exePath}\" --test \"%1\"", icon);
        string exeName = Path.GetFileName(exePath);
        SetCommand($@"Applications\{exeName}\shell\open", "Abrir con MedMDR", $"\"{exePath}\" \"%1\"", icon);
        using (var types = Root.CreateSubKey($@"{_classes}\Applications\{exeName}\SupportedTypes"))
            foreach (var ext in OpenWithExtensions) types.SetValue(ext, "");
        foreach (var ext in OpenWithExtensions)
        {
            using var k = Root.CreateSubKey($@"{_classes}\{ext}\OpenWithProgids");
            k.SetValue("MedMDR.Archive", "");
        }
        if (associateMdr)
        {
            using var mdr = Root.CreateSubKey($@"{_classes}\.mdr");
            mdr.SetValue("", "MedMDR.Archive");
        }
    }

    public void Uninstall()
    {
        foreach (var ext in OpenWithExtensions)
        {
            try { using var k = Root.OpenSubKey($@"{_classes}\{ext}\OpenWithProgids", writable: true); k?.DeleteValue("MedMDR.Archive", throwOnMissingValue: false); } catch { }
        }
        foreach (var k in new[]
        {
            $@"*\shell\{Verb}.Add", $@"Directory\shell\{Verb}.Add", $@"Directory\Background\shell\{Verb}.Open",
            "MedMDR.Archive", ".mdr", @"Applications\MedMDR.exe"
        })
        {
            try { Root.DeleteSubKeyTree($@"{_classes}\{k}", throwOnMissingSubKey: false); } catch { }
        }
    }

    public bool IsContextMenuInstalled()
    {
        using var k = Root.OpenSubKey($@"{_classes}\*\shell\{Verb}.Add");
        return k != null;
    }

    public bool IsAssociated()
    {
        using var k = Root.OpenSubKey($@"{_classes}\.mdr");
        return (k?.GetValue("") as string) == "MedMDR.Archive";
    }
}
