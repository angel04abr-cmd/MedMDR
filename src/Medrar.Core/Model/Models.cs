namespace Medrar.Core;

/// <summary>Formatos que Medrar sabe CREAR. Para leer admite además RAR, BZ2, XZ, etc.</summary>
public enum ArchiveFormat { Mdr, Zip, Tar, TarGz, Gz, SevenZip }

public enum UpdateMode
{
    /// <summary>Añadir y reemplazar ficheros.</summary>
    AddReplace,
    /// <summary>Añadir y actualizar (solo reemplaza si el nuevo es más reciente).</summary>
    AddUpdate,
    /// <summary>Solo actualizar los que ya existen en el archivo.</summary>
    FreshenOnly,
    /// <summary>Sincronizar: añade, actualiza y elimina lo que ya no existe en el origen.</summary>
    Synchronize
}

public enum OverwriteMode { Ask, Overwrite, Skip, Rename }
public enum PathMode { Full, Relative, None }
public enum ExtractUpdateMode { ExtractReplace, ExtractUpdate, FreshenOnly }
public enum OverwriteDecision { Overwrite, Skip, Rename, Cancel }

/// <summary>Un elemento dentro de un archivo comprimido.</summary>
public sealed class ArchiveEntry
{
    public string Path { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public long PackedSize { get; init; }
    public DateTime Modified { get; init; }
    public uint? Crc { get; init; }
    public bool IsEncrypted { get; init; }
    public string Method { get; init; } = "";
    public int Index { get; init; }
    internal object? Tag { get; init; }

    public string Name => Path.TrimEnd('/').Split('/').Last();
    public string? Parent
    {
        get
        {
            var p = Path.TrimEnd('/');
            int i = p.LastIndexOf('/');
            return i < 0 ? "" : p[..i];
        }
    }
}

/// <summary>Un fichero (o carpeta) que se va a meter en un archivo.</summary>
public sealed class SourceItem
{
    public string EntryPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public long Size { get; init; }
    public DateTime Modified { get; init; } = DateTime.Now;
    public Func<Stream>? Open { get; init; }
    public string? FullPath { get; init; }
}

public sealed class ArchiveOptions
{
    public ArchiveFormat Format { get; set; } = ArchiveFormat.Mdr;
    /// <summary>0 = almacenar, 1 = más rápido ... 5 = el mejor.</summary>
    public int Level { get; set; } = 3;
    /// <summary>Tamaño del diccionario en KB (solo .mdr/Brotli). 0 = automático según el nivel.</summary>
    public int DictionaryKb { get; set; }
    public bool UseDeflate { get; set; }
    public bool Solid { get; set; }
    public string? Password { get; set; }
    public bool EncryptNames { get; set; }
    /// <summary>Porcentaje del registro de recuperación (0 = ninguno).</summary>
    public int RecoveryPercent { get; set; }
    public long VolumeSizeBytes { get; set; }
    public string? Comment { get; set; }
    public bool Lock { get; set; }
    public bool SelfExtracting { get; set; }
    /// <summary>Autoextraíble que incluye el runtime de .NET (más grande, pero no necesita nada instalado).</summary>
    public bool SfxIncludeRuntime { get; set; }
    public bool TestAfter { get; set; }
    public bool DeleteSourceAfter { get; set; }
    /// <summary>Patrones (comodines) de ficheros que se guardan sin comprimir.</summary>
    public List<string> StorePatterns { get; set; } = new(DefaultStorePatterns);
    public List<string> ExcludePatterns { get; set; } = new();
    public UpdateMode UpdateMode { get; set; } = UpdateMode.AddReplace;

    public static readonly string[] DefaultStorePatterns =
    {
        "*.zip", "*.rar", "*.7z", "*.gz", "*.bz2", "*.xz", "*.zst", "*.mdr",
        "*.jpg", "*.jpeg", "*.png", "*.gif", "*.webp", "*.avif",
        "*.mp3", "*.mp4", "*.m4a", "*.mkv", "*.avi", "*.mov", "*.flac", "*.ogg"
    };

    public ArchiveOptions Clone()
    {
        var c = (ArchiveOptions)MemberwiseClone();
        c.StorePatterns = new List<string>(StorePatterns);
        c.ExcludePatterns = new List<string>(ExcludePatterns);
        return c;
    }
}

public sealed record ProgressInfo(string CurrentName, long BytesDone, long BytesTotal, int FilesDone, int FilesTotal)
{
    public double Fraction => BytesTotal <= 0 ? 0 : Math.Min(1.0, (double)BytesDone / BytesTotal);
}

/// <summary>Progreso + cancelación compartidos por todas las operaciones largas.</summary>
public sealed class ProgressReporter
{
    private readonly IProgress<ProgressInfo>? _sink;
    public CancellationToken Token { get; }
    public long Total { get; set; }
    public int FilesTotal { get; set; }
    public long Done { get; private set; }
    public int FilesDone { get; private set; }
    public string Current { get; private set; } = "";
    private DateTime _last = DateTime.MinValue;

    public ProgressReporter(IProgress<ProgressInfo>? sink = null, CancellationToken token = default)
    {
        _sink = sink;
        Token = token;
    }

    public static ProgressReporter None { get; } = new();

    public void Begin(string name) { Current = name; Token.ThrowIfCancellationRequested(); Push(true); }
    public void Add(long bytes)
    {
        Done += bytes;
        Token.ThrowIfCancellationRequested();
        Push(false);
    }
    public void EndFile() { FilesDone++; Push(true); }
    private void Push(bool force)
    {
        if (_sink == null) return;
        var now = DateTime.UtcNow;
        if (!force && (now - _last).TotalMilliseconds < 60) return;
        _last = now;
        _sink.Report(new ProgressInfo(Current, Done, Total, FilesDone, FilesTotal));
    }
}

public class MedrarException : Exception
{
    public MedrarException(string message) : base(message) { }
    public MedrarException(string message, Exception inner) : base(message, inner) { }
}
public sealed class PasswordRequiredException : MedrarException
{
    public PasswordRequiredException() : base("El archivo está cifrado: hace falta la contraseña.") { }
}
public sealed class WrongPasswordException : MedrarException
{
    public WrongPasswordException() : base("Contraseña incorrecta.") { }
}
public sealed class ChecksumException : MedrarException
{
    public string EntryPath { get; }
    public ChecksumException(string path) : base($"Error de suma de control en «{path}»: el archivo está dañado.") { EntryPath = path; }
}
public sealed class UnsupportedFormatException : MedrarException
{
    public UnsupportedFormatException(string message) : base(message) { }
}
public sealed class LockedArchiveException : MedrarException
{
    public LockedArchiveException() : base("El archivo está bloqueado: no se puede modificar.") { }
}
