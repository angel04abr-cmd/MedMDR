using System.Diagnostics;
using System.IO.Compression;
using Medrar.Core;
using Microsoft.Win32;

namespace Medrar.Tests;

public class FormatTests
{
    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.Tar)]
    [InlineData(ArchiveFormat.TarGz)]
    [InlineData(ArchiveFormat.SevenZip)]
    public void Standard_formats_roundtrip(ArchiveFormat f)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string path = Data.Create(tmp, "t" + ArchiveBuilder.Extension(f), new ArchiveOptions { Format = f }, tree);
        using var a = ArchiveOpener.Open(path);
        Assert.Equal(expected.Count, a.Files().Count());
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }

    [Fact]
    public void Gz_single_file_and_rejects_many()
    {
        using var tmp = new TempDir();
        string f = tmp.Combine("uno.txt");
        File.WriteAllBytes(f, Data.Text(50_000));
        var o = new ArchiveOptions { Format = ArchiveFormat.Gz };
        string gz = ArchiveBuilder.Create(tmp.Combine("uno.txt.gz"), SourceCollector.Collect(new[] { f }, o), o);
        using (var a = ArchiveOpener.Open(gz)) Assert.Single(a.Files());
        string tree = Data.MakeTree(tmp.Path);
        Assert.Throws<UnsupportedFormatException>(() =>
            ArchiveBuilder.Create(tmp.Combine("x.gz"), SourceCollector.Collect(new[] { tree }, o), o));
    }

    [Theory]
    [InlineData(ArchiveFormat.Tar)]
    [InlineData(ArchiveFormat.TarGz)]
    [InlineData(ArchiveFormat.SevenZip)]
    public void Password_rejected_for_formats_that_cannot_encrypt(ArchiveFormat f)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var o = new ArchiveOptions { Format = f, Password = "x" };
        Assert.Throws<UnsupportedFormatException>(() => ArchiveBuilder.Create(tmp.Combine("x" + ArchiveBuilder.Extension(f)), SourceCollector.Collect(new[] { tree }, o), o));
    }

    [Fact]
    public void Reads_rar_made_by_winrar_if_installed()
    {
        string rar = @"C:\Program Files\WinRAR\Rar.exe";
        if (!File.Exists(rar)) return;           // prueba opcional: solo si WinRAR está instalado
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        foreach (var args in new[] { "a -r -m3 -ep1 -ma5 plain.rar arbol", "a -r -s -m5 -ep1 solid.rar arbol", "a -r -m3 -ep1 -hpsecreto enc.rar arbol" })
        {
            var p = Process.Start(new ProcessStartInfo(rar, args) { WorkingDirectory = tmp.Path, RedirectStandardOutput = true, UseShellExecute = false })!;
            p.StandardOutput.ReadToEnd(); p.WaitForExit();
            Assert.Equal(0, p.ExitCode);
        }
        foreach (var (name, pw) in new[] { ("plain.rar", (string?)null), ("solid.rar", null), ("enc.rar", "secreto") })
        {
            using var a = ArchiveOpener.Open(tmp.Combine(name), pw);
            string dest = tmp.Combine("o_" + name);
            var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Overwrite });
            Assert.True(r.Ok, name + ": " + string.Join(";", r.Errors));
            Data.AssertSame(expected, Data.Snapshot(Path.Combine(dest, "arbol")));
        }
    }

    [Theory]
    [InlineData("-hpclave1", false)]      // nombres cifrados: ni siquiera se puede listar sin clave
    [InlineData("-hpclave1 -s", false)]
    [InlineData("-pclave1", true)]        // solo el contenido cifrado: se lista, pero no se extrae sin clave
    [InlineData("-pclave1 -s", true)]
    [InlineData("-pclave1 -m0", true)]    // sin comprimir: no hay descompresor que falle con una clave mala
    public void Encrypted_rar_asks_for_password_instead_of_failing(string options, bool listingVisible)
    {
        string rar = @"C:\Program Files\WinRAR\Rar.exe";
        if (!File.Exists(rar)) return;
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        var p = Process.Start(new ProcessStartInfo(rar, $"a -r -m3 -ep1 {options} enc.rar arbol") { WorkingDirectory = tmp.Path, RedirectStandardOutput = true, UseShellExecute = false })!;
        p.StandardOutput.ReadToEnd(); p.WaitForExit();
        Assert.Equal(0, p.ExitCode);
        string path = tmp.Combine("enc.rar");

        if (listingVisible)
        {
            using var listed = ArchiveOpener.Open(path);                     // se puede abrir y ver el contenido
            Assert.NotEmpty(listed.Entries);
            Assert.Throws<PasswordRequiredException>(() => Extractor.Test(listed));   // pero no extraer sin clave
            using var bad = ArchiveOpener.Open(path, "mala");
            Assert.Throws<WrongPasswordException>(() => Extractor.Test(bad));
        }
        else
        {
            Assert.Throws<PasswordRequiredException>(() => ArchiveOpener.Open(path));
            Assert.Throws<WrongPasswordException>(() => ArchiveOpener.Open(path, "mala"));
        }

        using var ok = ArchiveOpener.Open(path, "clave1");
        var r = Extractor.Extract(ok, ok.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }

    [Fact]
    public void Retrying_extraction_after_password_with_entries_from_the_old_copy_works()
    {
        // Es lo que hace la ventana: extrae, ve que falta la clave, la pide, REABRE el archivo y repite
        // con la selección de la copia anterior. Antes fallaba con «An item with the same key has already been added».
        string rar = @"C:\Program Files\WinRAR\Rar.exe";
        if (!File.Exists(rar)) return;
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        var p = Process.Start(new ProcessStartInfo(rar, "a -r -m3 -ep1 -pclave1 enc.rar arbol") { WorkingDirectory = tmp.Path, RedirectStandardOutput = true, UseShellExecute = false })!;
        p.StandardOutput.ReadToEnd(); p.WaitForExit();
        string path = tmp.Combine("enc.rar");

        using var first = ArchiveOpener.Open(path);
        var selection = first.Entries.ToList();                      // incluye carpetas y ficheros
        var opts = new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite };
        Assert.Throws<PasswordRequiredException>(() => Extractor.Extract(first, selection, opts));
        using var second = ArchiveOpener.Open(path, "clave1");       // copia nueva con la clave
        var r = Extractor.Extract(second, selection, opts);          // selección de la copia vieja
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Assert.Equal(expected.Count, r.Extracted);
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }

    [Fact]
    public void Extractor_does_not_duplicate_entries_from_another_instance_of_the_same_archive()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string zip = Data.Create(tmp, "t.zip", new ArchiveOptions { Format = ArchiveFormat.Zip }, tree);
        using var a = ArchiveOpener.Open(zip);
        var stale = a.Entries.ToList();
        using var b = ArchiveOpener.Open(zip);
        var r = Extractor.Extract(b, stale, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok);
        Assert.Equal(expected.Count, r.Extracted);
    }

    [Fact]
    public void Rar_to_mdr_to_zip_conversion_keeps_content()
    {
        string rar = @"C:\Program Files\WinRAR\Rar.exe";
        if (!File.Exists(rar)) return;
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        var p = Process.Start(new ProcessStartInfo(rar, "a -r -m3 -ep1 src.rar arbol") { WorkingDirectory = tmp.Path, RedirectStandardOutput = true, UseShellExecute = false })!;
        p.StandardOutput.ReadToEnd(); p.WaitForExit();
        string mdr = ArchiveEditor.Convert(tmp.Combine("src.rar"), null, tmp.Combine("c.mdr"), new ArchiveOptions { Format = ArchiveFormat.Mdr, Solid = true });
        string zip = ArchiveEditor.Convert(mdr, null, tmp.Combine("c.zip"), new ArchiveOptions { Format = ArchiveFormat.Zip });
        using var a = ArchiveOpener.Open(zip);
        Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }
}

public class SecurityTests
{
    [Fact]
    public void Zip_slip_entries_are_blocked()
    {
        using var tmp = new TempDir();
        string zipPath = tmp.Combine("evil.zip");
        using (var z = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("../../escapado.txt").Open())) w.Write("mal");
            using (var w = new StreamWriter(z.CreateEntry("bien.txt").Open())) w.Write("ok");
        }
        string dest = tmp.Combine("seguro");
        using var a = ArchiveOpener.Open(zipPath);
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Overwrite });
        Assert.True(File.Exists(Path.Combine(dest, "bien.txt")));
        Assert.False(File.Exists(tmp.Combine("escapado.txt")));
        Assert.False(File.Exists(Path.Combine(Path.GetDirectoryName(tmp.Path)!, "escapado.txt")));
        Assert.Contains(r.Errors, e => e.Error.Contains("peligrosa"));
    }

    [Theory]
    [InlineData("a/../../x")]
    [InlineData("..\\x")]
    public void SafePath_rejects_parent_traversal(string p) =>
        Assert.Throws<MedrarException>(() => SafePath.Resolve(Path.Combine(Path.GetTempPath(), "dest"), p));

    [Fact]
    public void SafePath_neutralizes_absolute_paths_and_reserved_names()
    {
        string root = Path.Combine(Path.GetTempPath(), "dest");
        string r = SafePath.Resolve(root, "C:/Windows/system32/x.dll");
        Assert.StartsWith(Path.GetFullPath(root), r);
        Assert.StartsWith(Path.GetFullPath(root), SafePath.Resolve(root, "con.txt"));
        Assert.DoesNotContain(@"\con.txt", SafePath.Resolve(root, "con.txt"));
    }

    [Fact]
    public void Existing_files_are_not_overwritten_when_skip_or_rename()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions(), tree);
        string dest = tmp.Combine("o");
        Directory.CreateDirectory(Path.Combine(dest, "arbol"));
        File.WriteAllText(Path.Combine(dest, "arbol", "texto.txt"), "MIO");
        using var a = ArchiveOpener.Open(path);
        Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Skip });
        Assert.Equal("MIO", File.ReadAllText(Path.Combine(dest, "arbol", "texto.txt")));
        Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Rename });
        Assert.Equal("MIO", File.ReadAllText(Path.Combine(dest, "arbol", "texto.txt")));
        Assert.True(File.Exists(Path.Combine(dest, "arbol", "texto (1).txt")));
    }
}

public class RecoveryTests
{
    private static string Make(TempDir tmp, int percent)
    {
        string tree = Data.MakeTree(tmp.Path);
        return Data.Create(tmp, "r.mdr", new ArchiveOptions { RecoveryPercent = percent, Level = 0 }, tree);
    }

    private static byte[] Damage(byte[] original, params int[] blocks)
    {
        var d = (byte[])original.Clone();
        foreach (var b in blocks) { d[b * 16384 + 5] ^= 0xFF; d[b * 16384 + 9000] ^= 0x5A; }
        return d;
    }

    [Fact]
    public void Healthy_archive_tests_ok()
    {
        using var tmp = new TempDir();
        string p = Make(tmp, 5);
        using (var a = ArchiveOpener.Open(p)) { Assert.True(a.HasRecovery); Assert.Equal(5, ((MdrArchive)a).RecoveryPercent); }
        var t = RecoveryRecord.Test(p);
        Assert.True(t.Success, t.Message);
    }

    [Fact]
    public void Damaged_block_is_repaired_and_content_matches()
    {
        using var tmp = new TempDir();
        string p = Make(tmp, 5);
        var original = File.ReadAllBytes(p);
        var damaged = (byte[])original.Clone();
        for (int i = 0; i < 4000; i++) damaged[100_000 + i] ^= 0xA5;
        File.WriteAllBytes(p, damaged);

        using (var a = ArchiveOpener.Open(p)) Assert.False(Extractor.Test(a).Ok);
        var test = RecoveryRecord.Test(p);
        Assert.False(test.Success);
        Assert.Equal(1, test.BlocksDamaged);

        string fixedPath = tmp.Combine("reparado.mdr");
        var rep = RecoveryRecord.Repair(p, fixedPath);
        Assert.True(rep.Success, rep.Message);
        Assert.Equal(1, rep.BlocksRepaired);
        Assert.True(File.ReadAllBytes(fixedPath).AsSpan().SequenceEqual(original));
        using (var a = ArchiveOpener.Open(fixedPath)) Assert.True(Extractor.Test(a).Ok);
        Assert.True(File.ReadAllBytes(p).AsSpan().SequenceEqual(damaged));
    }

    [Fact]
    public void Many_damaged_blocks_scattered_and_in_a_burst_are_repaired()
    {
        using var tmp = new TempDir();
        string p = Make(tmp, 5);                       // un grupo de ~190 bloques con 10 de paridad
        var original = File.ReadAllBytes(p);
        // 3 sueltos + una ráfaga de 5 seguidos = 8 bloques dañados en el mismo grupo
        File.WriteAllBytes(p, Damage(original, 3, 40, 150, 100, 101, 102, 103, 104));
        var rep = RecoveryRecord.Repair(p, tmp.Combine("x.mdr"));
        Assert.True(rep.Success, rep.Message);
        Assert.Equal(8, rep.BlocksRepaired);
        Assert.True(File.ReadAllBytes(tmp.Combine("x.mdr")).AsSpan().SequenceEqual(original));
    }

    [Fact]
    public void More_damage_than_the_record_can_fix_is_reported_without_inventing_data()
    {
        using var tmp = new TempDir();
        string p = Make(tmp, 5);
        var original = File.ReadAllBytes(p);
        File.WriteAllBytes(p, Damage(original, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13));   // 12 > 10
        var rep = RecoveryRecord.Repair(p, tmp.Combine("x.mdr"));
        Assert.False(rep.Success);
        Assert.Equal(12, rep.BlocksDamaged);
        Assert.Equal(0, rep.BlocksRepaired);
        Assert.Contains("supera", rep.Message);
    }

    [Fact]
    public void Damage_in_different_groups_both_repaired()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string big = Path.Combine(tree, "grande.bin");
        File.WriteAllBytes(big, Data.Random(6_000_000, 7));                          // más de un grupo
        string p = Data.Create(tmp, "g.mdr", new ArchiveOptions { RecoveryPercent = 5, Level = 0 }, tree);
        var original = File.ReadAllBytes(p);
        File.WriteAllBytes(p, Damage(original, 10, 250, 400));
        var rep = RecoveryRecord.Repair(p, tmp.Combine("x.mdr"));
        Assert.True(rep.Success, rep.Message);
        Assert.Equal(3, rep.BlocksRepaired);
        Assert.True(File.ReadAllBytes(tmp.Combine("x.mdr")).AsSpan().SequenceEqual(original));
    }

    [Fact]
    public void No_record_gives_clear_message()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "n.mdr", new ArchiveOptions(), tree);
        var rep = RecoveryRecord.Repair(p, tmp.Combine("x.mdr"));
        Assert.False(rep.Success);
        Assert.Contains("no tiene registro", rep.Message);
        Assert.False(File.Exists(tmp.Combine("x.mdr")));
    }

    [Fact]
    public void Encrypted_archive_can_also_be_repaired()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "e.mdr", new ArchiveOptions { Password = "z", RecoveryPercent = 10 }, tree);
        var original = File.ReadAllBytes(p);
        var d = (byte[])original.Clone();
        d[30_000] ^= 0x77;
        File.WriteAllBytes(p, d);
        var rep = RecoveryRecord.Repair(p, tmp.Combine("x.mdr"));
        Assert.True(rep.Success);
        using var a = ArchiveOpener.Open(tmp.Combine("x.mdr"), "z");
        Assert.True(Extractor.Test(a).Ok);
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.SevenZip)]
    [InlineData(ArchiveFormat.TarGz)]
    public void Other_formats_get_a_recovery_file_that_repairs_damage_and_truncation(ArchiveFormat f)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "t" + ArchiveBuilder.Extension(f), new ArchiveOptions { Format = f, RecoveryPercent = 10, Level = 1 }, tree);
        Assert.True(File.Exists(RecoveryRecord.SidecarPathFor(p)));
        Assert.True(RecoveryRecord.Test(p).Success);
        var original = File.ReadAllBytes(p);

        // daño en varios sitios
        var damaged = (byte[])original.Clone();
        // un archivo pequeño tiene pocos bloques (y por tanto poca paridad): se dañan dos bloques
        damaged[3000] ^= 0xFF;
        damaged[Math.Min(60_000, damaged.Length / 2)] ^= 0xFF;
        File.WriteAllBytes(p, damaged);
        var rep = RecoveryRecord.Repair(p, tmp.Combine("fix1" + ArchiveBuilder.Extension(f)));
        Assert.True(rep.Success, rep.Message);
        Assert.True(File.ReadAllBytes(tmp.Combine("fix1" + ArchiveBuilder.Extension(f))).AsSpan().SequenceEqual(original));

        // archivo truncado (se pierde el final): con el fichero aparte también se recupera
        File.WriteAllBytes(p, original.AsSpan(0, original.Length - 20_000).ToArray());
        var rep2 = RecoveryRecord.Repair(p, tmp.Combine("fix2" + ArchiveBuilder.Extension(f)));
        Assert.True(rep2.Success, rep2.Message);
        Assert.True(File.ReadAllBytes(tmp.Combine("fix2" + ArchiveBuilder.Extension(f))).AsSpan().SequenceEqual(original));
    }

    [Fact]
    public void Editing_a_zip_rebuilds_its_recovery_file()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "t.zip", new ArchiveOptions { Format = ArchiveFormat.Zip, RecoveryPercent = 5 }, tree);
        var plan = new EditPlan(); plan.Delete.Add("arbol/texto.txt");
        ArchiveEditor.Apply(p, null, plan);
        Assert.True(RecoveryRecord.Test(p).Success);                   // el .mdrrec coincide con el zip nuevo
        Assert.Equal(5, RecoveryRecord.SidecarPercent(p));
    }
}

public class EditorTests
{
    private static string Base(TempDir tmp, ArchiveOptions? o = null)
    {
        string tree = Data.MakeTree(tmp.Path);
        return Data.Create(tmp, "e.mdr", o ?? new ArchiveOptions(), tree);
    }

    [Fact]
    public void Delete_file_and_folder()
    {
        using var tmp = new TempDir();
        string p = Base(tmp);
        var plan = new EditPlan();
        plan.Delete.Add("arbol/aleatorio.bin");
        plan.Delete.Add("arbol/sub");
        ArchiveEditor.Apply(p, null, plan);
        using var a = ArchiveOpener.Open(p);
        Assert.DoesNotContain(a.Entries, e => e.Path.StartsWith("arbol/sub") || e.Path == "arbol/aleatorio.bin");
        Assert.Contains(a.Entries, e => e.Path == "arbol/texto.txt");
        Assert.True(Extractor.Test(a).Ok);
    }

    [Fact]
    public void Rename_file_and_folder()
    {
        using var tmp = new TempDir();
        string p = Base(tmp);
        var plan = new EditPlan();
        plan.Rename["arbol/texto.txt"] = "arbol/nuevo.txt";
        plan.Rename["arbol/sub"] = "arbol/carpeta2";
        ArchiveEditor.Apply(p, null, plan);
        using var a = ArchiveOpener.Open(p);
        Assert.Contains(a.Entries, e => e.Path == "arbol/nuevo.txt");
        Assert.Contains(a.Entries, e => e.Path == "arbol/carpeta2/hondo/grande.txt");
        Assert.DoesNotContain(a.Entries, e => e.Path == "arbol/texto.txt");
        Assert.True(Extractor.Test(a).Ok);
    }

    [Fact]
    public void Add_files_and_update_modes()
    {
        using var tmp = new TempDir();
        string p = Base(tmp);
        string extra = tmp.Combine("extra.txt");
        File.WriteAllText(extra, "nuevo");
        var plan = new EditPlan();
        plan.Add.AddRange(SourceCollector.Collect(new[] { extra }, new ArchiveOptions(), PathMode.Relative, "arbol/mas"));
        ArchiveEditor.Apply(p, null, plan);
        using (var a = ArchiveOpener.Open(p))
        {
            var e = a.Entries.Single(x => x.Path == "arbol/mas/extra.txt");
            Assert.Equal("nuevo", System.Text.Encoding.UTF8.GetString(a.ReadAll(e)));
        }
        // actualizar: un fichero MÁS ANTIGUO no debe reemplazar
        File.WriteAllText(extra, "viejo");
        File.SetLastWriteTime(extra, DateTime.Now.AddDays(-3));
        var plan2 = new EditPlan { Mode = UpdateMode.AddUpdate };
        plan2.Add.AddRange(SourceCollector.Collect(new[] { extra }, new ArchiveOptions(), PathMode.Relative, "arbol/mas"));
        ArchiveEditor.Apply(p, null, plan2);
        using (var a = ArchiveOpener.Open(p))
            Assert.Equal("nuevo", System.Text.Encoding.UTF8.GetString(a.ReadAll(a.Entries.Single(x => x.Path == "arbol/mas/extra.txt"))));
        // reemplazar siempre
        var plan3 = new EditPlan { Mode = UpdateMode.AddReplace };
        plan3.Add.AddRange(SourceCollector.Collect(new[] { extra }, new ArchiveOptions(), PathMode.Relative, "arbol/mas"));
        ArchiveEditor.Apply(p, null, plan3);
        using (var a = ArchiveOpener.Open(p))
            Assert.Equal("viejo", System.Text.Encoding.UTF8.GetString(a.ReadAll(a.Entries.Single(x => x.Path == "arbol/mas/extra.txt"))));
    }

    [Fact]
    public void Comment_lock_recovery_and_locked_refuses_edits()
    {
        using var tmp = new TempDir();
        string p = Base(tmp);
        ArchiveEditor.Apply(p, null, new EditPlan { SetComment = true, NewComment = "Mi comentario" });
        ArchiveEditor.Apply(p, null, new EditPlan { RecoveryPercent = 5 });
        ArchiveEditor.Apply(p, null, new EditPlan { Lock = true });
        using (var a = ArchiveOpener.Open(p))
        {
            Assert.Equal("Mi comentario", a.Comment);
            Assert.True(a.HasRecovery);
            Assert.True(a.IsLocked);
        }
        var plan = new EditPlan(); plan.Delete.Add("arbol/texto.txt");
        Assert.Throws<LockedArchiveException>(() => ArchiveEditor.Apply(p, null, plan));
        ArchiveEditor.Apply(p, null, new EditPlan { Lock = false });       // desbloquear está permitido
        ArchiveEditor.Apply(p, null, plan);
    }

    [Fact]
    public void Edit_encrypted_archive_keeps_password()
    {
        using var tmp = new TempDir();
        string p = Base(tmp, new ArchiveOptions { Password = "pw", EncryptNames = true });
        var plan = new EditPlan(); plan.Delete.Add("arbol/texto.txt");
        ArchiveEditor.Apply(p, "pw", plan);
        Assert.Throws<PasswordRequiredException>(() => ArchiveOpener.Open(p));
        using var a = ArchiveOpener.Open(p, "pw");
        Assert.DoesNotContain(a.Entries, e => e.Path == "arbol/texto.txt");
    }

    [Fact]
    public void Failed_edit_leaves_original_untouched()
    {
        using var tmp = new TempDir();
        string p = Base(tmp);
        var before = File.ReadAllBytes(p);
        var plan = new EditPlan();
        plan.Add.Add(new SourceItem { EntryPath = "x.txt", Size = 3, Open = () => throw new IOException("fallo simulado") });
        Assert.Throws<IOException>(() => ArchiveEditor.Apply(p, null, plan));
        Assert.True(File.ReadAllBytes(p).AsSpan().SequenceEqual(before));
        Assert.Empty(Directory.GetFiles(tmp.Path, ".e.mdr*"));
    }

    [Fact]
    public void Zip_can_be_edited_and_rar_is_read_only()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string zip = Data.Create(tmp, "e.zip", new ArchiveOptions { Format = ArchiveFormat.Zip }, tree);
        var plan = new EditPlan(); plan.Delete.Add("arbol/texto.txt");
        ArchiveEditor.Apply(zip, null, plan);
        using (var a = ArchiveOpener.Open(zip)) Assert.DoesNotContain(a.Entries, e => e.Path == "arbol/texto.txt");
    }

    [Fact]
    public void Convert_between_formats_keeps_data_and_adds_options()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string zip = Data.Create(tmp, "a.zip", new ArchiveOptions { Format = ArchiveFormat.Zip }, tree);
        string mdr = ArchiveEditor.Convert(zip, null, tmp.Combine("b.mdr"), new ArchiveOptions { Password = "k", Solid = true, RecoveryPercent = 5 });
        using var a = ArchiveOpener.Open(mdr, "k");
        Assert.True(a.IsSolid && a.IsEncrypted && a.HasRecovery);
        Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }
}

public class ToolTests
{
    [Fact]
    public void Glob_matching()
    {
        Assert.True(Glob.IsMatch("Foto.JPG", "*.jpg"));
        Assert.True(Glob.IsMatch("a1.txt", "a?.txt"));
        Assert.False(Glob.IsMatch("a12.txt", "a?.txt"));
        Assert.True(Glob.MatchesAny("dir/sub/x.zip", new[] { "*.rar", "*.zip" }));
    }

    [Fact]
    public void Exclude_patterns_and_path_modes()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var items = SourceCollector.Collect(new[] { tree }, new ArchiveOptions { ExcludePatterns = { "*.jpg", "hondo" } });
        Assert.DoesNotContain(items, i => i.EntryPath.EndsWith(".jpg"));
        Assert.DoesNotContain(items, i => i.EntryPath.Contains("hondo"));
        var flat = SourceCollector.Collect(new[] { tree }, new ArchiveOptions(), PathMode.None);
        Assert.All(flat, i => Assert.DoesNotContain("/", i.EntryPath));
        Assert.DoesNotContain(flat, i => i.IsDirectory);
    }

    [Fact]
    public void Search_text_and_hex_inside_archive_and_folder()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        File.WriteAllText(Path.Combine(tree, "secreto.txt"), "esto contiene la AGUJA escondida\nsegunda línea");
        string p = Data.Create(tmp, "s.mdr", new ArchiveOptions(), tree);
        using var a = ArchiveOpener.Open(p);
        var hits = Finder.SearchArchive(a, new SearchQuery { Text = "aguja" });
        Assert.Single(hits);
        Assert.EndsWith("secreto.txt", hits[0].Item);
        Assert.Empty(Finder.SearchArchive(a, new SearchQuery { Text = "aguja", CaseSensitive = true }));
        var hex = Finder.SearchArchive(a, new SearchQuery { Text = "41 47 55 4A 41", Hex = true });
        Assert.Single(hex);
        var folder = Finder.SearchFolder(tmp.Path, new SearchQuery { Text = "AGUJA", InsideArchives = true });
        Assert.True(folder.Count >= 2);           // el fichero suelto y el contenido dentro del .mdr
    }

    [Fact]
    public void Report_contains_summary()
    {
        using var tmp = new TempDir();
        string p = Data.Create(tmp, "r.mdr", new ArchiveOptions { Comment = "nota" });
        using var a = ArchiveOpener.Open(p);
        var t = ArchiveReport.Text(a);
        Assert.Contains("Ratio", t);
        Assert.Contains("arbol/texto.txt", t);
        Assert.Contains("<table>", ArchiveReport.Html(a));
    }

    [Fact]
    public void Benchmark_smoke()
    {
        var r = Benchmark.Run(1);
        Assert.Equal(6, r.Count);
        Assert.All(r, x => Assert.True(x.CompressMBps > 0 && x.RatioPercent < 100));
    }

    [Fact]
    public void Settings_save_load_export_import()
    {
        using var tmp = new TempDir();
        var s = new AppSettings { Theme = ThemeMode.Dark, DefaultLevel = 5 };
        s.Favorites.Add(new Favorite { Name = "Docs", Path = @"C:\Docs" });
        s.Profiles.Add(new CompressionProfile { Name = "Fuerte", Options = new ArchiveOptions { Level = 5, Solid = true } });
        SettingsStore.Save(s, tmp.Combine("s.json"));
        var l = SettingsStore.Load(tmp.Combine("s.json"));
        Assert.Equal(ThemeMode.Dark, l.Theme);
        Assert.Equal("Docs", l.Favorites[0].Name);
        Assert.True(l.Profiles[0].Options.Solid);
        SettingsStore.Export(l, tmp.Combine("exp.json"));
        Assert.Equal(5, SettingsStore.Import(tmp.Combine("exp.json")).DefaultLevel);
        File.WriteAllText(tmp.Combine("bad.json"), "{no es json");
        Assert.NotNull(SettingsStore.Load(tmp.Combine("bad.json")));
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public void Shell_integration_writes_only_under_test_key_and_cleans_up()
    {
        string root = @"Software\MedrarTest_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            var sh = new ShellIntegration(root);
            Assert.False(sh.IsContextMenuInstalled());
            sh.Install(@"C:\Prueba\Medrar.exe");
            Assert.True(sh.IsContextMenuInstalled());
            Assert.True(sh.IsAssociated());
            using (var k = Registry.CurrentUser.OpenSubKey($@"{root}\Medrar.Archive\shell\open\command"))
                Assert.Contains("Medrar.exe", (string)k!.GetValue("")!);
            sh.Uninstall();
            Assert.False(sh.IsContextMenuInstalled());
            Assert.False(sh.IsAssociated());
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
    }
}
