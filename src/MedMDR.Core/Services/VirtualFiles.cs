using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Runtime.Versioning;
using ComIDataObject = System.Runtime.InteropServices.ComTypes.IDataObject;

namespace MedMDR.Core;

/// <summary>Un fichero o carpeta que se ofrece al arrastrar sin que exista todavía en disco.</summary>
/// <param name="RelativePath">Ruta dentro de lo arrastrado, con «\» (por ejemplo «Fotos\playa.jpg»).</param>
/// <param name="Open">Abre el contenido cuando el destino lo pide (null en las carpetas).</param>
public sealed record VirtualFileItem(string RelativePath, bool IsDirectory, long Size, DateTime Modified, Func<Stream>? Open);

/// <summary>
/// Lo que se arrastra desde un archivo comprimido hacia el Explorador (u otro programa) **sin extraer nada antes**: el arrastre empieza al
/// instante y el destino pide cada fichero al soltar (formatos «FileGroupDescriptorW» y «FileContents», los mismos que usa el Explorador de
/// Windows con un ZIP, Outlook o el navegador). Se entrega con <see cref="VirtualFileDrag.Start"/>.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class VirtualFilesDataObject : ComIDataObject, IDisposable
{
    public const string FileGroupDescriptorW = "FileGroupDescriptorW", FileContents = "FileContents", PreferredDropEffect = "Preferred DropEffect";

    // HRESULT que Windows espera
    private const int DV_E_FORMATETC = unchecked((int)0x80040064), DV_E_DVASPECT = unchecked((int)0x80040065), DV_E_LINDEX = unchecked((int)0x80040068),
                      DV_E_TYMED = unchecked((int)0x80040069), E_NOTIMPL = unchecked((int)0x80004001), E_FAIL = unchecked((int)0x80004005),
                      OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003), DATA_S_SAMEFORMATETC = 0x00040130;

    private readonly IReadOnlyList<VirtualFileItem> _items;
    private readonly IDisposable? _owner;
    private readonly short _cfDescriptor, _cfContents, _cfEffect;
    private readonly List<IDisposable> _streams = new();
    private bool _disposed;

    public VirtualFilesDataObject(IReadOnlyList<VirtualFileItem> items, IDisposable? owner = null)
    {
        _items = items;
        _owner = owner;
        _cfDescriptor = Register(FileGroupDescriptorW);
        _cfContents = Register(FileContents);
        _cfEffect = Register(PreferredDropEffect);
    }

    public IReadOnlyList<VirtualFileItem> Items => _items;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormatW(string name);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr h);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr h);
    [DllImport("ole32.dll")] private static extern void ReleaseStgMedium(ref STGMEDIUM medium);

    private static short Register(string name) => unchecked((short)RegisterClipboardFormatW(name));

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct FILEDESCRIPTORW
    {
        public uint dwFlags;
        public Guid clsid;
        public int sizelcx, sizelcy, pointlx, pointly;
        public uint dwFileAttributes;
        public FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
        public uint nFileSizeHigh, nFileSizeLow;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string cFileName;
    }

    private const uint FD_ATTRIBUTES = 0x4, FD_WRITESTIME = 0x20, FD_FILESIZE = 0x40, FD_PROGRESSUI = 0x4000;
    private const uint FILE_ATTRIBUTE_DIRECTORY = 0x10, FILE_ATTRIBUTE_NORMAL = 0x80;

    // ------------------------------------------------------------ IDataObject
    public void GetData(ref FORMATETC format, out STGMEDIUM medium)
    {
        medium = default;
        if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) throw new COMException("", DV_E_DVASPECT);

        if (format.cfFormat == _cfDescriptor)
        {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0) throw new COMException("", DV_E_TYMED);
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = BuildDescriptor(), pUnkForRelease = null };
            return;
        }
        if (format.cfFormat == _cfEffect)
        {
            if ((format.tymed & TYMED.TYMED_HGLOBAL) == 0) throw new COMException("", DV_E_TYMED);
            medium = new STGMEDIUM { tymed = TYMED.TYMED_HGLOBAL, unionmember = HGlobalWithInt(1 /* DROPEFFECT_COPY */), pUnkForRelease = null };
            return;
        }
        if (format.cfFormat == _cfContents)
        {
            if ((format.tymed & TYMED.TYMED_ISTREAM) == 0) throw new COMException("", DV_E_TYMED);
            int i = format.lindex;
            if (i < 0 || i >= _items.Count || _items[i].IsDirectory || _items[i].Open == null) throw new COMException("", DV_E_LINDEX);
            ComStream com;
            try { com = new ComStream(_items[i].Open!()); }
            catch (Exception ex) { throw new COMException(ex.Message, E_FAIL); }       // p. ej. falta la contraseña: el destino muestra su error
            _streams.Add(com);
            medium = new STGMEDIUM { tymed = TYMED.TYMED_ISTREAM, unionmember = Marshal.GetComInterfaceForObject(com, typeof(IStream)), pUnkForRelease = null };
            return;
        }
        throw new COMException("", DV_E_FORMATETC);
    }

    public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium) => throw new COMException("", E_NOTIMPL);

    public int QueryGetData(ref FORMATETC format)
    {
        if (format.dwAspect != DVASPECT.DVASPECT_CONTENT) return DV_E_DVASPECT;
        if (format.cfFormat == _cfDescriptor || format.cfFormat == _cfEffect) return (format.tymed & TYMED.TYMED_HGLOBAL) != 0 ? 0 : DV_E_TYMED;
        if (format.cfFormat == _cfContents) return (format.tymed & TYMED.TYMED_ISTREAM) != 0 ? 0 : DV_E_TYMED;
        return DV_E_FORMATETC;
    }

    public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
    {
        formatOut = formatIn;
        formatOut.ptd = IntPtr.Zero;
        return DATA_S_SAMEFORMATETC;
    }

    public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
    {
        // El destino anota aquí cosas como «Performed DropEffect» o «Paste Succeeded»: se aceptan y se descartan.
        if (release) ReleaseStgMedium(ref medium);
    }

    public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
    {
        if (direction != DATADIR.DATADIR_GET) throw new COMException("", E_NOTIMPL);
        return new FormatEnumerator(new[]
        {
            new FORMATETC { cfFormat = _cfDescriptor, ptd = IntPtr.Zero, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL },
            new FORMATETC { cfFormat = _cfContents, ptd = IntPtr.Zero, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_ISTREAM },
            new FORMATETC { cfFormat = _cfEffect, ptd = IntPtr.Zero, dwAspect = DVASPECT.DVASPECT_CONTENT, lindex = -1, tymed = TYMED.TYMED_HGLOBAL },
        });
    }

    public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection) { connection = 0; return OLE_E_ADVISENOTSUPPORTED; }
    public void DUnadvise(int connection) => throw new COMException("", OLE_E_ADVISENOTSUPPORTED);
    public int EnumDAdvise(out IEnumSTATDATA? enumAdvise) { enumAdvise = null; return OLE_E_ADVISENOTSUPPORTED; }

    // ------------------------------------------------------------ memoria global
    private static IntPtr AllocGlobal(int bytes)
    {
        IntPtr h = GlobalAlloc(0x2 /* GMEM_MOVEABLE */ | 0x40 /* GMEM_ZEROINIT */, (UIntPtr)(uint)bytes);
        if (h == IntPtr.Zero) throw new OutOfMemoryException();
        return h;
    }

    private static IntPtr HGlobalWithInt(int value)
    {
        IntPtr h = AllocGlobal(4);
        IntPtr p = GlobalLock(h);
        Marshal.WriteInt32(p, value);
        GlobalUnlock(h);
        return h;
    }

    private IntPtr BuildDescriptor()
    {
        int size = Marshal.SizeOf<FILEDESCRIPTORW>();
        IntPtr h = AllocGlobal(4 + size * _items.Count);
        IntPtr p = GlobalLock(h);
        try
        {
            Marshal.WriteInt32(p, _items.Count);
            for (int i = 0; i < _items.Count; i++)
            {
                var it = _items[i];
                long ticks = it.Modified.ToFileTime();
                var d = new FILEDESCRIPTORW
                {
                    dwFlags = FD_ATTRIBUTES | FD_WRITESTIME | FD_PROGRESSUI | (it.IsDirectory ? 0 : FD_FILESIZE),
                    dwFileAttributes = it.IsDirectory ? FILE_ATTRIBUTE_DIRECTORY : FILE_ATTRIBUTE_NORMAL,
                    ftLastWriteTime = new FILETIME { dwLowDateTime = unchecked((int)(ticks & 0xFFFFFFFF)), dwHighDateTime = (int)(ticks >> 32) },
                    nFileSizeLow = (uint)(it.Size & 0xFFFFFFFF), nFileSizeHigh = (uint)(it.Size >> 32),
                    cFileName = it.RelativePath.Length < 260 ? it.RelativePath : it.RelativePath[^259..]
                };
                Marshal.StructureToPtr(d, p + 4 + i * size, false);
            }
        }
        finally { GlobalUnlock(h); }
        return h;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var s in _streams) { try { s.Dispose(); } catch { /* ya cerrado */ } }
        _streams.Clear();
        try { _owner?.Dispose(); } catch { /* carpeta temporal en uso: se limpia al cerrar */ }
    }

    // ------------------------------------------------------------ enumerador de formatos
    private sealed class FormatEnumerator : IEnumFORMATETC
    {
        private readonly FORMATETC[] _formats;
        private int _pos;
        public FormatEnumerator(FORMATETC[] formats, int pos = 0) { _formats = formats; _pos = pos; }
        public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
        {
            int n = 0;
            while (n < celt && _pos < _formats.Length) rgelt[n++] = _formats[_pos++];
            if (pceltFetched != null && pceltFetched.Length > 0) pceltFetched[0] = n;
            return n == celt ? 0 : 1;      // S_OK / S_FALSE
        }
        public int Skip(int celt) { _pos += celt; return _pos <= _formats.Length ? 0 : 1; }
        public int Reset() { _pos = 0; return 0; }
        public void Clone(out IEnumFORMATETC newEnum) => newEnum = new FormatEnumerator(_formats, _pos);
    }

    // ------------------------------------------------------------ flujo COM de solo lectura
    internal sealed class ComStream : IStream, IDisposable
    {
        private readonly Stream _s;
        public ComStream(Stream s) => _s = s;
        public void Read(byte[] pv, int cb, IntPtr pcbRead)
        {
            int n = _s.Read(pv, 0, cb);
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt32(pcbRead, n);
        }
        public void Seek(long dlibMove, int dwOrigin, IntPtr plibNewPosition)
        {
            long pos = _s.Seek(dlibMove, (SeekOrigin)dwOrigin);
            if (plibNewPosition != IntPtr.Zero) Marshal.WriteInt64(plibNewPosition, pos);
        }
        public void Stat(out STATSTG pstatstg, int grfStatFlag)
        {
            pstatstg = new STATSTG { type = 2 /* STGTY_STREAM */, cbSize = _s.Length, grfMode = 0 };
        }
        public void CopyTo(IStream pstm, long cb, IntPtr pcbRead, IntPtr pcbWritten)
        {
            var buf = new byte[81920];
            long total = 0;
            while (total < cb)
            {
                int n = _s.Read(buf, 0, (int)Math.Min(buf.Length, cb - total));
                if (n <= 0) break;
                pstm.Write(buf, n, IntPtr.Zero);
                total += n;
            }
            if (pcbRead != IntPtr.Zero) Marshal.WriteInt64(pcbRead, total);
            if (pcbWritten != IntPtr.Zero) Marshal.WriteInt64(pcbWritten, total);
        }
        public void Write(byte[] pv, int cb, IntPtr pcbWritten) => throw new COMException("", unchecked((int)0x80030005));          // STG_E_ACCESSDENIED
        public void SetSize(long libNewSize) => throw new COMException("", unchecked((int)0x80030005));
        public void Commit(int grfCommitFlags) { }
        public void Revert() { }
        public void LockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("", unchecked((int)0x80030001));  // STG_E_INVALIDFUNCTION
        public void UnlockRegion(long libOffset, long cb, int dwLockType) => throw new COMException("", unchecked((int)0x80030001));
        public void Clone(out IStream ppstm) => throw new COMException("", E_NOTIMPL);
        public void Dispose() => _s.Dispose();
    }
}

/// <summary>
/// Prepara lo que se arrastra desde un archivo: la lista de ficheros y carpetas (con sus rutas) y, cuando el destino pide un fichero, lo extrae
/// a una carpeta temporal y lo entrega. En archivos «sólidos» (donde sacar un fichero obliga a descomprimir lo anterior) extrae todos los
/// ficheros elegidos de una sola pasada la primera vez que se pide alguno.
/// </summary>
public sealed class ArchiveDragSource : IDisposable
{
    private readonly IArchive _archive;
    private readonly string _base;
    private readonly string _temp;
    private readonly List<ArchiveEntry> _fileEntries = new();
    private readonly Dictionary<int, string> _extracted = new();
    private readonly Dictionary<int, string> _errors = new();
    private bool _batchDone;

    public List<VirtualFileItem> Items { get; } = new();

    /// <param name="selection">Entradas elegidas (ficheros y/o carpetas) tal como las da la lista.</param>
    /// <param name="baseFolder">Carpeta del archivo que se está viendo («» = raíz); las rutas se dan relativas a ella.</param>
    public ArchiveDragSource(IArchive archive, IEnumerable<ArchiveEntry> selection, string baseFolder, string? tempRoot = null)
    {
        _archive = archive;
        _base = baseFolder.Trim('/');
        _temp = Path.Combine(tempRoot ?? Path.GetTempPath(), "MedMDR", "arrastre-" + Guid.NewGuid().ToString("N")[..8]);

        var chosen = selection.ToList();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(ArchiveEntry e)
        {
            string rel = Relative(e.Path.TrimEnd('/'));
            if (rel.Length == 0 || !seen.Add(rel + (e.IsDirectory ? "/" : ""))) return;
            if (e.IsDirectory) Items.Add(new VirtualFileItem(rel.Replace('/', '\\'), true, 0, e.Modified, null));
            else
            {
                _fileEntries.Add(e);
                var entry = e;
                Items.Add(new VirtualFileItem(rel.Replace('/', '\\'), false, e.Size, e.Modified, () => Open(entry)));
            }
        }
        foreach (var e in chosen)
        {
            Add(e);
            if (!e.IsDirectory) continue;
            string prefix = e.Path.TrimEnd('/') + "/";
            foreach (var child in archive.Entries.Where(x => x.Path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) Add(child);
        }
        // las carpetas antes que lo que contienen
        Items.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));
    }

    private string Relative(string path) =>
        _base.Length > 0 && path.StartsWith(_base + "/", StringComparison.OrdinalIgnoreCase) ? path[(_base.Length + 1)..] : path;

    private Stream Open(ArchiveEntry entry)
    {
        string path = EnsureExtracted(entry);
        return new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 128 * 1024);
    }

    private string EnsureExtracted(ArchiveEntry entry)
    {
        lock (_extracted)
        {
            if (_extracted.TryGetValue(entry.Index, out var done) && File.Exists(done)) return done;
            if (_errors.TryGetValue(entry.Index, out var failed)) throw new MedMDRException(failed);

            var todo = _archive.IsSolid && !_batchDone ? _fileEntries : new List<ArchiveEntry> { entry };
            if (_archive.IsSolid) _batchDone = true;
            var o = new ExtractOptions { DestinationDir = _temp, PathMode = PathMode.Relative, BaseFolder = _base, Overwrite = OverwriteMode.Overwrite };
            var res = Extractor.Extract(_archive, todo, o);
            foreach (var e in todo)
            {
                string target = SafePath.Resolve(_temp, Relative(e.Path.TrimEnd('/')));
                if (File.Exists(target)) _extracted[e.Index] = target;
            }
            foreach (var (path, error) in res.Errors)
                foreach (var e in todo.Where(x => x.Path == path)) _errors[e.Index] = error;
            if (_extracted.TryGetValue(entry.Index, out done) && File.Exists(done)) return done;
            throw new MedMDRException(_errors.TryGetValue(entry.Index, out var msg) ? msg : "No se pudo extraer el fichero.");
        }
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_temp)) Directory.Delete(_temp, true); } catch { /* se limpia al cerrar */ }
    }
}
