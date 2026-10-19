using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Datos que Windows conoce de cada tipo de fichero: el icono y el nombre del tipo («Documento de texto»…).</summary>
internal static class ShellInfo
{
    private const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_TYPENAME = 0x400, SHGFI_USEFILEATTRIBUTES = 0x10;
    private const uint FILE_ATTRIBUTE_NORMAL = 0x80;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr h);

    private static readonly Dictionary<string, ImageSource?> Icons = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> Types = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icono pequeño que Windows usa para esa extensión (null si no se pudo obtener).</summary>
    public static ImageSource? IconFor(string fileName)
    {
        string ext = Path.GetExtension(fileName);
        if (ext.Length == 0) ext = ".";
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase) || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;   // cada uno tiene el suyo; se usa el icono genérico
        if (Icons.TryGetValue(ext, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            var info = new SHFILEINFO();
            SHGetFileInfo("x" + ext, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
            if (info.hIcon != IntPtr.Zero)
            {
                var bmp = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze();
                img = bmp;
                DestroyIcon(info.hIcon);
            }
        }
        catch { }
        Icons[ext] = img;
        return img;
    }

    public static string TypeName(string name, bool isDirectory)
    {
        if (isDirectory) return "Carpeta de archivos";
        if (VolumeSet.IsVolumeName(name)) return "Volumen de archivo";
        string ext = Path.GetExtension(name).ToLowerInvariant();
        switch (ext)
        {
            case ".mdr": return "Archivo " + AppInfo.Name;
            case ".zip": return "Archivo ZIP";
            case ".rar": return "Archivo RAR";
            case ".7z": return "Archivo 7Z";
            case ".tar": return "Archivo TAR";
            case ".gz": case ".tgz": return "Archivo GZ";
            case ".bz2": return "Archivo BZ2";
            case ".xz": return "Archivo XZ";
            case ".cab": return "Archivo CAB";
            case ".iso": return "Imagen de disco ISO";
        }
        if (Types.TryGetValue(ext, out var t)) return t;
        string result = "Fichero";
        try
        {
            var info = new SHFILEINFO();
            SHGetFileInfo("x" + (ext.Length == 0 ? "." : ext), FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info), SHGFI_TYPENAME | SHGFI_USEFILEATTRIBUTES);
            if (!string.IsNullOrEmpty(info.szTypeName)) result = info.szTypeName;
        }
        catch { }
        Types[ext] = result;
        return result;
    }
}
