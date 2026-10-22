using MedMDR.Core;

namespace MedMDR.Tests;

public class DataFolderTests
{
    [Fact]
    public void Data_folder_can_be_redirected_for_tests_but_only_with_a_full_path()
    {
        string normal = SettingsStore.DataDirFor(null);
        Assert.EndsWith("MedMDR", normal);
        Assert.Equal(normal, SettingsStore.DataDirFor(""));
        Assert.Equal(normal, SettingsStore.DataDirFor("relativa"));          // una ruta relativa se ignora
        Assert.Equal(@"C:\pruebas\datos", SettingsStore.DataDirFor(@"C:\pruebas\datos "));
    }

    [Fact]
    public void Updater_only_accepts_the_installer_named_after_the_program()
    {
        string json = System.Text.Json.JsonSerializer.Serialize(new
        {
            tag_name = "v9.0.0", body = "n", html_url = "https://x.test/r", draft = false, prerelease = false,
            assets = new[] { new { name = "otro-9.0.0-x64.msi", url = "https://x.test/a/1", size = 10 }, new { name = "MedMDR-9.0.0-x64.msi", url = "https://x.test/a/2", size = 10 } }
        });
        Assert.Equal("MedMDR-9.0.0-x64.msi", UpdateService.ParseRelease(json, new Version(2, 0, 0))!.MsiName);
    }
}
