using System.IO;
using System.Net.Http;
using System.Security.Cryptography;

namespace PlutoniumLauncher;

public sealed record DetectedMod(string ProjectId, string VersionId, string Version, string Title, string Path, bool Enabled, bool Managed);
public sealed record ModDetectionResult(List<DetectedMod> Mods, string? Warning);
public sealed class ModDetection(ModrinthService service)
{
    private readonly Dictionary<string, (long Length, DateTime Modified, string Hash)> _hashes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModVersion?> _versions = new(StringComparer.OrdinalIgnoreCase);
    public async Task<ModDetectionResult> ScanAsync(string gameDirectory, CancellationToken token)
    {
        var library = new ModLibrary(gameDirectory, new()); var managed = await library.LoadAsync();
        if (!Directory.Exists(library.DirectoryPath)) return new([], null);
        var files = new List<(string Path, string Hash)>();
        foreach (var path in Directory.EnumerateFiles(library.DirectoryPath).Where(p => p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
        {
            token.ThrowIfCancellationRequested(); var info = new FileInfo(path);
            if (!_hashes.TryGetValue(path, out var saved) || saved.Length != info.Length || saved.Modified != info.LastWriteTimeUtc)
            {
                await using var stream = File.OpenRead(path);
                saved = (info.Length, info.LastWriteTimeUtc, Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).ToLowerInvariant()); _hashes[path] = saved;
            }
            files.Add((path, saved.Hash));
        }
        var known = new List<DetectedMod>(); var unknown = new List<(string Path, string Hash)>();
        foreach (var file in files)
        {
            var record = managed.FirstOrDefault(m => library.PathFor(m).Equals(file.Path, StringComparison.OrdinalIgnoreCase) && m.Sha512.Equals(file.Hash, StringComparison.OrdinalIgnoreCase));
            if (record is not null) known.Add(new(record.ProjectId, record.VersionId, record.Version, record.Title, file.Path, record.Enabled, true));
            else unknown.Add(file);
        }
        string? warning = null;
        try
        {
            foreach (var batch in unknown.Select(f => f.Hash).Distinct().Where(h => !_versions.ContainsKey(h)).Chunk(100))
            {
                var identified = await service.IdentifyAsync(batch, token);
                foreach (var hash in batch)
                {
                    identified.TryGetValue(hash, out var version);
                    if (version is not null && !version.Files.Any(f => f.Hashes.TryGetValue("sha512", out var actual) && actual.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("Modrinth returned a mismatched file identity.");
                    _versions[hash] = version;
                }
            }
        }
        catch (HttpRequestException) { warning = "Some local mods could not be identified while Modrinth is unavailable."; }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { warning = "Local mod identification timed out. Retry when Modrinth responds."; }
        foreach (var file in unknown)
            if (_versions.TryGetValue(file.Hash, out var version) && version is not null)
                known.Add(new(version.ProjectId, version.Id, version.VersionNumber, version.Name, file.Path, !file.Path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase), false));
        return new(known, warning);
    }
}
