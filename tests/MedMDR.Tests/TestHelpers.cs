using MedMDR.Core;

namespace MedMDR.Tests;

public sealed class TempDir : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "medmdr-test-" + Guid.NewGuid().ToString("N")[..10]);
    public TempDir() => Directory.CreateDirectory(Path);
    public string Combine(params string[] parts) => System.IO.Path.Combine(new[] { Path }.Concat(parts).ToArray());
    public void Dispose() { try { Directory.Delete(Path, true); } catch { } }
}

public static class Data
{
    public static byte[] Text(int bytes, int seed = 1)
    {
        var rnd = new Random(seed);
        string[] w = { "hola", "mundo", "archivo", "compresión", "ñandú", "datos", "prueba", "carpeta", "añadir", "extraer" };
        var sb = new System.Text.StringBuilder();
        while (sb.Length < bytes) { sb.Append(w[rnd.Next(w.Length)]); sb.Append(rnd.Next(10) == 0 ? "\r\n" : " "); }
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString())[..bytes];
    }

    public static byte[] Random(int bytes, int seed = 2)
    {
        var b = new byte[bytes];
        new Random(seed).NextBytes(b);
        return b;
    }

    /// <summary>Crea un árbol de prueba y devuelve la ruta raíz.</summary>
    public static string MakeTree(string root)
    {
        string t = System.IO.Path.Combine(root, "arbol");
        Directory.CreateDirectory(System.IO.Path.Combine(t, "sub", "hondo"));
        Directory.CreateDirectory(System.IO.Path.Combine(t, "vacia"));
        File.WriteAllBytes(System.IO.Path.Combine(t, "texto.txt"), Text(300_000, 1));
        File.WriteAllBytes(System.IO.Path.Combine(t, "aleatorio.bin"), Random(150_000, 2));
        File.WriteAllBytes(System.IO.Path.Combine(t, "vacio.dat"), Array.Empty<byte>());
        File.WriteAllBytes(System.IO.Path.Combine(t, "sub", "otro ñ.txt"), Text(5_000, 3));
        File.WriteAllBytes(System.IO.Path.Combine(t, "sub", "hondo", "grande.txt"), Text(2_500_000, 4));
        File.WriteAllBytes(System.IO.Path.Combine(t, "foto.jpg"), Random(70_000, 5));
        File.SetLastWriteTime(System.IO.Path.Combine(t, "texto.txt"), new DateTime(2024, 5, 17, 10, 30, 0));
        return t;
    }

    public static Dictionary<string, byte[]> Snapshot(string root)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            d[System.IO.Path.GetRelativePath(root, f).Replace('\\', '/')] = File.ReadAllBytes(f);
        return d;
    }

    public static void AssertSame(Dictionary<string, byte[]> expected, Dictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys.OrderBy(k => k), actual.Keys.OrderBy(k => k));
        foreach (var k in expected.Keys) Assert.True(expected[k].AsSpan().SequenceEqual(actual[k]), $"Contenido distinto en {k}");
    }

    public static string Create(TempDir tmp, string name, ArchiveOptions o, string? treeRoot = null)
    {
        string tree = treeRoot ?? MakeTree(tmp.Path);
        var items = SourceCollector.Collect(new[] { tree }, o);
        return ArchiveBuilder.Create(tmp.Combine(name), items, o);
    }
}
