using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace PlutoniumLauncher;

public sealed class BootstrapService
{
    private const string StandaloneResource = "PlutoniumLauncher.Payload.standalone.jar";
    private const string FabricResource = "PlutoniumLauncher.Payload.fabric.jar";
    private const string VersionResource = "PlutoniumLauncher.Payload.minecraft-version.json";
    private const string StandaloneVersion = "plutonium-1.21.11";
    private const string FabricModFile = "plutonium-client-fabric.jar";
    private readonly Assembly _assembly;

    public BootstrapService(Assembly? assembly = null) => _assembly = assembly ?? Assembly.GetExecutingAssembly();

    public async Task<InstallationInfo> InstallOrRepairAsync(
        LauncherConfig config,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var detected = InstallationDiscovery.Detect(config);
        Directory.CreateDirectory(detected.MinecraftDirectory);
        Directory.CreateDirectory(detected.StandaloneGameDirectory);

        progress?.Report("Verifying standalone client files");
        var installRoots = new[] { detected.MinecraftDirectory, detected.StandaloneGameDirectory }
            .Distinct(StringComparer.OrdinalIgnoreCase);
        if (!IsNewer(config.StandaloneClientVersion, "1.1.1")
            || !IsReadableJar(Path.Combine(detected.StandaloneGameDirectory, "versions", StandaloneVersion, StandaloneVersion + ".jar")))
        {
            foreach (var gameDirectory in installRoots)
            {
                var versionDirectory = Path.Combine(gameDirectory, "versions", StandaloneVersion);
                Directory.CreateDirectory(versionDirectory);
                await InstallEmbeddedAsync(StandaloneResource, Path.Combine(versionDirectory, StandaloneVersion + ".jar"), cancellationToken);
            }
            config.StandaloneClientVersion = "1.1.1";
        }
        // Metadata can go missing independently of the jar. Repair it without downgrading a newer client.
        foreach (var gameDirectory in installRoots)
        {
            var versionDirectory = Path.Combine(gameDirectory, "versions", StandaloneVersion);
            Directory.CreateDirectory(versionDirectory);
            await InstallStandaloneMetadataAsync(Path.Combine(versionDirectory, StandaloneVersion + ".json"), cancellationToken);
        }

        progress?.Report("Verifying Fabric client files");
        var modsDirectory = Path.Combine(detected.FabricGameDirectory, "mods");
        Directory.CreateDirectory(modsDirectory);
        var fabricMod = InstallationDiscovery.FabricModPath(detected.FabricGameDirectory);
        if (!IsNewer(config.FabricClientVersion, "1.1.1") || !IsReadableJar(fabricMod))
        {
            await InstallEmbeddedAsync(FabricResource, fabricMod, cancellationToken);
            config.FabricClientVersion = "1.1.1";
        }

        config.MinecraftDirectory = detected.MinecraftDirectory;
        config.StandaloneGameDirectory = detected.StandaloneGameDirectory;
        await config.SaveAsync(cancellationToken);
        progress?.Report("Plutonium files are ready");
        return InstallationDiscovery.Detect(config);
    }

    private static bool IsNewer(string candidate, string baseline) =>
        Version.TryParse(candidate, out var candidateVersion)
        && Version.TryParse(baseline, out var baselineVersion)
        && candidateVersion > baselineVersion;

    private static bool IsReadableJar(string path)
    {
        try { using var jar = ZipFile.OpenRead(path); return jar.GetEntry("com/quirk/client/Quirk.class") is not null; }
        catch (InvalidDataException) { return false; }
        catch (FileNotFoundException) { return false; }
        catch (DirectoryNotFoundException) { return false; }
    }

    private async Task InstallStandaloneMetadataAsync(string target, CancellationToken cancellationToken)
    {
        var source = await ReadResourceAsync(VersionResource, cancellationToken);
        var metadata = JsonNode.Parse(source) as JsonObject
            ?? throw new InvalidDataException("Bundled Minecraft version metadata is invalid.");
        metadata["id"] = StandaloneVersion;
        metadata.AsObject().Remove("downloads");
        var bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(metadata, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true
        });
        if (await MatchesAsync(target, bytes, cancellationToken)) return;
        await WriteAtomicallyAsync(target, bytes, cancellationToken);
    }

    private async Task InstallEmbeddedAsync(string resourceName, string target, CancellationToken cancellationToken)
    {
        var bytes = await ReadResourceAsync(resourceName, cancellationToken);
        if (await MatchesAsync(target, bytes, cancellationToken)) return;
        await WriteAtomicallyAsync(target, bytes, cancellationToken);
    }

    private async Task<byte[]> ReadResourceAsync(string resourceName, CancellationToken cancellationToken)
    {
        await using var source = _assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Required embedded payload is missing: {resourceName}");
        using var memory = new MemoryStream();
        await source.CopyToAsync(memory, cancellationToken);
        return memory.ToArray();
    }

    private static async Task<bool> MatchesAsync(string target, byte[] expected, CancellationToken cancellationToken)
    {
        if (!File.Exists(target)) return false;
        await using var stream = File.OpenRead(target);
        if (stream.Length != expected.Length) return false;
        var actualHash = await SHA256.HashDataAsync(stream, cancellationToken);
        var expectedHash = SHA256.HashData(expected);
        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    private static async Task WriteAtomicallyAsync(string target, byte[] bytes, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + ".pending-" + Guid.NewGuid().ToString("N");
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             128 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }
            AtomicFile.Replace(temporary, target);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
