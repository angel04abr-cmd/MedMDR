using System.Net;
using System.Net.Http.Headers;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Medrar.Core;

/// <summary>Una versión nueva encontrada en GitHub Releases.</summary>
public sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl,
                                string MsiName, string MsiUrl, long MsiSize, string? SumsUrl);

public enum UpdateFailure { NoAccess, Network, Format, Integrity }

public sealed class UpdateException : MedrarException
{
    public UpdateFailure Kind { get; }
    public UpdateException(UpdateFailure kind, string message, Exception? inner = null) : base(message, inner!) => Kind = kind;
}

/// <summary>
/// Busca y descarga las versiones nuevas de Medrar en GitHub Releases.
/// La descarga se comprueba con la suma SHA-256 publicada en <c>SHA256SUMS.txt</c> antes de entregarla.
/// Es lo único que usa la red en toda la aplicación, y solo si el usuario no lo ha desactivado.
/// </summary>
public sealed class UpdateService : IDisposable
{
    public const string DefaultRepo = "angel04abr-cmd/Medrar";
    public const string DefaultApi = "https://api.github.com";
    public const string SumsName = "SHA256SUMS.txt";
    private const int MaxRedirects = 5;

    private static readonly Regex MsiName = new(@"^Medrar-(\d+\.\d+\.\d+)-x64\.msi$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly HttpClient _http;
    private readonly string _api, _repo;
    private readonly string? _token;

    public UpdateService(string? token = null, string? api = null, string? repo = null, HttpMessageHandler? handler = null)
    {
        _api = (string.IsNullOrWhiteSpace(api) ? DefaultApi : api).TrimEnd('/');
        _repo = string.IsNullOrWhiteSpace(repo) ? DefaultRepo : repo;
        _token = string.IsNullOrWhiteSpace(token) ? null : token.Trim();
        // Las redirecciones se siguen a mano para no enviar el token a otros servidores.
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(10) };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Medrar-Updater", "1"));
    }

    /// <summary>
    /// Crea el servicio con la configuración habitual. Para las pruebas se puede redirigir a un servidor local con
    /// MEDRAR_UPDATE_API (solo se aceptan direcciones de este mismo equipo), MEDRAR_UPDATE_REPO y MEDRAR_UPDATE_TOKEN.
    /// </summary>
    public static UpdateService FromEnvironment(string? token)
    {
        string? envToken = Environment.GetEnvironmentVariable("MEDRAR_UPDATE_TOKEN");
        return new UpdateService(string.IsNullOrWhiteSpace(envToken) ? token : envToken,
            LocalOnlyApi(Environment.GetEnvironmentVariable("MEDRAR_UPDATE_API")), Environment.GetEnvironmentVariable("MEDRAR_UPDATE_REPO"));
    }

    /// <summary>Una dirección de API alternativa solo vale si es de este mismo equipo; si no, se descarta (usa la de GitHub).</summary>
    public static string? LocalOnlyApi(string? api) =>
        !string.IsNullOrWhiteSpace(api) && Uri.TryCreate(api, UriKind.Absolute, out var u) && u.IsLoopback ? api : null;

    // ---------------------------------------------------------------- comparar versiones

    /// <summary>Convierte «v1.2.3» o «1.2.3» en una versión comparable; null si no tiene ese aspecto.</summary>
    public static Version? ParseTag(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag)) return null;
        string t = tag.Trim().TrimStart('v', 'V');
        int cut = t.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0) t = t[..cut];
        if (!Version.TryParse(t, out var v)) return null;
        return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
    }

    public static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    // ---------------------------------------------------------------- buscar

    /// <summary>Devuelve la versión más reciente si es posterior a <paramref name="current"/>; null si ya se está al día.</summary>
    public async Task<UpdateInfo?> CheckAsync(Version current, CancellationToken ct = default)
    {
        string json;
        using (var resp = await SendAsync($"{_api}/repos/{_repo}/releases/latest", "application/vnd.github+json", ct).ConfigureAwait(false))
            json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseRelease(json, current);
    }

    public static UpdateInfo? ParseRelease(string json, Version current)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new UpdateException(UpdateFailure.Format, "La respuesta de GitHub no tiene el formato esperado.");
            if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
            if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;

            string tag = Str(root, "tag_name");
            var version = ParseTag(tag) ?? throw new UpdateException(UpdateFailure.Format, $"La etiqueta «{tag}» no es un número de versión.");
            if (version <= Normalize(current)) return null;

            string? msiName = null, msiUrl = null, sumsUrl = null; long msiSize = 0;
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
                foreach (var a in assets.EnumerateArray())
                {
                    string name = Str(a, "name");
                    string url = a.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String ? u.GetString()! : Str(a, "browser_download_url");
                    if (MsiName.IsMatch(name)) { msiName = name; msiUrl = url; msiSize = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var n) ? n : 0; }
                    else if (name.Equals(SumsName, StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
                }
            if (msiName == null || msiUrl == null)
                throw new UpdateException(UpdateFailure.Format, $"La versión {version} no trae el instalador .msi.");
            return new UpdateInfo(version, tag, Str(root, "body"), Str(root, "html_url"), msiName, msiUrl, msiSize, sumsUrl);
        }
        catch (JsonException ex) { throw new UpdateException(UpdateFailure.Format, "La respuesta de GitHub no se pudo leer.", ex); }
    }

    private static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    // ---------------------------------------------------------------- descargar

    /// <summary>Descarga el instalador a <paramref name="dir"/>, comprueba su SHA-256 y devuelve la ruta del .msi.</summary>
    public async Task<string> DownloadAsync(UpdateInfo info, string dir, ProgressReporter? progress = null, CancellationToken ct = default)
    {
        progress ??= ProgressReporter.None;
        if (info.SumsUrl == null)
            throw new UpdateException(UpdateFailure.Integrity, $"La versión {info.Version} no publica {SumsName}, así que no se puede comprobar la descarga. No se instalará.");
        if (info.MsiName != Path.GetFileName(info.MsiName) || !MsiName.IsMatch(info.MsiName))
            throw new UpdateException(UpdateFailure.Format, "El nombre del instalador no es válido.");

        string sums;
        using (var r = await SendAsync(info.SumsUrl, "application/octet-stream", ct).ConfigureAwait(false))
            sums = await r.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        string expected = FindSum(sums, info.MsiName)
            ?? throw new UpdateException(UpdateFailure.Integrity, $"{SumsName} no incluye {info.MsiName}. No se instalará.");

        Directory.CreateDirectory(dir);
        string final = Path.Combine(dir, info.MsiName), part = final + ".part";
        try
        {
            using (var resp = await SendAsync(info.MsiUrl, "application/octet-stream", ct).ConfigureAwait(false))
            {
                progress.Total = resp.Content.Headers.ContentLength ?? info.MsiSize;
                progress.Begin(info.MsiName);
                using var input = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buf = new byte[81920];
                int n;
                while ((n = await input.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    hash.AppendData(buf, 0, n);
                    output.Write(buf, 0, n);
                    progress.Add(n);
                }
                output.Flush();
                string actual = Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
                if (!actual.Equals(expected, StringComparison.OrdinalIgnoreCase))
                    throw new UpdateException(UpdateFailure.Integrity, "La descarga está dañada o ha sido alterada: su suma SHA-256 no coincide con la publicada. No se instalará.");
            }
            File.Move(part, final, overwrite: true);
            return final;
        }
        catch
        {
            try { File.Delete(part); } catch { }
            throw;
        }
    }

    /// <summary>Busca en el texto de SHA256SUMS.txt (formato «hash *nombre») la suma de un fichero.</summary>
    public static string? FindSum(string sums, string fileName)
    {
        foreach (var raw in sums.Split('\n'))
        {
            string line = raw.Trim();
            var m = Regex.Match(line, @"^([0-9a-fA-F]{64})\s+\*?(.+)$");
            if (m.Success && m.Groups[2].Value.Trim().Equals(fileName, StringComparison.OrdinalIgnoreCase)) return m.Groups[1].Value.ToLowerInvariant();
        }
        return null;
    }

    // ---------------------------------------------------------------- red

    private async Task<HttpResponseMessage> SendAsync(string url, string accept, CancellationToken ct)
    {
        var uri = new Uri(url);
        for (int hop = 0; hop <= MaxRedirects; hop++)
        {
            if (uri.Scheme != Uri.UriSchemeHttps && !uri.IsLoopback)
                throw new UpdateException(UpdateFailure.Network, "Solo se admiten conexiones seguras (https).");
            using var req = new HttpRequestMessage(HttpMethod.Get, uri);
            req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            // El token solo viaja al servidor de GitHub (o al de pruebas local): nunca a los servidores a los que redirige.
            if (_token != null && hop == 0) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
            HttpResponseMessage resp;
            try { resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false); }
            catch (HttpRequestException ex) { throw new UpdateException(UpdateFailure.Network, "No se pudo conectar con GitHub. Comprueba tu conexión a Internet.", ex); }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) { throw new UpdateException(UpdateFailure.Network, "GitHub tardó demasiado en responder.", ex); }

            if (resp.StatusCode is HttpStatusCode.Moved or HttpStatusCode.Found or HttpStatusCode.SeeOther or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect
                && resp.Headers.Location != null)
            {
                uri = resp.Headers.Location.IsAbsoluteUri ? resp.Headers.Location : new Uri(uri, resp.Headers.Location);
                resp.Dispose();
                continue;
            }
            if (resp.IsSuccessStatusCode) return resp;
            var code = resp.StatusCode;
            resp.Dispose();
            throw code switch
            {
                HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.NotFound =>
                    new UpdateException(UpdateFailure.NoAccess, _token == null
                        ? "GitHub no deja ver las versiones (puede que haya limitado las consultas desde tu red). Inténtalo más tarde o indica un token de acceso de solo lectura en Configuración > Actualizaciones."
                        : "GitHub rechazó el acceso. Revisa que el token de Configuración > Actualizaciones sea válido, no haya caducado y tenga permiso de lectura sobre el repositorio."),
                _ => new UpdateException(UpdateFailure.Network, $"GitHub respondió con un error ({(int)code}). Inténtalo más tarde.")
            };
        }
        throw new UpdateException(UpdateFailure.Network, "Demasiadas redirecciones.");
    }

    public void Dispose() => _http.Dispose();
}

/// <summary>Guarda el token de GitHub cifrado con DPAPI: solo se puede leer con esta cuenta de Windows en este equipo.</summary>
[SupportedOSPlatform("windows")]
public static class TokenProtector
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("Medrar/update-token/v1");

    public static string Protect(string token)
    {
        if (string.IsNullOrEmpty(token)) return "";
        return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
    }

    public static string Unprotect(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return "";
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser)); }
        catch (Exception ex) when (ex is CryptographicException or FormatException) { return ""; }   // otro usuario/equipo: se ignora
    }
}

/// <summary>Cómo está instalado Medrar y cómo se aplica una actualización.</summary>
[SupportedOSPlatform("windows")]
public static class UpdateInstaller
{
    /// <summary>True si esta copia salió del instalador .msi para todo el equipo (deja una marca en HKLM con la ruta de instalación).</summary>
    public static bool IsMsiInstall(string? appDir = null) => MarkerMatches(Microsoft.Win32.RegistryHive.LocalMachine, appDir);

    /// <summary>True si esta copia es de las versiones 1.2.x, instaladas solo para el usuario (marca en HKCU): no se actualizan solas.</summary>
    public static bool IsLegacyPerUserInstall(string? appDir = null) => MarkerMatches(Microsoft.Win32.RegistryHive.CurrentUser, appDir);

    private static bool MarkerMatches(Microsoft.Win32.RegistryHive hive, string? appDir)
    {
        try
        {
            using var baseKey = Microsoft.Win32.RegistryKey.OpenBaseKey(hive, Microsoft.Win32.RegistryView.Registry64);
            using var k = baseKey.OpenSubKey(@"Software\Medrar");
            if (k?.GetValue("Installer") as string != "msi") return false;
            string? dir = k.GetValue("InstallDir") as string;
            appDir ??= AppContext.BaseDirectory;
            return dir != null && Path.GetFullPath(dir).TrimEnd('\\').Equals(Path.GetFullPath(appDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Escribe y lanza (oculto) un script que espera a que Medrar se cierre, instala el MSI y vuelve a abrir Medrar.
    /// El que llama debe cerrar la aplicación justo después.
    /// </summary>
    public static void LaunchApply(string msiPath, string exePath, int waitForPid)
    {
        string script = BuildScript(msiPath, exePath, waitForPid);
        string file = Path.Combine(Path.GetDirectoryName(msiPath)!, "aplicar-actualizacion.cmd");
        File.WriteAllText(file, script, new UTF8Encoding(false));
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c \"\"{file}\"\"")
        {
            UseShellExecute = false, CreateNoWindow = true, WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden,
            WorkingDirectory = Path.GetDirectoryName(msiPath)!
        });
    }

    public static string BuildScript(string msiPath, string exePath, int waitForPid) =>
        "@echo off\r\n" +
        "chcp 65001 >nul\r\n" +
        $"for /l %%i in (1,1,60) do (\r\n" +
        $"  tasklist /FI \"PID eq {waitForPid}\" 2>nul | find \"{waitForPid}\" >nul || goto instalar\r\n" +
        "  ping -n 2 127.0.0.1 >nul\r\n" +
        ")\r\n" +
        ":instalar\r\n" +
        $"msiexec /i \"{msiPath}\" /passive /norestart\r\n" +
        $"start \"\" \"{exePath}\"\r\n";
}
