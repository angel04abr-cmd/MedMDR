using System.Runtime.InteropServices;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>
/// Arrastre de Windows (OLE) con un objeto de datos propio. WPF solo sabe arrastrar datos que ya existen en memoria; aquí hace falta
/// entregar ficheros virtuales (<see cref="VirtualFilesDataObject"/>), así que se llama directamente a DoDragDrop de Windows.
/// </summary>
internal static class OleDrag
{
    [ComImport, Guid("00000121-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropSource
    {
        [PreserveSig] int QueryContinueDrag([MarshalAs(UnmanagedType.Bool)] bool fEscapePressed, int grfKeyState);
        [PreserveSig] int GiveFeedback(int dwEffect);
    }

    private sealed class DropSource : IDropSource
    {
        private const int MK_LBUTTON = 1, DRAGDROP_S_DROP = 0x40100, DRAGDROP_S_CANCEL = 0x40101, DRAGDROP_S_USEDEFAULTCURSORS = 0x40102;
        public int QueryContinueDrag(bool fEscapePressed, int grfKeyState) =>
            fEscapePressed ? DRAGDROP_S_CANCEL : (grfKeyState & MK_LBUTTON) == 0 ? DRAGDROP_S_DROP : 0;
        public int GiveFeedback(int dwEffect) => DRAGDROP_S_USEDEFAULTCURSORS;
    }

    [DllImport("ole32.dll")]
    private static extern int DoDragDrop([MarshalAs(UnmanagedType.Interface)] System.Runtime.InteropServices.ComTypes.IDataObject dataObject,
                                         [MarshalAs(UnmanagedType.Interface)] IDropSource dropSource, int okEffects, out int effect);

    /// <summary>Empieza el arrastre y no vuelve hasta que se suelta o se cancela. Devuelve el efecto (0 = nada se copió).</summary>
    public static int Start(VirtualFilesDataObject data)
    {
        DoDragDrop(data, new DropSource(), 1 /* DROPEFFECT_COPY */, out int effect);
        return effect;
    }
}
