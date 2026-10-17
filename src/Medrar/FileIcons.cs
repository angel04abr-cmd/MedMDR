using System.Runtime.InteropServices;
using Medrar.Core;

namespace Medrar.App;

/// <summary>Iconos pequeños de la lista: los de Windows para cada tipo de fichero y los propios para carpetas y archivos.</summary>
internal static class FileIcons
{
    private const uint SHGFI_ICON = 0x100, SHGFI_SMALLICON = 0x1, SHGFI_USEFILEATTRIBUTES = 0x10;
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

    private static readonly Dictionary<string, int> Map = new(StringComparer.OrdinalIgnoreCase);
    public static ImageList List { get; } = new() { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
    public const int Folder = 0, Up = 1, Archive = 2, Generic = 3;

    static FileIcons()
    {
        List.Images.Add(Icons.Get(IconKind.Folder, 16));
        List.Images.Add(Icons.Get(IconKind.Up, 16));
        List.Images.Add(Icons.Get(IconKind.Archive, 16));
        List.Images.Add(SystemIcons.Application.ToBitmap().GetThumbnailImage(16, 16, null, IntPtr.Zero));
    }

    public static int For(string name, bool isDir)
    {
        if (isDir) return Folder;
        string ext = Path.GetExtension(name);
        if (VolumeSet.IsVolumeName(name)) return Archive;
        if (ArchiveOpener.SupportedExtensions.Contains(ext.ToLowerInvariant())) return Archive;
        if (ext.Length == 0) ext = ".";
        if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) || ext.Equals(".ico", StringComparison.OrdinalIgnoreCase)) return Generic;
        if (Map.TryGetValue(ext, out int idx)) return idx;
        idx = Generic;
        try
        {
            var info = new SHFILEINFO();
            SHGetFileInfo("x" + ext, FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info), SHGFI_ICON | SHGFI_SMALLICON | SHGFI_USEFILEATTRIBUTES);
            if (info.hIcon != IntPtr.Zero)
            {
                using (var ic = Icon.FromHandle(info.hIcon)) List.Images.Add(ic.ToBitmap());
                idx = List.Images.Count - 1;
                DestroyIcon(info.hIcon);
            }
        }
        catch { }
        Map[ext] = idx;
        return idx;
    }

    public static string TypeName(string name, bool isDir)
    {
        if (isDir) return "Carpeta de archivos";
        if (VolumeSet.IsVolumeName(name)) return "Volumen de archivo";
        switch (Path.GetExtension(name).ToLowerInvariant())
        {
            case ".mdr": return "Archivo Medrar";
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
        try
        {
            string ext = Path.GetExtension(name);
            var info = new SHFILEINFO();
            SHGetFileInfo("x" + (ext.Length == 0 ? "." : ext), FILE_ATTRIBUTE_NORMAL, ref info, (uint)Marshal.SizeOf(info), 0x400 | SHGFI_USEFILEATTRIBUTES);
            if (!string.IsNullOrEmpty(info.szTypeName)) return info.szTypeName;
        }
        catch { }
        return "Fichero";
    }
}

/// <summary>Envía ficheros a la Papelera de reciclaje.</summary>
internal static class Recycle
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode, Pack = 1)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd; public uint wFunc; public string pFrom; public string? pTo; public ushort fFlags;
        public bool fAnyOperationsAborted; public IntPtr hNameMappings; public string? lpszProgressTitle;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    public static bool Delete(IntPtr owner, IEnumerable<string> paths)
    {
        var op = new SHFILEOPSTRUCT { hwnd = owner, wFunc = 3 /*FO_DELETE*/, pFrom = string.Join("\0", paths) + "\0\0", fFlags = 0x40 | 0x10 /*ALLOWUNDO|NOCONFIRMATION*/ };
        return SHFileOperation(ref op) == 0 && !op.fAnyOperationsAborted;
    }
}
