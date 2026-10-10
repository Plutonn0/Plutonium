using System.Diagnostics;
using System.IO;

namespace PlutoniumLauncher;

/// <summary>
/// Removes disposable staging files without hiding the operation's original failure.
/// Call only for temporary paths, never for rollback/recovery copies that must be retained.
/// </summary>
internal static class TemporaryCleanup
{
    internal static void DeleteFile(string path) => Run(() =>
    {
        if (File.Exists(path))
            File.Delete(path);
    });

    internal static void DeleteDirectory(string path) => Run(() =>
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    });

    private static void Run(Action cleanup)
    {
        try
        {
            cleanup();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            // A locked staging file must not replace a cancellation, checksum error or rollback failure.
            // Keep paths and exception messages out of diagnostics: they can contain private data.
            Trace.TraceWarning("Plutonium temporary cleanup failed ({0}); the temporary item was left in place.", error.GetType().Name);
        }
    }
}
