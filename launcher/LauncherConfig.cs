using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlutoniumLauncher;

public sealed class LauncherConfig
{
    public string MinecraftDirectory { get; set; } = string.Empty;
    public string StandaloneGameDirectory { get; set; } = string.Empty;
    public string JavaPath { get; set; } = string.Empty;
    public int MaximumMemoryGb { get; set; } = 4;
    public int ScreenWidth { get; set; } = 1920;
    public int ScreenHeight { get; set; } = 1080;
    public bool Fullscreen { get; set; }
    public string SelectedProfile { get; set; } = "standalone";
    public string SelectedAccountId { get; set; } = string.Empty;
    public string InstallationId { get; set; } = Guid.NewGuid().ToString();
    public string Theme { get; set; } = "Black";
    public bool AutomaticUpdates { get; set; } = true;
    public bool ShowPlayerHead { get; set; } = true;
    public List<ServerFavorite> Servers { get; set; } = [];
    public string FabricRollbackHash { get; set; } = "";
    public string StandaloneRollbackHash { get; set; } = "";
    public string StandaloneClientVersion { get; set; } = "2.0.1";
    public string FabricClientVersion { get; set; } = "2.0.1";
    public string ClientManifestUrl { get; set; } = "https://github.com/Plutonn0/Plutonium/releases/latest/download/client.json";
    public string LauncherManifestUrl { get; set; } = "https://github.com/Plutonn0/Plutonium/releases/latest/download/launcher.json";

    [JsonIgnore]
    public string? DataRootOverride { get; set; }

    [JsonIgnore]
    public string DataDirectory => DataRootOverride
        ?? Environment.GetEnvironmentVariable("PLUTONIUM_LAUNCHER_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plutonium Client");

    [JsonIgnore]
    public string ConfigPath => Path.Combine(DataDirectory, "launcher.json");

    public static async Task<LauncherConfig> LoadAsync(CancellationToken cancellationToken = default, string? dataDirectory = null)
    {
        var config = new LauncherConfig { DataRootOverride = dataDirectory };
        Directory.CreateDirectory(config.DataDirectory);
        if (File.Exists(config.ConfigPath))
        {
            try
            {
                await using var stream = File.OpenRead(config.ConfigPath);
                config = await JsonSerializer.DeserializeAsync<LauncherConfig>(stream, cancellationToken: cancellationToken)
                    ?? new LauncherConfig();
            }
            catch (JsonException)
            {
                var backup = config.ConfigPath + ".invalid-" + DateTime.UtcNow.ToString("yyyyMMddHHmmss");
                File.Move(config.ConfigPath, backup, overwrite: true);
            }
        }

        config.DataRootOverride = dataDirectory;
        if (string.IsNullOrWhiteSpace(config.ClientManifestUrl)) config.ClientManifestUrl = "https://github.com/Plutonn0/Plutonium/releases/latest/download/client.json";
        if (string.IsNullOrWhiteSpace(config.LauncherManifestUrl)) config.LauncherManifestUrl = "https://github.com/Plutonn0/Plutonium/releases/latest/download/launcher.json";
        config.Normalize();
        return config;
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        Normalize();
        Directory.CreateDirectory(DataDirectory);
        var temporary = ConfigPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                         16 * 1024, FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, this, new JsonSerializerOptions { WriteIndented = true }, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }
        AtomicFile.Replace(temporary, ConfigPath);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public void Normalize()
    {
        MaximumMemoryGb = Math.Clamp(MaximumMemoryGb, 2, 32);
        ScreenWidth = Math.Clamp(ScreenWidth, 800, 7680);
        ScreenHeight = Math.Clamp(ScreenHeight, 600, 4320);
        if (SelectedProfile is not ("standalone" or "fabric")) SelectedProfile = "standalone";
        MinecraftDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(MinecraftDirectory)
            ? Environment.GetEnvironmentVariable("PLUTONIUM_MINECRAFT_DIR")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft")
            : MinecraftDirectory.Trim());
        StandaloneGameDirectory = Path.GetFullPath(string.IsNullOrWhiteSpace(StandaloneGameDirectory)
            ? MinecraftDirectory
            : StandaloneGameDirectory.Trim());
    }
}

internal static class AtomicFile
{
    public static void Replace(string temporaryPath, string targetPath)
    {
        File.Move(temporaryPath, targetPath, overwrite: true);
    }
}
