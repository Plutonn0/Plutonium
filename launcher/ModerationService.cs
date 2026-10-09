using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Security.Cryptography;
using CmlLib.Core.Auth;

namespace PlutoniumLauncher;

public sealed class ModerationApiException(HttpStatusCode status, string message) : Exception(message)
{
    public HttpStatusCode Status { get; } = status;
}
public sealed class ModerationService : IDisposable
{
    private readonly HttpClient _http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
    private string? _token;
    private DateTimeOffset _expires;
    private string _uuid = "";
    public static readonly string BaseUrl = LoadEndpoint();
    public static bool Configured => BaseUrl.Length > 0;
    public string Role { get; private set; } = "user";
    public bool OwnerCandidate { get; private set; }
    private static string DevicePath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Plutonium","owner-device.bin");
    private string? ReadDevice()
    {
        try {
            if(!File.Exists(DevicePath))return null;
            var bytes=ProtectedData.Unprotect(File.ReadAllBytes(DevicePath),null,DataProtectionScope.CurrentUser);
            using var doc=JsonDocument.Parse(bytes);var value=doc.RootElement;
            return value.GetProperty("uuid").GetString()==_uuid && value.GetProperty("endpoint").GetString()==BaseUrl ? value.GetProperty("token").GetString():null;
        } catch(IOException) {return null;} catch(CryptographicException) {return null;} catch(JsonException) {return null;} catch(KeyNotFoundException) {return null;} catch(InvalidOperationException) {return null;}
    }
    public async Task RememberDeviceAsync()
    {
        var result=await RequestAsync("owner/device/register",HttpMethod.Post,new {});
        var bytes=JsonSerializer.SerializeToUtf8Bytes(new {uuid=_uuid,endpoint=BaseUrl,token=result.GetProperty("deviceToken").GetString()});
        Directory.CreateDirectory(Path.GetDirectoryName(DevicePath)!);
        var temporary=DevicePath+".tmp";
        await File.WriteAllBytesAsync(temporary,ProtectedData.Protect(bytes,null,DataProtectionScope.CurrentUser));
        File.Move(temporary,DevicePath,true);
    }
    public async Task ForgetDeviceAsync()
    {
        var device=ReadDevice();
        if(device is not null && HasSession) await RequestAsync("owner/device/forget",HttpMethod.Post,new {deviceToken=device});
        if(File.Exists(DevicePath))File.Delete(DevicePath);
        await SignOutAsync();
    }
    public bool HasSession => _token is not null && _expires > DateTimeOffset.UtcNow;
    private static string LoadEndpoint()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("PlutoniumLauncher.ServiceEndpoint")!;
        var value = JsonDocument.Parse(stream).RootElement.GetProperty("baseUrl").GetString() ?? "";
        if (value.Length == 0) return ""; // Unprovisioned builds explicitly show that cloud moderation is unavailable.
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0) throw new InvalidDataException("Invalid bundled moderation endpoint.");
        return value.TrimEnd('/') + "/";
    }
    public async Task<JsonElement> RequestAsync(string path, HttpMethod method, object? body = null, CancellationToken cancellationToken = default)
    {
        if (!Configured) throw new InvalidOperationException("Cloud moderation is not activated in this build. Complete the server setup in docs/moderation-setup.md before publishing an enabled build.");
        using var request = new HttpRequestMessage(method, BaseUrl + path);
        if (_token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http.SendAsync(request, cancellationToken);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(cancellationToken), cancellationToken: cancellationToken);
        var result = document.RootElement.Clone();
        if (!response.IsSuccessStatusCode)
            throw new ModerationApiException(response.StatusCode, result.TryGetProperty("error", out var error) ? error.GetString() ?? "Moderation request failed." : "Moderation request failed.");
        return result;
    }
    public async Task ConnectAsync(MSession session, CancellationToken cancellationToken)
    {
        if (HasSession && _uuid == session.UUID) return;
        Reset();
        var result = await RequestAsync("session", HttpMethod.Post, new { minecraftToken = session.AccessToken }, cancellationToken);
        _token = result.GetProperty("token").GetString(); _uuid = session.UUID ?? "";
        _expires = DateTimeOffset.UtcNow.AddSeconds(result.GetProperty("expiresIn").GetInt32() - 30);
        await RefreshIdentityAsync(cancellationToken);
        if(OwnerCandidate && ReadDevice() is string device)
        {
            try { ApplyVerifiedSession(await RequestAsync("owner/device/resume",HttpMethod.Post,new {deviceToken=device},cancellationToken)); await RefreshIdentityAsync(cancellationToken); }
            catch(ModerationApiException error) when(error.Status==HttpStatusCode.Forbidden) { if(File.Exists(DevicePath))File.Delete(DevicePath); }
        }
    }
    public async Task<JsonElement> RefreshIdentityAsync(CancellationToken cancellationToken = default)
    {
        var me = await RequestAsync("me", HttpMethod.Get, cancellationToken: cancellationToken);
        Role = me.GetProperty("role").GetString() ?? "user";
        OwnerCandidate = me.GetProperty("ownerCandidate").GetBoolean(); return me;
    }
    public void ApplyVerifiedSession(JsonElement response)
    {
        if (_token is not null && response.TryGetProperty("sessionExpiresIn",out var lifetime))
            _expires=DateTimeOffset.UtcNow.AddSeconds(Math.Clamp(lifetime.GetInt32(),1,7200)-30);
    }
    public void Reset() { _token = null; _uuid = ""; _expires = default; Role = "user"; OwnerCandidate = false; }
    public async Task SignOutAsync()
    {
        try { if (HasSession) await RequestAsync("session", HttpMethod.Delete); } finally { Reset(); }
    }
    public void Dispose() { Reset(); _http.Dispose(); }
}
