using Microsoft.Win32;

namespace Medrar.Core;

/// <summary>
/// Integración con el Explorador de Windows (menú de clic derecho y asociación de .mdr).
/// Solo escribe en HKEY_CURRENT_USER, así que no necesita permisos de administrador y se deshace por completo.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class ShellIntegration
{
    private const string Verb = "Medrar";
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
        if (associateMdr)
        {
            using (var ext = Root.CreateSubKey($@"{_classes}\.mdr")) ext.SetValue("", "Medrar.Archive");
            using (var prog = Root.CreateSubKey($@"{_classes}\Medrar.Archive"))
            {
                prog.SetValue("", "Archivo MedMDR");
                using var di = prog.CreateSubKey("DefaultIcon"); di.SetValue("", icon);
            }
            SetCommand(@"Medrar.Archive\shell\open", "Abrir con MedMDR", $"\"{exePath}\" \"%1\"", icon);
            SetCommand(@"Medrar.Archive\shell\extracthere", "Extraer aquí", $"\"{exePath}\" --extract-here \"%1\"", icon);
            SetCommand(@"Medrar.Archive\shell\extractto", "Extraer en una carpeta…", $"\"{exePath}\" --extract-to \"%1\"", icon);
            SetCommand(@"Medrar.Archive\shell\test", "Comprobar el archivo", $"\"{exePath}\" --test \"%1\"", icon);
        }
    }

    public void Uninstall()
    {
        foreach (var k in new[]
        {
            $@"*\shell\{Verb}.Add", $@"Directory\shell\{Verb}.Add", $@"Directory\Background\shell\{Verb}.Open",
            "Medrar.Archive", ".mdr"
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
        return (k?.GetValue("") as string) == "Medrar.Archive";
    }
}
