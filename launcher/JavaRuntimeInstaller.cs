using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace PlutoniumLauncher;

public sealed class JavaRuntimeInstaller
{
    private const string AdoptiumManifest = "https://api.adoptium.net/v3/assets/latest/21/hotspot?architecture=x64&image_type=jre&os=windows&vendor=eclipse";
    private readonly HttpClient _httpClient;
    private readonly string _dataDirectory;
    private readonly DownloadManager? _downloads;

    public JavaRuntimeInstaller(HttpClient? httpClient = null, string? dataDirectory = null, DownloadManager? downloads = null)
    {
        _downloads = downloads;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        _dataDirectory = dataDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plutonium Client");
    }

    public async Task<JavaRuntime> InstallAsync(
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var dataDirectory = _dataDirectory;
        var runtimeDirectory = Path.Combine(dataDirectory, "runtime", "java-21");
        var stagingDirectory = runtimeDirectory + ".staging-" + Guid.NewGuid().ToString("N");
        var archivePath = Path.Combine(dataDirectory, "runtime-" + Guid.NewGuid().ToString("N") + ".zip");
        Directory.CreateDirectory(dataDirectory);
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeDirectory)!);

        string downloadUrl;
        string expectedHash;
        using (var response = await _httpClient.GetAsync(AdoptiumManifest, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            var package = document.RootElement[0].GetProperty("binary").GetProperty("package");
            downloadUrl = package.GetProperty("link").GetString()!;
            expectedHash = package.GetProperty("checksum").GetString()!;
        }
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidDataException("The Java runtime manifest returned an insecure package URL.");

        try
        {
            if (_downloads is null) await DownloadAsync(uri, archivePath, progress, cancellationToken);
            else await _downloads.DownloadAsync("Java 21 runtime", uri.AbsoluteUri, archivePath, expectedHash, HashAlgorithmName.SHA256, cancellationToken, progress);
            await using (var archive = File.OpenRead(archivePath))
            {
                var actualHash = Convert.ToHexString(await SHA256.HashDataAsync(archive, cancellationToken));
                if (!CryptographicOperations.FixedTimeEquals(
                        Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
                    throw new InvalidDataException("The Java runtime checksum did not match the official manifest.");
            }

            Directory.CreateDirectory(stagingDirectory);
            ZipFile.ExtractToDirectory(archivePath, stagingDirectory, overwriteFiles: false);
            var javaPath = Directory.EnumerateFiles(stagingDirectory, "javaw.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? Directory.EnumerateFiles(stagingDirectory, "java.exe", SearchOption.AllDirectories).FirstOrDefault()
                ?? throw new InvalidDataException("The verified runtime archive did not contain Java 21.");
            var javaDirectory = Path.GetDirectoryName(javaPath)!;
            var normalizedRoot = Path.Combine(stagingDirectory, "runtime-root");
            Directory.Move(Directory.GetParent(javaDirectory)!.FullName, normalizedRoot);
            var stagedJava = Path.Combine(normalizedRoot, "bin", "javaw.exe");
            if (!File.Exists(stagedJava)) stagedJava = Path.Combine(normalizedRoot, "bin", "java.exe");
            var version = await JavaRuntimeDetector.ProbePathAsync(stagedJava, cancellationToken);
            if (version is null) throw new InvalidDataException("The downloaded runtime is not a compatible Java 21 runtime.");

            var previousDirectory = runtimeDirectory + ".previous";
            if (Directory.Exists(previousDirectory)) Directory.Delete(previousDirectory, recursive: true);
            if (Directory.Exists(runtimeDirectory)) Directory.Move(runtimeDirectory, previousDirectory);
            try
            {
                Directory.Move(normalizedRoot, runtimeDirectory);
                if (Directory.Exists(previousDirectory)) Directory.Delete(previousDirectory, recursive: true);
            }
            catch
            {
                if (!Directory.Exists(runtimeDirectory) && Directory.Exists(previousDirectory))
                    Directory.Move(previousDirectory, runtimeDirectory);
                throw;
            }

            return new JavaRuntime(Path.Combine(runtimeDirectory, "bin", "javaw.exe"), version.Version, "managed runtime");
        }
        finally
        {
            if (File.Exists(archivePath)) File.Delete(archivePath);
            if (Directory.Exists(stagingDirectory)) Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private async Task DownloadAsync(Uri uri, string target, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength;
        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var destination = new FileStream(target, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough);
        var buffer = new byte[128 * 1024];
        long received = 0;
        while (true)
        {
            var count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            received += count;
            if (total is > 0) progress?.Report(Math.Clamp(received / (double)total.Value, 0, 1));
        }
        await destination.FlushAsync(cancellationToken);
        if (total is > 0 && received != total.Value) throw new IOException("The Java runtime download ended before completion.");
    }
}
