using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using PlutoniumLauncher;
using Xunit;

namespace PlutoniumLauncher.Tests;

public sealed class CompatibilityAndCrashTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlutoniumCompatibilityTests-" + Guid.NewGuid().ToString("N"));
    private readonly Dictionary<string, (ModVersion Version, byte[] Jar)> _fixtures = [];
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }
    private ModVersion Add(string project, string id, string number, int age, Dictionary<string, string[]>? depends = null, Dictionary<string, string[]>? breaks = null, params ModDependency[] dependencies)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        { using var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open()); writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id = project, version = number, depends = depends ?? [], breaks = breaks ?? [] })); }
        var bytes = memory.ToArray();
        var version = new ModVersion(id, project, project, number, "release", DateTimeOffset.UtcNow.AddDays(-age), ["1.21.11"], ["fabric"],
            [new("https://cdn.modrinth.com/" + id, id + ".jar", true, bytes.Length, new() { ["sha512"] = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant() }, null)], dependencies.ToList());
        _fixtures[id] = (version, bytes); return version;
    }
    private (ModrinthService Service, DownloadManager Downloads) Services()
    {
        var api = new ModrinthService(new(new Handler(request =>
        {
            object response;
            var path = request.RequestUri!.AbsolutePath.Split('/');
            if (request.Method == HttpMethod.Post) response = _fixtures.Values.ToDictionary(f => f.Version.Files[0].Hashes["sha512"], f => f.Version);
            else if (path[^2] == "version") response = _fixtures[path[^1]].Version;
            else response = _fixtures.Values.Select(f => f.Version).Where(v => v.ProjectId == path[^2]).ToArray();
            return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(response, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower })) };
        })));
        return (api, new(new(new Handler(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(_fixtures[r.RequestUri!.AbsolutePath.Trim('/')].Jar) }))));
    }
    private async Task InstallFixture(string version)
    { Directory.CreateDirectory(Path.Combine(_root, "mods")); await File.WriteAllBytesAsync(Path.Combine(_root, "mods", version + ".jar"), _fixtures[version].Jar); }

    [Fact]
    public async Task PreviewOnlyModExplainsHowToEnableItsReleaseChannel()
    {
        var beta = Add("intro", "intro-beta", "2.1", 1) with { VersionType = "beta" };
        _fixtures[beta.Id] = (beta, _fixtures[beta.Id].Jar);
        var (api, downloads) = Services(); var planner = new ModCompatibilityPlanner(api, downloads);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => planner.ResolveAsync(_root, "intro", "Reimagined Intro", false, default));
        Assert.Contains("Reimagined Intro only has beta/alpha versions", error.Message);
        Assert.Contains("Include beta / alpha", error.Message); Assert.Empty(downloads.Items);
        var plan = await planner.ResolveAsync(_root, "intro", "Reimagined Intro", true, default);
        Assert.Equal(beta.Id, Assert.Single(plan.Entries).Version.Id);
    }
    [Fact]
    public async Task MissingDependencyCandidateHasAnActionableReason()
    {
        Add("intro", "intro", "1", 1, dependencies: [new("unavailable", null, "required")]);
        var (api, downloads) = Services();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new ModCompatibilityPlanner(api, downloads).ResolveAsync(_root, "intro", "Intro", false, default));
        Assert.Contains("unavailable has no available stable version", error.Message);
    }
    [Fact]
    public async Task ConflictingExactDependenciesExplainBothVersions()
    {
        Add("api", "api1", "1", 2); Add("api", "api2", "2", 1);
        Add("addon", "addon", "1", 1, dependencies: [new("api", "api2", "required")]);
        Add("intro", "intro", "1", 1, dependencies: [new("api", "api1", "required"), new("addon", null, "required")]);
        var (api, downloads) = Services();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new ModCompatibilityPlanner(api, downloads).ResolveAsync(_root, "intro", "Intro", false, default));
        Assert.Contains("api2", error.Message); Assert.Contains("api1", error.Message);
    }
    [Fact]
    public async Task UninstallLocalModKeepsRecoverableFile()
    {
        Add("local", "local", "1", 1); await InstallFixture("local");
        var library = new ModLibrary(_root, new()); var path = Path.Combine(library.DirectoryPath, "local.jar");
        await library.UninstallFileAsync(path);
        Assert.False(File.Exists(path));
        Assert.Equal(_fixtures["local"].Jar, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(Path.Combine(library.DirectoryPath, ".removed")))));
    }
    [Fact]
    public async Task LocalDependencyAndBundledClientCannotBeUninstalled()
    {
        Add("api", "api", "1", 1); Add("addon", "addon", "1", 1, new() { ["api"] = [">=1"] }); Add("plutonium", "renamed-client", "1", 1);
        await InstallFixture("api"); await InstallFixture("addon"); await InstallFixture("renamed-client");
        var library = new ModLibrary(_root, new());
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => library.UninstallFileAsync(Path.Combine(library.DirectoryPath, "api.jar")));
        Assert.Contains("required by addon", error.Message);
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.UninstallFileAsync(Path.Combine(library.DirectoryPath, "renamed-client.jar")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => library.UninstallFileAsync(Path.Combine(_root, "outside.jar")));
        Assert.Equal(3, Directory.GetFiles(library.DirectoryPath).Length);
    }

    [LibraryTests.LiveModrinthFact]
    public async Task LiveReimaginedIntroReportsPreviewChannelAndResolvesWithOptIn()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var planner = new ModCompatibilityPlanner(new(), new());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => planner.ResolveAsync(_root, "reimagined-intro", "Reimagined Intro", false, timeout.Token));
        Assert.Contains("Include beta / alpha", error.Message);
        var plan = await planner.ResolveAsync(_root, "reimagined-intro", "Reimagined Intro", true, timeout.Token);
        Assert.Contains(plan.Entries, e => e.Version.ProjectId == "BsnF5g7E");
        Assert.True(plan.Entries.Count > 1);
    }

    [Fact]
    public async Task SelectsOlderReleaseToRespectInstalledFabricRange()
    {
        Add("renderer", "renderer1", "1.0", 4); Add("renderer", "renderer2", "2.0", 1);
        Add("shader", "shader", "1.0", 2, new() { ["renderer"] = [">=1 <2"] }); await InstallFixture("shader");
        var (api, downloads) = Services();
        var plan = await new ModCompatibilityPlanner(api, downloads).ResolveAsync(_root, "renderer", "Renderer", false, default);
        Assert.Equal("renderer1", Assert.Single(plan.Entries).Version.Id); Assert.Contains(plan.Notes, n => n.Contains("alternative"));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "mods"))); // Planning never modifies the installation.
    }
    [Fact]
    public async Task BacktracksDependencyVersionForNewModsRange()
    {
        Add("api", "api1", "1.0", 4); Add("api", "api2", "2.0", 1);
        Add("addon", "addon", "1.0", 1, new() { ["api"] = ["1.x"] }, dependencies: [new("api", null, "required")]);
        var (api, downloads) = Services();
        var plan = await new ModCompatibilityPlanner(api, downloads).ResolveAsync(_root, "addon", "Addon", false, default);
        Assert.Contains(plan.Entries, e => e.Version.Id == "api1"); Assert.DoesNotContain(plan.Entries, e => e.Version.Id == "api2");
    }
    [Fact]
    public async Task RespectsExactApiRequirementFromInstalledMod()
    {
        Add("api", "api1", "1.0", 4); Add("api", "api2", "2.0", 1);
        Add("addon", "addon", "1.0", 1, dependencies: [new("api", "api1", "required")]); await InstallFixture("addon");
        var (api, downloads) = Services();
        var plan = await new ModCompatibilityPlanner(api, downloads).ResolveAsync(_root, "api", "API", false, default);
        Assert.Equal("api1", Assert.Single(plan.Entries).Version.Id);
    }
    [Fact]
    public async Task ImpossibleConflictLeavesFilesUntouched()
    {
        Add("renderer", "renderer", "2.0", 1); Add("shader", "shader", "1.0", 2, breaks: new() { ["renderer"] = ["*"] }); await InstallFixture("shader");
        var (api, downloads) = Services();
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => new ModCompatibilityPlanner(api, downloads).ResolveAsync(_root, "renderer", "Renderer", false, default));
        Assert.Contains("shader is incompatible", error.Message); Assert.Single(Directory.GetFiles(Path.Combine(_root, "mods")));
    }
    [Fact]
    public async Task CyclicRequiredDependenciesTerminate()
    {
        Add("one", "one", "1", 1, dependencies: [new("two", null, "required")]);
        Add("two", "two", "1", 1, dependencies: [new("one", null, "required")]);
        var (api, downloads) = Services();
        Assert.Equal(2, (await new ModCompatibilityPlanner(api, downloads).ResolveAsync(_root, "one", "One", false, default)).Entries.Count);
    }
    [Theory]
    [InlineData("0.8.14", ">=0.8.7 <0.9", true)]
    [InlineData("0.9", ">=0.8.7 <0.9", false)]
    [InlineData("0.9", "^0.8", true)]
    [InlineData("1.0-alpha", "^0.8", false)]
    [InlineData("1.2.4+mc1.21", "~1.2.0", true)]
    [InlineData("1.3-alpha", "~1.2.0", false)]
    [InlineData("1.2.3", "1.2.x", true)]
    [InlineData("1.2-alpha.2", ">=1.2-alpha.10", false)]
    [InlineData("1.2", "1.2.0", true)]
    [InlineData("custom-version", "custom-version", true)]
    public void MatchesFabricPredicates(string version, string predicate, bool expected) => Assert.Equal(expected, FabricVersionRange.Matches(version, [predicate]));

    [Theory]
    [InlineData("java.lang.OutOfMemoryError: Java heap space", "Not enough memory")]
    [InlineData("Incompatible mods found!", "Mod dependency conflict")]
    [InlineData("Mixin apply for mod foo failed", "Mod injection failed")]
    [InlineData("GLFW error 65542", "Graphics initialization failed")]
    [InlineData("UnsupportedClassVersionError", "Wrong Java version")]
    [InlineData("ZipException: zip END header not found", "Damaged archive")]
    [InlineData("NoClassDefFoundError: foo.Bar", "Missing or incompatible code")]
    [InlineData("exit code 1", "No clear cause found")]
    public void DiagnosisExplainsEvidence(string output, string expected)
    { var finding = Assert.Single(CrashDiagnosis.Analyze(output).Findings); Assert.Equal(expected, finding.Title); Assert.NotEmpty(finding.NextStep); }
    [Fact]
    public async Task IgnoresStaleCrashReportsAndRedactsSecrets()
    {
        Directory.CreateDirectory(Path.Combine(_root, "crash-reports")); var path = Path.Combine(_root, "crash-reports", "old.txt");
        await File.WriteAllTextAsync(path, "OutOfMemoryError"); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-1));
        var result = await CrashDiagnosis.ReadAsync(_root, "accessToken=secret123 exit code 1", DateTime.UtcNow);
        Assert.Equal("No clear cause found", Assert.Single(result.Findings).Title); Assert.DoesNotContain("secret123", result.Report);
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
