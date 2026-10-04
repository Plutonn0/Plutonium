using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PlutoniumLauncher;

public sealed record FabricMetadata(string Id, string Version, List<string> Provides, Dictionary<string, string[]> Depends, Dictionary<string, string[]> Breaks)
{
    public IEnumerable<string> Ids => Provides.Prepend(Id).Distinct();
    public static List<FabricMetadata> Read(string path)
    {
        using var stream = File.OpenRead(path); var result = new List<FabricMetadata>();
        ReadArchive(stream, result, 0); return result;
    }
    private static void ReadArchive(Stream stream, List<FabricMetadata> result, int depth)
    {
        if (depth > 8 || result.Count > 256) throw new InvalidDataException("Nested mod metadata exceeds the inspection limit.");
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, true);
        var entry = archive.GetEntry("fabric.mod.json"); if (entry is null) return;
        if (entry.Length > 1024 * 1024) throw new InvalidDataException("Mod metadata is too large.");
        using var jsonStream = entry.Open(); using var json = JsonDocument.Parse(jsonStream); var root = json.RootElement;
        if (root.TryGetProperty("environment", out var environment) && environment.ValueKind == JsonValueKind.String && environment.GetString() == "server") return;
        Dictionary<string, string[]> Rules(string key) => root.TryGetProperty(key, out var rules)
            ? rules.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.ValueKind == JsonValueKind.Array ? p.Value.EnumerateArray().Select(v => v.GetString()!).ToArray() : [p.Value.GetString()!]) : [];
        result.Add(new(root.GetProperty("id").GetString()!, root.GetProperty("version").GetString()!,
            root.TryGetProperty("provides", out var aliases) ? aliases.EnumerateArray().Select(v => v.GetString()!).ToList() : [], Rules("depends"), Rules("breaks")));
        if (!root.TryGetProperty("jars", out var jars)) return;
        foreach (var nested in jars.EnumerateArray())
        {
            var jar = archive.GetEntry(nested.GetProperty("file").GetString()!); if (jar is null) continue;
            if (jar.Length > 32 * 1024 * 1024) throw new InvalidDataException("A nested mod is too large to inspect safely.");
            using var data = new MemoryStream(); using (var source = jar.Open()) source.CopyTo(data);
            data.Position = 0; ReadArchive(data, result, depth + 1);
        }
    }
}

// Fabric's version predicates differ from npm: ^0.x still permits the entire major version.
public static class FabricVersionRange
{
    private sealed record Version(BigInteger[] Parts, string? Pre);
    private static Version? Parse(string value)
    {
        var match = Regex.Match(value, @"^(\d+(?:\.\d+)*)(?:-([^+]*))?(?:\+.*)?$");
        return !match.Success ? null : new(match.Groups[1].Value.Split('.').Select(BigInteger.Parse).ToArray(), match.Groups[2].Success ? match.Groups[2].Value : null);
    }
    private static int Compare(Version a, Version b)
    {
        for (var i = 0; i < Math.Max(a.Parts.Length, b.Parts.Length); i++)
        { var comparison = (i < a.Parts.Length ? a.Parts[i] : 0).CompareTo(i < b.Parts.Length ? b.Parts[i] : 0); if (comparison != 0) return comparison; }
        if (a.Pre is null || b.Pre is null) return a.Pre == b.Pre ? 0 : a.Pre is null ? 1 : -1;
        if (a.Pre.Length == 0 || b.Pre.Length == 0) return a.Pre == b.Pre ? 0 : a.Pre.Length == 0 ? -1 : 1;
        var left = a.Pre.Split('.'); var right = b.Pre.Split('.');
        for (var i = 0; i < Math.Min(left.Length, right.Length); i++)
        {
            var ln = BigInteger.TryParse(left[i], out var l); var rn = BigInteger.TryParse(right[i], out var r);
            var comparison = ln && rn ? l.CompareTo(r) : ln != rn ? ln ? -1 : 1 : string.CompareOrdinal(left[i], right[i]);
            if (comparison != 0) return comparison;
        }
        return left.Length.CompareTo(right.Length);
    }
    public static bool Matches(string version, IEnumerable<string> alternatives) => alternatives.Any(range => range.Split(' ', StringSplitOptions.RemoveEmptyEntries).All(term => Term(version, term)));
    private static bool Term(string value, string term)
    {
        if (term is "*" or "x" or "X") return true;
        var match = Regex.Match(term, @"^(>=|<=|>|<|=|\^|~)?(.+)$"); if (!match.Success) return false;
        var op = match.Groups[1].Value; var target = match.Groups[2].Value;
        var actual = Parse(value); var expected = Parse(target);
        if (Regex.IsMatch(target, @"^\d+(?:\.\d+)*\.[xX*](?:\.[xX*])*$") && op is "" or "=")
        {
            if (actual is null) return false;
            var prefix = target.Split('.').TakeWhile(p => p is not ("x" or "X" or "*")).Select(BigInteger.Parse).ToArray();
            return prefix.Select((n, i) => n == (i < actual.Parts.Length ? actual.Parts[i] : 0)).All(v => v);
        }
        if (actual is null || expected is null) return op is "" or "=" && value == target;
        var comparison = Compare(actual, expected);
        if (op is "^" or "~")
        {
            var index = op == "^" ? 0 : 1; var parts = new BigInteger[index + 1];
            for (var i = 0; i <= index; i++) parts[i] = i < expected.Parts.Length ? expected.Parts[i] : 0;
            parts[index]++;
            return comparison >= 0 && Compare(actual, new(parts, "")) < 0;
        }
        return op switch { "" or "=" => comparison == 0, ">" => comparison > 0, ">=" => comparison >= 0, "<" => comparison < 0, "<=" => comparison <= 0, _ => false };
    }
}
