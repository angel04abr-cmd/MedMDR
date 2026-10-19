using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Una fila de la lista: un fichero o carpeta del disco, o una entrada dentro de un archivo comprimido.</summary>
public sealed class FileItem
{
    public string Name { get; init; } = "";
    /// <summary>Ruta en disco o, dentro de un archivo, ruta de la entrada (con «/»).</summary>
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public bool IsUp { get; init; }
    public bool IsArchive { get; init; }
    public long Size { get; init; }
    public long Packed { get; init; } = -1;
    public DateTime Modified { get; init; }
    public string? Crc { get; init; }
    public string TypeName { get; init; } = "";
    public ImageSource? ShellIcon { get; init; }
    /// <summary>La entrada del archivo (null en el disco y en carpetas implícitas de un archivo).</summary>
    public ArchiveEntry? Entry { get; init; }

    /// <summary>Iconos propios para carpetas, archivos comprimidos y «subir»; el resto usa el icono de Windows para su tipo.</summary>
    public IconKind? VectorIcon => IsUp ? IconKind.Up : IsDirectory ? IconKind.Folder : IsArchive ? IconKind.Archive : null;
    public bool HasVectorIcon => VectorIcon != null || ShellIcon == null;
    public bool HasShellIcon => VectorIcon == null && ShellIcon != null;

    public string SizeText => IsDirectory || IsUp ? "" : ArchiveReport.FormatSize(Size);
    public string PackedText => IsDirectory || IsUp || Packed < 0 ? "" : ArchiveReport.FormatSize(Packed);
    public string ModifiedText => IsUp || Modified == default ? "" : Modified.ToString("dd/MM/yyyy HH:mm");
    public string CrcText => Crc ?? "";

    public static FileItem Up() => new() { Name = "..", IsDirectory = true, IsUp = true };

    public static FileItem FromDisk(FileSystemInfo fi)
    {
        bool dir = fi is DirectoryInfo;
        return new FileItem
        {
            Name = fi.Name,
            FullPath = fi.FullName,
            IsDirectory = dir,
            IsArchive = !dir && ArchiveOpener.LooksLikeArchive(fi.FullName),
            Size = fi is FileInfo f ? f.Length : 0,
            Modified = fi.LastWriteTime,
            TypeName = ShellInfo.TypeName(fi.Name, dir),
            ShellIcon = dir ? null : ShellInfo.IconFor(fi.Name),
        };
    }

    /// <summary>Un fichero de un archivo comprimido (nombre sin la parte de carpeta).</summary>
    public static FileItem FromEntry(string name, ArchiveEntry e) => new()
    {
        Name = name,
        FullPath = e.Path.TrimEnd('/'),
        Size = e.Size,
        Packed = e.PackedSize,
        Modified = e.Modified,
        Crc = e.Crc?.ToString("X8"),
        TypeName = ShellInfo.TypeName(name, false),
        ShellIcon = ShellInfo.IconFor(name),
        Entry = e,
    };

    /// <summary>Una carpeta de un archivo comprimido (puede ser implícita: solo aparece como parte de la ruta de otras entradas).</summary>
    public static FileItem FromEntryFolder(string name, string fullPath, ArchiveEntry? e, DateTime modified) => new()
    {
        Name = name,
        FullPath = fullPath,
        IsDirectory = true,
        Modified = modified,
        TypeName = "Carpeta de archivos",
        Entry = e,
    };
}

/// <summary>Orden de la lista: «..» primero, carpetas antes que ficheros y, dentro de cada grupo, por la columna elegida.</summary>
internal sealed class FileItemComparer : System.Collections.IComparer
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int StrCmpLogicalW(string a, string b);

    public int Column { get; }
    public bool Ascending { get; }
    public FileItemComparer(int column, bool ascending) { Column = column; Ascending = ascending; }

    public int Compare(object? x, object? y)
    {
        var a = (FileItem)x!; var b = (FileItem)y!;
        if (a.IsUp != b.IsUp) return a.IsUp ? -1 : 1;
        if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1;
        int c = Column switch
        {
            1 => a.Size.CompareTo(b.Size),
            2 => a.Packed.CompareTo(b.Packed),
            3 => string.Compare(a.TypeName, b.TypeName, StringComparison.CurrentCultureIgnoreCase),
            4 => a.Modified.CompareTo(b.Modified),
            5 => string.Compare(a.Crc, b.Crc, StringComparison.Ordinal),
            _ => StrCmpLogicalW(a.Name, b.Name),
        };
        if (c == 0 && Column != 0) c = StrCmpLogicalW(a.Name, b.Name);
        return Ascending ? c : -c;
    }
}

/// <summary>Un acceso del panel lateral: carpeta, unidad, archivo reciente o favorito.</summary>
public sealed class NavItem
{
    public string Section { get; init; } = "";
    public string Title { get; init; } = "";
    public string Path { get; init; } = "";
    public IconKind Kind { get; init; } = IconKind.Folder;
    public bool IsFavorite { get; init; }
}
