using System.Windows;
using System.Windows.Controls;

namespace Medrar.Wpf;

/// <summary>Qué pregunta el mensaje (decide el icono y los botones).</summary>
public enum MsgKind { Info, Warning, Error, Question }

/// <summary>Cuadro de mensaje con el aspecto Fluent. Devuelve el botón elegido (Cancel si se cierra).</summary>
public partial class MessageWindow : Window
{
    private MessageBoxResult _result = MessageBoxResult.Cancel;

    private MessageWindow(string text, string title, MsgKind kind, MessageBoxButton buttons)
    {
        InitializeComponent();
        DevTools.Hook(this);
        Title = title;
        Body.Text = text;
        Glyph.Kind = kind switch { MsgKind.Warning => IconKind.Warning, MsgKind.Error => IconKind.Error, MsgKind.Question => IconKind.Question, _ => IconKind.Info };
        switch (buttons)
        {
            case MessageBoxButton.OK: Add("Aceptar", MessageBoxResult.OK, accent: true, isDefault: true, isCancel: true); break;
            case MessageBoxButton.OKCancel: Add("Aceptar", MessageBoxResult.OK, true, true, false); Add("Cancelar", MessageBoxResult.Cancel, false, false, true); break;
            case MessageBoxButton.YesNo: Add("Sí", MessageBoxResult.Yes, true, true, false); Add("No", MessageBoxResult.No, false, false, true); break;
            case MessageBoxButton.YesNoCancel: Add("Sí", MessageBoxResult.Yes, true, true, false); Add("No", MessageBoxResult.No, false, false, false); Add("Cancelar", MessageBoxResult.Cancel, false, false, true); break;
        }
    }

    private void Add(string text, MessageBoxResult value, bool accent, bool isDefault, bool isCancel)
    {
        var b = new Button { Content = text, MinWidth = 96, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(8, 0, 0, 0), IsDefault = isDefault, IsCancel = isCancel };
        if (accent) b.SetResourceReference(StyleProperty, "AccentButtonStyle");
        b.Click += (_, _) => { _result = value; Close(); };
        Buttons.Children.Add(b);
    }

    public static MessageBoxResult Show(Window? owner, string text, string title, MsgKind kind, MessageBoxButton buttons)
    {
        if (App.Args.AutoYes) return buttons is MessageBoxButton.OK ? MessageBoxResult.OK : buttons is MessageBoxButton.OKCancel ? MessageBoxResult.OK : MessageBoxResult.Yes;
        var w = new MessageWindow(text, title, kind, buttons);
        if (owner != null && owner.IsVisible) w.Owner = owner;
        else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        return w._result;
    }
}

/// <summary>Atajos para los mensajes de uso corriente.</summary>
internal static class Msg
{
    public static void Info(Window? owner, string text, string title = AppInfo.Name) => MessageWindow.Show(owner, text, title, MsgKind.Info, MessageBoxButton.OK);
    public static void Warn(Window? owner, string text, string title = AppInfo.Name) => MessageWindow.Show(owner, text, title, MsgKind.Warning, MessageBoxButton.OK);
    public static void Error(Window? owner, string text, string title = AppInfo.Name) => MessageWindow.Show(owner, text, title, MsgKind.Error, MessageBoxButton.OK);
    public static bool Confirm(Window? owner, string text, string title = AppInfo.Name) => MessageWindow.Show(owner, text, title, MsgKind.Question, MessageBoxButton.YesNo) == MessageBoxResult.Yes;
    public static MessageBoxResult AskYesNoCancel(Window? owner, string text, string title = AppInfo.Name) => MessageWindow.Show(owner, text, title, MsgKind.Question, MessageBoxButton.YesNoCancel);

    /// <summary>Mensaje de error entendible para el usuario (igual que en Medrar.exe).</summary>
    public static string Describe(Exception ex) => ex switch
    {
        Medrar.Core.MedrarException m => m.Message,
        UnauthorizedAccessException => "No hay permiso para acceder a esa ubicación. Prueba con otra carpeta o ejecuta MedMDR como administrador.",
        DirectoryNotFoundException or FileNotFoundException => "No se encuentra el fichero o la carpeta: " + ex.Message,
        IOException io when io.Message.Contains("espacio", StringComparison.OrdinalIgnoreCase) || io.Message.Contains("space", StringComparison.OrdinalIgnoreCase) => "No hay espacio suficiente en el disco.",
        IOException io => "Error al leer o escribir un fichero: " + io.Message,
        _ => ex.Message
    };
}
