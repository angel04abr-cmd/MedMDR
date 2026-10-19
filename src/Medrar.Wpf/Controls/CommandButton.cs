using System.Windows;
using System.Windows.Controls;

namespace Medrar.Wpf;

/// <summary>Botón de la barra de comandos: icono arriba y texto debajo (su aspecto está en Themes/Styles.xaml).</summary>
public sealed class CommandButton : Button
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(IconKind), typeof(CommandButton), new PropertyMetadata(IconKind.Info));
    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(nameof(Label), typeof(string), typeof(CommandButton), new PropertyMetadata(""));

    public static readonly DependencyProperty ShowTextProperty = DependencyProperty.Register(nameof(ShowText), typeof(bool), typeof(CommandButton), new PropertyMetadata(true));
    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.Register(nameof(IconSize), typeof(double), typeof(CommandButton), new PropertyMetadata(24.0));

    static CommandButton() => DefaultStyleKeyProperty.OverrideMetadata(typeof(CommandButton), new FrameworkPropertyMetadata(typeof(CommandButton)));

    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public bool ShowText { get => (bool)GetValue(ShowTextProperty); set => SetValue(ShowTextProperty, value); }
    public double IconSize { get => (double)GetValue(IconSizeProperty); set => SetValue(IconSizeProperty, value); }
    public string Label { get => (string)GetValue(LabelProperty); set => SetValue(LabelProperty, value); }
}
