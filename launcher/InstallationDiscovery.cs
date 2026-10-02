using System.IO;
using System.Text.Json;

namespace PlutoniumLauncher;

public sealed record InstallationInfo(
    string MinecraftDirectory,
    string StandaloneGameDirectory,
    string FabricGameDirectory,
    bool HasLegacyQuirkSettings,
    bool HasStandaloneFiles,
    bool HasFabricFiles,
    string? LegacyProfileName);

public static class InstallationDiscovery
{
    public static InstallationInfo Detect(LauncherConfig config)
    {
        var minecraftDirectory = ExistingMinecraftDirectory(config.MinecraftDirectory);
        var standaloneDirectory = FindLegacyGameDirectory(minecraftDirectory) ?? config.StandaloneGameDirectory;
        var legacySettings = File.Exists(Path.Combine(standaloneDirectory, "quirk", "settings.json"));
        var standalone = new[] { minecraftDirectory, standaloneDirectory }.Distinct(StringComparer.OrdinalIgnoreCase).Any(gameDirectory =>
        {
            var versionDirectory = Path.Combine(gameDirectory, "versions", "plutonium-1.21.11");
            return File.Exists(Path.Combine(versionDirectory, "plutonium-1.21.11.jar"))
                && File.Exists(Path.Combine(versionDirectory, "plutonium-1.21.11.json"));
        });
        var fabric = File.Exists(FabricModPath(minecraftDirectory));

        return new InstallationInfo(minecraftDirectory, standaloneDirectory, minecraftDirectory, legacySettings,
            standalone, fabric, FindLegacyProfileName(minecraftDirectory));
    }

    public static string FabricModPath(string minecraftDirectory)
    {
        var mods = Path.Combine(minecraftDirectory, "mods");
        var legacyPath = Path.Combine(mods, "quirk-client-fabric.jar");
        return File.Exists(legacyPath) ? legacyPath : Path.Combine(mods, "plutonium-client-fabric.jar");
    }

    private static string ExistingMinecraftDirectory(string configured)
    {
        if (!string.IsNullOrWhiteSpace(configured)) return Path.GetFullPath(configured);
        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft");
        return Path.GetFullPath(defaultPath);
    }

    private static string? FindLegacyGameDirectory(string minecraftDirectory)
    {
        var legacyDirectory = Path.Combine(minecraftDirectory, "quirk-client");
        if (File.Exists(Path.Combine(legacyDirectory, "quirk", "settings.json"))) return legacyDirectory;
        var profilePath = Path.Combine(minecraftDirectory, "launcher_profiles.json");
        if (!File.Exists(profilePath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
            if (!document.RootElement.TryGetProperty("profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Object) return null;
            foreach (var profile in profiles.EnumerateObject())
            {
                var name = StringProperty(profile.Value, "name");
                var id = StringProperty(profile.Value, "lastVersionId");
                if (!LooksLikeLegacyProfile(profile.Name, name, id)) continue;
                var gameDirectory = StringProperty(profile.Value, "gameDir");
                if (!string.IsNullOrWhiteSpace(gameDirectory) && Directory.Exists(gameDirectory)) return Path.GetFullPath(gameDirectory);
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        return null;
    }

    private static string? FindLegacyProfileName(string minecraftDirectory)
    {
        var profilePath = Path.Combine(minecraftDirectory, "launcher_profiles.json");
        if (!File.Exists(profilePath)) return null;
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(profilePath));
            if (!document.RootElement.TryGetProperty("profiles", out var profiles)
                || profiles.ValueKind != JsonValueKind.Object) return null;
            foreach (var profile in profiles.EnumerateObject())
            {
                var name = StringProperty(profile.Value, "name");
                var id = StringProperty(profile.Value, "lastVersionId");
                if (LooksLikeLegacyProfile(profile.Name, name, id)) return name ?? profile.Name;
            }
        }
        catch (JsonException) { }
        catch (IOException) { }
        return null;
    }

    private static bool LooksLikeLegacyProfile(string key, string? name, string? version) =>
        (key + " " + name + " " + version).Contains("quirk", StringComparison.OrdinalIgnoreCase)
        || (key + " " + name + " " + version).Contains("plutonium", StringComparison.OrdinalIgnoreCase);

    private static string? StringProperty(JsonElement value, string property) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(property, out var element)
            && element.ValueKind == JsonValueKind.String ? element.GetString() : null;
}
