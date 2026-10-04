using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace PlutoniumLauncher;

public sealed record CrashFinding(string Title, string Confidence, string Explanation, string NextStep, string Evidence);
public sealed record CrashAnalysis(List<CrashFinding> Findings, string Sources)
{
    public string Summary => string.Join("\n\n", Findings.Select(f => $"{f.Title} ({f.Confidence})\n{f.Explanation}\nNext: {f.NextStep}"));
    public string Report => ErrorReport.Redact(Summary + "\n\nSources: " + Sources + "\n\n" + string.Join("\n\n", Findings.Select(f => f.Evidence)));
}

public static class CrashDiagnosis
{
    public static CrashAnalysis Analyze(string text, string sources = "Minecraft output")
    {
        text = ErrorReport.Redact(text);
        var lines = text.Split('\n'); var findings = new List<CrashFinding>();
        void Match(string pattern, string title, string confidence, string explanation, string next)
        {
            var evidence = lines.Where(l => Regex.IsMatch(l, pattern, RegexOptions.IgnoreCase)).Take(4).Select(l => l[..Math.Min(l.Length, 500)]).ToArray();
            if (evidence.Length > 0) findings.Add(new(title, confidence, explanation, next, string.Join("\n", evidence)));
        }
        Match("OutOfMemoryError|Could not reserve enough space|Native memory allocation.*failed", "Not enough memory", "Strong evidence",
            "Java reported a memory allocation failure.", "Close memory-heavy apps. For Java heap errors, increase the custom memory slider in Game settings within your available RAM.");
        Match("Incompatible mods found|Mod resolution encountered|requires version|depends on.*missing|incompatible mod set", "Mod dependency conflict", "Strong evidence",
            "Fabric could not satisfy the installed mods' requirements.", "Open Mods and reinstall or update the affected mod. The compatibility planner can select another version when the requirements are available.");
        Match("Mixin apply.*failed|MixinTransformerError|InvalidMixinException|InjectionError", "Mod injection failed", "Likely cause",
            "A mod could not apply a change to Minecraft. This can be a version mismatch or a conflict between mods.", "Use the mod or mixin name in the evidence to locate the affected mod. Update it or temporarily disable it in Mods, then retry.");
        Match("GLFW error 65542|does not support OpenGL|Failed to create.*OpenGL|EXCEPTION_ACCESS_VIOLATION.*(?:nvoglv|atio|ig[0-9])|Problematic frame:.*(?:nvoglv|atio|ig[0-9])", "Graphics initialization failed", "Likely cause",
            "The output points to an OpenGL or graphics-driver problem.", "Update the graphics driver from your GPU manufacturer and test without shader or rendering mods.");
        Match("UnsupportedClassVersionError|compiled by a more recent version", "Wrong Java version", "Strong evidence",
            "A loaded class requires a different Java version.", "Run Settings → Installation health. Minecraft 1.21.11 uses Java 21; remove mods built for a newer unsupported runtime.");
        Match("ZipException|zip END header not found|Invalid or corrupt jarfile", "Damaged archive", "Strong evidence",
            "Java could not read a jar or zip file.", "Use the path in the evidence to identify the file. Repair that mod from Mods, or run Installation health for client files.");
        Match("NoClassDefFoundError|ClassNotFoundException|NoSuchMethodError", "Missing or incompatible code", "Possible cause",
            "A required class or method could not be found. A dependency may be missing or on the wrong version.", "Check the named mod and its dependencies in Mods. Review other findings first if an earlier error caused this one.");
        if (findings.Count == 0) findings.Add(new("No clear cause found", "Uncertain", "The available output does not match a known failure pattern.",
            "Run Installation health, then copy or send the report for investigation. No mods have been disabled automatically.", text[^Math.Min(text.Length, 3000)..]));
        return new(findings, sources);
    }

    public static async Task<CrashAnalysis> ReadAsync(string gameDirectory, string output, DateTime? since = null)
    {
        var text = new StringBuilder(output); var sources = new List<string> { "Recent process output" };
        var candidates = new List<string> { Path.Combine(gameDirectory, "logs", "latest.log") };
        try
        {
            var crashes = Path.Combine(gameDirectory, "crash-reports");
            if (Directory.Exists(crashes)) candidates.AddRange(Directory.EnumerateFiles(crashes, "*.txt").OrderByDescending(File.GetLastWriteTimeUtc).Take(1));
            if (Directory.Exists(gameDirectory)) candidates.AddRange(Directory.EnumerateFiles(gameDirectory, "hs_err_pid*.log").OrderByDescending(File.GetLastWriteTimeUtc).Take(1));
            foreach (var path in candidates)
            {
                if (!File.Exists(path) || (since.HasValue && File.GetLastWriteTimeUtc(path) < since.Value.AddSeconds(-2))) continue;
                try
                {
                    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    stream.Seek(Math.Max(0, stream.Length - 256 * 1024), SeekOrigin.Begin);
                    using var reader = new StreamReader(stream);
                    text.AppendLine().AppendLine(await reader.ReadToEndAsync()); sources.Add(Path.GetFileName(path));
                }
                catch (IOException) { sources.Add(Path.GetFileName(path) + " (unreadable)"); }
                catch (UnauthorizedAccessException) { sources.Add(Path.GetFileName(path) + " (access denied)"); }
            }
        }
        catch (IOException) { sources.Add("Some log folders were unavailable"); }
        catch (UnauthorizedAccessException) { sources.Add("Some log folders were inaccessible"); }
        return Analyze(text.ToString(), string.Join(", ", sources));
    }
}
