using Medrar.Core;

namespace Medrar.Tests;

public class MdrTests
{
    private static void RoundTrip(ArchiveOptions o, string? password = null)
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string path = Data.Create(tmp, "t.mdr", o, tree);

        using var a = ArchiveOpener.Open(path, password);
        Assert.Equal(expected.Count, a.Entries.Count(e => !e.IsDirectory));
        Assert.Contains(a.Entries, e => e.IsDirectory && e.Path == "arbol/vacia");
        string dest = tmp.Combine("out");
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok, string.Join("; ", r.Errors.Select(x => x.Error)));
        Data.AssertSame(expected, Data.Snapshot(Path.Combine(dest, "arbol")));
        Assert.True(Directory.Exists(Path.Combine(dest, "arbol", "vacia")));
        Assert.Equal(new DateTime(2024, 5, 17, 10, 30, 0), File.GetLastWriteTime(Path.Combine(dest, "arbol", "texto.txt")));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, false)]
    [InlineData(3, false)]
    [InlineData(5, false)]
    [InlineData(3, true)]
    public void Brotli_roundtrip_levels_and_solid(int level, bool solid) => RoundTrip(new ArchiveOptions { Level = level, Solid = solid });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Deflate_roundtrip(bool solid) => RoundTrip(new ArchiveOptions { UseDeflate = true, Solid = solid, Level = 3 });

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void Encrypted_roundtrip(bool solid, bool names) =>
        RoundTrip(new ArchiveOptions { Password = "clave ñ 123", Solid = solid, EncryptNames = names }, "clave ñ 123");

    [Fact]
    public void Compression_actually_shrinks_text_and_stores_jpg()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Level = 4 });
        using var a = ArchiveOpener.Open(path);
        var txt = a.Entries.Single(e => e.Path == "arbol/sub/hondo/grande.txt");
        Assert.True(txt.PackedSize < txt.Size / 2, $"{txt.PackedSize} vs {txt.Size}");
        var jpg = a.Entries.Single(e => e.Path == "arbol/foto.jpg");
        Assert.Equal("Almacenado", jpg.Method);          // las extensiones ya comprimidas se guardan tal cual
        Assert.Equal(jpg.Size, jpg.PackedSize);
    }

    [Fact]
    public void Wrong_and_missing_password()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Password = "buena", EncryptNames = true });
        Assert.Throws<PasswordRequiredException>(() => ArchiveOpener.Open(path));
        Assert.Throws<WrongPasswordException>(() => ArchiveOpener.Open(path, "mala"));
        using var ok = ArchiveOpener.Open(path, "buena");
        Assert.True(ok.IsEncrypted && ok.HasEncryptedNames);
    }

    [Fact]
    public void Visible_names_when_not_hidden_but_content_needs_password()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Password = "buena" });
        using var a = ArchiveOpener.Open(path);          // se puede listar sin contraseña
        Assert.Contains(a.Entries, e => e.Path == "arbol/texto.txt");
        Assert.Throws<PasswordRequiredException>(() => Extractor.Test(a));
        ((MdrArchive)a).Unlock("buena");
        Assert.True(Extractor.Test(a).Ok);
    }

    [Fact]
    public void Names_and_content_are_not_readable_in_the_file_when_encrypted()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Password = "buena", EncryptNames = true, Level = 0 });
        var raw = File.ReadAllBytes(path);
        Assert.True(raw.AsSpan().IndexOf("grande.txt"u8) < 0);
        Assert.True(raw.AsSpan().IndexOf("hola mundo"u8) < 0);
    }

    [Fact]
    public void Tampering_is_detected()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Password = "buena", Level = 0 });
        var raw = File.ReadAllBytes(path);
        raw[raw.Length / 3] ^= 0x55;
        File.WriteAllBytes(path, raw);
        using var a = ArchiveOpener.Open(path, "buena");
        var r = Assert.Throws<WrongPasswordException>(() => Extractor.Test(a));   // GCM no autentica el bloque
    }

    [Fact]
    public void Corruption_without_encryption_is_detected_by_crc()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Level = 0 });
        var raw = File.ReadAllBytes(path);
        raw[200] ^= 0xFF;     // dentro de los datos
        File.WriteAllBytes(path, raw);
        using var a = ArchiveOpener.Open(path);
        var res = Extractor.Test(a);
        Assert.False(res.Ok);
    }

    [Fact]
    public void Solid_extracting_a_single_file()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Solid = true }, tree);
        using var a = ArchiveOpener.Open(path);
        var e = a.Entries.Single(x => x.Path == "arbol/sub/otro ñ.txt");
        var data = a.ReadAll(e);
        Assert.True(data.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(tree, "sub", "otro ñ.txt"))));
    }

    [Fact]
    public void Comment_and_lock_are_stored()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions { Comment = "Hola ñ", Lock = true });
        using var a = ArchiveOpener.Open(path);
        Assert.Equal("Hola ñ", a.Comment);
        Assert.True(a.IsLocked);
    }

    [Fact]
    public void Archive_after_exe_stub_is_found()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "t.mdr", new ArchiveOptions());
        string exe = tmp.Combine("sfx.exe");
        File.WriteAllBytes(exe, Data.Random(33_333, 9));
        using (var o = new FileStream(exe, FileMode.Append)) o.Write(File.ReadAllBytes(path));
        using var a = ArchiveOpener.Open(exe);
        Assert.True(Extractor.Test(a).Ok);
        string clean = SfxBuilder.ExtractArchivePart(exe, tmp.Combine("clean.mdr"));
        Assert.True(File.ReadAllBytes(clean).AsSpan().SequenceEqual(File.ReadAllBytes(path)));
    }

    [Fact]
    public void Volumes_split_and_open()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        var o = new ArchiveOptions { VolumeSizeBytes = 200_000 };
        string first = Data.Create(tmp, "v.mdr", o, tree);
        Assert.EndsWith(".001", first);
        Assert.True(File.Exists(first.Replace(".001", ".002")));
        Assert.False(File.Exists(tmp.Combine("v.mdr")));
        using var a = ArchiveOpener.Open(first);
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("o"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok);
        Data.AssertSame(expected, Data.Snapshot(tmp.Combine("o", "arbol")));
    }

    [Fact]
    public void Cancel_stops_creation_and_leaves_no_files()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var items = SourceCollector.Collect(new[] { tree }, new ArchiveOptions());
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var prog = new ProgressReporter(null, cts.Token);
        Assert.ThrowsAny<OperationCanceledException>(() => ArchiveBuilder.Create(tmp.Combine("c.mdr"), items, new ArchiveOptions(), prog));
        Assert.Empty(Directory.GetFiles(tmp.Path, "c.mdr*"));
        Assert.Empty(Directory.GetFiles(tmp.Path, ".c.mdr*"));
    }

    [Fact]
    public void Progress_reports_all_bytes()
    {
        using var tmp = new TempDir();
        string tree = Data.MakeTree(tmp.Path);
        var items = SourceCollector.Collect(new[] { tree }, new ArchiveOptions());
        ProgressInfo? last = null;
        var prog = new ProgressReporter(new Progress<ProgressInfo>(p => last = p));
        ArchiveBuilder.Create(tmp.Combine("p.mdr"), items, new ArchiveOptions(), prog);
        Assert.Equal(items.Where(i => !i.IsDirectory).Sum(i => i.Size), prog.Total);
        Assert.Equal(prog.Total, prog.Done);
    }
}
