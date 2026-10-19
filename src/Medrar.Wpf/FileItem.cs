using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media;
using Medrar.Core;

namespace Medrar.Wpf;

/// <summary>Una fila de la lista: un fichero o carpeta del disco (más adelante, también entradas dentro de un archivo).</summary>
public sealed class FileItem
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public bool IsUp { get; init; }
    public bool IsArchive { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; }
    public string TypeName { get; init; } = "";
    public ImageSource? ShellIcon { get; init; }

    /// <summary>Iconos propios para carpetas, archivos comprimidos y «subir»; el resto usa el icono de Windows para su tipo.</summary>
    public IconKind? VectorIcon => IsUp ? IconKind.Up : IsDirectory ? IconKind.Folder : IsArchive ? IconKind.Archive : null;
    public bool HasVectorIcon => VectorIcon != null || ShellIcon == null;
    public bool HasShellIcon => VectorIcon == null && ShellIcon != null;

    public string SizeText => IsDirectory || IsUp ? "" : ArchiveReport.FormatSize(Size);
    public string ModifiedText => IsUp ? "" : Modified.ToString("dd/MM/yyyy HH:mm");

    public static FileItem Up(string parentPath) => new() { Name = "..", FullPath = parentPath, IsDirectory = true, IsUp = true };

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
            2 => string.Compare(a.TypeName, b.TypeName, StringComparison.CurrentCultureIgnoreCase),
            3 => a.Modified.CompareTo(b.Modified),
            _ => StrCmpLogicalW(a.Name, b.Name),
        };
        if (c == 0 && Column != 0) c = StrCmpLogicalW(a.Name, b.Name);
        return Ascending ? c : -c;
    }
}
