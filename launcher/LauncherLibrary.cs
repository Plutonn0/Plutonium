using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace PlutoniumLauncher;

public sealed record ServerFavorite(string Name, string Address)
{
    public static ServerFavorite Create(string name, string address)
    {
        name = name.Trim(); address = address.Trim();
        if (name.Length is < 1 or > 64) throw new ArgumentException("Use a server name between 1 and 64 characters.");
        if (address.Length is < 1 or > 253 || address.Any(char.IsWhiteSpace) || address.Contains('/') || address.Contains('\\') || address.Contains('?') || address.Contains('#') || address.Contains('@') || address.Contains('"'))
            throw new ArgumentException("Enter a server hostname or IP address, optionally followed by :port.");
        if (!Uri.TryCreate("tcp://" + address, UriKind.Absolute, out var uri) || uri.HostNameType == UriHostNameType.Unknown || uri.Port is 0 or > 65535)
            throw new ArgumentException("The server address or port is invalid.");
        return new(name, address);
    }
}

public sealed record UpdateSnapshot(string Id, DateTimeOffset Created, string Component, string Version, string Target, string Backup, string Sha256);
public sealed record UpdateEvent(DateTimeOffset Created, string Component, string FromVersion, string ToVersion, string Status);

public sealed class UpdateHistory(string dataDirectory)
{
    private string Root => Path.Combine(dataDirectory, "history");
    public async Task RecordAsync(string component, string from, string to, string status)
    {
        var directory = Path.Combine(Root, "events"); Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, Guid.NewGuid().ToString("N") + ".json"),
            JsonSerializer.Serialize(new UpdateEvent(DateTimeOffset.Now, component, from, to, status)));
    }
    public async Task<List<UpdateEvent>> EventsAsync()
    {
        var directory = Path.Combine(Root, "events"); if (!Directory.Exists(directory)) return [];
        var events = new List<UpdateEvent>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json"))
            if (JsonSerializer.Deserialize<UpdateEvent>(await File.ReadAllTextAsync(path)) is { } entry) events.Add(entry);
        return events.OrderByDescending(e => e.Created).ToList();
    }
    public async Task<List<UpdateSnapshot>> LoadAsync()
    {
        if (!Directory.Exists(Root)) return [];
        var results = new List<UpdateSnapshot>();
        foreach (var file in Directory.EnumerateFiles(Root, "*.json"))
        {
            var record = JsonSerializer.Deserialize<UpdateSnapshot>(await File.ReadAllTextAsync(file));
            if (record is not null) results.Add(record);
        }
        return results.OrderByDescending(s => s.Created).ToList();
    }
    public async Task CaptureAsync(string component, string version, string target, CancellationToken token = default)
    {
        if (!File.Exists(target)) return;
        Directory.CreateDirectory(Root);
        var id = Guid.NewGuid().ToString("N");
        var backup = Path.Combine(Root, id + ".backup");
        await using (var source = File.OpenRead(target))
        await using (var output = File.Create(backup)) await source.CopyToAsync(output, token);
        await using var stream = File.OpenRead(backup);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
        var snapshot = new UpdateSnapshot(id, DateTimeOffset.Now, component, version, Path.GetFullPath(target), backup, hash);
        await File.WriteAllTextAsync(Path.Combine(Root, id + ".json"), JsonSerializer.Serialize(snapshot), token);
    }
    public async Task<string> PrepareRestoreAsync(UpdateSnapshot snapshot, CancellationToken token = default)
    {
        var expected = Path.GetFullPath(Path.Combine(Root, snapshot.Id + ".backup"));
        if (!Guid.TryParseExact(snapshot.Id, "N", out _) || !expected.Equals(Path.GetFullPath(snapshot.Backup), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid rollback backup path.");
        if (!await UpdateService.FileMatchesSha256Async(expected, snapshot.Sha256, token)) throw new InvalidDataException("The rollback backup is missing or damaged.");
        var staged = Path.Combine(Root, "restore-" + Guid.NewGuid().ToString("N"));
        File.Copy(expected, staged);
        return staged;
    }
}

public sealed record HealthFinding(string Name, string Status, string Detail);
public static class InstallationHealth
{
    public static async Task<List<HealthFinding>> CheckAsync(LauncherConfig config, CancellationToken token)
    {
        var findings = new List<HealthFinding>();
        var java = await JavaRuntimeDetector.ProbePathAsync(config.JavaPath, token);
        findings.Add(new("Java runtime", java is null ? "REPAIR" : "OK", java?.DisplayVersion ?? "Java 21 is missing or cannot start."));
        foreach (var root in new[] { config.MinecraftDirectory, config.StandaloneGameDirectory }.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Directory.CreateDirectory(root);
                var probe = Path.Combine(root, ".plutonium-write-" + Guid.NewGuid().ToString("N"));
                await File.WriteAllTextAsync(probe, "test", token); File.Delete(probe);
                var free = new DriveInfo(Path.GetPathRoot(root)!).AvailableFreeSpace;
                findings.Add(new("Game folder", free < 2L * 1024 * 1024 * 1024 ? "WARNING" : "OK", $"{root} · {free / 1073741824d:0.0} GB free"));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { findings.Add(new("Game folder", "REPAIR", ex.Message)); }
        }
        foreach (var path in new[] { InstallationDiscovery.FabricModPath(config.MinecraftDirectory), Path.Combine(config.StandaloneGameDirectory, "versions", "plutonium-1.21.11", "plutonium-1.21.11.jar") })
        {
            try { using var jar = System.IO.Compression.ZipFile.OpenRead(path); if (jar.GetEntry("com/quirk/client/Quirk.class") is null) throw new InvalidDataException("Client entry point missing."); findings.Add(new(Path.GetFileName(path), "OK", "Client archive and entry point readable")); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { findings.Add(new(Path.GetFileName(path), "REPAIR", ex.Message)); }
        }
        var library = new ModLibrary(config.MinecraftDirectory, new());
        var managed = await library.LoadAsync();
        foreach (var mod in managed)
        {
            token.ThrowIfCancellationRequested(); var path = library.PathFor(mod);
            var valid = false;
            if (File.Exists(path)) { await using var stream = File.OpenRead(path); valid = Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).Equals(mod.Sha512, StringComparison.OrdinalIgnoreCase); }
            findings.Add(new(mod.Title, valid ? "OK" : "REPAIR", valid ? "Managed mod checksum verified" : "Missing or modified mod. Reinstall it from Mods."));
        }
        var ids = new Dictionary<string, string>();
        if (Directory.Exists(library.DirectoryPath))
            foreach (var path in Directory.EnumerateFiles(library.DirectoryPath, "*.jar"))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    foreach (var id in ModLibrary.ReadModIds(path))
                        if (!ids.TryAdd(id, path)) findings.Add(new("Duplicate mod: " + id, "REPAIR", Path.GetFileName(path) + " and " + Path.GetFileName(ids[id])));
                }
                catch (InvalidDataException ex) { findings.Add(new(Path.GetFileName(path), "REPAIR", ex.Message)); }
            }
        findings.Add(new("Minecraft assets and libraries", "ON LAUNCH", "Minecraft's installer checks and downloads required game files when you press Play."));
        return findings;
    }
}
