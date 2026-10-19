using System.IO;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Anota los errores inesperados en %APPDATA%\MedMDR\error.log (el mismo fichero que usa MedMDR.exe).</summary>
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
