using CmlLib.Core;
using CmlLib.Core.Auth;
using CmlLib.Core.Auth.Microsoft;
using CmlLib.Core.Auth.Microsoft.Sessions;
using CmlLib.Core.ModLoaders.FabricMC;
using CmlLib.Core.ProcessBuilder;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using XboxAuthNet.Game.Accounts;

namespace PlutoniumLauncher;

public sealed class MinecraftLaunchService
{
    private readonly System.Collections.Concurrent.ConcurrentQueue<string> _recentOutput = new();
    public string RecentOutput => string.Join(Environment.NewLine, _recentOutput);
    private void Capture(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        _recentOutput.Enqueue(ErrorReport.Redact(line));
        while (_recentOutput.Count > 40) _recentOutput.TryDequeue(out _);
    }
    private const string GameVersion = "1.21.11";
    private const string StandaloneVersion = "plutonium-1.21.11";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    public DownloadManager? Downloads { get; set; }
    public async Task<Process> PrepareAndLaunchAsync(
        LauncherConfig config, JavaRuntime java, MSession session, bool fabric,
        IProgress<string>? progress = null, IProgress<double>? byteProgress = null,
        CancellationToken cancellationToken = default, string? serverAddress = null)
    {
        var item = new DownloadItem("Minecraft 1.21.11 · assets, libraries and loader") { CanPause = false };
        Downloads?.Items.Insert(0, item);
        var finished = false;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, item.Cancellation.Token);
        try
        {
            var process = await PrepareCoreAsync(config, java, session, fabric,
                new Progress<string>(value => { progress?.Report(value); if (!finished) item.Update("Downloading", value, item.Percent); }),
                new Progress<double>(value => { byteProgress?.Report(value); if (!finished) item.Update("Downloading", item.Detail, value * 100); }), linked.Token, serverAddress);
            item.Update("Complete", "Minecraft files checked and game started", 100); return process;
        }
        catch (OperationCanceledException) { item.Update("Cancelled", "Preparation cancelled. Play will resume missing files.", item.Percent); throw; }
        catch (Exception ex) { item.Update("Failed", ErrorReport.Redact(ex.Message), item.Percent); throw; }
        finally { finished = true; }
    }
    private async Task<Process> PrepareCoreAsync(
        LauncherConfig config,
        JavaRuntime java,
        MSession session,
        bool fabric,
        IProgress<string>? progress = null,
        IProgress<double>? byteProgress = null,
        CancellationToken cancellationToken = default,
        string? serverAddress = null)
    {
        var gameDirectory = fabric ? config.MinecraftDirectory : config.StandaloneGameDirectory;
        var path = new MinecraftPath(gameDirectory);
        var launcher = new MinecraftLauncher(path);
        launcher.FileProgressChanged += (_, args) => progress?.Report($"{args.EventType}: {args.Name}");
        launcher.ByteProgressChanged += (_, args) =>
        {
            if (args.TotalBytes > 0) byteProgress?.Report(Math.Clamp(args.ProgressedBytes / (double)args.TotalBytes, 0, 1));
        };

        progress?.Report("Checking Minecraft 1.21.11 files");
        await launcher.InstallAsync(GameVersion, cancellationToken);
        var version = StandaloneVersion;
        if (fabric)
        {
            progress?.Report("Checking Fabric loader");
            var fabricInstaller = new FabricInstaller(_httpClient);
            version = await fabricInstaller.Install(GameVersion, path);
        }

        progress?.Report("Verifying game files");
        var launchOptions = new MLaunchOption
        {
            Path = path,
            Session = session,
            JavaPath = java.ExecutablePath,
            MaximumRamMb = config.MaximumMemoryGb * 1024,
            MinimumRamMb = 1024,
            ScreenWidth = config.ScreenWidth,
            ScreenHeight = config.ScreenHeight,
            FullScreen = config.Fullscreen
        };
        var process = await launcher.InstallAndBuildProcessAsync(version, launchOptions, cancellationToken);
        if (!string.IsNullOrEmpty(serverAddress))
        {
            var address = ServerFavorite.Create("Server", serverAddress).Address;
            if (process.StartInfo.ArgumentList.Count > 0)
            { process.StartInfo.ArgumentList.Add("--quickPlayMultiplayer"); process.StartInfo.ArgumentList.Add(address); }
            else process.StartInfo.Arguments += " --quickPlayMultiplayer \"" + address + "\"";
        }
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Launching Minecraft");
        _recentOutput.Clear();
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.OutputDataReceived += (_, e) => Capture(e.Data);
        process.ErrorDataReceived += (_, e) => Capture(e.Data);
        if (!process.Start()) throw new InvalidOperationException("Windows could not start the Minecraft process.");
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }
}
