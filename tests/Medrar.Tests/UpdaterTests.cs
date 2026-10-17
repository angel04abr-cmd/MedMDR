using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Medrar.Core;

namespace Medrar.Tests;

public class UpdaterTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.10.3", "1.10.3")]
    [InlineData("V2.0", "2.0.0")]
    [InlineData("v1.3.0-beta.1", "1.3.0")]
    public void ParseTag_accepts_versions(string tag, string expected) =>
        Assert.Equal(Version.Parse(expected), UpdateService.ParseTag(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("release-5")]
    [InlineData(null)]
    public void ParseTag_rejects_garbage(string? tag) => Assert.Null(UpdateService.ParseTag(tag));

    [Fact]
    public void Versions_compare_numerically_not_as_text()
    {
        Assert.True(UpdateService.ParseTag("v1.10.0")! > UpdateService.ParseTag("v1.9.0")!);
        Assert.True(UpdateService.ParseTag("v1.2.0")! > UpdateService.Normalize(new Version(1, 1, 1, 0)));
    }

    private static string Release(string tag = "v1.3.0", bool draft = false, bool pre = false, bool msi = true, bool sums = true, string baseUrl = "https://x.test") =>
        JsonSerializer.Serialize(new
        {
            tag_name = tag, body = "Novedades de prueba", html_url = baseUrl + "/release", draft, prerelease = pre,
            assets = new object[]
            {
                new { name = "Medrar-1.3.0-win-x64.zip", url = baseUrl + "/a/zip", size = 5 },
                msi ? new { name = "Medrar-1.3.0-x64.msi", url = baseUrl + "/a/msi", size = 100 } : new { name = "otro.txt", url = baseUrl + "/a/otro", size = 1 },
                sums ? new { name = "SHA256SUMS.txt", url = baseUrl + "/a/sums", size = 10 } : new { name = "leeme.txt", url = baseUrl + "/a/leeme", size = 1 },
            }
        });

    [Fact]
    public void ParseRelease_returns_info_for_newer_version()
    {
        var info = UpdateService.ParseRelease(Release(), new Version(1, 2, 0))!;
        Assert.Equal(new Version(1, 3, 0), info.Version);
        Assert.Equal("Medrar-1.3.0-x64.msi", info.MsiName);
        Assert.Equal("https://x.test/a/msi", info.MsiUrl);
        Assert.Equal("https://x.test/a/sums", info.SumsUrl);
        Assert.Equal("Novedades de prueba", info.Notes);
    }

    [Fact]
    public void ParseRelease_is_null_when_up_to_date_or_ahead()
    {
        Assert.Null(UpdateService.ParseRelease(Release(), new Version(1, 3, 0)));
        Assert.Null(UpdateService.ParseRelease(Release(), new Version(1, 3, 0, 0)));
        Assert.Null(UpdateService.ParseRelease(Release(), new Version(2, 0, 0)));
    }

    [Fact]
    public void ParseRelease_ignores_drafts_and_prereleases()
    {
        Assert.Null(UpdateService.ParseRelease(Release(draft: true), new Version(1, 0, 0)));
        Assert.Null(UpdateService.ParseRelease(Release(pre: true), new Version(1, 0, 0)));
    }

    [Fact]
    public void ParseRelease_fails_clearly_on_bad_input()
    {
        Assert.Equal(UpdateFailure.Format, Assert.Throws<UpdateException>(() => UpdateService.ParseRelease("no es json", new Version(1, 0, 0))).Kind);
        Assert.Equal(UpdateFailure.Format, Assert.Throws<UpdateException>(() => UpdateService.ParseRelease(Release(tag: "banana"), new Version(1, 0, 0))).Kind);
        Assert.Equal(UpdateFailure.Format, Assert.Throws<UpdateException>(() => UpdateService.ParseRelease(Release(msi: false), new Version(1, 0, 0))).Kind);
    }

    [Fact]
    public void FindSum_reads_the_expected_line()
    {
        string h1 = new string('a', 64), h2 = new string('B', 64);
        string text = $"{h1} *Medrar-1.3.0-win-x64.zip\r\n{h2} *Medrar-1.3.0-x64.msi\r\n";
        Assert.Equal(h2.ToLowerInvariant(), UpdateService.FindSum(text, "Medrar-1.3.0-x64.msi"));
        Assert.Null(UpdateService.FindSum(text, "Medrar-9.9.9-x64.msi"));
        Assert.Null(UpdateService.FindSum("basura sin formato", "Medrar-1.3.0-x64.msi"));
    }

    [Fact]
    public void Script_waits_for_the_app_installs_and_relaunches()
    {
        string s = UpdateInstaller.BuildScript(@"C:\t\Medrar-1.3.0-x64.msi", @"C:\a\Medrar.exe", 4321);
        Assert.Contains("PID eq 4321", s);
        Assert.Contains(@"msiexec /i ""C:\t\Medrar-1.3.0-x64.msi"" /passive /norestart", s);
        Assert.True(s.IndexOf("msiexec", StringComparison.Ordinal) < s.IndexOf(@"start """" ""C:\a\Medrar.exe""", StringComparison.Ordinal));
    }

    [Fact]
    public void Token_is_protected_and_roundtrips()
    {
        Assert.Equal("", TokenProtector.Protect(""));
        string stored = TokenProtector.Protect("github_pat_ABC123");
        Assert.DoesNotContain("ABC123", stored);
        Assert.Equal("github_pat_ABC123", TokenProtector.Unprotect(stored));
        Assert.Equal("", TokenProtector.Unprotect("no es base64 válido!!"));
        Assert.Equal("", TokenProtector.Unprotect(null));
    }

    // ------------------------------------------------------------------ servidor local de GitHub falso

    private sealed class FakeGitHub : IDisposable
    {
        private readonly HttpListener _l = new();
        private readonly CancellationTokenSource _cts = new();
        public string Base { get; }
        public byte[] Msi { get; set; } = RandomNumberGenerator.GetBytes(300_000);
        public string? SumOverride { get; set; }
        public bool OmitSums { get; set; }
        public string Tag { get; set; } = "v1.3.0";
        public string? Token { get; set; }              // si no es null, exige «Authorization: Bearer <Token>»
        public List<(string Path, string? Auth)> Seen { get; } = new();

        public FakeGitHub()
        {
            int port = FreePort();
            Base = $"http://127.0.0.1:{port}";
            _l.Prefixes.Add(Base + "/");
            _l.Start();
            _ = Task.Run(Loop);
        }

        private static int FreePort()
        {
            var t = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
            t.Start(); int p = ((IPEndPoint)t.LocalEndpoint).Port; t.Stop(); return p;
        }

        private async Task Loop()
        {
            while (!_cts.IsCancellationRequested)
            {
                HttpListenerContext c;
                try { c = await _l.GetContextAsync(); } catch { return; }
                string path = c.Request.Url!.AbsolutePath, auth = c.Request.Headers["Authorization"] ?? "";
                lock (Seen) Seen.Add((path, auth.Length == 0 ? null : auth));
                try
                {
                    if (Token != null && !path.StartsWith("/cdn/") && auth != "Bearer " + Token) { c.Response.StatusCode = 404; c.Response.Close(); continue; }
                    byte[] body; string type = "application/octet-stream";
                    switch (path)
                    {
                        case "/repos/o/r/releases/latest":
                            type = "application/json";
                            body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new
                            {
                                tag_name = Tag, body = "Notas", html_url = Base + "/page", draft = false, prerelease = false,
                                assets = new object[]
                                {
                                    new { name = $"Medrar-{Tag.TrimStart('v')}-x64.msi", url = Base + "/asset/msi", size = Msi.Length },
                                    OmitSums ? new { name = "x.txt", url = Base + "/asset/x", size = 1 } : new { name = "SHA256SUMS.txt", url = Base + "/asset/sums", size = 80 },
                                }
                            }));
                            break;
                        case "/asset/msi":      // redirige como hace GitHub hacia su almacenamiento
                            c.Response.StatusCode = 302; c.Response.Headers["Location"] = Base + "/cdn/msi"; c.Response.Close(); continue;
                        case "/cdn/msi": body = Msi; break;
                        case "/asset/sums":
                            body = Encoding.UTF8.GetBytes($"{SumOverride ?? Convert.ToHexString(SHA256.HashData(Msi)).ToLowerInvariant()} *Medrar-{Tag.TrimStart('v')}-x64.msi\n");
                            break;
                        default: c.Response.StatusCode = 404; c.Response.Close(); continue;
                    }
                    c.Response.ContentType = type; c.Response.ContentLength64 = body.Length;
                    await c.Response.OutputStream.WriteAsync(body);
                    c.Response.Close();
                }
                catch { try { c.Response.Abort(); } catch { } }
            }
        }

        public UpdateService Client(string? token = null) => new(token, Base, "o/r");
        public void Dispose() { _cts.Cancel(); _l.Close(); }
    }

    [Fact]
    public async Task End_to_end_check_download_and_verify()
    {
        using var gh = new FakeGitHub { Token = "secreto" };
        using var tmp = new TempDir();
        using var svc = gh.Client("secreto");

        var info = await svc.CheckAsync(new Version(1, 2, 0));
        Assert.NotNull(info);
        Assert.Equal(new Version(1, 3, 0), info!.Version);

        var progress = new ProgressReporter();
        string path = await svc.DownloadAsync(info, tmp.Path, progress);
        Assert.Equal(gh.Msi, File.ReadAllBytes(path));
        Assert.Equal(gh.Msi.Length, progress.Done);
        Assert.Empty(Directory.GetFiles(tmp.Path, "*.part"));

        // El token va al servidor de la API, pero NO a la dirección a la que redirige (el almacenamiento de GitHub).
        lock (gh.Seen)
        {
            Assert.All(gh.Seen.Where(s => s.Path != "/cdn/msi"), s => Assert.Equal("Bearer secreto", s.Auth));
            Assert.Null(gh.Seen.Single(s => s.Path == "/cdn/msi").Auth);
        }
    }

    [Fact]
    public async Task Up_to_date_returns_null()
    {
        using var gh = new FakeGitHub();
        using var svc = gh.Client();
        Assert.Null(await svc.CheckAsync(new Version(1, 3, 0)));
    }

    [Fact]
    public async Task Missing_or_wrong_token_is_reported_as_no_access()
    {
        using var gh = new FakeGitHub { Token = "bueno" };
        using (var svc = gh.Client())
            Assert.Equal(UpdateFailure.NoAccess, (await Assert.ThrowsAsync<UpdateException>(() => svc.CheckAsync(new Version(1, 0, 0)))).Kind);
        using (var svc = gh.Client("malo"))
            Assert.Equal(UpdateFailure.NoAccess, (await Assert.ThrowsAsync<UpdateException>(() => svc.CheckAsync(new Version(1, 0, 0)))).Kind);
    }

    [Fact]
    public async Task Tampered_download_is_rejected_and_deleted()
    {
        using var gh = new FakeGitHub { SumOverride = new string('0', 64) };
        using var tmp = new TempDir();
        using var svc = gh.Client();
        var info = (await svc.CheckAsync(new Version(1, 0, 0)))!;
        var ex = await Assert.ThrowsAsync<UpdateException>(() => svc.DownloadAsync(info, tmp.Path));
        Assert.Equal(UpdateFailure.Integrity, ex.Kind);
        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task Release_without_checksums_is_never_installed()
    {
        using var gh = new FakeGitHub { OmitSums = true };
        using var tmp = new TempDir();
        using var svc = gh.Client();
        var info = (await svc.CheckAsync(new Version(1, 0, 0)))!;
        Assert.Null(info.SumsUrl);
        Assert.Equal(UpdateFailure.Integrity, (await Assert.ThrowsAsync<UpdateException>(() => svc.DownloadAsync(info, tmp.Path))).Kind);
        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task Cancelled_download_leaves_no_files()
    {
        using var gh = new FakeGitHub { Msi = RandomNumberGenerator.GetBytes(4_000_000) };
        using var tmp = new TempDir();
        using var svc = gh.Client();
        var info = (await svc.CheckAsync(new Version(1, 0, 0)))!;
        using var cts = new CancellationTokenSource();
        var reporter = new ProgressReporter(new Progress<ProgressInfo>(_ => cts.Cancel()), cts.Token);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => svc.DownloadAsync(info, tmp.Path, reporter, cts.Token));
        Assert.Empty(Directory.GetFiles(tmp.Path));
    }

    [Fact]
    public async Task Unreachable_server_is_a_network_error()
    {
        using var svc = new UpdateService(null, "http://127.0.0.1:1", "o/r");
        Assert.Equal(UpdateFailure.Network, (await Assert.ThrowsAsync<UpdateException>(() => svc.CheckAsync(new Version(1, 0, 0)))).Kind);
    }

    [Fact]
    public async Task Plain_http_to_a_remote_host_is_refused()
    {
        using var svc = new UpdateService(null, "http://ejemplo.invalid", "o/r");
        Assert.Equal(UpdateFailure.Network, (await Assert.ThrowsAsync<UpdateException>(() => svc.CheckAsync(new Version(1, 0, 0)))).Kind);
    }

    [Fact]
    public void Api_override_only_accepts_local_addresses()
    {
        Assert.Equal("http://127.0.0.1:5000", UpdateService.LocalOnlyApi("http://127.0.0.1:5000"));
        Assert.Equal("http://localhost:8080", UpdateService.LocalOnlyApi("http://localhost:8080"));
        Assert.Null(UpdateService.LocalOnlyApi("https://servidor-malicioso.example"));
        Assert.Null(UpdateService.LocalOnlyApi("http://127.0.0.1.malicioso.example"));
        Assert.Null(UpdateService.LocalOnlyApi("no es una url"));
        Assert.Null(UpdateService.LocalOnlyApi(null));
    }
}
