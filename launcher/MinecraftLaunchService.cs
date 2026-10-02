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
    private const string GameVersion = "1.21.11";
    private const string StandaloneVersion = "plutonium-1.21.11";
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromMinutes(10) };
    public async Task<Process> PrepareAndLaunchAsync(
        LauncherConfig config,
        JavaRuntime java,
        MSession session,
        bool fabric,
        IProgress<string>? progress = null,
        IProgress<double>? byteProgress = null,
        CancellationToken cancellationToken = default)
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
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report("Launching Minecraft");
        if (!process.Start()) throw new InvalidOperationException("Windows could not start the Minecraft process.");
        return process;
    }
}
