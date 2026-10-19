using System.Windows;
using Medrar.Core;

namespace Medrar.Wpf;

public partial class MainWindow
{
    /// <summary>Aplica a la ventana los ajustes de aspecto (texto e iconos de la barra, siempre visible…).</summary>
    private void ApplyWindowSettings()
    {
        Topmost = _settings.AlwaysOnTop;
        foreach (var b in new[] { CmdAdd, CmdExtract, CmdTest, CmdView, CmdDelete, CmdFind, CmdWizard, CmdInfo, CmdRepair })
        {
            b.ShowText = _settings.ShowButtonText;
            b.IconSize = _settings.LargeIcons ? 24 : 18;
        }
    }

    /// <summary>Título de la ventana: solo el nombre o, si se prefiere, la ruta completa.</summary>
    private string WindowTitle(string fullPath, string shortName) =>
        $"{(_settings.ShowFullPathInTitle ? fullPath : shortName)} — {AppInfo.Name}";

    private void Command_Settings(object sender, RoutedEventArgs e)
    {
        if (!SettingsWindow.Edit(this, _settings)) return;
        SaveSettings();
        ApplyAfterSettingsChange();
    }

    private void ApplyAfterSettingsChange()
    {
        ApplyTheme(_settings.Theme, save: false);
        ApplyWindowSettings();
        BuildSidebar();
        if (InArchive) { ShowArchiveLevel(); ShowComment(Archive.Comment); } else LoadFolder(_folder);
    }

    private void ExportSettings_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.SaveFileDialog { Title = "Exportar configuración", FileName = "medmdr-configuracion.json", Filter = "Configuración (*.json)|*.json" };
        if (d.ShowDialog(this) != true) return;
        try { SettingsStore.Export(_settings, d.FileName); Msg.Info(this, "Configuración exportada."); }
        catch (Exception ex) { Msg.Error(this, Msg.Describe(ex)); }
    }

    private void ImportSettings_Click(object sender, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Title = "Importar configuración", Filter = "Configuración (*.json)|*.json" };
        if (d.ShowDialog(this) != true) return;
        try
        {
            CopySettings(SettingsStore.Import(d.FileName), _settings);
            SaveSettings();
            ApplyAfterSettingsChange();
            Msg.Info(this, "Configuración importada.");
        }
        catch (Exception ex) { Msg.Error(this, "No se pudo importar: " + Msg.Describe(ex)); }
    }

    private void ResetSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!Msg.Confirm(this, "¿Restablecer toda la configuración (incluidos favoritos y perfiles)?")) return;
        CopySettings(new AppSettings(), _settings);
        SaveSettings();
        ApplyAfterSettingsChange();
        Msg.Info(this, "Configuración restablecida.");
    }

    /// <summary>Copia todas las propiedades públicas de un AppSettings a otro (el de la ventana es el que se guarda al cerrar).</summary>
    private static void CopySettings(AppSettings from, AppSettings to)
    {
        foreach (var p in typeof(AppSettings).GetProperties())
            if (p.CanRead && p.CanWrite && p.GetIndexParameters().Length == 0) p.SetValue(to, p.GetValue(from));
    }
}
