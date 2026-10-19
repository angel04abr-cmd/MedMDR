using System.Diagnostics;
using MedMDR.Core;

namespace MedMDR.Tests;

/// <summary>Muchos ficheros pequeños: crear un .mdr no debe costar milisegundos por fichero (antes ~10 ms cada uno con Brotli).</summary>
public class SmallFilesTests
{
    [Theory]
    [InlineData(0, 22, 22)]          // sin tamaño conocido: se respeta la ventana pedida
    [InlineData(100, 22, 10)]        // mínimo de Brotli
    [InlineData(1000, 24, 10)]
    [InlineData(5000, 22, 13)]
    [InlineData(1 << 20, 22, 20)]
    [InlineData(100L << 20, 22, 22)] // grande: no se supera la ventana pedida
    public void Brotli_window_is_bounded_by_the_file_size(long size, int window, int expected) =>
        Assert.Equal(expected, Codec.WindowFor(size, window));

    [Fact]
    public void Thousands_of_small_files_roundtrip_quickly()
    {
        using var tmp = new TempDir();
        string src = tmp.Combine("src"); Directory.CreateDirectory(src);
        for (int i = 0; i < 1500; i++) File.WriteAllText(Path.Combine(src, $"f{i:0000}.txt"), $"contenido del fichero {i} " + new string('x', i % 50));

        string mdr = tmp.Combine("muchos.mdr");
        var sw = Stopwatch.StartNew();
        var items = SourceCollector.Collect(new[] { src }, new ArchiveOptions(), PathMode.Relative, null);
        ArchiveBuilder.Create(mdr, items, new ArchiveOptions { Format = ArchiveFormat.Mdr, Level = 3 }, ProgressReporter.None);
        sw.Stop();
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"crear 1500 ficheros pequeños tardó {sw.Elapsed.TotalSeconds:0.0} s");

        using var a = ArchiveOpener.Open(mdr, null);
        Assert.Equal(1500, a.Entries.Count(e => !e.IsDirectory));
        Assert.True(Extractor.Test(a, ProgressReporter.None).Ok);
        var one = a.Entries.First(e => e.Path.EndsWith("f0042.txt"));
        Assert.Equal("contenido del fichero 42 " + new string('x', 42), System.Text.Encoding.UTF8.GetString(a.ReadAll(one, 1000)));
    }
}
