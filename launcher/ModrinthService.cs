using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PlutoniumLauncher;

public sealed record ModSearch(List<ModProject> Hits, int TotalHits);
public sealed record ModProject(string ProjectId, string Slug, string Title, string Description, string Author,
    long Downloads, string? IconUrl, string? ClientSide);
public sealed record ModFile(string Url, string Filename, bool Primary, long Size, Dictionary<string, string> Hashes, string? FileType);
public sealed record ModDependency(string? ProjectId, string? VersionId, string DependencyType);
public sealed record ModVersion(string Id, string ProjectId, string Name, string VersionNumber, string VersionType,
    DateTimeOffset DatePublished, List<string> GameVersions, List<string> Loaders, List<ModFile> Files, List<ModDependency> Dependencies);
public sealed record ModPlanEntry(string Title, ModVersion Version, ModFile File);
public sealed record ModProjectDetails(string Id, string Slug, string Title, string Description, string Body, long Downloads, List<string> Categories, ModLicense License, List<ModGalleryImage> Gallery);
public sealed record ModLicense(string Id, string Name);
public sealed record ModGalleryImage(string Url, bool Featured, string? Title, string? Description);

public sealed class ModrinthService(HttpClient? client = null)
{
    public const string MinecraftVersion = "1.21.11";
    private readonly HttpClient _http = client ?? new() { Timeout = TimeSpan.FromSeconds(30) };
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, PropertyNameCaseInsensitive = true };
    private async Task<T> GetAsync<T>(string path, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.modrinth.com/v2/" + path);
        request.Headers.UserAgent.ParseAdd($"PlutoniumLauncher/{typeof(ModrinthService).Assembly.GetName().Version?.ToString(3)} (https://github.com/Plutonn0/Plutonium)");
        using var response = await _http.SendAsync(request, token);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("Modrinth is rate limiting requests. Please wait a minute and try again.");
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(token);
        return await JsonSerializer.DeserializeAsync<T>(stream, Json, token) ?? throw new InvalidDataException("Modrinth returned an empty response.");
    }
    public Task<ModSearch> SearchAsync(string query, string sort, string category, int offset, CancellationToken token)
    {
        var facets = new List<string[]> { new[] { "project_type:mod" }, new[] { "categories:fabric" }, new[] { "versions:" + MinecraftVersion }, new[] { "client_side:required", "client_side:optional" } };
        if (!string.IsNullOrEmpty(category)) facets.Add(["categories:" + category]);
        return GetAsync<ModSearch>($"search?query={Uri.EscapeDataString(query)}&index={Uri.EscapeDataString(sort)}&offset={offset}&limit=20&facets={Uri.EscapeDataString(JsonSerializer.Serialize(facets))}", token);
    }
    public Task<List<ModVersion>> VersionsAsync(string project, CancellationToken token) => GetAsync<List<ModVersion>>(
        $"project/{Uri.EscapeDataString(project)}/version?loaders=%5B%22fabric%22%5D&game_versions=%5B%221.21.11%22%5D&include_changelog=false", token);
    public Task<ModVersion> VersionAsync(string id, CancellationToken token) => GetAsync<ModVersion>("version/" + Uri.EscapeDataString(id), token);
    public Task<ModProjectDetails> ProjectAsync(string id, CancellationToken token) => GetAsync<ModProjectDetails>("project/" + Uri.EscapeDataString(id), token);
    public async Task<Dictionary<string, ModVersion>> IdentifyAsync(IEnumerable<string> hashes, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.modrinth.com/v2/version_files")
        { Content = JsonContent.Create(new { hashes = hashes.ToArray(), algorithm = "sha512" }) };
        request.Headers.UserAgent.ParseAdd($"PlutoniumLauncher/{typeof(ModrinthService).Assembly.GetName().Version?.ToString(3)} (https://github.com/Plutonn0/Plutonium)");
        using var response = await _http.SendAsync(request, token); response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<Dictionary<string, ModVersion>>(Json, token) ?? [];
    }
    public static bool Compatible(ModVersion version) => version.GameVersions.Contains(MinecraftVersion) && version.Loaders.Contains("fabric");
    public static ModVersion SelectVersion(IEnumerable<ModVersion> versions, bool previews) => versions
        .Where(v => Compatible(v) && (previews || v.VersionType == "release"))
        .OrderByDescending(v => v.DatePublished).FirstOrDefault() ?? throw new InvalidDataException("No compatible version in this release channel. Enable beta/alpha versions to include previews.");
    public static ModFile SelectFile(ModVersion version)
    {
        var file = version.Files.Where(f => f.Filename.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) && f.FileType is null or "unknown")
            .OrderByDescending(f => f.Primary).FirstOrDefault() ?? throw new InvalidDataException("This version has no installable mod jar.");
        if (file.Filename != Path.GetFileName(file.Filename) || file.Filename.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file.Filename.Contains('\\') || file.Filename.Contains('/'))
            throw new InvalidDataException("Modrinth supplied an unsafe filename.");
        if (!Uri.TryCreate(file.Url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "cdn.modrinth.com")
            throw new InvalidDataException("Mod downloads must come from Modrinth's HTTPS CDN.");
        if (!file.Hashes.TryGetValue("sha512", out var hash) || hash.Length != 128 || !hash.All(Uri.IsHexDigit))
            throw new InvalidDataException("This mod is missing a valid SHA-512 checksum.");
        return file;
    }
    public async Task<List<ModPlanEntry>> PlanAsync(string project, string title, bool previews, CancellationToken token)
    {
        var selected = new Dictionary<string, ModPlanEntry>();
        async Task Visit(ModVersion version, string name)
        {
            if (!Compatible(version)) throw new InvalidDataException($"{name} requires a dependency that does not support Fabric {MinecraftVersion}.");
            if (selected.TryGetValue(version.ProjectId, out var prior))
            { if (prior.Version.Id != version.Id) throw new InvalidDataException("Conflicting required dependency versions: " + name); return; }
            if (selected.Count >= 64) throw new InvalidDataException("Dependency tree exceeds 64 projects.");
            selected.Add(version.ProjectId, new(name, version, SelectFile(version)));
            foreach (var dependency in version.Dependencies.Where(d => d.DependencyType == "required"))
            {
                ModVersion next;
                if (dependency.VersionId is not null) next = await VersionAsync(dependency.VersionId, token);
                else if (dependency.ProjectId is not null) next = SelectVersion(await VersionsAsync(dependency.ProjectId, token), previews);
                else throw new InvalidDataException("A required dependency cannot be resolved automatically.");
                await Visit(next, next.Name);
            }
        }
        await Visit(SelectVersion(await VersionsAsync(project, token), previews), title);
        foreach (var entry in selected.Values)
            foreach (var dependency in entry.Version.Dependencies.Where(d => d.DependencyType == "incompatible"))
                if (selected.Values.Any(p => (dependency.ProjectId == p.Version.ProjectId && dependency.VersionId is null) || dependency.VersionId == p.Version.Id))
                    throw new InvalidDataException("This dependency plan contains incompatible mods.");
        return selected.Values.Reverse().ToList();
    }
}
