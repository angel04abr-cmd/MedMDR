using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using MedMDR.Core;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace MedMDR.Tests;

/// <summary>
/// Arrastre hacia el Explorador con ficheros virtuales. Estas pruebas hacen de «destino»: piden al objeto de datos lo mismo que pide el
/// Explorador al soltar (lista de ficheros y, uno a uno, su contenido) y comprueban que llega exactamente lo que hay en el archivo.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class VirtualDragTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM m);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FILEDESCRIPTORW
    {
        public uint dwFlags; public Guid clsid; public int sizelcx, sizelcy, pointlx, pointly; public uint dwFileAttributes;
        public FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime; public uint nFileSizeHigh, nFileSizeLow;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
    }

    private sealed record Described(string Name, bool IsDirectory, long Size);

    private static short Cf(string name) => unchecked((short)RegisterClipboardFormatW(name));

    private static FORMATETC Fmt(string name, TYMED tymed, int lindex = -1) =>
        new() { cfFormat = Cf(name), ptd = IntPtr.Zero, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = lindex, tymed = tymed };

    private static List<Described> Descriptors(ComIDataObject data)
    {
        var f = Fmt("FileGroupDescriptorW", TYMED.TYMED_HGLOBAL);
        data.GetData(ref f, out var m);
        var list = new List<Described>();
        IntPtr p = GlobalLock(m.unionmember);
        try
        {
            int n = Marshal.ReadInt32(p), size = Marshal.SizeOf<FILEDESCRIPTORW>();
            for (int i = 0; i < n; i++)
            {
                var d = Marshal.PtrToStructure<FILEDESCRIPTORW>(p + 4 + i * size);
                list.Add(new Described(d.cFileName, (d.dwFileAttributes & 0x10) != 0, ((long)d.nFileSizeHigh << 32) | d.nFileSizeLow));
            }
        }
        finally { GlobalUnlock(m.unionmember); ReleaseStgMedium(ref m); }
        return list;
    }

    private static byte[] Contents(ComIDataObject data, int index)
    {
        var f = Fmt("FileContents", TYMED.TYMED_ISTREAM, index);
        data.GetData(ref f, out var m);
        var stream = (IStream)Marshal.GetObjectForIUnknown(m.unionmember);
        try
        {
            stream.Stat(out var st, 0);
            var all = new MemoryStream();
            var buf = new byte[64 * 1024];
            IntPtr read = Marshal.AllocHGlobal(4);
            try
            {
                for (;;)
                {
                    stream.Read(buf, buf.Length, read);
                    int n = Marshal.ReadInt32(read);
                    if (n == 0) break;
                    all.Write(buf, 0, n);
                }
            }
            finally { Marshal.FreeHGlobal(read); }
            Assert.Equal(st.cbSize, all.Length);                    // el tamaño que declara el flujo es el real
            return all.ToArray();
        }
        finally { Marshal.Release(m.unionmember); }
    }

    private static (IArchive Archive, Dictionary<string, byte[]> Expected, TempDir Tmp) Prepare(ArchiveOptions o)
    {
        var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string archive = Data.Create(tmp, "t" + ArchiveBuilder.Extension(o.Format), o, tree);
        return (ArchiveOpener.Open(archive, o.Password), expected, tmp);
    }

    public static IEnumerable<object[]> Formats() => new[]
    {
        new object[] { ArchiveFormat.Mdr, false }, new object[] { ArchiveFormat.Mdr, true },     // normal y sólido
        new object[] { ArchiveFormat.Zip, false }, new object[] { ArchiveFormat.SevenZip, true }, new object[] { ArchiveFormat.TarGz, false }
    };

    [Theory]
    [MemberData(nameof(Formats))]
    public void Whole_folder_arrives_with_names_folders_and_exact_contents(ArchiveFormat format, bool solid)
    {
        var (archive, expected, tmp) = Prepare(new ArchiveOptions { Format = format, Solid = solid });
        using var _ = tmp; using var a = archive;
        var root = a.Entries.First(e => e.IsDirectory && e.Path.TrimEnd('/') == "arbol");
        using var src = new ArchiveDragSource(a, new[] { root }, "", tmp.Combine("tmp"));
        using var data = new VirtualFilesDataObject(src.Items, src);

        var d = Descriptors(data);
        Assert.Contains(d, x => x.Name == "arbol" && x.IsDirectory);
        Assert.Contains(d, x => x.Name == @"arbol\vacia" && x.IsDirectory);                       // las carpetas vacías también llegan
        Assert.Contains(d, x => x.Name == @"arbol\sub\otro ñ.txt" && !x.IsDirectory);             // con ñ y espacios
        var files = d.Select((x, i) => (x, i)).Where(t => !t.x.IsDirectory).ToList();
        Assert.Equal(expected.Count, files.Count);
        foreach (var (x, i) in files)
        {
            byte[] got = Contents(data, i);
            Assert.True(expected[x.Name.Replace('\\', '/')["arbol/".Length..]].AsSpan().SequenceEqual(got), "contenido distinto en " + x.Name);
            Assert.Equal(got.Length, x.Size);                                                      // el tamaño anunciado es el real
        }
    }

    [Fact]
    public void A_file_inside_the_current_folder_arrives_with_a_relative_name_only()
    {
        var (archive, expected, tmp) = Prepare(new ArchiveOptions { Format = ArchiveFormat.Mdr });
        using var _ = tmp; using var a = archive;
        var file = a.Entries.First(e => e.Path == "arbol/sub/otro ñ.txt");
        using var src = new ArchiveDragSource(a, new[] { file }, "arbol/sub", tmp.Combine("tmp"));
        using var data = new VirtualFilesDataObject(src.Items, src);

        var d = Descriptors(data);
        var one = Assert.Single(d);
        Assert.Equal("otro ñ.txt", one.Name);
        Assert.True(expected["sub/otro ñ.txt"].AsSpan().SequenceEqual(Contents(data, 0)));
    }

    [Fact]
    public void Nothing_is_extracted_until_the_target_asks_and_temp_files_disappear_on_dispose()
    {
        var (archive, _, tmp) = Prepare(new ArchiveOptions { Format = ArchiveFormat.Zip });
        using var _t = tmp; using var a = archive;
        string tempRoot = tmp.Combine("tmp");
        var root = a.Entries.First(e => e.IsDirectory && e.Path.TrimEnd('/') == "arbol");
        var src = new ArchiveDragSource(a, new[] { root }, "", tempRoot);
        var data = new VirtualFilesDataObject(src.Items, src);

        Descriptors(data);                                                                           // empezar a arrastrar no extrae nada
        Assert.False(Directory.Exists(tempRoot) && Directory.EnumerateFileSystemEntries(tempRoot, "*", SearchOption.AllDirectories).Any());

        int first = Descriptors(data).FindIndex(x => !x.IsDirectory);
        Contents(data, first);                                                                       // al pedir un fichero, se extrae
        Assert.True(Directory.Exists(tempRoot) && Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories).Any());

        data.Dispose();                                                                              // al terminar el arrastre, se limpia
        Assert.False(Directory.Exists(tempRoot) && Directory.EnumerateFiles(tempRoot, "*", SearchOption.AllDirectories).Any());
    }

    [Fact]
    public void Missing_password_gives_the_target_an_error_instead_of_garbage()
    {
        var (_, _, tmp) = Prepare(new ArchiveOptions { Format = ArchiveFormat.Zip, Password = "clave1" });
        using var _t = tmp;
        using var noPw = ArchiveOpener.Open(Directory.GetFiles(tmp.Path, "t.zip").Single());        // abierto sin la clave
        var file = noPw.Entries.First(e => !e.IsDirectory);
        using var src = new ArchiveDragSource(noPw, new[] { file }, "arbol", tmp.Combine("tmp"));
        using var data = new VirtualFilesDataObject(src.Items, src);

        var f = Fmt("FileContents", TYMED.TYMED_ISTREAM, 0);
        var ex = Assert.Throws<COMException>(() => data.GetData(ref f, out _));
        Assert.NotEqual(0, ex.HResult);
    }

    [Fact]
    public void Unsupported_requests_are_refused_with_the_standard_error_codes()
    {
        var (archive, _, tmp) = Prepare(new ArchiveOptions { Format = ArchiveFormat.Mdr });
        using var _t = tmp; using var a = archive;
        var file = a.Entries.First(e => !e.IsDirectory);
        using var src = new ArchiveDragSource(a, new[] { file }, "", tmp.Combine("tmp"));
        using var data = new VirtualFilesDataObject(src.Items, src);

        var desc = Fmt("FileGroupDescriptorW", TYMED.TYMED_HGLOBAL);
        var text = new FORMATETC { cfFormat = 1 /* CF_TEXT */, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL };
        Assert.Equal(0, data.QueryGetData(ref desc));
        Assert.Equal(unchecked((int)0x80040064), data.QueryGetData(ref text));                       // DV_E_FORMATETC

        var outOfRange = Fmt("FileContents", TYMED.TYMED_ISTREAM, 99);
        Assert.Equal(unchecked((int)0x80040068), Assert.Throws<COMException>(() => data.GetData(ref outOfRange, out _)).HResult);   // DV_E_LINDEX

        var effect = Fmt("Preferred DropEffect", TYMED.TYMED_HGLOBAL);
        data.GetData(ref effect, out var m);
        IntPtr p = GlobalLock(m.unionmember);
        try { Assert.Equal(1, Marshal.ReadInt32(p)); } finally { GlobalUnlock(m.unionmember); ReleaseStgMedium(ref m); }          // copiar

        var e = data.EnumFormatEtc(DATADIR.DATADIR_GET);
        var buf = new FORMATETC[8]; var fetched = new int[1];
        e.Next(8, buf, fetched);
        Assert.Equal(3, fetched[0]);
    }
}
