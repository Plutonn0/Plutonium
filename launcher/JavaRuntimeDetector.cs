using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace PlutoniumLauncher;

public sealed record JavaRuntime(string ExecutablePath, Version Version, string Source)
{
    public string DisplayVersion => $"Java {Version.Major}";
}

public static partial class JavaRuntimeDetector
{
    private static readonly string[] RuntimeRoots =
    [
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Eclipse Adoptium"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Java"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Amazon Corretto"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Zulu"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Eclipse Adoptium"),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plutonium Client", "runtime", "java-21")
    ];

    public static async Task<JavaRuntime?> FindAsync(string? configuredPath, CancellationToken cancellationToken = default)
    {
        foreach (var candidate in GetCandidates(configuredPath))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var runtime = await ProbeAsync(candidate.Path, candidate.Source, cancellationToken);
            if (runtime is not null) return runtime;
        }
        return null;
    }

    public static Task<JavaRuntime?> ProbePathAsync(string executable, CancellationToken cancellationToken = default) =>
        ProbeAsync(executable, "verified runtime", cancellationToken);

    private static IEnumerable<(string Path, string Source)> GetCandidates(string? configuredPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(string Path, string Source)>();
        void Add(string? path, string source)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            if (File.Exists(fullPath) && seen.Add(fullPath)) candidates.Add((fullPath, source));
        }
        Add(configuredPath, "configured");
        var javaHome = Environment.GetEnvironmentVariable("JAVA_HOME");
        if (!string.IsNullOrWhiteSpace(javaHome))
        {
            Add(Path.Combine(javaHome, "bin", "javaw.exe"), "JAVA_HOME");
            Add(Path.Combine(javaHome, "bin", "java.exe"), "JAVA_HOME");
        }
        foreach (var path in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            Add(Path.Combine(path.Trim('"'), "javaw.exe"), "PATH");
            Add(Path.Combine(path.Trim('"'), "java.exe"), "PATH");
        }
        var minecraft = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".minecraft", "runtime");
        AddRuntimeTree(minecraft, "Minecraft runtime", Add, recursive: true);
        foreach (var root in RuntimeRoots) AddRuntimeTree(root, "installed Java", Add, recursive: false);
        foreach (var candidate in candidates) yield return candidate;
    }

    private static void AddRuntimeTree(string root, string source, Action<string?, string> add, bool recursive)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            var roots = recursive ? Directory.EnumerateDirectories(root, "*", SearchOption.AllDirectories) : Directory.EnumerateDirectories(root);
            foreach (var directory in roots.Prepend(root))
            {
                add(Path.Combine(directory, "bin", "javaw.exe"), source);
                add(Path.Combine(directory, "bin", "java.exe"), source);
            }
        }
        catch (UnauthorizedAccessException) { }
        catch (IOException) { }
    }

    private static async Task<JavaRuntime?> ProbeAsync(string executable, string source, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(executable, "-version")
                {
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                }
            };
            if (!process.Start()) return null;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
                return null;
            }
            var output = await stdout + " " + await stderr;
            var match = VersionPattern().Match(output);
            if (!match.Success || !Version.TryParse(match.Groups["version"].Value, out var version) || version.Major != 21) return null;
            return new JavaRuntime(executable, version, source);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception) { return null; }
    }

    [GeneratedRegex("(?:version\\s+)?[\\\"'](?<version>(?:1\\.)?\\d+(?:\\.\\d+)*(?:_[0-9]+)?)", RegexOptions.IgnoreCase)]
    private static partial Regex VersionPattern();
}
