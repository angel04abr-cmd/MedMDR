using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;

namespace MedMDR.Wpf;

/// <summary>Detalles de Windows que necesita la barra de título propia: fondo Mica, sitio de los botones nativos y ajuste al maximizar.</summary>
internal static class WindowBackdrop
{
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetricsForDpi(int index, uint dpi);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(IntPtr hwnd);

    private const int DWMWA_SYSTEMBACKDROP_TYPE = 38, DWMSBT_MAINWINDOW = 2;     // 2 = Mica
    private const int DWMWA_CAPTION_COLOR = 35;
    private const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;                            // sin color propio: se ve el fondo (Mica)
    private const int SM_CXFRAME = 32, SM_CXPADDEDBORDER = 92, SM_CYFRAME = 33;

    /// <summary>Pide a Windows 11 (22H2 o posterior) el fondo Mica. En Windows 10 devuelve false y se usa un color liso.</summary>
    public static bool TryEnableMica(Window w)
    {
        if (Environment.OSVersion.Version.Build < 22621) return false;
        try
        {
            IntPtr hwnd = new WindowInteropHelper(w).Handle;
            int value = DWMSBT_MAINWINDOW;
            bool ok = DwmSetWindowAttribute(hwnd, DWMWA_SYSTEMBACKDROP_TYPE, ref value, sizeof(int)) == 0;
            // Si el usuario tiene «color de énfasis en las barras de título», Windows pintaría la franja de arriba de ese color: se deja transparente
            int none = unchecked((int)DWMWA_COLOR_NONE);
            DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref none, sizeof(int));
            return ok;
        }
        catch { return false; }
    }

    /// <summary>Ancho (en unidades de WPF) que ocupan los tres botones nativos de ventana, con un pequeño margen.</summary>
    public static double CaptionButtonsWidth(Window w) => 150;

    /// <summary>Lo que una ventana maximizada con barra propia sobresale por cada lado, en unidades de WPF.</summary>
    public static Thickness MaximizedOverhang(Window w)
    {
        IntPtr hwnd = new WindowInteropHelper(w).Handle;
        uint dpi = hwnd == IntPtr.Zero ? 96 : GetDpiForWindow(hwnd);
        double scale = dpi / 96.0;
        double x = (GetSystemMetricsForDpi(SM_CXFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi)) / scale;
        double y = (GetSystemMetricsForDpi(SM_CYFRAME, dpi) + GetSystemMetricsForDpi(SM_CXPADDEDBORDER, dpi)) / scale;
        return new Thickness(x, y, x, y);
    }
}

/// <summary>El título de la ventana es «nombre — MedMDR»; en el encabezado solo se muestra el nombre.</summary>
internal sealed class HeaderTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string t = value as string ?? "";
        string suffix = " — " + AppInfo.Name;
        return t.EndsWith(suffix, StringComparison.Ordinal) ? t[..^suffix.Length] : t;
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
