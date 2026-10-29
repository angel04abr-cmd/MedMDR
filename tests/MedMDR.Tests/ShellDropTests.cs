using System.Runtime.InteropServices;
using MedMDR.Core;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace MedMDR.Tests;

/// <summary>
/// Prueba opt-in (MEDMDR_SHELL_DROP=1): suelta lo arrastrado sobre el destino de soltar REAL del Explorador de Windows para una carpeta
/// (la misma pieza de Windows que recibe un arrastre con el ratón) y comprueba que los ficheros aparecen en la carpeta con su contenido.
/// Necesita un escritorio interactivo y puede mostrar brevemente la ventana de progreso de Windows, por eso no corre sola.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class ShellDropTests
{
    [StructLayout(LayoutKind.Sequential)] private struct POINTL { public int x, y; }
    [StructLayout(LayoutKind.Sequential)] private struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public int px, py; }

    [ComImport, Guid("00000122-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IDropTarget
    {
        [PreserveSig] int DragEnter([MarshalAs(UnmanagedType.Interface)] ComIDataObject data, int keyState, POINTL pt, ref int effect);
        [PreserveSig] int DragOver(int keyState, POINTL pt, ref int effect);
        [PreserveSig] int DragLeave();
        [PreserveSig] int Drop([MarshalAs(UnmanagedType.Interface)] ComIDataObject data, int keyState, POINTL pt, ref int effect);
    }

    [ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(IntPtr hwnd, IntPtr bc, [MarshalAs(UnmanagedType.LPWStr)] string name, out uint eaten, out IntPtr pidl, ref uint attrs);
        [PreserveSig] int EnumObjects(IntPtr hwnd, int flags, out IntPtr enumIdList);
        [PreserveSig] int BindToObject(IntPtr pidl, IntPtr bc, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int BindToStorage(IntPtr pidl, IntPtr bc, [In] ref Guid riid, out IntPtr ppv);
        [PreserveSig] int CompareIDs(IntPtr lParam, IntPtr pidl1, IntPtr pidl2);
        [PreserveSig] int CreateViewObject(IntPtr hwndOwner, [In] ref Guid riid, out IntPtr ppv);
    }

    [DllImport("shell32.dll")] private static extern int SHGetDesktopFolder(out IShellFolder folder);
    [DllImport("user32.dll")] private static extern bool PeekMessage(out MSG msg, IntPtr hwnd, uint min, uint max, uint remove);
    [DllImport("user32.dll")] private static extern bool TranslateMessage(ref MSG msg);
    [DllImport("user32.dll")] private static extern IntPtr DispatchMessage(ref MSG msg);
    [DllImport("ole32.dll")] private static extern int OleInitialize(IntPtr reserved);

    private static void Pump(int ms)
    {
        var end = DateTime.UtcNow.AddMilliseconds(ms);
        while (DateTime.UtcNow < end)
        {
            while (PeekMessage(out var m, IntPtr.Zero, 0, 0, 1)) { TranslateMessage(ref m); DispatchMessage(ref m); }
            Thread.Sleep(20);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_real_windows_folder_drop_target_copies_the_virtual_files(bool solid)
    {
        if (Environment.GetEnvironmentVariable("MEDMDR_SHELL_DROP") != "1") return;       // prueba manual: ver el resumen de la clase

        string? failure = null;
        var t = new Thread(() =>
        {
            try { failure = Run(solid); } catch (Exception ex) { failure = ex.ToString(); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        Assert.True(t.Join(TimeSpan.FromSeconds(120)), "el destino de Windows no terminó");
        Assert.Null(failure);
    }

    private static string? Run(bool solid)
    {
        OleInitialize(IntPtr.Zero);
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string archivePath = Data.Create(tmp, "t.mdr", new ArchiveOptions { Format = ArchiveFormat.Mdr, Solid = solid }, tree);
        using var archive = ArchiveOpener.Open(archivePath);
        var root = archive.Entries.First(e => e.IsDirectory && e.Path.TrimEnd('/') == "arbol");
        string dest = tmp.Combine("destino"); Directory.CreateDirectory(dest);

        using var src = new ArchiveDragSource(archive, new[] { root }, "", tmp.Combine("tmp"));
        using var data = new VirtualFilesDataObject(src.Items, src);

        SHGetDesktopFolder(out var desktop);
        uint attrs = 0;
        int hr = desktop.ParseDisplayName(IntPtr.Zero, IntPtr.Zero, dest, out _, out var pidl, ref attrs);
        if (hr != 0) return "ParseDisplayName 0x" + hr.ToString("X");
        var iidFolder = new Guid("000214E6-0000-0000-C000-000000000046");
        hr = desktop.BindToObject(pidl, IntPtr.Zero, ref iidFolder, out var pFolder);
        if (hr != 0) return "BindToObject 0x" + hr.ToString("X");
        var folder = (IShellFolder)Marshal.GetObjectForIUnknown(pFolder);
        var iidDrop = new Guid("00000122-0000-0000-C000-000000000046");
        hr = folder.CreateViewObject(IntPtr.Zero, ref iidDrop, out var pTarget);
        if (hr != 0) return "CreateViewObject(IDropTarget) 0x" + hr.ToString("X");
        var target = (IDropTarget)Marshal.GetObjectForIUnknown(pTarget);

        int effect = 1; var pt = new POINTL { x = 10, y = 10 };
        hr = target.DragEnter(data, 1 /* MK_LBUTTON */, pt, ref effect);
        if (hr != 0 || effect == 0) return $"DragEnter 0x{hr:X} efecto {effect} (el destino no acepta lo arrastrado)";
        effect = 1; target.DragOver(1, pt, ref effect);
        effect = 1;
        hr = target.Drop(data, 0, pt, ref effect);
        if (hr != 0) return "Drop 0x" + hr.ToString("X");

        // el Explorador copia con su propia ventana de progreso: se espera a que aparezcan los ficheros
        var deadline = DateTime.UtcNow.AddSeconds(60);
        string root2 = Path.Combine(dest, "arbol");
        while (DateTime.UtcNow < deadline)
        {
            Pump(300);
            if (Directory.Exists(root2) && Directory.EnumerateFiles(root2, "*", SearchOption.AllDirectories).Count() >= expected.Count) { Pump(500); break; }
        }
        if (!Directory.Exists(root2)) return "el Explorador no creó la carpeta «arbol» en el destino";
        if (!Directory.Exists(Path.Combine(root2, "vacia"))) return "falta la carpeta vacía";
        var got = Data.Snapshot(root2);
        if (got.Count != expected.Count) return $"llegaron {got.Count} ficheros de {expected.Count}";
        foreach (var kv in expected)
            if (!got.TryGetValue(kv.Key, out var bytes) || !kv.Value.AsSpan().SequenceEqual(bytes)) return "contenido distinto en " + kv.Key;
        Console.WriteLine($"DESTINO REAL DE WINDOWS: {got.Count} ficheros copiados y comparados byte a byte (sólido: {solid})");
        return null;
    }
}
