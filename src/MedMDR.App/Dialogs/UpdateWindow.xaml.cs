using System.Windows;
using MedMDR.Core;

namespace MedMDR.Wpf;

public enum UpdateChoice { Later, Install, Skip }

/// <summary>Aviso de versión nueva: notas, «Actualizar», «Más tarde» y «Omitir esta versión».</summary>
public partial class UpdateWindow : Window
{
    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    public UpdateWindow(UpdateInfo info, Version current)
    {
        InitializeComponent();
        DevTools.Hook(this);
        Title = $"Actualización de {AppInfo.Name}";
        HeadText.Text = $"Hay una versión nueva de {AppInfo.Name}: {info.Version}";
        SubText.Text = $"Tienes la {current}. Al actualizar, {AppInfo.Name} se cierra, se instala la nueva y se vuelve a abrir.";
        NotesBox.Text = string.IsNullOrWhiteSpace(info.Notes) ? "(Esta versión no incluye notas.)" : info.Notes.Replace("\r\n", "\n").Replace("\n", "\r\n");
    }

    private void Update_Click(object sender, RoutedEventArgs e) { Choice = UpdateChoice.Install; DialogResult = true; }
    private void Skip_Click(object sender, RoutedEventArgs e) { Choice = UpdateChoice.Skip; DialogResult = false; }

    public static UpdateChoice Ask(Window? owner, UpdateInfo info, Version current)
    {
        var w = new UpdateWindow(info, current);
        if (owner != null && owner.IsVisible) w.Owner = owner; else w.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        w.ShowDialog();
        return w.Choice;
    }
}
