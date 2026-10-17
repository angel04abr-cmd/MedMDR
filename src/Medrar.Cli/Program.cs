using System.Text;
using Medrar.Core;

// mdr: versión de línea de comandos de Medrar (usa el mismo motor que la ventana).
Console.OutputEncoding = Encoding.UTF8;
return Cli.Run(args);

static class Cli
{
    const string Help = """
        Medrar — línea de comandos (mdr)

        Uso:  mdr <orden> [opciones] <archivo> [ficheros...]

        Órdenes:
          a   Añadir ficheros a un archivo (lo crea si no existe)
          x   Extraer con rutas completas           e   Extraer sin rutas (todo en una carpeta)
          l   Listar contenido                      t   Comprobar integridad
          d   Eliminar ficheros del archivo         c   Convertir a otro formato
          r   Reparar con el registro de recuperación (crea <nombre>.reparado)
          s   Convertir un archivo en autoextraíble (.exe)
          i   Información del archivo               b   Prueba de velocidad

        Opciones:
          -m0..-m5        Nivel de compresión (0 = almacenar, 5 = máximo)       -s     Modo sólido
          -p<clave>       Contraseña                                            -hp<clave>  Contraseña y nombres cifrados
          -rr<N>          Registro de recuperación de N % (en .mdr va dentro; en los demás, en un fichero .mdrrec)                       -v<tam>     Volúmenes (ej. -v10m, -v700k)
          -sfx            Autoextraíble (necesita .NET 10)    -sfxfull  Autoextraíble que incluye .NET      -k  Bloquear
          -z<texto>       Comentario                                            -f<fmt>     Formato: mdr, zip, 7z, tar, tgz, gz
          -o<carpeta>     Carpeta de destino (x, e)                             -y     Sobrescribir sin preguntar
          -x<patrón>      Excluir (puede repetirse)                             -deflate    Usar Deflate en lugar de Brotli
        """;

    public static int Run(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "-?") { Console.WriteLine(Help); return 0; }
        string cmd = args[0].ToLowerInvariant();
        var opts = new ArchiveOptions { Level = 3 };
        string? pass = null, outDir = null;
        bool yes = false;
        var rest = new List<string>();
        foreach (var a in args.Skip(1))
        {
            if (a.StartsWith("-hp")) { pass = a[3..]; opts.Password = pass; opts.EncryptNames = true; }
            else if (a.StartsWith("-rr")) opts.RecoveryPercent = int.TryParse(a[3..], out var n) ? n : 5;
            else if (a == "-sfxfull") { opts.SelfExtracting = true; opts.SfxIncludeRuntime = true; }
            else if (a.StartsWith("-sfx")) opts.SelfExtracting = true;
            else if (a.StartsWith("-deflate")) opts.UseDeflate = true;
            else if (a.StartsWith("-m") && a.Length == 3 && char.IsDigit(a[2])) opts.Level = a[2] - '0';
            else if (a == "-s") opts.Solid = true;
            else if (a == "-k") opts.Lock = true;
            else if (a == "-y") yes = true;
            else if (a.StartsWith("-p")) { pass = a[2..]; opts.Password = pass; }
            else if (a.StartsWith("-v")) opts.VolumeSizeBytes = ParseSize(a[2..]);
            else if (a.StartsWith("-z")) opts.Comment = a[2..];
            else if (a.StartsWith("-o")) outDir = a[2..];
            else if (a.StartsWith("-x")) opts.ExcludePatterns.Add(a[2..]);
            else if (a.StartsWith("-f"))
                opts.Format = a[2..].ToLowerInvariant() switch { "zip" => ArchiveFormat.Zip, "7z" => ArchiveFormat.SevenZip, "tar" => ArchiveFormat.Tar, "tgz" or "targz" => ArchiveFormat.TarGz, "gz" => ArchiveFormat.Gz, _ => ArchiveFormat.Mdr };
            else rest.Add(a);
        }

        var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };
        var progress = new ProgressReporter(new Progress<ProgressInfo>(p => Draw(p)), cancel.Token);
        try
        {
            switch (cmd)
            {
                case "a": return Add(rest, opts, progress);
                case "x": case "e": return Extract(rest, pass, outDir, cmd == "e", yes, progress);
                case "l": return List(rest, pass);
                case "i": return Info(rest, pass);
                case "t": return Test(rest, pass, progress);
                case "d": return Delete(rest, pass, progress);
                case "c": return Convert(rest, pass, opts, progress);
                case "r": return Repair(rest, progress);
                case "s":
                    Need(rest, 1);
                    Console.WriteLine("Creado: " + SfxBuilder.ConvertToSfx(rest[0]));
                    return 0;
                case "b":
                    foreach (var r in Benchmark.Run(8, new Progress<string>(Console.Error.WriteLine)))
                        Console.WriteLine($"{r.Method,-8} nivel {r.Level}: comprime {r.CompressMBps,7:0.0} MB/s  descomprime {r.DecompressMBps,7:0.0} MB/s  tamaño {r.RatioPercent,5:0.0} %");
                    return 0;
                default:
                    Console.Error.WriteLine($"Orden desconocida: {cmd}. Usa «mdr -h» para ver la ayuda.");
                    return 2;
            }
        }
        catch (OperationCanceledException) { Console.Error.WriteLine("\nCancelado."); return 255; }
        catch (PasswordRequiredException) { Console.Error.WriteLine("\nError: el archivo está cifrado. Indica la contraseña con -p<clave>."); return 11; }
        catch (WrongPasswordException) { Console.Error.WriteLine("\nError: contraseña incorrecta."); return 11; }
        catch (MedrarException ex) { Console.Error.WriteLine("\nError: " + ex.Message); return 2; }
        catch (FileNotFoundException ex) { Console.Error.WriteLine("\nError: no existe " + ex.FileName); return 2; }
        catch (IOException ex) { Console.Error.WriteLine("\nError de E/S: " + ex.Message); return 2; }
    }

    static void Need(List<string> rest, int n)
    {
        if (rest.Count < n) throw new MedrarException("Faltan argumentos. Usa «mdr -h».");
    }

    static long ParseSize(string s)
    {
        s = s.Trim().ToLowerInvariant();
        long mult = 1;
        if (s.EndsWith("k")) { mult = 1024; s = s[..^1]; }
        else if (s.EndsWith("m")) { mult = 1024 * 1024; s = s[..^1]; }
        else if (s.EndsWith("g")) { mult = 1024L * 1024 * 1024; s = s[..^1]; }
        return long.TryParse(s, out var v) ? v * mult : 0;
    }

    static void Draw(ProgressInfo p)
    {
        if (Console.IsOutputRedirected) return;
        string name = p.CurrentName.Length > 50 ? "…" + p.CurrentName[^49..] : p.CurrentName;
        Console.Write($"\r{(int)(p.Fraction * 100),3} %  {name,-52}");
    }

    static void Done() { if (!Console.IsOutputRedirected) Console.Write("\r" + new string(' ', 70) + "\r"); }

    static int Add(List<string> rest, ArchiveOptions opts, ProgressReporter progress)
    {
        Need(rest, 2);
        string target = rest[0];
        if (ArchiveBuilder.FormatFromName(target) is { } f && opts.Format == ArchiveFormat.Mdr) opts.Format = f;
        if (Path.GetExtension(target) == "") target += ArchiveBuilder.Extension(opts.Format);
        var items = SourceCollector.Collect(rest.Skip(1), opts);
        if (File.Exists(target) && opts.VolumeSizeBytes == 0 && !opts.SelfExtracting)
        {
            var plan = new EditPlan();
            plan.Add.AddRange(items);
            ArchiveEditor.Apply(target, opts.Password, plan, progress);
            Done();
            Console.WriteLine($"Actualizado: {target}");
            return 0;
        }
        string made = ArchiveBuilder.Create(target, items, opts, progress);
        Done();
        Console.WriteLine($"Creado: {made}  ({ArchiveReport.FormatSize(new FileInfo(made).Length)})");
        return 0;
    }

    static int Extract(List<string> rest, string? pass, string? outDir, bool flat, bool yes, ProgressReporter progress)
    {
        Need(rest, 1);
        using var a = ArchiveOpener.Open(rest[0], pass);
        outDir ??= Path.GetFileNameWithoutExtension(rest[0]);
        var masks = rest.Skip(1).ToList();
        var sel = masks.Count == 0 ? a.Entries : a.Entries.Where(e => Glob.MatchesAny(e.Path, masks)).ToList();
        var o = new ExtractOptions
        {
            DestinationDir = outDir,
            PathMode = flat ? PathMode.None : PathMode.Full,
            Overwrite = yes ? OverwriteMode.Overwrite : OverwriteMode.Ask,
            AskOverwrite = p =>
            {
                Console.Write($"\n«{p}» ya existe. ¿Sobrescribir? [s]í / [n]o / [r]enombrar / [c]ancelar: ");
                return (Console.ReadLine() ?? "c").Trim().ToLowerInvariant() switch
                { "s" => OverwriteDecision.Overwrite, "n" => OverwriteDecision.Skip, "r" => OverwriteDecision.Rename, _ => OverwriteDecision.Cancel };
            }
        };
        var r = Extractor.Extract(a, sel, o, progress);
        Done();
        foreach (var (p, e) in r.Errors) Console.Error.WriteLine($"  ERROR {p}: {e}");
        Console.WriteLine($"Extraídos {r.Extracted} fichero(s) en {Path.GetFullPath(outDir)}" + (r.Skipped > 0 ? $", omitidos {r.Skipped}" : ""));
        return r.Ok ? 0 : 3;
    }

    static int List(List<string> rest, string? pass)
    {
        Need(rest, 1);
        using var a = ArchiveOpener.Open(rest[0], pass);
        Console.WriteLine($"{"Tamaño",12} {"Comprimido",12}  {"Modificado",-16} {"CRC32",8}  Nombre");
        foreach (var e in a.Entries)
            Console.WriteLine($"{(e.IsDirectory ? "<DIR>" : e.Size.ToString("N0")),12} {(e.IsDirectory ? "" : e.PackedSize.ToString("N0")),12}  {e.Modified:yyyy-MM-dd HH:mm} {(e.Crc.HasValue ? e.Crc.Value.ToString("X8") : ""),8}  {e.Path}");
        Console.WriteLine($"{a.Files().Count()} fichero(s), {ArchiveReport.FormatSize(a.Files().Sum(e => e.Size))}");
        return 0;
    }

    static int Info(List<string> rest, string? pass)
    {
        Need(rest, 1);
        using var a = ArchiveOpener.Open(rest[0], pass);
        Console.Write(ArchiveReport.Summary(a));
        return 0;
    }

    static int Test(List<string> rest, string? pass, ProgressReporter progress)
    {
        Need(rest, 1);
        using var a = ArchiveOpener.Open(rest[0], pass);
        var r = Extractor.Test(a, progress);
        Done();
        foreach (var (p, e) in r.Errors) Console.Error.WriteLine($"  ERROR {p}: {e}");
        Console.WriteLine(r.Ok ? $"Todo correcto ({r.Extracted} ficheros)." : $"Se encontraron {r.Errors.Count} error(es).");
        if (a.HasRecovery || File.Exists(RecoveryRecord.SidecarPathFor(rest[0])))
        {
            var rr = RecoveryRecord.Test(rest[0]);
            Console.WriteLine("Registro de recuperación: " + rr.Message);
        }
        return r.Ok ? 0 : 3;
    }

    static int Delete(List<string> rest, string? pass, ProgressReporter progress)
    {
        Need(rest, 2);
        var plan = new EditPlan();
        using (var a = ArchiveOpener.Open(rest[0], pass))
            foreach (var e in a.Entries.Where(e => Glob.MatchesAny(e.Path, rest.Skip(1)))) plan.Delete.Add(e.Path);
        if (plan.Delete.Count == 0) { Console.WriteLine("Nada que eliminar."); return 0; }
        ArchiveEditor.Apply(rest[0], pass, plan, progress);
        Done();
        Console.WriteLine($"Eliminados {plan.Delete.Count} elemento(s).");
        return 0;
    }

    static int Convert(List<string> rest, string? pass, ArchiveOptions opts, ProgressReporter progress)
    {
        Need(rest, 2);
        string dest = rest[1];
        if (ArchiveBuilder.FormatFromName(dest) is { } f) opts.Format = f;
        string made = ArchiveEditor.Convert(rest[0], pass, dest, opts, progress);
        Done();
        Console.WriteLine("Creado: " + made);
        return 0;
    }

    static int Repair(List<string> rest, ProgressReporter progress)
    {
        Need(rest, 1);
        string src = rest[0];
        string dest = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(src))!, Path.GetFileNameWithoutExtension(src) + ".reparado" + Path.GetExtension(src));
        var r = RecoveryRecord.Repair(src, dest, null, progress);
        Done();
        Console.WriteLine(r.Message + (r.OutputPath != null ? $"\nResultado: {r.OutputPath}" : ""));
        return r.Success ? 0 : 3;
    }
}
