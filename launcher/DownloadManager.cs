using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace PlutoniumLauncher;

public sealed class DownloadItem(string name) : INotifyPropertyChanged
{
    public string Name { get; } = name;
    public string Status { get; private set; } = "Queued";
    public double Percent { get; private set; }
    public string Detail { get; private set; } = "Waiting";
    public bool Paused { get; private set; }
    public bool CanPause { get; init; } = true;
    public bool Active => Status is "Downloading" or "Paused" or "Queued";
    internal CancellationTokenSource Cancellation { get; } = new();
    public event PropertyChangedEventHandler? PropertyChanged;
    public void TogglePause() { if (!Active || !CanPause) return; Paused = !Paused; Update(Paused ? "Paused" : "Downloading", Detail, Percent); }
    public void Cancel() { if (Active) Cancellation.Cancel(); }
    internal void Update(string status, string detail, double percent)
    {
        Status = status; Detail = detail; Percent = percent;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

public sealed class DownloadManager(HttpClient? client = null)
{
    private readonly HttpClient _http = client ?? new() { Timeout = Timeout.InfiniteTimeSpan };
    public ObservableCollection<DownloadItem> Items { get; } = [];

    public async Task DownloadAsync(string name, string url, string destination, string digest,
        HashAlgorithmName algorithm, CancellationToken cancellation = default, IProgress<double>? progress = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https")
            throw new InvalidDataException("Downloads must use HTTPS.");
        var hashLength = algorithm == HashAlgorithmName.SHA512 ? 128 : algorithm == HashAlgorithmName.SHA256 ? 64 : 0;
        if (hashLength == 0 || digest.Length != hashLength || !digest.All(Uri.IsHexDigit))
            throw new InvalidDataException("Missing or invalid download checksum.");
        var item = new DownloadItem(name); Items.Insert(0, item);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, item.Cancellation.Token);
        linked.CancelAfter(TimeSpan.FromHours(2));
        var token = linked.Token;
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temp = destination + ".download-" + Guid.NewGuid().ToString("N");
        try
        {
            // A transient transfer is retried from byte zero so servers without range support are safe.
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    item.Update("Downloading", attempt == 0 ? "Connecting…" : "Retrying transfer…", 0);
                    using var response = await GetHeadersAsync(uri, token);
                    response.EnsureSuccessStatusCode();
                    var total = response.Content.Headers.ContentLength;
                    await using var input = await response.Content.ReadAsStreamAsync(token);
                    using var hash = IncrementalHash.CreateHash(algorithm);
                    long received = 0;
                    var clock = Stopwatch.StartNew();
                    var buffer = new byte[128 * 1024];
                    await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None, buffer.Length, true))
                    {
                        while (true)
                        {
                            while (item.Paused) await Task.Delay(150, token);
                            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                            readTimeout.CancelAfter(TimeSpan.FromSeconds(45));
                            var count = await input.ReadAsync(buffer, readTimeout.Token);
                            if (count == 0) break;
                            await output.WriteAsync(buffer.AsMemory(0, count), token);
                            hash.AppendData(buffer, 0, count); received += count;
                            var fraction = total > 0 ? (double)received / total.Value : 0;
                            var speed = received / Math.Max(1, clock.Elapsed.TotalSeconds);
                            var eta = total > 0 ? $" · {Math.Max(0, (total.Value - received) / Math.Max(1, speed)):0}s left" : "";
                            item.Update("Downloading", $"{received / 1048576d:0.0} / {(total is > 0 ? (total.Value / 1048576d).ToString("0.0") : "?")} MB · {speed / 1048576d:0.0} MB/s{eta}", fraction * 100);
                            progress?.Report(fraction);
                        }
                        await output.FlushAsync(token);
                    }
                    if (total is > 0 && received != total) throw new IOException("The download ended early.");
                    if (!CryptographicOperations.FixedTimeEquals(hash.GetHashAndReset(), Convert.FromHexString(digest)))
                        throw new InvalidDataException("Checksum verification failed. The downloaded file was rejected.");
                    token.ThrowIfCancellationRequested();
                    AtomicFile.Replace(temp, destination);
                    item.Update("Complete", "Downloaded and checksum verified", 100); progress?.Report(1);
                    break;
                }
                catch (Exception ex) when (attempt < 2 && !token.IsCancellationRequested &&
                    ex is HttpRequestException or OperationCanceledException or IOException)
                { item.Update("Downloading", "Connection interrupted · retrying", 0); await Task.Delay(1000 * (attempt + 1), token); }
            }
        }
        catch (OperationCanceledException) { item.Update("Cancelled", "Transfer cancelled; existing files preserved", item.Percent); throw; }
        catch (Exception ex) { item.Update("Failed", ErrorReport.Redact(ex.Message) + " Retry from the original install or update button.", item.Percent); throw; }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    private async Task<HttpResponseMessage> GetHeadersAsync(Uri uri, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(45));
        return await _http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
    }
}
