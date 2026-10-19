using System.Diagnostics;
using System.Windows;

namespace Medrar.Wpf;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        DevTools.Hook(this);
        NameText.Text = AppInfo.Name;
        VersionText.Text = "Versión " + AppInfo.Version;
        AuthorRun.Text = AppInfo.Author;
        RepoLink.Inlines.Add(AppInfo.RepoUrl);
    }

    private void RepoLink_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(AppInfo.RepoUrl) { UseShellExecute = true }); } catch { /* sin navegador: no es crítico */ }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
