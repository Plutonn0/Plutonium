using System.IO;
using System.Net.Http;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using PlutoniumLauncher;
using Xunit;

namespace PlutoniumLauncher.Tests;

public sealed class LauncherSafetyTests : IDisposable
{
    private readonly string _temporaryRoot = Path.Combine(Path.GetTempPath(), "PlutoniumLauncherTests", Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task FreshInstallAddsOnlyPlutoniumFilesAndKeepsMinecraftProfilesUntouched()
    {
        var minecraft = Path.Combine(_temporaryRoot, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var profilePath = Path.Combine(minecraft, "launcher_profiles.json");
        const string profileContents = "{\"profiles\":{\"other\":{\"name\":\"Existing\"}}}";
        await File.WriteAllTextAsync(profilePath, profileContents);
        var config = CreateConfig(minecraft, minecraft);

        var result = await new BootstrapService().InstallOrRepairAsync(config);

        Assert.True(result.HasStandaloneFiles);
        Assert.True(result.HasFabricFiles);
        Assert.Equal(profileContents, await File.ReadAllTextAsync(profilePath));
        Assert.True(File.Exists(Path.Combine(minecraft, "versions", "plutonium-1.21.11", "plutonium-1.21.11.jar")));
        Assert.True(File.Exists(Path.Combine(minecraft, "mods", "plutonium-client-fabric.jar")));
        Assert.False(Directory.Exists(Path.Combine(minecraft, "assets")));
    }

    [Fact]
    public async Task LegacyQuirkInstallPreservesWorldsSettingsProfilesAndOtherMods()
    {
        var minecraft = Path.Combine(_temporaryRoot, ".minecraft");
        var legacyGame = Path.Combine(minecraft, "quirk-client");
        var mods = Path.Combine(minecraft, "mods");
        Directory.CreateDirectory(Path.Combine(legacyGame, "quirk"));
        Directory.CreateDirectory(Path.Combine(legacyGame, "saves", "OldWorld"));
        Directory.CreateDirectory(mods);
        const string settings = "{\"freecam\":true}";
        const string world = "world data";
        const string unrelatedMod = "another mod";
        await File.WriteAllTextAsync(Path.Combine(legacyGame, "quirk", "settings.json"), settings);
        await File.WriteAllTextAsync(Path.Combine(legacyGame, "saves", "OldWorld", "level.dat"), world);
        await File.WriteAllTextAsync(Path.Combine(mods, "other-mod.jar"), unrelatedMod);
        var profilePath = Path.Combine(minecraft, "launcher_profiles.json");
        var profileJson = System.Text.Json.JsonSerializer.Serialize(new
        {
            profiles = new
            {
                quirk = new { name = "Quirk Client", lastVersionId = "plutonium-1.21.11", gameDir = legacyGame },
                other = new { name = "Unrelated profile", lastVersionId = "1.21.11" }
            }
        });
        await File.WriteAllTextAsync(profilePath, profileJson);
        var config = CreateConfig(minecraft, minecraft);

        var result = await new BootstrapService().InstallOrRepairAsync(config);

        Assert.True(result.HasLegacyQuirkSettings);
        Assert.Equal(Path.GetFullPath(legacyGame), result.StandaloneGameDirectory);
        Assert.Equal(settings, await File.ReadAllTextAsync(Path.Combine(legacyGame, "quirk", "settings.json")));
        Assert.Equal(world, await File.ReadAllTextAsync(Path.Combine(legacyGame, "saves", "OldWorld", "level.dat")));
        Assert.Equal(unrelatedMod, await File.ReadAllTextAsync(Path.Combine(mods, "other-mod.jar")));
        Assert.Equal(profileJson, await File.ReadAllTextAsync(profilePath));
        Assert.True(File.Exists(Path.Combine(legacyGame, "versions", "plutonium-1.21.11", "plutonium-1.21.11.jar")));
    }

    [Fact]
    public async Task RepairRestoresCorruptedEmbeddedClientArtifact()
    {
        var minecraft = Path.Combine(_temporaryRoot, ".minecraft");
        Directory.CreateDirectory(minecraft);
        var config = CreateConfig(minecraft, minecraft);
        var installer = new BootstrapService();
        await installer.InstallOrRepairAsync(config);
        var target = Path.Combine(minecraft, "versions", "plutonium-1.21.11", "plutonium-1.21.11.jar");
        await File.WriteAllTextAsync(target, "corrupted payload");

        await installer.InstallOrRepairAsync(config);

        await using var installedFile = File.OpenRead(target);
        var installedHash = await SHA256.HashDataAsync(installedFile);
        await using var embedded = typeof(BootstrapService).Assembly
            .GetManifestResourceStream("PlutoniumLauncher.Payload.standalone.jar")!;
        var bundledHash = await SHA256.HashDataAsync(embedded);
        Assert.Equal(Convert.ToHexString(bundledHash), Convert.ToHexString(installedHash));
    }

    [Fact]
    public async Task MissingJavaExecutableIsRejectedWithoutLaunchingAnything()
    {
        var missingJava = Path.Combine(_temporaryRoot, "missing", "javaw.exe");
        Assert.Null(await JavaRuntimeDetector.ProbePathAsync(missingJava));
    }

    [Fact]
    public async Task FailedDownloadChecksumLeavesExistingClientAndNoPartialFile()
    {
        var target = Path.Combine(_temporaryRoot, "client.jar");
        Directory.CreateDirectory(_temporaryRoot);
        const string existing = "known good client";
        const string download = "untrusted replacement";
        await File.WriteAllTextAsync(target, existing);
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(download)
        });
        var updates = new UpdateService(new HttpClient(handler));
        var wrongHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("different content")));

        await Assert.ThrowsAsync<InvalidDataException>(() => updates.DownloadVerifiedAsync(
            new UpdateAsset("https://updates.example/client.jar", wrongHash), target));

        Assert.Equal(existing, await File.ReadAllTextAsync(target));
        Assert.Single(Directory.GetFiles(_temporaryRoot));
    }

    [Fact]
    public async Task VerifiedDownloadAtomicallyReplacesExistingClient()
    {
        var target = Path.Combine(_temporaryRoot, "client.jar");
        Directory.CreateDirectory(_temporaryRoot);
        const string replacement = "verified replacement";
        await File.WriteAllTextAsync(target, "old client");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(replacement)));
        var updates = new UpdateService(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(replacement)
        })));

        await updates.DownloadVerifiedAsync(new UpdateAsset("https://updates.example/client.jar", hash), target);

        Assert.Equal(replacement, await File.ReadAllTextAsync(target));
        Assert.Single(Directory.GetFiles(_temporaryRoot));
    }

    [Fact]
    public async Task ConfiguredReleaseManifestChecksVersionAndRejectsHttp()
    {
        const string hash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
        var manifest = $$"""
        {
          "version": "1.2.0",
          "standalone": { "url": "https://updates.example/standalone.jar", "sha256": "{{hash}}" },
          "fabric": { "url": "https://updates.example/fabric.jar", "sha256": "{{hash}}" }
        }
        """;
        var updates = new UpdateService(new HttpClient(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(manifest)
        })));

        var (result, _) = await updates.CheckClientAsync("https://updates.example/client.json", "1.0.0");

        Assert.True(result.Configured);
        Assert.True(result.Available);
        await Assert.ThrowsAsync<InvalidDataException>(() => updates.CheckClientAsync("http://updates.example/client.json", "1.0.0"));
    }

    private LauncherConfig CreateConfig(string minecraft, string standalone) => new()
    {
        DataRootOverride = Path.Combine(_temporaryRoot, "launcher-data"),
        MinecraftDirectory = minecraft,
        StandaloneGameDirectory = standalone
    };

    public void Dispose()
    {
        if (Directory.Exists(_temporaryRoot)) Directory.Delete(_temporaryRoot, recursive: true);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request));
    }
}
