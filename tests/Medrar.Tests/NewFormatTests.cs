using Medrar.Core;

namespace Medrar.Tests;

public class SevenZipAndZipAesTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(5)]
    public void SevenZip_roundtrip(int level)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string p = Data.Create(tmp, "t.7z", new ArchiveOptions { Format = ArchiveFormat.SevenZip, Level = level }, tree);
        using var a = ArchiveOpener.Open(p);
        Assert.Equal("SEVENZIP", a.FormatName.ToUpperInvariant());
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
        if (level >= 3)
            Assert.True(new FileInfo(p).Length < expected.Values.Sum(v => (long)v.Length) / 2);   // comprime de verdad
    }

    [Fact]
    public void SevenZip_with_password_is_rejected_clearly()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var o = new ArchiveOptions { Format = ArchiveFormat.SevenZip, Password = "x" };
        var ex = Assert.Throws<UnsupportedFormatException>(() => ArchiveBuilder.Create(tmp.Combine("x.7z"), SourceCollector.Collect(new[] { tree }, o), o));
        Assert.Contains(".7z", ex.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4)]
    public void Zip_with_password_roundtrip(int level)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string p = Data.Create(tmp, "t.zip", new ArchiveOptions { Format = ArchiveFormat.Zip, Password = "clave ñ 1", Level = level }, tree);

        // los nombres se ven (el ZIP no puede ocultarlos) pero el contenido pide contraseña
        using (var open = ArchiveOpener.Open(p))
        {
            Assert.True(open.IsEncrypted);
            Assert.Contains(open.Entries, e => e.Path == "arbol/texto.txt");
            Assert.Throws<PasswordRequiredException>(() => Extractor.Test(open));
        }
        using (var bad = ArchiveOpener.Open(p, "otra"))
            Assert.Throws<WrongPasswordException>(() => Extractor.Test(bad));

        using var ok = ArchiveOpener.Open(p, "clave ñ 1");
        var r = Extractor.Extract(ok, ok.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }

    [Fact]
    public void Zip_with_password_does_not_leak_content_in_the_file()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "t.zip", new ArchiveOptions { Format = ArchiveFormat.Zip, Password = "k", Level = 0 }, tree);
        var raw = File.ReadAllBytes(p);
        Assert.True(raw.AsSpan().IndexOf("hola mundo"u8) < 0);          // con nivel 0 se vería en claro si no estuviera cifrado
    }

    [Fact]
    public void Zip_with_password_detects_tampering()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "t.zip", new ArchiveOptions { Format = ArchiveFormat.Zip, Password = "k", Level = 0 }, tree);
        var raw = File.ReadAllBytes(p);
        raw[5000] ^= 0x01;
        File.WriteAllBytes(p, raw);
        using var a = ArchiveOpener.Open(p, "k");
        Assert.False(Extractor.Test(a).Ok && true);   // o falla la autenticación o la descompresión: nunca da por bueno el contenido
    }

    [Fact]
    public void Zip_with_password_can_be_edited_and_keeps_its_password()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "t.zip", new ArchiveOptions { Format = ArchiveFormat.Zip, Password = "k" }, tree);
        var plan = new EditPlan(); plan.Delete.Add("arbol/texto.txt");
        ArchiveEditor.Apply(p, "k", plan);
        using var a = ArchiveOpener.Open(p, "k");
        Assert.DoesNotContain(a.Entries, e => e.Path == "arbol/texto.txt");
        Assert.True(a.IsEncrypted);
        Assert.True(Extractor.Test(a).Ok);
    }

    [Fact]
    public void SevenZip_and_zip_can_be_edited()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        foreach (var f in new[] { ArchiveFormat.SevenZip, ArchiveFormat.Zip })
        {
            string p = Data.Create(tmp, "e" + ArchiveBuilder.Extension(f), new ArchiveOptions { Format = f }, tree);
            var plan = new EditPlan();
            plan.Delete.Add("arbol/aleatorio.bin");
            plan.Rename["arbol/texto.txt"] = "arbol/renombrado.txt";
            ArchiveEditor.Apply(p, null, plan);
            using var a = ArchiveOpener.Open(p);
            Assert.DoesNotContain(a.Entries, e => e.Path == "arbol/aleatorio.bin");
            Assert.Contains(a.Entries, e => e.Path == "arbol/renombrado.txt");
            Assert.True(Extractor.Test(a).Ok);
        }
    }

    [Fact]
    public void Tar_gz_and_tar_can_be_edited()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        foreach (var f in new[] { ArchiveFormat.TarGz, ArchiveFormat.Tar })
        {
            string p = Data.Create(tmp, "e" + ArchiveBuilder.Extension(f), new ArchiveOptions { Format = f }, tree);
            var plan = new EditPlan(); plan.Delete.Add("arbol/sub");
            ArchiveEditor.Apply(p, null, plan);
            using var a = ArchiveOpener.Open(p);
            Assert.DoesNotContain(a.Entries, e => e.Path.StartsWith("arbol/sub"));
            Assert.Contains(a.Entries, e => e.Path == "arbol/texto.txt");
        }
    }
}

public class LockTests
{
    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.SevenZip)]
    [InlineData(ArchiveFormat.TarGz)]
    [InlineData(ArchiveFormat.Mdr)]
    public void Lock_works_for_every_format(ArchiveFormat f)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "l" + ArchiveBuilder.Extension(f), new ArchiveOptions { Format = f, Lock = true }, tree);
        try
        {
            using (var a = ArchiveOpener.Open(p)) Assert.True(a.IsLocked);
            var plan = new EditPlan(); plan.Delete.Add("arbol/texto.txt");
            Assert.Throws<LockedArchiveException>(() => ArchiveEditor.Apply(p, null, plan));
            ArchiveEditor.Apply(p, null, new EditPlan { Lock = false });                  // desbloquear
            using (var a = ArchiveOpener.Open(p)) Assert.False(a.IsLocked);
            ArchiveEditor.Apply(p, null, plan);                                           // ya se puede modificar
            ArchiveEditor.Apply(p, null, new EditPlan { Lock = true });                  // y volver a bloquear
            using (var a = ArchiveOpener.Open(p)) Assert.True(a.IsLocked);
        }
        finally { if (File.Exists(p)) ArchiveBuilder.UnlockFile(p); }
    }

    [Fact]
    public void A_read_only_mdr_is_treated_as_locked()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string p = Data.Create(tmp, "t.mdr", new ArchiveOptions(), tree);
        ArchiveBuilder.LockFile(p);
        try
        {
            using var a = ArchiveOpener.Open(p);
            Assert.True(a.IsLocked);
            var plan = new EditPlan(); plan.Delete.Add("arbol/texto.txt");
            Assert.Throws<LockedArchiveException>(() => ArchiveEditor.Apply(p, null, plan));
        }
        finally { ArchiveBuilder.UnlockFile(p); }
    }
}

/// <summary>
/// Autoextraíbles de cualquier formato. Como en las pruebas no está el módulo real, se usa uno falso (solo bytes):
/// lo importante es que el archivo pegado detrás se localiza, se abre y se extrae.
/// </summary>
[Collection("SFX")]
public class SfxTests : IDisposable
{
    private readonly string _stub = Path.Combine(AppContext.BaseDirectory, "Medrar.Sfx.exe");
    private readonly string _stubFull = Path.Combine(AppContext.BaseDirectory, "Medrar.Sfx.Full.exe");
    public SfxTests()
    {
        File.WriteAllBytes(_stub, Data.Random(40_000, 21));
        File.WriteAllBytes(_stubFull, Data.Random(60_000, 22));
    }
    public void Dispose() { try { File.Delete(_stub); File.Delete(_stubFull); } catch { } }

    [Theory]
    [InlineData(ArchiveFormat.Mdr)]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.SevenZip)]
    [InlineData(ArchiveFormat.TarGz)]
    [InlineData(ArchiveFormat.Tar)]
    public void Self_extracting_works_for_every_format(ArchiveFormat f)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string exe = Data.Create(tmp, "s" + ArchiveBuilder.Extension(f), new ArchiveOptions { Format = f, SelfExtracting = true }, tree);
        Assert.Equal("s.exe", Path.GetFileName(exe));                                  // sin restos de la extensión (.tar.gz → .exe)
        Assert.True(SfxBuilder.IsSfx(exe));
        Assert.True(ArchiveOpener.LooksLikeArchive(exe));
        using var a = ArchiveOpener.Open(exe);
        Assert.False(a.CanModify);                                                      // un autoextraíble es de solo lectura
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok, string.Join(";", r.Errors));
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }

    [Fact]
    public void Self_extracting_zip_with_password_asks_for_it()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string exe = Data.Create(tmp, "s.zip", new ArchiveOptions { Format = ArchiveFormat.Zip, Password = "pw", SelfExtracting = true }, tree);
        using (var a = ArchiveOpener.Open(exe)) Assert.Throws<PasswordRequiredException>(() => Extractor.Test(a));
        using var ok = ArchiveOpener.Open(exe, "pw");
        Assert.True(Extractor.Test(ok).Ok);
    }

    [Fact]
    public void Full_runtime_stub_is_used_when_asked()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string exe = Data.Create(tmp, "s.mdr", new ArchiveOptions { SelfExtracting = true, SfxIncludeRuntime = true }, tree);
        Assert.True(new FileInfo(exe).Length > 60_000);
        Assert.StartsWith(File.ReadAllBytes(_stubFull)[..64].ToHex(), File.ReadAllBytes(exe)[..64].ToHex());
    }

    [Fact]
    public void Convert_existing_archive_to_sfx_and_back()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string zip = Data.Create(tmp, "c.zip", new ArchiveOptions { Format = ArchiveFormat.Zip }, tree);
        string exe = SfxBuilder.ConvertToSfx(zip);
        using (var a = ArchiveOpener.Open(exe)) Assert.True(Extractor.Test(a).Ok);
        Assert.Throws<UnsupportedFormatException>(() => SfxBuilder.ConvertToSfx(exe));      // ya lo es
        string back = SfxBuilder.ExtractArchivePart(exe, tmp.Combine("back.zip"));
        Assert.True(File.ReadAllBytes(back).AsSpan().SequenceEqual(File.ReadAllBytes(zip)));
    }

    [Fact]
    public void Sfx_with_recovery_and_lock_gets_both()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string exe = Data.Create(tmp, "s.zip", new ArchiveOptions { Format = ArchiveFormat.Zip, SelfExtracting = true, RecoveryPercent = 5, Lock = true }, tree);
        try
        {
            Assert.True(File.Exists(RecoveryRecord.SidecarPathFor(exe)));
            Assert.True(RecoveryRecord.Test(exe).Success);
            using var a = ArchiveOpener.Open(exe);
            Assert.True(a.IsLocked);
        }
        finally { ArchiveBuilder.UnlockFile(exe); }
    }
}

[CollectionDefinition("SFX", DisableParallelization = true)]
public class SfxCollection { }

internal static class HexExt
{
    public static string ToHex(this byte[] b) => Convert.ToHexString(b);
}
