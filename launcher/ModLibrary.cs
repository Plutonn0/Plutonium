using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace PlutoniumLauncher;

public sealed record InstalledMod(string ProjectId, string Title, string VersionId, string Version, string Filename,
    string Sha512, bool Enabled, List<ModDependency> Dependencies, List<string> ModIds);

public sealed class ModLibrary(string gameDirectory, DownloadManager downloads, ModrinthService? modrinth = null)
{
    public string DirectoryPath { get; } = Path.Combine(Path.GetFullPath(gameDirectory), "mods");
    private string IndexPath => Path.Combine(DirectoryPath, ".plutonium-mods.json");
    public async Task<List<InstalledMod>> LoadAsync()
    {
        if (!File.Exists(IndexPath)) return [];
        var mods = JsonSerializer.Deserialize<List<InstalledMod>>(await File.ReadAllTextAsync(IndexPath))
            ?? throw new InvalidDataException("The installed mod index is invalid.");
        foreach (var mod in mods) _ = PathFor(mod);
        return mods;
    }
    public string PathFor(InstalledMod mod)
    {
        if (mod.Filename != Path.GetFileName(mod.Filename) || mod.Filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || !mod.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Unsafe filename in the mod index.");
        if (mod.Filename.Equals("plutonium-client-fabric.jar", StringComparison.OrdinalIgnoreCase) || mod.Filename.Equals("quirk-client-fabric.jar", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The bundled client cannot be managed as a third-party mod.");
        return Path.Combine(DirectoryPath, mod.Filename + (mod.Enabled ? "" : ".disabled"));
    }
    private async Task SaveAsync(List<InstalledMod> mods)
    {
        Directory.CreateDirectory(DirectoryPath);
        var temp = IndexPath + "." + Guid.NewGuid().ToString("N");
        try { await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(mods, new JsonSerializerOptions { WriteIndented = true })); AtomicFile.Replace(temp, IndexPath); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    public static List<string> ReadModIds(string file)
    {
        using var jar = ZipFile.OpenRead(file);
        var metadata = jar.GetEntry("fabric.mod.json") ?? throw new InvalidDataException(Path.GetFileName(file) + " is not a Fabric mod.");
        using var stream = metadata.Open(); using var json = JsonDocument.Parse(stream);
        var ids = new List<string> { json.RootElement.GetProperty("id").GetString() ?? throw new InvalidDataException("Missing mod ID.") };
        if (json.RootElement.TryGetProperty("provides", out var provides)) ids.AddRange(provides.EnumerateArray().Select(v => v.GetString()!).Where(v => v is not null));
        return ids.Distinct(StringComparer.Ordinal).ToList();
    }
    public async Task InstallAsync(List<ModPlanEntry> plan, CancellationToken token)
    {
        Directory.CreateDirectory(DirectoryPath);
        var existing = await LoadAsync();
        var preservedProjects = new HashSet<string>();
        var adoptedProjects = new HashSet<string>();
        // Identify local versions by verified content, never by filename or mod ID alone.
        if (modrinth is not null)
        {
            var local = new List<(string Path, string Hash)>();
            foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.jar"))
            {
                if (existing.Any(m => PathFor(m).Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
                await using var stream = File.OpenRead(path);
                local.Add((path, Convert.ToHexString(await SHA512.HashDataAsync(stream, token)).ToLowerInvariant()));
            }
            foreach (var batch in local.Chunk(100))
            {
                var identified = await modrinth.IdentifyAsync(batch.Select(f => f.Hash).Distinct(), token);
                foreach (var (path, hash) in batch)
                {
                    if (!identified.TryGetValue(hash, out var version)) continue;
                    if (!version.Files.Any(f => f.Hashes.TryGetValue("sha512", out var actual) && actual.Equals(hash, StringComparison.OrdinalIgnoreCase)))
                        throw new InvalidDataException("Modrinth returned a mismatched file identity.");
                    var localIds = ReadModIds(path);
                    if (localIds.Any(id => id is "quirk" or "plutonium")) continue;
                    if (existing.Any(m => m.ProjectId == version.ProjectId && File.Exists(PathFor(m))))
                        throw new InvalidDataException("Multiple local copies of " + version.Name + " are installed. Remove the extra copy before installing mods.");
                    existing.RemoveAll(m => m.ProjectId == version.ProjectId);
                    existing.Add(new(version.ProjectId, version.Name, version.Id, version.VersionNumber, Path.GetFileName(path), hash, true, version.Dependencies, localIds));
                    adoptedProjects.Add(version.ProjectId);
                    if (plan.Any(p => p.Version.ProjectId == version.ProjectId && p.File.Hashes["sha512"].Equals(hash, StringComparison.OrdinalIgnoreCase)))
                        preservedProjects.Add(version.ProjectId);
                }
            }
        }
        // Reuse already-installed local dependency files when their exact bytes match the plan.
        foreach (var path in Directory.EnumerateFiles(DirectoryPath, "*.jar"))
        {
            if (existing.Any(m => PathFor(m).Equals(path, StringComparison.OrdinalIgnoreCase))) continue;
            await using var stream = File.OpenRead(path); var hash = Convert.ToHexString(await SHA512.HashDataAsync(stream, token));
            var match = plan.FirstOrDefault(p => p.File.Hashes["sha512"].Equals(hash, StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;
            if (existing.Any(m => m.ProjectId == match.Version.ProjectId && File.Exists(PathFor(m)))) continue;
            existing.RemoveAll(m => m.ProjectId == match.Version.ProjectId);
            var ids = ReadModIds(path); if (ids.Any(id => id is "quirk" or "plutonium")) continue;
            existing.Add(new(match.Version.ProjectId, match.Title, match.Version.Id, match.Version.VersionNumber, Path.GetFileName(path), hash, true, match.Version.Dependencies, ids));
            preservedProjects.Add(match.Version.ProjectId);
        }
        var proposed = plan.Select(p => p.Version.ProjectId).ToHashSet();
        foreach (var entry in plan)
        {
            foreach (var dependency in entry.Version.Dependencies.Where(d => d.DependencyType == "incompatible"))
                if (existing.Any(m => m.Enabled && !proposed.Contains(m.ProjectId) &&
                    ((dependency.ProjectId == m.ProjectId && dependency.VersionId is null) || dependency.VersionId == m.VersionId)))
                    throw new InvalidDataException(entry.Title + " conflicts with an installed mod.");
        }
        foreach (var mod in existing.Where(m => m.Enabled && File.Exists(PathFor(m)) && !proposed.Contains(m.ProjectId)))
            foreach (var dependency in mod.Dependencies)
            {
                var dependencyProject = dependency.ProjectId ?? existing.FirstOrDefault(m => m.VersionId == dependency.VersionId)?.ProjectId;
                if (plan.Any(p => dependency.DependencyType == "required" && dependencyProject == p.Version.ProjectId && dependency.VersionId is not null && dependency.VersionId != p.Version.Id
                    || dependency.DependencyType == "incompatible" && ((dependency.ProjectId == p.Version.ProjectId && dependency.VersionId is null) || dependency.VersionId == p.Version.Id)))
                    throw new InvalidDataException("Installing this version would conflict with " + mod.Title + ".");
            }
        var stage = Path.Combine(DirectoryPath, ".install-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        var rollback = new List<(string Original, string Backup)>();
        var created = new List<string>();
        var canCleanStage = true;
        try
        {
            var additions = new List<InstalledMod>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var replacedFiles = existing.Where(m => proposed.Contains(m.ProjectId) && !preservedProjects.Contains(m.ProjectId)).Select(PathFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var jar in Directory.EnumerateFiles(DirectoryPath, "*.jar"))
                if (!replacedFiles.Contains(jar))
                    foreach (var id in ReadModIds(jar)) ids.Add(id);
            foreach (var entry in plan)
            {
                if (preservedProjects.Contains(entry.Version.ProjectId)) continue;
                var file = ModrinthService.SelectFile(entry.Version);
                var target = Path.Combine(stage, file.Filename);
                if (File.Exists(target)) throw new InvalidDataException("Two dependencies use the same filename.");
                await downloads.DownloadAsync(entry.Title + " · " + entry.Version.VersionNumber, file.Url, target, file.Hashes["sha512"], HashAlgorithmName.SHA512, token);
                var modIds = ReadModIds(target);
                if (modIds.Any(id => id is "quirk" or "plutonium" || !ids.Add(id)))
                    throw new InvalidDataException(entry.Title + " duplicates an installed mod ID. Disable or remove the conflicting local jar first.");
                var installed = new InstalledMod(entry.Version.ProjectId, entry.Title, entry.Version.Id, entry.Version.VersionNumber,
                    file.Filename, file.Hashes["sha512"], true, entry.Version.Dependencies, modIds);
                if (File.Exists(PathFor(installed)) && !replacedFiles.Contains(PathFor(installed)))
                    throw new IOException("A local mod already uses " + file.Filename + ". It has been preserved.");
                additions.Add(installed);
            }
            token.ThrowIfCancellationRequested();
            // Commit only once every dependency is downloaded, verified, and checked for duplicate IDs.
            foreach (var old in existing.Where(m => proposed.Contains(m.ProjectId) && !preservedProjects.Contains(m.ProjectId)))
            {
                var path = PathFor(old);
                if (!File.Exists(path)) continue;
                await using (var stream = File.OpenRead(path))
                    if (adoptedProjects.Contains(old.ProjectId) || !Convert.ToHexString(await SHA512.HashDataAsync(stream)).Equals(old.Sha512, StringComparison.OrdinalIgnoreCase))
                    {
                        var recovery = Path.Combine(DirectoryPath, ".replaced"); Directory.CreateDirectory(recovery);
                        File.Copy(path, Path.Combine(recovery, Guid.NewGuid().ToString("N") + "-" + old.Filename));
                    }
                var backup = Path.Combine(stage, "backup-" + Guid.NewGuid().ToString("N"));
                File.Move(path, backup); rollback.Add((path, backup));
            }
            foreach (var add in additions) { var path = PathFor(add); File.Move(Path.Combine(stage, add.Filename), path); created.Add(path); }
            await SaveAsync(existing.Where(m => (!adoptedProjects.Contains(m.ProjectId) && !proposed.Contains(m.ProjectId)) || preservedProjects.Contains(m.ProjectId)).Concat(additions).ToList());
        }
        catch
        {
            try
            {
                foreach (var path in created) File.Delete(path);
                foreach (var (original, backup) in rollback) File.Move(backup, original);
            }
            catch (Exception recovery)
            {
                canCleanStage = false;
                throw new IOException("Could not finish restoring the previous mod files. Recovery copies are preserved in " + stage, recovery);
            }
            throw;
        }
        finally { if (canCleanStage) Directory.Delete(stage, true); }
    }
    public async Task ChangeAsync(string projectId, bool remove)
    {
        var mods = await LoadAsync(); var mod = mods.Single(m => m.ProjectId == projectId);
        if (remove || mod.Enabled)
        {
            var dependent = mods.FirstOrDefault(m => m.ProjectId != projectId && (remove || m.Enabled) &&
                m.Dependencies.Any(d => d.DependencyType == "required" && (d.ProjectId == projectId || d.VersionId == mod.VersionId)));
            if (dependent is not null) throw new InvalidOperationException(mod.Title + " is required by " + dependent.Title + ". Remove or disable that mod first.");
        }
        else
        {
            foreach (var dependency in mod.Dependencies.Where(d => d.DependencyType == "required"))
                if (!mods.Any(m => m.Enabled && (dependency.VersionId is not null ? m.VersionId == dependency.VersionId : m.ProjectId == dependency.ProjectId)))
                    throw new InvalidOperationException("Enable this mod's required dependencies first.");
            foreach (var jar in Directory.EnumerateFiles(DirectoryPath, "*.jar"))
                if (ReadModIds(jar).Intersect(mod.ModIds).Any()) throw new InvalidOperationException("A conflicting mod is already enabled.");
        }
        var original = PathFor(mod);
        if (!remove && !File.Exists(original)) throw new FileNotFoundException("The mod file is missing. Reinstall it before enabling or disabling it.");
        if (File.Exists(original))
        {
            await using var stream = File.OpenRead(original);
            if (!Convert.ToHexString(await SHA512.HashDataAsync(stream)).Equals(mod.Sha512, StringComparison.OrdinalIgnoreCase))
                throw new IOException("This mod was modified outside the launcher. Its file has been preserved.");
        }
        var changed = mod with { Enabled = !mod.Enabled };
        // Removed mods go to a recoverable folder rather than being permanently deleted.
        var target = remove ? Path.Combine(DirectoryPath, ".removed", Guid.NewGuid().ToString("N") + "-" + mod.Filename) : PathFor(changed);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var moved = File.Exists(original);
        if (moved) File.Move(original, target);
        try { mods.Remove(mod); if (!remove) mods.Add(changed); await SaveAsync(mods); }
        catch { if (moved) File.Move(target, original); throw; }
    }

    public static bool IsBundledClient(string path)
    {
        var name = Path.GetFileName(path).Replace(".disabled", "", StringComparison.OrdinalIgnoreCase).ToLowerInvariant();
        if (name is "plutonium-client-fabric.jar" or "quirk-client-fabric.jar") return true;
        try { return ReadModIds(path).Any(id => id is "quirk" or "plutonium"); }
        catch (InvalidDataException) { return false; }
        catch (System.Text.Json.JsonException) { return false; }
        catch (IOException) { return false; }
    }

    public async Task UninstallFileAsync(string path)
    {
        path = Path.GetFullPath(path);
        if (!string.Equals(Path.GetDirectoryName(path), DirectoryPath, StringComparison.OrdinalIgnoreCase)
            || !(path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Only a mod file inside this profile's mods folder can be uninstalled.");
        if (IsBundledClient(path)) throw new InvalidOperationException("The bundled Plutonium client is managed by the launcher and cannot be uninstalled here.");
        var managed = (await LoadAsync()).FirstOrDefault(m => PathFor(m).Equals(path, StringComparison.OrdinalIgnoreCase));
        if (!File.Exists(path))
        {
            if (managed is not null) { await ChangeAsync(managed.ProjectId, true); return; }
            throw new FileNotFoundException("This mod has already been removed. Refresh Your Mods.");
        }
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) throw new IOException("This mod is a linked file. Manage the link in the Files tab.");
        List<FabricMetadata> ReadMetadata(string file)
        {
            try { return FabricMetadata.Read(file); }
            catch (InvalidDataException) { return []; }
            catch (System.Text.Json.JsonException) { return []; }
        }
        if (path.EndsWith(".jar", StringComparison.OrdinalIgnoreCase))
        {
            var removed = ReadMetadata(path); var removedIds = removed.SelectMany(m => m.Ids).ToHashSet();
            var remaining = Directory.EnumerateFiles(DirectoryPath, "*.jar").Where(f => !f.Equals(path, StringComparison.OrdinalIgnoreCase)).SelectMany(ReadMetadata).ToList();
            foreach (var owner in remaining)
                foreach (var rule in owner.Depends.Where(r => removedIds.Contains(r.Key)))
                    if (!remaining.Any(m => m.Ids.Contains(rule.Key) && FabricVersionRange.Matches(m.Version, rule.Value)))
                        throw new InvalidOperationException($"{Path.GetFileName(path)} is required by {owner.Id}. Uninstall or disable {owner.Id} first.");
        }
        if (managed is not null) { await ChangeAsync(managed.ProjectId, true); return; }
        var recovery = Path.Combine(DirectoryPath, ".removed"); Directory.CreateDirectory(recovery);
        File.Move(path, Path.Combine(recovery, Guid.NewGuid().ToString("N") + "-" + Path.GetFileName(path)));
    }
}
