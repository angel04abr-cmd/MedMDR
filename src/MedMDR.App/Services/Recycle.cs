using System.Runtime.InteropServices;

namespace MedMDR.Wpf;

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
