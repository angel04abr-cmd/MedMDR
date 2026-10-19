using System.IO;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Anota los errores inesperados en %APPDATA%\Medrar\error.log (el mismo fichero que usa Medrar.exe).</summary>
internal static class ErrorLog
{
    public static void Write(Exception ex)
    {
        try
        {
            string log = Path.Combine(Path.GetDirectoryName(SettingsStore.DefaultPath)!, "error.log");
            Directory.CreateDirectory(Path.GetDirectoryName(log)!);
            File.AppendAllText(log, $"[{DateTime.Now:s}] (WPF) {ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* sin permisos: no es crítico */ }
    }
}
