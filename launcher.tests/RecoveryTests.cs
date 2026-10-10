using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using PlutoniumLauncher;
using Xunit;

namespace PlutoniumLauncher.Tests;

public sealed class RecoveryTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "PlutoniumRecoveryTests", Guid.NewGuid().ToString("N"));

    public RecoveryTests() => Directory.CreateDirectory(root);

    [Fact]
    public void LockedTemporaryFileDoesNotHideTheOriginalFailure()
    {
        string path = Path.Combine(root, "download.tmp");
        File.WriteAllText(path, "partial download");
        using (var locked = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var original = new InvalidDataException("Checksum verification failed");
            var observed = Assert.Throws<InvalidDataException>((Action)(() =>
            {
                try
                {
                    throw original;
                }
                finally { TemporaryCleanup.DeleteFile(path); }
            }));
            Assert.Same(original, observed);
            Assert.True(File.Exists(path));
            TemporaryCleanup.DeleteDirectory(root);
            Assert.True(Directory.Exists(root));
        }
        TemporaryCleanup.DeleteFile(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task DownloadPreparationFailureDoesNotLeaveAnItemQueued()
    {
        var blockingFile = Path.Combine(root, "not-a-directory");
        await File.WriteAllTextAsync(blockingFile, "preserve me");
        using var http = new HttpClient(new NoNetwork());
        var downloads = new DownloadManager(http);
        await Assert.ThrowsAsync<IOException>(() => downloads.DownloadAsync("Test", "https://example.invalid/file",
            Path.Combine(blockingFile, "mod.jar"), new string('a', 64), HashAlgorithmName.SHA256));
        Assert.Equal("Failed", Assert.Single(downloads.Items).Status);
        Assert.Equal("preserve me", await File.ReadAllTextAsync(blockingFile));
    }

    private sealed class NoNetwork : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            throw new InvalidOperationException("No network request should be made during a filesystem preparation failure.");
    }

    public void Dispose()
    {
        if (Directory.Exists(root))
            Directory.Delete(root, true);
    }
}
