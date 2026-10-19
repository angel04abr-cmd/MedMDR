using System.Windows;
using System.Windows.Media;

namespace Medrar.Wpf;

/// <summary>Los iconos de Medrar. Todos están dibujados aquí, a mano, en una cuadrícula de 24 × 24.</summary>
public enum IconKind
{
    Add, Extract, Test, View, Delete, Find, Wizard, Info, Repair, Up, Folder, Archive,
    Settings, ChevronDown, ChevronRight, Theme, Check
}

/// <summary>
/// Icono vectorial de línea (estilo Fluent): el trazo toma el color de texto del contenedor (así sigue el tema claro/oscuro y
/// los estados del botón) y los detalles de énfasis toman el color de énfasis de Windows.
/// </summary>
public sealed class Icon : FrameworkElement
{
    private sealed record Shape(string Main, string? Accent = null, string? Fill = null, bool MainIsAccent = false);

    private const string Cube = "M10 2.5 L17.5 6.3 V14.2 L10 18 L2.5 14.2 V6.3 Z M2.5 6.3 L10 10.1 L17.5 6.3 M10 10.1 V18";

    private static readonly Dictionary<IconKind, Shape> Shapes = new()
    {
        [IconKind.Add] = new(Cube, "M18 14.2 V21.8 M14.2 18 H21.8"),
        [IconKind.Extract] = new(Cube, "M13.8 18 H21.8 M18.6 14.8 L21.8 18 L18.6 21.2"),
        [IconKind.Test] = new("M6 3.5 H14 L19 8.5 V20.5 H6 Z M14 3.5 V8.5 H19", "M9 14.2 L11.4 16.6 L15.6 11.6"),
        [IconKind.View] = new("M2.5 12 C5 7.5 8.5 5.5 12 5.5 C15.5 5.5 19 7.5 21.5 12 C19 16.5 15.5 18.5 12 18.5 C8.5 18.5 5 16.5 2.5 12 Z M15 12 A3 3 0 1 1 9 12 A3 3 0 1 1 15 12 Z"),
        [IconKind.Delete] = new("M4 6.5 H20 M9.5 6.5 V4.5 H14.5 V6.5 M6 6.5 L7 20 H17 L18 6.5 M10 10.5 V16 M14 10.5 V16"),
        [IconKind.Find] = new("M17 10.5 A6.5 6.5 0 1 1 4 10.5 A6.5 6.5 0 1 1 17 10.5 Z M15.3 15.3 L21 21"),
        [IconKind.Wizard] = new("M4.5 19.5 L15 9", "M17.5 3 V7 M15.5 5 H19.5 M20 11 V14 M18.5 12.5 H21.5 M7 4 V7 M5.5 5.5 H8.5"),
        [IconKind.Info] = new("M21 12 A9 9 0 1 1 3 12 A9 9 0 1 1 21 12 Z M12 11 V16.5", "M12 7.8 H12.02"),
        [IconKind.Repair] = new("M10 4 H14 V10 H20 V14 H14 V20 H10 V14 H4 V10 H10 Z"),
        [IconKind.Up] = new("M12 19 V6 M6.5 11.5 L12 6 L17.5 11.5"),
        [IconKind.Folder] = new("", Fill: "M3 7.5 A2 2 0 0 1 5 5.5 H9.4 A1 1 0 0 1 10.1 5.8 L11.9 7.6 H19 A2 2 0 0 1 21 9.6 V17 A2 2 0 0 1 19 19 H5 A2 2 0 0 1 3 17 Z"),
        [IconKind.Archive] = new(Cube, MainIsAccent: true),
        [IconKind.Settings] = new("M4 7 H7 M11 7 H20 M4 12 H13 M17 12 H20 M4 17 H6 M10 17 H20 M11 7 A2 2 0 1 1 7 7 A2 2 0 1 1 11 7 Z M17 12 A2 2 0 1 1 13 12 A2 2 0 1 1 17 12 Z M10 17 A2 2 0 1 1 6 17 A2 2 0 1 1 10 17 Z"),
        [IconKind.ChevronDown] = new("M6 9 L12 15 L18 9"),
        [IconKind.ChevronRight] = new("M9 6 L15 12 L9 18"),
        [IconKind.Theme] = new("M12 3 A9 9 0 1 0 12 21 A9 9 0 0 1 12 3 Z"),
        [IconKind.Check] = new("M5 12.5 L10 17.5 L19 7"),
    };

    private static readonly Dictionary<string, Geometry> Cache = new();
    private static readonly Brush FolderBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xF2, 0xB7, 0x3F)));

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }

    private static Geometry Path(string data)
    {
        if (!Cache.TryGetValue(data, out var g)) { g = Geometry.Parse(data); g.Freeze(); Cache[data] = g; }
        return g;
    }

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(IconKind), typeof(Icon),
        new FrameworkPropertyMetadata(IconKind.Info, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Color del trazo: por defecto, el color de texto heredado (cambia con el tema y con los estados del botón).</summary>
    public static readonly DependencyProperty ForegroundProperty = System.Windows.Documents.TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(SystemColors.ControlTextBrush, FrameworkPropertyMetadataOptions.AffectsRender | FrameworkPropertyMetadataOptions.Inherits));

    public static readonly DependencyProperty AccentBrushProperty = DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.DodgerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(20.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

    public Icon()
    {
        SetResourceReference(AccentBrushProperty, "AccentFillColorDefaultBrush");   // sigue al color de énfasis de Windows
        SnapsToDevicePixels = false;
        IsHitTestVisible = false;
    }

    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }
    public Brush AccentBrush { get => (Brush)GetValue(AccentBrushProperty); set => SetValue(AccentBrushProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }

    protected override Size MeasureOverride(Size availableSize) => new(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        var shape = Shapes[Kind];
        double scale = Size / 24.0;
        double stroke = Math.Max(1.0, 1.5);
        dc.PushTransform(new ScaleTransform(scale, scale));
        var ink = shape.MainIsAccent ? AccentBrush : Foreground;
        var cap = PenLineCap.Round;
        var main = new Pen(ink, stroke) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
        if (shape.Fill != null) dc.DrawGeometry(FolderBrush, null, Path(shape.Fill));
        if (shape.Main.Length > 0) dc.DrawGeometry(null, main, Path(shape.Main));
        if (shape.Accent != null)
        {
            var accent = new Pen(AccentBrush, stroke + 0.4) { StartLineCap = cap, EndLineCap = cap, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, accent, Path(shape.Accent));
        }
        dc.Pop();
    }
}
