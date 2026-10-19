using System.Reflection;

namespace MedMDR.Wpf;

/// <summary>Nombre, autoría y enlaces del programa (un solo sitio para que los cambios de nombre no se repitan por todas partes).</summary>
internal static class AppInfo
{
    public const string Name = "MedMDR";
    public const string Author = "angel04abr-cmd";
    public const string RepoUrl = "https://github.com/angel04abr-cmd/Medrar";
    public static string Version => Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "";
}
