using MedMDR.Core;

namespace MedMDR.Tests;

public class UltraTests
{
    /// <summary>Texto, algo parecido a un programa (registros binarios repetitivos), datos aleatorios, vacíos y muchos ficheros pequeños.</summary>
    private static string MakeTree(string root, int textBytes = 700_000, int binBytes = 700_000)
    {
        string t = Path.Combine(root, "ultra");
        Directory.CreateDirectory(Path.Combine(t, "pequenos"));
        File.WriteAllBytes(Path.Combine(t, "texto.txt"), Data.Text(textBytes, 11));
        var bin = new byte[binBytes];
        var rnd = new Random(12);
        for (int i = 0; i + 16 <= bin.Length; i += 16)       // «instrucciones» de 16 bytes con operandos que se parecen
        {
            bin[i] = (byte)(0x40 + rnd.Next(4)); bin[i + 1] = 0x8B; bin[i + 2] = (byte)(rnd.Next(8) * 8 + 0x05);
            BitConverter.GetBytes(0x140000000 + (i * 7 % 0x4000)).CopyTo(bin, i + 3);
            bin[i + 7] = (byte)rnd.Next(256);
        }
        File.WriteAllBytes(Path.Combine(t, "programa.exe"), bin);   // .exe no está en la lista de «ya comprimidos»
        File.WriteAllBytes(Path.Combine(t, "aleatorio.bin"), Data.Random(120_000, 13));
        File.WriteAllBytes(Path.Combine(t, "vacio.dat"), Array.Empty<byte>());
        for (int i = 0; i < 40; i++) File.WriteAllBytes(Path.Combine(t, "pequenos", $"f{i}.txt"), Data.Text(20 + i * 37, 100 + i));
        File.SetLastWriteTime(Path.Combine(t, "texto.txt"), new DateTime(2025, 1, 2, 3, 4, 5));
        return t;
    }

    private static void RoundTrip(ArchiveOptions o, string? password = null, bool alsoSingle = false)
    {
        using var tmp = new TempDir();
        string tree = MakeTree(tmp.Path);
        var expected = Data.Snapshot(tree);
        string path = Data.Create(tmp, "u.mdr", o, tree);

        using var a = ArchiveOpener.Open(path, password);
        string dest = tmp.Combine("out");
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = dest, Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok, string.Join("; ", r.Errors.Select(x => x.Error)));
        Data.AssertSame(expected, Data.Snapshot(Path.Combine(dest, "ultra")));
        Assert.Equal(new DateTime(2025, 1, 2, 3, 4, 5), File.GetLastWriteTime(Path.Combine(dest, "ultra", "texto.txt")));

        if (alsoSingle)    // extraer un solo fichero del medio de un bloque sólido (obliga a saltar datos descomprimidos)
        {
            string one = tmp.Combine("uno");
            var pick = a.Entries.Single(e => e.Path == "ultra/programa.exe");
            var r2 = Extractor.Extract(a, new[] { pick }, new ExtractOptions { DestinationDir = one, Overwrite = OverwriteMode.Overwrite });
            Assert.True(r2.Ok);
            Assert.True(File.ReadAllBytes(Path.Combine(one, "ultra", "programa.exe")).AsSpan().SequenceEqual(expected["programa.exe"]));
        }
        var t = Extractor.Test(a, ProgressReporter.None);
        Assert.True(t.Ok, string.Join("; ", t.Errors.Select(x => x.Error)));
    }

    [Theory]
    [InlineData(false, UltraMethod.Auto)]
    [InlineData(true, UltraMethod.Auto)]
    [InlineData(false, UltraMethod.Brotli)]
    [InlineData(true, UltraMethod.Brotli)]
    [InlineData(false, UltraMethod.Lzma)]
    [InlineData(true, UltraMethod.Lzma)]
    public void Ultra_roundtrip(bool solid, UltraMethod method) =>
        RoundTrip(new ArchiveOptions { Level = 6, Solid = solid, UltraMethod = method }, alsoSingle: true);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Ultra_lzma_with_password_hidden_names_and_recovery(bool solid)
    {
        var o = new ArchiveOptions { Level = 6, Solid = solid, UltraMethod = UltraMethod.Lzma, Password = "clave ñ 9", EncryptNames = true, RecoveryPercent = 5 };
        RoundTrip(o, "clave ñ 9", alsoSingle: true);
    }

    [Fact]
    public void Forced_method_is_what_the_archive_reports()
    {
        foreach (var (m, name) in new[] { (UltraMethod.Lzma, "LZMA"), (UltraMethod.Brotli, "Brotli") })
        {
            using var tmp = new TempDir();
            string path = Data.Create(tmp, "u.mdr", new ArchiveOptions { Level = 6, UltraMethod = m }, MakeTree(tmp.Path, 100_000, 100_000));
            using var a = ArchiveOpener.Open(path);
            var texto = a.Entries.Single(e => e.Path == "ultra/texto.txt");
            Assert.Equal(name, texto.Method);
            Assert.Equal("Almacenado", a.Entries.Single(e => e.Path == "ultra/vacio.dat").Method);   // los vacíos no se comprimen
        }
    }

    [Fact]
    public void Auto_picks_a_real_method_and_is_never_bigger_than_level_5()
    {
        using var tmp = new TempDir();
        string tree = MakeTree(tmp.Path);
        string p5 = Data.Create(tmp, "n5.mdr", new ArchiveOptions { Level = 5, Solid = true }, tree);
        string p6 = Data.Create(tmp, "n6.mdr", new ArchiveOptions { Level = 6, Solid = true }, tree);
        long s5 = new FileInfo(p5).Length, s6 = new FileInfo(p6).Length;
        Assert.True(s6 <= s5, $"Ultra {s6} > nivel 5 {s5}");
        using var a = ArchiveOpener.Open(p6);
        Assert.Contains(a.Entries.Where(e => !e.IsDirectory && e.Size > 0 && e.Method != "Almacenado").Select(e => e.Method).Distinct().Single(), new[] { "Brotli", "LZMA" });
    }

    [Fact]
    public void Non_solid_ultra_chooses_per_file()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "u.mdr", new ArchiveOptions { Level = 6, Solid = false }, MakeTree(tmp.Path));
        using var a = ArchiveOpener.Open(path);
        var methods = a.Entries.Where(e => !e.IsDirectory && e.Size > 0).Select(e => e.Method).Distinct().ToList();
        Assert.All(methods, m => Assert.Contains(m, new[] { "Brotli", "LZMA", "Almacenado" }));
    }

    [Fact]
    public void Damaged_lzma_data_is_reported_not_crashing()
    {
        using var tmp = new TempDir();
        string path = Data.Create(tmp, "u.mdr", new ArchiveOptions { Level = 6, Solid = true, UltraMethod = UltraMethod.Lzma }, MakeTree(tmp.Path, 200_000, 200_000));
        var bytes = File.ReadAllBytes(path);
        for (int k = 0; k < 30; k++) bytes[bytes.Length / 3 + k] ^= 0xFF;
        File.WriteAllBytes(path, bytes);
        using var a = ArchiveOpener.Open(path);
        bool ok;
        try { ok = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("out"), Overwrite = OverwriteMode.Overwrite }).Ok; }
        catch (MedMDRException) { ok = false; }      // en modo sólido un daño corta la extracción con un error de comprobación
        Assert.False(ok);
    }

    [Theory]
    [InlineData(ArchiveFormat.Zip)]
    [InlineData(ArchiveFormat.SevenZip)]
    [InlineData(ArchiveFormat.TarGz)]
    public void Level_6_is_harmless_for_other_formats(ArchiveFormat f)
    {
        using var tmp = new TempDir();
        string tree = MakeTree(tmp.Path, 80_000, 80_000);
        var expected = Data.Snapshot(tree);
        string path = Data.Create(tmp, "x" + ArchiveBuilder.Extension(f), new ArchiveOptions { Format = f, Level = 6 }, tree);
        using var a = ArchiveOpener.Open(path);
        var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("out"), Overwrite = OverwriteMode.Overwrite });
        Assert.True(r.Ok);
        Data.AssertSame(expected, Data.Snapshot(Path.Combine(tmp.Combine("out"), "ultra")));
    }

    [Fact]
    public void Lzma_stream_roundtrips_edge_sizes()
    {
        foreach (int n in new[] { 0, 1, 5, 255, 65_536, 1_000_003 })
        {
            using var tmp = new TempDir();
            string tree = Path.Combine(tmp.Path, "t");
            Directory.CreateDirectory(tree);
            var data = n == 0 ? Array.Empty<byte>() : Data.Text(n, n);
            File.WriteAllBytes(Path.Combine(tree, "a.txt"), data);
            string path = Data.Create(tmp, "e.mdr", new ArchiveOptions { Level = 6, UltraMethod = UltraMethod.Lzma, Solid = n % 2 == 1 }, tree);
            using var a = ArchiveOpener.Open(path);
            var r = Extractor.Extract(a, a.Entries, new ExtractOptions { DestinationDir = tmp.Combine("out"), Overwrite = OverwriteMode.Overwrite });
            Assert.True(r.Ok, $"n={n}");
            Assert.True(File.ReadAllBytes(Path.Combine(tmp.Combine("out"), "t", "a.txt")).AsSpan().SequenceEqual(data), $"n={n}");
        }
    }
}
