using System.Runtime.InteropServices;

namespace MedMDR.Wpf;

/// <summary>Envía ficheros a la Papelera de reciclaje.</summary>
internal static class Recycle
{
    // En 64 bits esta estructura NO va empaquetada (solo en 32 bits lo está): con Pack = 1 los campos quedaban desalineados y
    // Windows leía memoria que no era suya, lo que cerraba el programa al borrar. BOOL son 4 bytes (int), no un bool de .NET.
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEOPSTRUCT
    {
        public IntPtr hwnd;
        public uint wFunc;
        public string pFrom;
        public string? pTo;
        public ushort fFlags;
        public int fAnyOperationsAborted;
        public IntPtr hNameMappings;
        public string? lpszProgressTitle;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

    private const uint FO_DELETE = 3;
    private const ushort FOF_ALLOWUNDO = 0x40, FOF_NOCONFIRMATION = 0x10, FOF_NOERRORUI = 0x400, FOF_WANTNUKEWARNING = 0x4000;

    /// <summary>Manda los ficheros o carpetas a la Papelera (se pueden recuperar). Devuelve false si no se pudo con todos.</summary>
    public static bool Delete(IntPtr owner, IEnumerable<string> paths)
    {
        var list = paths.Where(p => !string.IsNullOrEmpty(p)).Select(Path.GetFullPath).ToList();
        if (list.Count == 0) return true;
        var op = new SHFILEOPSTRUCT
        {
            hwnd = owner, wFunc = FO_DELETE, pFrom = string.Join("\0", list) + "\0\0",
            fFlags = FOF_ALLOWUNDO | FOF_NOCONFIRMATION | FOF_NOERRORUI | FOF_WANTNUKEWARNING   // si no cupiera en la Papelera, Windows pregunta en vez de borrar para siempre
        };
        return SHFileOperation(ref op) == 0 && op.fAnyOperationsAborted == 0;
    }
}
