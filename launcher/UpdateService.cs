using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PlutoniumLauncher;

public sealed record UpdateAsset(string Url, string Sha256);
public sealed record ClientUpdateManifest(string Version, UpdateAsset Standalone, UpdateAsset Fabric);
public sealed record LauncherUpdateManifest(string Version, UpdateAsset Executable);
public sealed record UpdateCheckResult(bool Configured, bool Available, string? Version, string Status);

public sealed class UpdateService(HttpClient? httpClient = null)
{
    private readonly HttpClient _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<(UpdateCheckResult Result, ClientUpdateManifest? Manifest)> CheckClientAsync(
        string manifestUrl, string currentVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return (new(false, false, null, "Client update source not configured"), null);
        var manifest = await ReadManifestAsync<ClientUpdateManifest>(manifestUrl, cancellationToken);
        ValidateAsset(manifest.Standalone);
        ValidateAsset(manifest.Fabric);
        var available = IsNewer(manifest.Version, currentVersion);
        return (new(true, available, manifest.Version, available ? $"Client update {manifest.Version} available" : "Client is up to date"), manifest);
    }

    public async Task<(UpdateCheckResult Result, LauncherUpdateManifest? Manifest)> CheckLauncherAsync(
        string manifestUrl, string currentVersion, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(manifestUrl))
            return (new(false, false, null, "Launcher update source not configured"), null);
        var manifest = await ReadManifestAsync<LauncherUpdateManifest>(manifestUrl, cancellationToken);
        ValidateAsset(manifest.Executable);
        var available = IsNewer(manifest.Version, currentVersion);
        return (new(true, available, manifest.Version, available ? $"Launcher update {manifest.Version} available" : "Launcher is up to date"), manifest);
    }

    public async Task DownloadVerifiedAsync(
        UpdateAsset asset,
        string targetPath,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ValidateAsset(asset);
        var uri = new Uri(asset.Url, UriKind.Absolute);
        var directory = Path.GetDirectoryName(targetPath)!;
        Directory.CreateDirectory(directory);
        var temporary = Path.Combine(directory, Path.GetFileName(targetPath) + ".download-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var expectedBytes = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[128 * 1024];
            long received = 0;
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                while (true)
                {
                    var count = await input.ReadAsync(buffer, cancellationToken);
                    if (count == 0) break;
                    await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
                    hash.AppendData(buffer, 0, count);
                    received += count;
                    if (expectedBytes is > 0) progress?.Report(Math.Clamp(received / (double)expectedBytes.Value, 0, 1));
                }
                await output.FlushAsync(cancellationToken);
            }
            if (expectedBytes is > 0 && received != expectedBytes.Value)
                throw new IOException("The downloaded update ended before completion.");
            var actual = Convert.ToHexString(hash.GetHashAndReset());
            if (!CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(asset.Sha256)))
                throw new InvalidDataException("The downloaded update failed SHA-256 verification.");
            AtomicFile.Replace(temporary, targetPath);
            progress?.Report(1);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
    
    public static async Task<bool> FileMatchesSha256Async(string path, string expectedSha256, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path) || expectedSha256.Length != 64 || !expectedSha256.All(Uri.IsHexDigit)) return false;
        await using var stream = File.OpenRead(path);
        var actual = await SHA256.HashDataAsync(stream, cancellationToken);
        return CryptographicOperations.FixedTimeEquals(actual, Convert.FromHexString(expectedSha256));
    }

    private async Task<T> ReadManifestAsync<T>(string manifestUrl, CancellationToken cancellationToken)
    {
        var uri = RequireHttps(manifestUrl);
        using var response = await _httpClient.GetAsync(uri, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidDataException("The update manifest was empty or invalid.");
    }

    private static Uri RequireHttps(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("Update sources must use HTTPS.");
        return uri;
    }

    public static void ValidateManifestUrl(string value)
    {
        if (!string.IsNullOrWhiteSpace(value)) _ = RequireHttps(value);
    }

    private static void ValidateAsset(UpdateAsset asset)
    {
        if (asset is null || string.IsNullOrWhiteSpace(asset.Url) || string.IsNullOrWhiteSpace(asset.Sha256))
            throw new InvalidDataException("The update manifest omitted an asset URL or SHA-256 digest.");
        _ = RequireHttps(asset.Url);
        if (asset.Sha256.Length != 64 || !asset.Sha256.All(Uri.IsHexDigit))
            throw new InvalidDataException("The update manifest must provide a SHA-256 digest for every asset.");
    }

    private static bool IsNewer(string remote, string current)
    {
        if (!Version.TryParse(remote, out var remoteVersion) || !Version.TryParse(current, out var currentVersion))
            throw new InvalidDataException("The update manifest contains an invalid version.");
        return remoteVersion > currentVersion;
    }
}
