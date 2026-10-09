using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PlutoniumLauncher;
using Xunit;

namespace PlutoniumLauncher.Tests;

public sealed class LibraryTests : IDisposable
{
    public sealed class LiveModrinthFactAttribute : FactAttribute
    {
        public LiveModrinthFactAttribute() { if (Environment.GetEnvironmentVariable("PLUTONIUM_LIVE_MODRINTH") != "1") Skip = "Opt-in network integration test"; }
    }
    private readonly string _root = Path.Combine(Path.GetTempPath(), "PlutoniumLibraryTests-" + Guid.NewGuid().ToString("N"));
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> reply) : HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => Task.FromResult(reply(request)); }
    private static HttpResponseMessage Response(object value) => new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(value, Json)) };
    private static byte[] Jar(string id, string version = "1.0", params string[] provides)
    {
        using var memory = new MemoryStream();
        using (var archive = new ZipArchive(memory, ZipArchiveMode.Create, true))
        { using var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open()); writer.Write(JsonSerializer.Serialize(new { schemaVersion = 1, id, version, provides })); }
        return memory.ToArray();
    }
    private static ModVersion Version(string project, string version, byte[] jar, params ModDependency[] dependencies) => new(version, project, project, version, "release", DateTimeOffset.Now,
        ["1.21.11"], ["fabric"], [new("https://cdn.modrinth.com/" + project, project + ".jar", true, jar.Length, new() { ["sha512"] = Convert.ToHexString(SHA512.HashData(jar)) }, null)], dependencies.ToList());

    [Fact]
    public async Task CorruptDownloadNeverReplacesInstalledFile()
    {
        Directory.CreateDirectory(_root); var target = Path.Combine(_root, "old.jar"); await File.WriteAllTextAsync(target, "existing");
        var manager = new DownloadManager(new(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1, 2, 3]) })));
        await Assert.ThrowsAsync<InvalidDataException>(() => manager.DownloadAsync("bad", "https://cdn.modrinth.com/mod", target, new string('0', 128), HashAlgorithmName.SHA512));
        Assert.Equal("existing", await File.ReadAllTextAsync(target)); Assert.Single(Directory.GetFiles(_root)); Assert.Equal("Failed", manager.Items[0].Status);
    }
    [Fact]
    public async Task DownloadRetriesTransientFailureAndVerifiesResult()
    {
        var count = 0; byte[] bytes = [4, 5, 6];
        var manager = new DownloadManager(new(new Handler(_ => ++count == 1 ? new(HttpStatusCode.ServiceUnavailable) : new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
        var target = Path.Combine(_root, "mod.jar");
        await manager.DownloadAsync("retry", "https://cdn.modrinth.com/mod", target, Convert.ToHexString(SHA512.HashData(bytes)), HashAlgorithmName.SHA512);
        Assert.Equal(2, count); Assert.Equal(bytes, await File.ReadAllBytesAsync(target)); Assert.Equal("Complete", manager.Items[0].Status);
    }
    [Fact]
    public async Task DependencyFailureLeavesEntireInstallationUntouched()
    {
        var first = Jar("first"); var second = Jar("second");
        var one = Version("first", "1", first); var two = Version("second", "2", second);
        var manager = new DownloadManager(new(new Handler(request => new(HttpStatusCode.OK) { Content = new ByteArrayContent(request.RequestUri!.AbsolutePath == "/first" ? first : [1, 2]) })));
        var library = new ModLibrary(_root, manager);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.InstallAsync([new("First", one, one.Files[0]), new("Second", two, two.Files[0])], default));
        Assert.Empty(Directory.EnumerateFiles(library.DirectoryPath, "*.jar")); Assert.Empty(await library.LoadAsync());
    }
    [Fact]
    public async Task LibraryPersistsAndPreventsRemovingRequiredDependency()
    {
        var api = Jar("api"); var addon = Jar("addon"); var a = Version("api", "api1", api); var b = Version("addon", "addon1", addon, new ModDependency("api", "api1", "required"));
        var manager = new DownloadManager(new(new Handler(r => new(HttpStatusCode.OK) { Content = new ByteArrayContent(r.RequestUri!.AbsolutePath == "/api" ? api : addon) })));
        var library = new ModLibrary(_root, manager);
        await library.InstallAsync([new("API", a, a.Files[0]), new("Addon", b, b.Files[0])], default);
        var reopened = new ModLibrary(_root, manager); Assert.Equal(2, (await reopened.LoadAsync()).Count);
        await Assert.ThrowsAsync<InvalidOperationException>(() => reopened.ChangeAsync("api", true));
        await reopened.ChangeAsync("addon", false); Assert.True(File.Exists(Path.Combine(library.DirectoryPath, "addon.jar.disabled")));
        await reopened.ChangeAsync("addon", true); await reopened.ChangeAsync("api", true); Assert.Empty(await reopened.LoadAsync());
    }
    [Fact]
    public async Task LocalModIdCollisionDoesNotOverwriteLocalFile()
    {
        var bytes = Jar("same_id"); var version = Version("remote", "1", bytes);
        var manager = new DownloadManager(new HttpClient(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
        var library = new ModLibrary(_root, manager);
        Directory.CreateDirectory(library.DirectoryPath); var local = Path.Combine(library.DirectoryPath, "local.jar"); await File.WriteAllBytesAsync(local, bytes);
        using (var archive = ZipFile.Open(local, ZipArchiveMode.Update)) { using var writer = new StreamWriter(archive.CreateEntry("local-changes.txt").Open()); writer.Write("different version with the same mod ID"); }
        var original = await File.ReadAllBytesAsync(local);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.InstallAsync([new("Remote", version, version.Files[0])], default));
        Assert.Equal(original, await File.ReadAllBytesAsync(local)); Assert.Single(Directory.GetFiles(library.DirectoryPath));
    }
    [Fact]
    public async Task UpgradesIdentifiedLocalVersionAndPreservesBackup()
    {
        var oldBytes = Jar("sodium", "0.8.7"); var newBytes = Jar("sodium", "0.8.14");
        var old = Version("sodium-project", "old", oldBytes); var next = Version("sodium-project", "new", newBytes);
        var hash = Convert.ToHexString(SHA512.HashData(oldBytes)).ToLowerInvariant();
        var api = new ModrinthService(new(new Handler(_ => Response(new Dictionary<string, ModVersion> { [hash] = old }))));
        var downloads = new DownloadManager(new(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(newBytes) })));
        var library = new ModLibrary(_root, downloads, api); Directory.CreateDirectory(library.DirectoryPath);
        var local = Path.Combine(library.DirectoryPath, "sodium-renamed (1).jar"); await File.WriteAllBytesAsync(local, oldBytes);
        await library.InstallAsync([new("Sodium", next, next.Files[0])], default);
        Assert.False(File.Exists(local));
        Assert.Equal(newBytes, await File.ReadAllBytesAsync(library.PathFor(Assert.Single(await library.LoadAsync()))));
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(Assert.Single(Directory.GetFiles(Path.Combine(library.DirectoryPath, ".replaced")))));
        Assert.Single(Directory.GetFiles(library.DirectoryPath, "*.jar"));
    }

    [Fact]
    public async Task LocalExactDependencyConstraintPreventsUnsafeUpgrade()
    {
        var oldBytes = Jar("sodium", "old"); var addonBytes = Jar("iris");
        var old = Version("sodium-project", "old", oldBytes);
        var addon = Version("iris-project", "iris", addonBytes, new ModDependency("sodium-project", "old", "required"));
        var next = Version("sodium-project", "new", Jar("sodium", "new"));
        var identities = new Dictionary<string, ModVersion> { [Convert.ToHexString(SHA512.HashData(oldBytes)).ToLowerInvariant()] = old, [Convert.ToHexString(SHA512.HashData(addonBytes)).ToLowerInvariant()] = addon };
        var api = new ModrinthService(new(new Handler(_ => Response(identities))));
        var library = new ModLibrary(_root, new(new(new Handler(_ => throw new Exception("Must not download an incompatible update")))), api);
        Directory.CreateDirectory(library.DirectoryPath);
        var local = Path.Combine(library.DirectoryPath, "sodium.jar"); await File.WriteAllBytesAsync(local, oldBytes);
        await File.WriteAllBytesAsync(Path.Combine(library.DirectoryPath, "iris.jar"), addonBytes);
        await Assert.ThrowsAsync<InvalidDataException>(() => library.InstallAsync([new("Sodium", next, next.Files[0])], default));
        Assert.Equal(oldBytes, await File.ReadAllBytesAsync(local)); Assert.Empty(await library.LoadAsync());
    }

    [Fact]
    public async Task RepeatedProvidesAliasDoesNotConflictWithItself()
    {
        var bytes = Jar("sodium", "1", "sodium", "alias", "alias"); var version = Version("sodium", "1", bytes);
        var library = new ModLibrary(_root, new(new(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }))));
        await library.InstallAsync([new("Sodium", version, version.Files[0])], default);
        Assert.Equal(new[] { "sodium", "alias" }, Assert.Single(await library.LoadAsync()).ModIds);
    }

    [Fact]
    public async Task ResolvesExactRequiredDependencyAndSkipsOptional()
    {
        var api = Version("api", "api-exact", Jar("api")); var main = Version("main", "main1", Jar("main"), new("api", "api-exact", "required"), new("optional", null, "optional"));
        var calls = new List<string>();
        var service = new ModrinthService(new(new Handler(r => { var path = r.RequestUri!.AbsolutePath; calls.Add(path); return path.EndsWith("/api-exact") ? Response(api) : Response(new[] { main }); })));
        var plan = await service.PlanAsync("main", "Main", false, default);
        Assert.Equal(2, plan.Count); Assert.Contains(plan, p => p.Version.Id == "api-exact"); Assert.DoesNotContain(calls, c => c.Contains("optional"));
    }
    [Fact]
    public async Task RecognizesRenamedLocalAndDisabledModsByHashAndCachesLookups()
    {
        var bytes = Jar("localmod"); var version = Version("local-project", "local-version", bytes);
        var hash = Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant(); var calls = 0;
        var api = new ModrinthService(new(new Handler(request => { Assert.Equal(HttpMethod.Post, request.Method); calls++; return Response(new Dictionary<string, ModVersion> { [hash] = version }); })));
        var directory = Path.Combine(_root, "mods"); Directory.CreateDirectory(directory);
        await File.WriteAllBytesAsync(Path.Combine(directory, "renamed.jar.disabled"), bytes);
        var detection = new ModDetection(api); var found = Assert.Single((await detection.ScanAsync(_root, default)).Mods);
        Assert.Equal("local-project", found.ProjectId); Assert.False(found.Enabled); Assert.False(found.Managed);
        await detection.ScanAsync(_root, default); Assert.Equal(1, calls);
        File.Delete(found.Path); Assert.Empty((await detection.ScanAsync(_root, default)).Mods);
    }
    [Fact]
    public async Task ReusesExactLocalDependencyWithoutRedownloadingOrRenaming()
    {
        var bytes = Jar("api"); var version = Version("api", "api1", bytes);
        var downloads = new DownloadManager(new(new Handler(_ => throw new InvalidOperationException("A matching installed dependency must not be downloaded."))));
        var library = new ModLibrary(_root, downloads); Directory.CreateDirectory(library.DirectoryPath);
        var path = Path.Combine(library.DirectoryPath, "my-renamed-api.jar"); await File.WriteAllBytesAsync(path, bytes);
        await library.InstallAsync([new("API", version, version.Files[0])], default);
        Assert.Equal("my-renamed-api.jar", Assert.Single(await library.LoadAsync()).Filename); Assert.Equal(bytes, await File.ReadAllBytesAsync(path)); Assert.Empty(downloads.Items);
    }
    [Fact]
    public async Task RejectsExactDependencyForWrongMinecraftVersion()
    {
        var dependency = Version("api", "api1", Jar("api")) with { GameVersions = ["1.20.1"] };
        var main = Version("main", "main1", Jar("main"), new ModDependency("api", "api1", "required"));
        var service = new ModrinthService(new(new Handler(r => r.RequestUri!.AbsolutePath.EndsWith("/api1") ? Response(dependency) : Response(new[] { main }))));
        await Assert.ThrowsAsync<InvalidDataException>(() => service.PlanAsync("main", "Main", false, default));
    }
    [Fact]
    public void SelectsLatestStableCompatibleVersion()
    {
        var stable = Version("main", "stable", Jar("main")); var beta = stable with { Id = "beta", VersionType = "beta", DatePublished = stable.DatePublished.AddDays(1) };
        var wrong = beta with { Id = "wrong", GameVersions = ["1.20.1"] };
        Assert.Equal("stable", ModrinthService.SelectVersion([wrong, beta, stable], false).Id);
        Assert.Equal("beta", ModrinthService.SelectVersion([wrong, beta, stable], true).Id);
    }
    [Theory]
    [InlineData("../escape.jar")]
    [InlineData("C:\\escape.jar")]
    [InlineData("mod.jar:stream")]
    public void RejectsUnsafeModFilename(string filename)
    {
        var version = Version("main", "1", Jar("main")); version.Files[0] = version.Files[0] with { Filename = filename };
        Assert.Throws<InvalidDataException>(() => ModrinthService.SelectFile(version));
    }
    [Fact]
    public async Task RollbackRejectsTamperedBackup()
    {
        Directory.CreateDirectory(_root); var target = Path.Combine(_root, "client.jar"); await File.WriteAllTextAsync(target, "original");
        var history = new UpdateHistory(_root); await history.CaptureAsync("Fabric", "1.0", target);
        var snapshot = Assert.Single(await history.LoadAsync()); var staged = await history.PrepareRestoreAsync(snapshot); Assert.Equal("original", await File.ReadAllTextAsync(staged));
        await File.WriteAllTextAsync(snapshot.Backup, "modified"); await Assert.ThrowsAsync<InvalidDataException>(() => history.PrepareRestoreAsync(snapshot));
    }
    [Fact]
    public async Task RepairRetainsValidRollbackEvenWhenOlderThanBundledClient()
    {
        var config = new LauncherConfig { DataRootOverride = _root, MinecraftDirectory = Path.Combine(_root, "game") };
        config.Normalize();
        await new BootstrapService().InstallOrRepairAsync(config);
        var path = InstallationDiscovery.FabricModPath(config.MinecraftDirectory);
        using (var archive = ZipFile.Open(path, ZipArchiveMode.Update)) { using var writer = new StreamWriter(archive.CreateEntry("rollback-marker.txt").Open()); writer.Write("older fixture"); }
        config.FabricClientVersion = "1.0.0"; config.FabricRollbackHash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        await new BootstrapService().InstallOrRepairAsync(config);
        Assert.Equal("1.0.0", config.FabricClientVersion); Assert.True(await UpdateService.FileMatchesSha256Async(path, config.FabricRollbackHash));
    }
    [Fact]
    public async Task ReinstallRepairsDamagedManagedModAndPreservesChangedCopy()
    {
        var bytes = Jar("repair"); var version = Version("repair", "1", bytes);
        var manager = new DownloadManager(new(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) })));
        var library = new ModLibrary(_root, manager); List<ModPlanEntry> plan = [new("Repair", version, version.Files[0])];
        await library.InstallAsync(plan, default); var path = library.PathFor(Assert.Single(await library.LoadAsync())); await File.WriteAllTextAsync(path, "changed file");
        await library.InstallAsync(plan, default); Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal("changed file", await File.ReadAllTextAsync(Assert.Single(Directory.GetFiles(Path.Combine(library.DirectoryPath, ".replaced")))));
    }
    [Fact]
    public async Task CancellationPreservesDestinationAndRemovesPartialTransfer()
    {
        Directory.CreateDirectory(_root); var destination = Path.Combine(_root, "mod.jar"); await File.WriteAllTextAsync(destination, "old");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        var manager = new DownloadManager(new(new Handler(_ => new(HttpStatusCode.OK) { Content = new ByteArrayContent([1]) })));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => manager.DownloadAsync("test", "https://cdn.modrinth.com/mod", destination, new string('0', 128), HashAlgorithmName.SHA512, cancellation.Token));
        Assert.Equal("old", await File.ReadAllTextAsync(destination)); Assert.Single(Directory.GetFiles(_root)); Assert.Equal("Cancelled", manager.Items[0].Status);
    }
    [LiveModrinthFact]
    public async Task LiveSodiumUpgradeFromRenamedOlderJar()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); var service = new ModrinthService();
        var versions = await service.VersionsAsync("sodium", timeout.Token);
        var old = versions.First(v => v.VersionNumber.Contains("0.8.7"));
        var next = versions.First(v => v.VersionNumber.Contains("0.8.14"));
        var oldFile = ModrinthService.SelectFile(old); var nextFile = ModrinthService.SelectFile(next);
        var downloads = new DownloadManager(); var library = new ModLibrary(_root, downloads, service);
        var path = Path.Combine(library.DirectoryPath, "sodium-old (1).jar");
        await downloads.DownloadAsync("Old Sodium fixture", oldFile.Url, path, oldFile.Hashes["sha512"], HashAlgorithmName.SHA512, timeout.Token);
        await library.InstallAsync([new("Sodium", next, nextFile)], timeout.Token);
        Assert.Equal(next.Id, Assert.Single(await library.LoadAsync()).VersionId);
        Assert.False(File.Exists(path)); Assert.Single(Directory.GetFiles(library.DirectoryPath, "*.jar"));
        Assert.Single(Directory.GetFiles(Path.Combine(library.DirectoryPath, ".replaced")));
    }

    [LiveModrinthFact]
    public async Task LiveModrinthSearchPlanDownloadAndReopen()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3)); var service = new ModrinthService();
        var result = await service.SearchAsync("mod menu", "relevance", "", 0, timeout.Token); Assert.NotEmpty(result.Hits);
        var plan = (await new ModCompatibilityPlanner(service, new()).ResolveAsync(_root, "modmenu", "Mod Menu", false, timeout.Token)).Entries;
        Assert.All(plan, entry => Assert.True(ModrinthService.Compatible(entry.Version)));
        var downloads = new DownloadManager(); var library = new ModLibrary(_root, downloads);
        await library.InstallAsync(plan, timeout.Token);
        var reopened = await new ModLibrary(_root, downloads).LoadAsync(); Assert.Equal(plan.Count, reopened.Count);
        Assert.All(reopened, mod => Assert.True(File.Exists(library.PathFor(mod))));
        Assert.All(downloads.Items, item => Assert.Equal("Complete", item.Status));
        var details = await service.ProjectAsync("modmenu", timeout.Token); Assert.False(string.IsNullOrWhiteSpace(details.Body));
        File.Delete(Path.Combine(library.DirectoryPath, ".plutonium-mods.json"));
        var discovered = await new ModDetection(service).ScanAsync(_root, timeout.Token);
        Assert.Equal(reopened.Count, discovered.Mods.Count); Assert.All(discovered.Mods, mod => Assert.False(mod.Managed));
    }
    [Fact]
    public async Task FavoriteSurvivesRestart()
    {
        var config = new LauncherConfig { DataRootOverride = _root, MinecraftDirectory = Path.Combine(_root, "game") };
        config.Servers.Add(ServerFavorite.Create("Example", "mc.example.com:25565")); await config.SaveAsync();
        var saved = await LauncherConfig.LoadAsync(dataDirectory: _root); Assert.Equal("mc.example.com:25565", Assert.Single(saved.Servers).Address);
    }
    [Fact]
    public void NativeDescriptionAndCustomControlsRenderWithoutOpeningAWindow()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new App(); app.InitializeComponent();
                var document = ModDescription.Render("# A mod\n**Readable** description\n<script>alert('unsafe')</script>");
                var text = new System.Windows.Documents.TextRange(document.ContentStart, document.ContentEnd).Text;
                Assert.Contains("Readable", text); Assert.DoesNotContain("alert", text);
                var window = new MainWindow();
                var slider = (System.Windows.Controls.Slider)window.FindName("MemoryInput");
                slider.ApplyTemplate(); slider.Value = 17;
                var track = (System.Windows.Controls.Primitives.Track)slider.Template.FindName("PART_Track", slider);
                Assert.Equal(17, track.Value); Assert.Equal(2, track.Minimum); Assert.Equal(32, track.Maximum);
                System.Windows.Controls.Slider.IncreaseSmall.Execute(null, slider); Assert.Equal(18, slider.Value);
                foreach (var orientation in new[] { System.Windows.Controls.Orientation.Vertical, System.Windows.Controls.Orientation.Horizontal })
                {
                    var scrollbar = new System.Windows.Controls.Primitives.ScrollBar { Orientation = orientation, Minimum = 0, Maximum = 100, ViewportSize = 20, Value = 30, Style = (System.Windows.Style)app.FindResource(typeof(System.Windows.Controls.Primitives.ScrollBar)) };
                    scrollbar.ApplyTemplate();
                    var scrollTrack = (System.Windows.Controls.Primitives.Track)scrollbar.Template.FindName("PART_Track", scrollbar);
                    Assert.Equal(orientation, scrollTrack.Orientation); Assert.Equal(30, scrollTrack.Value); Assert.Equal(20, scrollTrack.ViewportSize);
                    Assert.Equal(orientation == System.Windows.Controls.Orientation.Vertical, scrollTrack.IsDirectionReversed);
                }
                var dropdown = (System.Windows.Controls.ComboBox)window.FindName("ResolutionInput");
                dropdown.ApplyTemplate(); dropdown.SelectedValue = "2560x1440";
                Assert.NotNull(dropdown.SelectedItem);
                Assert.Equal(5, ((System.Windows.Controls.StackPanel)window.FindName("Navigation")).Children.Count);
                var reflection = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                var transfers = (DownloadManager)typeof(MainWindow).GetField("_downloads", reflection)!.GetValue(window)!;
                var refresh = typeof(MainWindow).GetMethod("RefreshActivity", reflection)!;
                var queued = new DownloadItem("Example mod"); transfers.Items.Add(queued); refresh.Invoke(window, null);
                var indicator = (ActivityIndicator)window.FindName("DownloadSpinner"); Assert.True(indicator.IsRunning);
                queued.TogglePause(); refresh.Invoke(window, null); Assert.False(indicator.IsRunning);
                Assert.Contains("Paused", ((System.Windows.Controls.TextBlock)window.FindName("DownloadDetail")).Text);
                transfers.Items.Clear(); refresh.Invoke(window, null); Assert.False(indicator.IsRunning);
                var root = (System.Windows.FrameworkElement)window.Content;
                foreach (var size in new[] { new System.Windows.Size(1320, 840), new System.Windows.Size(1120, 740) })
                {
                    root.Measure(size); root.Arrange(new System.Windows.Rect(size)); root.UpdateLayout();
                    var play = (System.Windows.Controls.Button)window.FindName("PlayButton");
                    var bounds = play.TransformToAncestor(root).TransformBounds(new System.Windows.Rect(play.RenderSize));
                    Assert.True(bounds.Right <= size.Width && bounds.Bottom <= size.Height && bounds.Width >= 150);
                    var preview = Environment.GetEnvironmentVariable("PLUTONIUM_UI_PREVIEW_DIR");
                    if (preview is not null)
                    {
                        Directory.CreateDirectory(preview);
                        var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)size.Width * 2, (int)size.Height * 2, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                        bitmap.Render(root); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                        using var output = File.Create(Path.Combine(preview, $"launcher-{size.Width:0}.png")); encoder.Save(output);
                    }
                }
                var analyticsType = typeof(MainWindow).Assembly.GetType("PlutoniumLauncher.ModerationAnalytics")!;
                var analyticsData = System.Text.Json.JsonSerializer.SerializeToElement(new {
                    accounts=new {active=12,playing=8,total=126,disabled=3}, installs=new {total=150,this_week=25,last_week=20},
                    weekly=new {this_week=48,last_week=40}, pendingAppeals=2,
                    daily=Enumerable.Range(0,14).Select(i=>new {day=new DateTime(2026,9,25).AddDays(i).ToString("yyyy-MM-dd"),active=i*2,installs=i%4}),
                    versions=new[]{new {version="2.0.1",count=48}}, reviews=new {accepted=4,rejected=1},
                    definition="Preview fixture · UTC calendar days · today is partial"
                });
                var analytics=(System.Windows.FrameworkElement)analyticsType.GetMethod("Build",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,new object[]{analyticsData})!;
                analytics.Measure(new System.Windows.Size(940,1100));analytics.Arrange(new System.Windows.Rect(0,0,940,1100));analytics.UpdateLayout();
                Assert.True(analytics.DesiredSize.Width<=940);
                var analyticsPreview=Environment.GetEnvironmentVariable("PLUTONIUM_UI_PREVIEW_DIR");
                if(analyticsPreview is not null) {
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1880,2200,192,192,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(analytics);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output=File.Create(Path.Combine(analyticsPreview,"moderation-analytics.png"));encoder.Save(output);
                }
                typeof(MainWindow).GetField("_maintenance",reflection)!.SetValue(window,true);
                typeof(MainWindow).GetField("_maintenanceMenuOpen",reflection)!.SetValue(window,true);
                typeof(MainWindow).GetMethod("Navigate",reflection)!.Invoke(window,new object[]{"play"});
                var maintenancePanel=(System.Windows.Controls.Border)typeof(MainWindow).GetField("_maintenancePanel",reflection)!.GetValue(window)!;
                Assert.Equal(System.Windows.Visibility.Visible,maintenancePanel.Visibility);
                Assert.False((bool)typeof(MainWindow).GetField("_maintenanceMenuOpen",reflection)!.GetValue(window)!);
                Assert.Equal("MAINTENANCE MODE",((System.Windows.Controls.TextBlock)typeof(MainWindow).GetField("_maintenanceTitle",reflection)!.GetValue(window)!).Text);
                typeof(MainWindow).GetField("_maintenance",reflection)!.SetValue(window,false);
                maintenancePanel.Visibility=System.Windows.Visibility.Collapsed;
                var editorType=typeof(MainWindow).Assembly.GetType("PlutoniumLauncher.ModerationSettingsEditor")!;
                var settingsData=System.Text.Json.JsonSerializer.SerializeToElement(new {maintenance=false,message="Scheduled maintenance",revision=7,disabledFeatures=new[]{"fly"},availableFeatures=new[]{"players","storage","spawners","tracers","fly","inventorymove","aimassist","notifications","radio","mods"}});
                object? savedPayload=null;
                var editor=(System.Windows.FrameworkElement)Activator.CreateInstance(editorType,System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new object[]{settingsData,new Func<object,Task>(payload=>{savedPayload=payload;return Task.CompletedTask;}),new Func<Task>(()=>Task.CompletedTask)},null)!;
                editor.Measure(new System.Windows.Size(850,580));editor.Arrange(new System.Windows.Rect(0,0,850,580));editor.UpdateLayout();
                var saveButton=(System.Windows.Controls.Button)editorType.GetProperty("Save",reflection)!.GetValue(editor)!;
                var reasonInput=(System.Windows.Controls.TextBox)editorType.GetProperty("Reason",reflection)!.GetValue(editor)!;
                Assert.False(saveButton.IsEnabled);reasonInput.Text="New maintenance reason";Assert.True(saveButton.IsEnabled);
                var saveBounds=saveButton.TransformToAncestor(editor).TransformBounds(new System.Windows.Rect(saveButton.RenderSize));Assert.True(saveBounds.Bottom<=580);Assert.True(reasonInput.ActualHeight>=90);
                if(analyticsPreview is not null){var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1700,1160,192,192,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(editor);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(analyticsPreview,"moderation-settings.png"));encoder.Save(output);}
                saveButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.NotNull(savedPayload);var payloadJson=System.Text.Json.JsonSerializer.SerializeToElement(savedPayload);Assert.Equal(7,payloadJson.GetProperty("revision").GetInt32());Assert.Equal("fly",payloadJson.GetProperty("disabledFeatures")[0].GetString());
                var betaDialog=(System.Windows.Window)typeof(MainWindow).GetMethod("CreateBetaPrompt",reflection)!.Invoke(window,null)!;
                var betaSurface=(System.Windows.Controls.StackPanel)betaDialog.Content;
                betaSurface.Measure(new System.Windows.Size(520,double.PositiveInfinity));
                betaSurface.Arrange(new System.Windows.Rect(0,0,520,betaSurface.DesiredSize.Height));betaSurface.UpdateLayout();
                var betaField=betaSurface.Children.OfType<System.Windows.Controls.TextBox>().Single();
                Assert.True(betaField.ActualHeight>=46);
                var betaRedeem=betaSurface.Children.OfType<System.Windows.Controls.Button>().Single(b=>Equals(b.Content,"UNLOCK BETA"));
                betaRedeem.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                Assert.Contains(betaSurface.Children.OfType<System.Windows.Controls.TextBlock>(),t=>t.Text=="Enter the invitation key first.");
                if(analyticsPreview is not null) {
                    var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1040,(int)Math.Ceiling(betaSurface.DesiredSize.Height*2),192,192,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(betaSurface);
                    var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(analyticsPreview,"beta-enrollment.png"));encoder.Save(output);
                }
                betaDialog.Close();
                var splash = new SplashWindow();
                splash.SetStatus("Checking client files…");
                Assert.Contains("Checking", ((System.Windows.Controls.TextBlock)splash.FindName("StatusText")).Text);
                var splashPreview = Environment.GetEnvironmentVariable("PLUTONIUM_UI_PREVIEW_DIR");
                if (splashPreview is not null)
                {
                    var surface = (System.Windows.FrameworkElement)splash.Content;
                    surface.Measure(new System.Windows.Size(500, 340)); surface.Arrange(new System.Windows.Rect(0, 0, 500, 340)); surface.UpdateLayout();
                    var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap(1000, 680, 192, 192, System.Windows.Media.PixelFormats.Pbgra32);
                    bitmap.Render(surface); var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                    using var output = File.Create(Path.Combine(splashPreview, "splash.png")); encoder.Save(output);
                }
                splash.Close();
                window.Close(); app.Shutdown();
            }
            catch (Exception ex) { failure = ex; }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA); thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(15)), "Native UI test timed out.");
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    [Theory]
    [InlineData("example.org:99999")]
    [InlineData("example.org --demo")]
    [InlineData("example.org\\")]
    [InlineData("https://example.org")]
    public void RejectsInvalidServerAddress(string value) => Assert.Throws<ArgumentException>(() => ServerFavorite.Create("Test", value));

    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
