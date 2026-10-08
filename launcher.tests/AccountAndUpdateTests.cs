using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using PlutoniumLauncher;
using Xunit;

namespace PlutoniumLauncher.Tests;

public sealed class AccountAndUpdateTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlutoniumTests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void AccountVaultRoundTripsWithoutPlaintextCredentials()
    {
        var path = Path.Combine(_root, "accounts.bin");
        var storage = new DpapiJsonFileStorage(path);
        var data = new JsonObject { ["test-token"] = "synthetic-secret-not-a-real-token" };
        storage.Write(data, null);
        Assert.DoesNotContain("synthetic-secret", Encoding.UTF8.GetString(File.ReadAllBytes(path)));
        Assert.Equal(data.ToJsonString(), new DpapiJsonFileStorage(path).ReadAsJsonNode()!.ToJsonString());
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task SavedAccountAndSelectionSurviveNewLauncherInstances()
    {
        var config = Config();
        var storage = new DpapiJsonFileStorage(Path.Combine(config.DataDirectory, "accounts.bin"));
        var manager = new XboxAuthNet.Game.Accounts.JsonXboxGameAccountManager(storage,
            CmlLib.Core.Auth.Microsoft.Sessions.JEGameAccount.FromSessionStorage, null);
        var account = manager.NewAccount();
        CmlLib.Core.Auth.Microsoft.Sessions.JEProfileSource.Default.Set(account.SessionStorage,
            new CmlLib.Core.Auth.Microsoft.Sessions.JEProfile {
                Username = "RestartFixture", UUID = "00000000000000000000000000000042" });
        manager.SaveAccounts();
        var saved = Assert.Single(new AccountService(config.DataDirectory).GetAccounts());
        config.SelectedAccountId = saved.Id;
        await config.SaveAsync();

        var reopened = await LauncherConfig.LoadAsync(dataDirectory: config.DataDirectory);
        var restored = Assert.Single(new AccountService(reopened.DataDirectory).GetAccounts());
        Assert.Equal("RestartFixture", restored.Name);
        Assert.Equal(config.SelectedAccountId, reopened.SelectedAccountId);
        Assert.Equal(reopened.SelectedAccountId, restored.Id);
    }

    [Fact]
    public async Task AccountsUseTheConfiguredDataDirectoryAndRejectUnknownSelections()
    {
        var accounts = new AccountService(_root);
        Assert.Empty(accounts.GetAccounts());
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.AuthenticateAsync("missing-account", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => accounts.RemoveAsync("missing-account", CancellationToken.None));
    }

    [Fact]
    public async Task RepairRestoresMissingMetadataWithoutDowngradingNewerJar()
    {
        var config = Config();
        var bootstrap = new BootstrapService();
        await bootstrap.InstallOrRepairAsync(config);
        var directory = Path.Combine(config.MinecraftDirectory, "versions", "plutonium-1.21.11");
        var jar = Path.Combine(directory, "plutonium-1.21.11.jar");
        var metadata = Path.Combine(directory, "plutonium-1.21.11.json");
        using (var archive = System.IO.Compression.ZipFile.Open(jar, System.IO.Compression.ZipArchiveMode.Update))
        { using var writer = new StreamWriter(archive.CreateEntry("new-release.txt").Open()); writer.Write("newer client fixture"); }
        var newerHash = SHA256.HashData(await File.ReadAllBytesAsync(jar));
        config.StandaloneClientVersion = "99.0.0";
        File.Delete(metadata);
        await bootstrap.InstallOrRepairAsync(config);
        Assert.Equal(newerHash, SHA256.HashData(await File.ReadAllBytesAsync(jar)));
        Assert.True(File.Exists(metadata));
        Assert.Equal("99.0.0", config.StandaloneClientVersion);
    }

    [Fact]
    public async Task MissingNewerClientFallsBackToAccurateBundledVersion()
    {
        var config = Config();
        config.StandaloneClientVersion = config.FabricClientVersion = "99.0.0";
        await new BootstrapService().InstallOrRepairAsync(config);
        Assert.Equal("2.0.1", config.StandaloneClientVersion);
        Assert.Equal("2.0.1", config.FabricClientVersion);
    }

    [Fact]
    public async Task CorruptedNewerClientIsRepairedInsteadOfBeingMarkedReady()
    {
        var config = Config();
        var bootstrap = new BootstrapService();
        await bootstrap.InstallOrRepairAsync(config);
        var target = Path.Combine(config.StandaloneGameDirectory, "versions", "plutonium-1.21.11", "plutonium-1.21.11.jar");
        await File.WriteAllTextAsync(target, "truncated downloaded release");
        config.StandaloneClientVersion = "99.0.0";
        await bootstrap.InstallOrRepairAsync(config);
        using var jar = System.IO.Compression.ZipFile.OpenRead(target);
        Assert.NotNull(jar.GetEntry("com/quirk/client/Quirk.class"));
        Assert.Equal("2.0.1", config.StandaloneClientVersion);
    }

    [Fact]
    public async Task CancelledDownloadPreservesInstalledFile()
    {
        Directory.CreateDirectory(_root);
        var target = Path.Combine(_root, "client.jar");
        await File.WriteAllTextAsync(target, "installed client");
        using var cancellation = new CancellationTokenSource();
        var updates = new UpdateService(new HttpClient(new CancelHandler(cancellation)));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("replacement")));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => updates.DownloadVerifiedAsync(
            new UpdateAsset("https://updates.example/client.jar", hash), target, cancellationToken: cancellation.Token));
        Assert.Equal("installed client", await File.ReadAllTextAsync(target));
        Assert.Single(Directory.GetFiles(_root));
    }

    [Fact]
    public async Task LauncherReplacementHandlesUnicodeAndShellCharactersAndKeepsBackup()
    {
        var directory = Path.Combine(_root, "Łauncher's 100% & files");
        Directory.CreateDirectory(directory);
        var staged = Path.Combine(directory, "new.exe");
        var target = Path.Combine(directory, "Plutonium Client.exe");
        await File.WriteAllTextAsync(staged, "new launcher fixture");
        await File.WriteAllTextAsync(target, "previous launcher fixture");
        var script = LauncherUpdateHandoff.WriteScript(directory);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-Staged", staged, "-Target", target, "-LauncherPid", "0", "-NoRestart" }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(0, process.ExitCode);
        Assert.Equal("new launcher fixture", await File.ReadAllTextAsync(target));
        Assert.Equal("previous launcher fixture", await File.ReadAllTextAsync(target + ".previous"));
        Assert.Empty(Directory.GetFiles(directory, "*.next-*"));
    }

    [Theory]
    [InlineData("http://updates.example/manifest.json")]
    [InlineData("file:///C:/manifest.json")]
    [InlineData("not a URL")]
    public void SettingsRejectInvalidUpdateFeeds(string value) =>
        Assert.Throws<InvalidDataException>(() => UpdateService.ValidateManifestUrl(value));

    private LauncherConfig Config() => new()
    {
        DataRootOverride = Path.Combine(_root, "launcher"),
        MinecraftDirectory = Path.Combine(_root, "minecraft"),
        StandaloneGameDirectory = Path.Combine(_root, "minecraft")
    };

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }

    private sealed class CancelHandler(CancellationTokenSource source) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            source.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
