using System.Text.Json;
using Microsoft.Win32;
using MedMDR.Core;

namespace MedMDR.Tests;

/// <summary>Cambio de nombre de Medrar a MedMDR: lo que ya tenían instalado los usuarios debe seguir funcionando.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public class MigrationTests
{
    [Fact]
    public void Settings_are_copied_from_the_old_folder_once_and_the_original_is_kept()
    {
        using var tmp = new TempDir();
        string legacy = tmp.Combine("Medrar", "settings.json"), now = tmp.Combine("MedMDR", "settings.json");
        Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
        var old = new AppSettings { DefaultLevel = 5 };
        old.Favorites.Add(new Favorite { Name = "Fotos", Path = @"C:\Fotos" });
        SettingsStore.Save(old, legacy);

        Assert.True(SettingsStore.MigrateLegacy(now, legacy));
        var loaded = SettingsStore.Load(now);
        Assert.Equal(5, loaded.DefaultLevel);
        Assert.Equal("Fotos", Assert.Single(loaded.Favorites).Name);
        Assert.True(File.Exists(legacy));                       // la antigua no se toca (la versión vieja puede seguir instalada)

        // segunda vez: ya existe la nueva, no se pisa
        loaded.DefaultLevel = 2; SettingsStore.Save(loaded, now);
        Assert.False(SettingsStore.MigrateLegacy(now, legacy));
        Assert.Equal(2, SettingsStore.Load(now).DefaultLevel);
    }

    [Fact]
    public void Migration_does_nothing_when_there_is_no_old_settings()
    {
        using var tmp = new TempDir();
        Assert.False(SettingsStore.MigrateLegacy(tmp.Combine("MedMDR", "settings.json"), tmp.Combine("Medrar", "settings.json")));
        Assert.False(Directory.Exists(tmp.Combine("MedMDR")));
    }

    [Fact]
    public void Shell_integration_removes_the_keys_left_by_the_old_name()
    {
        string root = @"Software\MedMDRTest_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            // lo que dejaba la versión anterior
            using (var k = Registry.CurrentUser.CreateSubKey($@"{root}\.mdr")) k.SetValue("", "Medrar.Archive");
            using (var k = Registry.CurrentUser.CreateSubKey($@"{root}\Medrar.Archive\shell\open\command")) k.SetValue("", "\"C:\\Medrar.exe\" \"%1\"");
            using (var k = Registry.CurrentUser.CreateSubKey($@"{root}\Directory\shell\Medrar.Add\command")) k.SetValue("", "x");

            new ShellIntegration(root).Install(@"C:\Prueba\MedMDR.exe");

            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{root}\Medrar.Archive"));
            Assert.Null(Registry.CurrentUser.OpenSubKey($@"{root}\Directory\shell\Medrar.Add"));
            using var ext = Registry.CurrentUser.OpenSubKey($@"{root}\.mdr");
            Assert.Equal("MedMDR.Archive", ext!.GetValue("") as string);
        }
        finally { Registry.CurrentUser.DeleteSubKeyTree(root, false); }
    }

    [Fact]
    public void A_token_saved_under_the_old_name_is_still_readable()
    {
        // lo que guardaba la versión anterior (cuando el programa se llamaba Medrar)
        string old = Convert.ToBase64String(System.Security.Cryptography.ProtectedData.Protect(
            System.Text.Encoding.UTF8.GetBytes("github_pat_viejo"), System.Text.Encoding.UTF8.GetBytes("Medrar/update-token/v1"),
            System.Security.Cryptography.DataProtectionScope.CurrentUser));
        Assert.Equal("github_pat_viejo", TokenProtector.Unprotect(old));
        Assert.Equal("github_pat_nuevo", TokenProtector.Unprotect(TokenProtector.Protect("github_pat_nuevo")));
    }

    [Fact]
    public void Data_folder_can_be_redirected_for_tests_but_only_with_a_full_path()
    {
        string normal = SettingsStore.DataDirFor(null);
        Assert.EndsWith("MedMDR", normal);
        Assert.Equal(normal, SettingsStore.DataDirFor(""));
        Assert.Equal(normal, SettingsStore.DataDirFor("relativa"));          // una ruta relativa se ignora
        Assert.Equal(@"C:\pruebas\datos", SettingsStore.DataDirFor(@"C:\pruebas\datos "));
    }

    private static string ReleaseWith(params string[] assetNames) =>
        JsonSerializer.Serialize(new
        {
            tag_name = "v2.0.0", body = "n", html_url = "https://x.test/r", draft = false, prerelease = false,
            assets = assetNames.Select(n => new { name = n, url = "https://x.test/a/" + n, size = 10 }).ToArray()
        });

    [Fact]
    public void Updater_finds_the_installer_under_the_old_or_the_new_name_and_prefers_the_new()
    {
        var both = UpdateService.ParseRelease(ReleaseWith("Medrar-2.0.0-x64.msi", "MedMDR-2.0.0-x64.msi", "SHA256SUMS.txt"), new Version(1, 5, 0))!;
        Assert.Equal("MedMDR-2.0.0-x64.msi", both.MsiName);
        var bothReversed = UpdateService.ParseRelease(ReleaseWith("MedMDR-2.0.0-x64.msi", "Medrar-2.0.0-x64.msi", "SHA256SUMS.txt"), new Version(1, 5, 0))!;
        Assert.Equal("MedMDR-2.0.0-x64.msi", bothReversed.MsiName);
        var onlyOld = UpdateService.ParseRelease(ReleaseWith("Medrar-2.0.0-x64.msi", "SHA256SUMS.txt"), new Version(1, 5, 0))!;
        Assert.Equal("Medrar-2.0.0-x64.msi", onlyOld.MsiName);
    }
}
