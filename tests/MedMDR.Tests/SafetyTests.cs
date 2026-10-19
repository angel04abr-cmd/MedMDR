using MedMDR.Core;

namespace MedMDR.Tests;

/// <summary>«Eliminar los originales»: solo se borra lo que de verdad quedó dentro del archivo.</summary>
public class SafetyTests
{
    private static string Tree(TempDir tmp)
    {
        string t = tmp.Combine("T");
        Directory.CreateDirectory(Path.Combine(t, "sub"));
        File.WriteAllText(Path.Combine(t, "a.txt"), "a");
        File.WriteAllText(Path.Combine(t, "b.log"), "b");
        File.WriteAllText(Path.Combine(t, "sub", "c.txt"), "c");
        File.WriteAllText(Path.Combine(t, "sub", "d.log"), "d");
        return t;
    }

    [Fact]
    public void Deleting_originals_keeps_what_was_excluded_from_the_archive()
    {
        using var tmp = new TempDir();
        string t = Tree(tmp);
        var o = new ArchiveOptions { ExcludePatterns = { "*.log" } };
        var items = SourceCollector.Collect(new[] { t }, o);

        int deleted = SourceCollector.DeleteArchived(items);

        Assert.Equal(2, deleted);                                           // a.txt y sub\c.txt, que sí se archivaron
        Assert.False(File.Exists(Path.Combine(t, "a.txt")));
        Assert.False(File.Exists(Path.Combine(t, "sub", "c.txt")));
        Assert.True(File.Exists(Path.Combine(t, "b.log")));                 // excluidos: intactos
        Assert.True(File.Exists(Path.Combine(t, "sub", "d.log")));
        Assert.True(Directory.Exists(Path.Combine(t, "sub")));              // la carpeta no está vacía: se queda
    }

    [Fact]
    public void Deleting_originals_removes_folders_left_empty()
    {
        using var tmp = new TempDir();
        string t = Tree(tmp);
        var items = SourceCollector.Collect(new[] { t }, new ArchiveOptions());

        SourceCollector.DeleteArchived(items);

        Assert.False(Directory.Exists(t));                                  // todo se archivó: no queda nada
    }

    [Fact]
    public void Deleting_originals_never_deletes_the_archive_itself()
    {
        using var tmp = new TempDir();
        string t = Tree(tmp);
        string keep = Path.Combine(t, "a.txt");
        var items = SourceCollector.Collect(new[] { t }, new ArchiveOptions());

        SourceCollector.DeleteArchived(items, keep);

        Assert.True(File.Exists(keep));
    }
}
