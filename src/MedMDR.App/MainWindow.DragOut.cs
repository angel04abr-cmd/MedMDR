using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MedMDR.Core;

namespace MedMDR.Wpf;

/// <summary>Arrastrar ficheros desde la lista hacia el Explorador u otro programa.</summary>
public partial class MainWindow
{
    private readonly Dictionary<string, string> _dragCache = new();
    private bool _draggingOut;
    private Point _dragStart;
    private bool _dragPending;

    private void FileList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // solo si se pulsa sobre una fila (no en la cabecera ni en la barra de desplazamiento)
        _dragPending = ItemsControl.ContainerFromElement(FileList, (DependencyObject)e.OriginalSource) is ListViewItem;
        _dragStart = e.GetPosition(null);
    }

    private void FileList_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragPending || e.LeftButton != MouseButtonState.Pressed) { _dragPending = false; return; }
        var d = e.GetPosition(null) - _dragStart;
        if (Math.Abs(d.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(d.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        _dragPending = false;
        StartDragOut();
    }

    /// <summary>
    /// Desde un archivo, los ficheros se extraen a una carpeta temporal y se ofrecen al Explorador (u otro programa) como ficheros
    /// normales. Desde una carpeta, se ofrecen los ficheros reales.
    /// </summary>
    private void StartDragOut()
    {
        var paths = PrepareDragPaths();
        if (paths.Length == 0) return;
        _draggingOut = true;
        try { DragDrop.DoDragDrop(FileList, new DataObject(DataFormats.FileDrop, paths), DragDropEffects.Copy); }
        finally { _draggingOut = false; }
    }

    /// <summary>Rutas reales (en disco) de la selección; desde un archivo, las extrae antes a una carpeta temporal.</summary>
    private string[] PrepareDragPaths()
    {
        var sel = Selected().Where(n => !n.IsUp).ToList();
        if (sel.Count == 0) return Array.Empty<string>();
        string[] paths;
        if (!InArchive) paths = sel.Select(n => n.FullPath).Where(p => p.Length > 0).ToArray();
        else
        {
            var entries = EntriesForSelection(false);
            string key = _session!.Path + "|" + _inside + "|" + string.Join("|", entries.Select(x => x.Path).OrderBy(x => x));
            if (!_dragCache.TryGetValue(key, out var dir) || !Directory.Exists(dir))
            {
                dir = Path.Combine(Path.GetTempPath(), AppInfo.Name, "drag-" + Guid.NewGuid().ToString("N")[..8]);
                var o = new ExtractOptions { DestinationDir = dir, PathMode = PathMode.Relative, BaseFolder = _inside, Overwrite = OverwriteMode.Overwrite };
                long total = entries.Where(x => !x.IsDirectory).Sum(x => x.Size)
                           + entries.Where(x => x.IsDirectory).Sum(d => Archive.Entries.Where(f => !f.IsDirectory && f.Path.StartsWith(d.Path.TrimEnd('/') + "/")).Sum(f => f.Size));
                bool done = false;
                if (total <= 64L * 1024 * 1024)
                {
                    // poco contenido: se extrae al instante, sin ventanas, para que el arrastre siga activo
                    try { Extractor.Extract(Archive, entries, o); done = true; }
                    catch (PasswordRequiredException) { }
                    catch (WrongPasswordException) { }
                    catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); return Array.Empty<string>(); }
                }
                if (!done)
                {
                    // mucho contenido (o falta la contraseña): se extrae con progreso y se vuelve a intentar el arrastre
                    var res = ExtractWith(_session!, entries, o);
                    if (res != null && res.Files.Count > 0)
                    {
                        _tempDirs.Add(dir); _dragCache[key] = dir;
                        Say("Listo: ya se puede arrastrar la selección al Explorador (vuelve a arrastrarla).");
                    }
                    return Array.Empty<string>();
                }
                _tempDirs.Add(dir); _dragCache[key] = dir;
            }
            paths = sel.Select(n => Path.Combine(dir, n.Name)).Where(p => File.Exists(p) || Directory.Exists(p)).ToArray();
        }
        return paths;
    }
}
