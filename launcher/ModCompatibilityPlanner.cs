using System.IO;
using System.Security.Cryptography;

namespace PlutoniumLauncher;

public sealed record CompatibleModPlan(List<ModPlanEntry> Entries, List<string> Notes);

public sealed class ModCompatibilityPlanner(ModrinthService service, DownloadManager downloads)
{
    private sealed record LocalMod(string Path, List<FabricMetadata> Metadata, ModVersion? Version);
    private sealed record Requirement(string Project, string? Exact, string Title);

    public async Task<CompatibleModPlan> ResolveAsync(string gameDirectory, string project, string title, bool previews, CancellationToken token)
    {
        var directory = Path.Combine(gameDirectory, "mods"); var local = new List<LocalMod>();
        var files = new List<(string Path, string Hash, List<FabricMetadata> Metadata)>();
        if (Directory.Exists(directory))
            foreach (var path in Directory.EnumerateFiles(directory, "*.jar"))
            {
                token.ThrowIfCancellationRequested();
                await using var stream = File.OpenRead(path); var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).ToLowerInvariant();
                files.Add((path, hash, FabricMetadata.Read(path)));
            }
        foreach (var batch in files.Chunk(100))
        {
            var identified = await service.IdentifyAsync(batch.Select(f => f.Hash).Distinct(), token);
            foreach (var file in batch)
            {
                identified.TryGetValue(file.Hash, out var version);
                if (version is not null && !version.Files.Any(f => f.Hashes.TryGetValue("sha512", out var h) && h.Equals(file.Hash, StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidDataException("Modrinth returned a mismatched local file identity.");
                local.Add(new(file.Path, file.Metadata, version));
            }
        }
        var versions = new Dictionary<string, List<ModVersion>>(); var metadata = new Dictionary<string, List<FabricMetadata>>();
        var reasons = new HashSet<string>(); var attempts = 0;
        var temporary = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "PlutoniumCompatibility-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(temporary);
        async Task<List<FabricMetadata>> Inspect(ModVersion version)
        {
            if (metadata.TryGetValue(version.Id, out var cached)) return cached;
            var installed = local.FirstOrDefault(l => l.Version?.Id == version.Id);
            if (installed is not null) return metadata[version.Id] = installed.Metadata;
            var file = ModrinthService.SelectFile(version); var path = Path.Combine(temporary, file.Hashes["sha512"] + ".jar");
            if (!File.Exists(path)) await downloads.DownloadAsync("Compatibility check · " + version.Name, file.Url, path, file.Hashes["sha512"], HashAlgorithmName.SHA512, token);
            return metadata[version.Id] = FabricMetadata.Read(path);
        }
        bool ApiConflict(IEnumerable<ModVersion> state)
        {
            var all = state.ToList();
            foreach (var owner in all)
                foreach (var rule in owner.Dependencies)
                {
                    var targetProject = rule.ProjectId ?? local.FirstOrDefault(l => l.Version?.Id == rule.VersionId)?.Version?.ProjectId;
                    var target = all.FirstOrDefault(v => targetProject is not null ? v.ProjectId == targetProject : v.Id == rule.VersionId);
                    if (rule.DependencyType == "incompatible" && target is not null && (rule.VersionId is null || target.Id == rule.VersionId)
                        || rule.DependencyType == "required" && target is not null && rule.VersionId is not null && target.Id != rule.VersionId)
                    { reasons.Add(owner.Name + " conflicts with " + target!.Name + " (declared version requirement)."); return true; }
                }
            return false;
        }
        async Task<bool> Check(Dictionary<string, ModVersion> chosen)
        {
            var remaining = local.Where(l => l.Version is null || !chosen.ContainsKey(l.Version.ProjectId)).ToList();
            if (ApiConflict(remaining.Where(l => l.Version is not null).Select(l => l.Version!).Concat(chosen.Values))) return false;
            var additions = new List<FabricMetadata>();
            foreach (var version in chosen.Values) additions.AddRange(await Inspect(version));
            if (additions.Count == 0) return false;
            var changed = additions.SelectMany(m => m.Ids).ToHashSet();
            var all = remaining.SelectMany(l => l.Metadata).Concat(additions).ToList();
            foreach (var candidate in additions)
                if (remaining.Any(l => l.Metadata.Any(m => m.Ids.Intersect(candidate.Ids).Any())))
                { reasons.Add(candidate.Id + " conflicts with an unidentified or duplicate local jar. Its file has been preserved."); return false; }
            foreach (var owner in all)
            {
                foreach (var rule in owner.Depends)
                {
                    if (!additions.Contains(owner) && !changed.Contains(rule.Key)) continue;
                    if (rule.Key is "fabricloader" or "fabric-loader") continue; // Loader is installed separately on launch.
                    var available = rule.Key switch { "minecraft" => new[] { ModrinthService.MinecraftVersion }, "java" => new[] { "21" }, _ => all.Where(m => m.Ids.Contains(rule.Key)).Select(m => m.Version).ToArray() };
                    if (!available.Any(v => FabricVersionRange.Matches(v, rule.Value)))
                    { reasons.Add($"{owner.Id} requires {rule.Key} {string.Join(" or ", rule.Value)}."); return false; }
                }
                foreach (var rule in owner.Breaks)
                {
                    if (!additions.Contains(owner) && !changed.Contains(rule.Key)) continue;
                    if (all.Any(m => m.Ids.Contains(rule.Key) && FabricVersionRange.Matches(m.Version, rule.Value)))
                    { reasons.Add($"{owner.Id} is incompatible with {rule.Key} {string.Join(" or ", rule.Value)}."); return false; }
                }
            }
            return true;
        }
        async Task<Dictionary<string, ModVersion>?> Search(List<Requirement> pending, Dictionary<string, ModVersion> chosen)
        {
            token.ThrowIfCancellationRequested();
            if (++attempts > 256) throw new InvalidDataException("Compatibility search reached its limit. No files were changed. Try a smaller mod set or a specific version.");
            if (pending.Count == 0) return await Check(chosen) ? chosen : null;
            if (chosen.Count > 64) throw new InvalidDataException("Dependency tree exceeds 64 projects.");
            var need = pending[0]; var rest = pending.Skip(1).ToList();
            if (chosen.TryGetValue(need.Project, out var prior))
                return need.Exact is null || need.Exact == prior.Id ? await Search(rest, chosen) : null;
            List<ModVersion> candidates;
            if (need.Exact is not null) candidates = [await service.VersionAsync(need.Exact, token)];
            else
            {
                if (!versions.TryGetValue(need.Project, out candidates!))
                    versions[need.Project] = candidates = (await service.VersionsAsync(need.Project, token)).Where(v => ModrinthService.Compatible(v) && (previews || v.VersionType == "release")).OrderByDescending(v => v.DatePublished).Take(50).ToList();
                // Keep existing dependencies when possible; the requested mod still prefers newest.
                var installed = local.FirstOrDefault(l => l.Version?.ProjectId == need.Project)?.Version;
                if (need.Project != project && installed is not null) candidates = candidates.Prepend(installed).DistinctBy(v => v.Id).ToList();
            }
            foreach (var candidate in candidates.Where(ModrinthService.Compatible))
            {
                if (need.Exact is not null && candidate.ProjectId != need.Project) continue;
                var next = new Dictionary<string, ModVersion>(chosen) { [candidate.ProjectId] = candidate };
                var required = new List<Requirement>();
                foreach (var dependency in candidate.Dependencies.Where(d => d.DependencyType == "required"))
                {
                    var id = dependency.ProjectId;
                    if (id is null && dependency.VersionId is not null) id = (await service.VersionAsync(dependency.VersionId, token)).ProjectId;
                    if (id is null) throw new InvalidDataException("A required dependency cannot be resolved automatically.");
                    required.Add(new(id, dependency.VersionId, id));
                }
                required.AddRange(rest);
                var answer = await Search(required, next); if (answer is not null) return answer;
            }
            return null;
        }
        try
        {
            // Normalize slugs to project IDs so cycles and installed versions use the same keys.
            var rootVersions = await service.VersionsAsync(project, token);
            if (rootVersions.Count == 0) throw new InvalidDataException("No Fabric 1.21.11 version is available.");
            project = rootVersions[0].ProjectId;
            versions[project] = rootVersions.Where(v => ModrinthService.Compatible(v) && (previews || v.VersionType == "release")).OrderByDescending(v => v.DatePublished).Take(50).ToList();
            var solution = await Search([new(project, null, title)], []);
            if (solution is null) throw new InvalidDataException("No compatible combination found within the checked releases. Your mods have not been changed.\n" + string.Join("\n", reasons.Take(5)));
            var entries = solution.Values.Reverse().Select(v => new ModPlanEntry(v.ProjectId == project ? title : v.Name, v, ModrinthService.SelectFile(v))).ToList();
            var notes = reasons.Count > 0 ? new List<string> { "Selected an alternative version to satisfy the installed mods' requirements." } : new List<string>();
            notes.Add("Checked declared Fabric dependencies and incompatibilities. Undeclared runtime conflicts can still occur.");
            return new(entries, notes);
        }
        finally
        {
            if (temporary.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)) Directory.Delete(temporary, true);
        }
    }
}
