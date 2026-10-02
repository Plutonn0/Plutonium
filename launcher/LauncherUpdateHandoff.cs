using System.Diagnostics;
using System.IO;
using System.Text;

namespace PlutoniumLauncher;

public static class LauncherUpdateHandoff
{
    // No cmd interpolation: Unicode, apostrophes and percent signs in paths remain literal.
    public static string WriteScript(string directory)
    {
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, "apply-launcher-update.ps1");
        File.WriteAllText(scriptPath, """
            param([string]$Staged, [string]$Target, [int]$LauncherPid, [switch]$NoRestart)
            $ErrorActionPreference = 'Stop'
            $next = $Target + '.next-' + [Guid]::NewGuid().ToString('N')
            $backup = $Target + '.previous'
            $result = Join-Path $PSScriptRoot 'update-result.txt'
            try {
                if ($LauncherPid -gt 0) {
                    $launcher = Get-Process -Id $LauncherPid -ErrorAction SilentlyContinue
                    if ($launcher) { $launcher.WaitForExit(90000) | Out-Null; if (-not $launcher.HasExited) { throw 'Launcher did not close.' } }
                }
                Copy-Item -LiteralPath $Staged -Destination $next
                $replaced = $false
                for ($attempt = 0; $attempt -lt 30; $attempt++) {
                    try {
                        if (Test-Path -LiteralPath $Target) { [IO.File]::Replace($next, $Target, $backup) }
                        else { [IO.File]::Move($next, $Target) }
                        $replaced = $true
                        break
                    } catch [IO.IOException] { Start-Sleep -Milliseconds 500 }
                }
                if (-not $replaced) { throw 'Launcher is still locked. The existing executable has been preserved.' }
                [IO.File]::WriteAllText($result, 'PASS: Launcher update installed. Previous executable saved beside it.')
                if (-not $NoRestart) { Start-Process -FilePath $Target -WorkingDirectory (Split-Path -Parent $Target) }
            } catch {
                [IO.File]::WriteAllText($result, 'FAIL: ' + $_.Exception.Message)
                exit 1
            } finally {
                if (Test-Path -LiteralPath $next) { Remove-Item -LiteralPath $next }
            }
            """, new UTF8Encoding(true));
        return scriptPath;
    }

    public static void Start(string staged, string executable)
    {
        var script = WriteScript(Path.GetDirectoryName(staged)!);
        var start = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script,
                     "-Staged", Path.GetFullPath(staged), "-Target", Path.GetFullPath(executable),
                     "-LauncherPid", Environment.ProcessId.ToString() }) start.ArgumentList.Add(argument);
        _ = Process.Start(start) ?? throw new IOException("The launcher update helper could not start.");
    }
}
