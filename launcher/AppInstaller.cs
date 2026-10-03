using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace PlutoniumLauncher;

public static class AppInstaller
{
    public static string InstallDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Plutonium");
    public static string InstalledExecutable => Path.Combine(InstallDirectory, "Plutonium Client.exe");
    public static string Shortcut => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Plutonium.lnk");

    public static bool EnsureInstalled()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLUTONIUM_LAUNCHER_HOME")) || Environment.GetCommandLineArgs().Any(a => a.StartsWith("--smoke"))) return false;
        var source = Environment.ProcessPath!;
        // Development builds run beside runtimeconfig.json; downloaded single-file builds may have any filename.
        if (string.Equals(Path.GetFileName(source), "dotnet.exe", StringComparison.OrdinalIgnoreCase)
            || File.Exists(Path.ChangeExtension(source, ".runtimeconfig.json"))) return false;
        Directory.CreateDirectory(InstallDirectory);
        if (!string.Equals(Path.GetFullPath(source), InstalledExecutable, StringComparison.OrdinalIgnoreCase))
        {
            if (File.Exists(InstalledExecutable)
                && Version.TryParse(FileVersionInfo.GetVersionInfo(InstalledExecutable).FileVersion, out var installedVersion)
                && installedVersion > Assembly.GetExecutingAssembly().GetName().Version)
            {
                Process.Start(new ProcessStartInfo(InstalledExecutable) { UseShellExecute = true, WorkingDirectory = InstallDirectory });
                return true;
            }
            var pending = InstalledExecutable + ".installing";
            try { File.Copy(source, pending, true); File.Move(pending, InstalledExecutable, true); }
            finally { if (File.Exists(pending)) File.Delete(pending); }
            Register();
            Process.Start(new ProcessStartInfo(InstalledExecutable) { UseShellExecute = true, WorkingDirectory = InstallDirectory });
            return true;
        }
        Register();
        return false;
    }

    private static void Register()
    {
        CreateShortcut(Shortcut);
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\Plutonium");
        key.SetValue("DisplayName", "Plutonium Client");
        key.SetValue("DisplayVersion", Assembly.GetExecutingAssembly().GetName().Version!.ToString(3));
        key.SetValue("Publisher", "Plutonium");
        key.SetValue("InstallLocation", InstallDirectory);
        key.SetValue("DisplayIcon", InstalledExecutable + ",0");
        key.SetValue("URLInfoAbout", "https://plutonium-lime.vercel.app/");
        key.SetValue("UninstallString", $"\"{InstalledExecutable}\" --uninstall");
        key.SetValue("NoModify", 1); key.SetValue("NoRepair", 1);
    }

    public static void CreateShortcut(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        dynamic shortcut = shell.CreateShortcut(path);
        try { shortcut.TargetPath = InstalledExecutable; shortcut.WorkingDirectory = InstallDirectory; shortcut.IconLocation = InstalledExecutable + ",0"; shortcut.Description = "Plutonium Minecraft client"; shortcut.Save(); }
        finally { Marshal.FinalReleaseComObject(shortcut); Marshal.FinalReleaseComObject(shell); }
    }

    public static void Uninstall()
    {
        // Remove only the registered application. Account data and all Minecraft saves stay intact.
        var script = Path.Combine(Path.GetTempPath(), "plutonium-uninstall-" + Guid.NewGuid().ToString("N") + ".ps1");
        File.WriteAllText(script, """
            param([int]$LauncherPid)
            $ErrorActionPreference='Stop'
            $process=Get-Process -Id $LauncherPid -ErrorAction SilentlyContinue
            if($process){$process.WaitForExit(30000)|Out-Null; if(-not $process.HasExited){exit 1}}
            $root=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs\Plutonium'))
            $allowed=[IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs')) + '\'
            if(-not $root.StartsWith($allowed,[StringComparison]::OrdinalIgnoreCase)){throw 'Invalid install path'}
            $shortcut=Join-Path ([Environment]::GetFolderPath('Programs')) 'Plutonium.lnk'
            if(Test-Path -LiteralPath $shortcut){Remove-Item -LiteralPath $shortcut}
            if(Test-Path -LiteralPath $root){Remove-Item -LiteralPath $root -Recurse -Force}
            $key='HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Plutonium'
            if(Test-Path -LiteralPath $key){Remove-Item -LiteralPath $key}
            Remove-Item -LiteralPath $PSCommandPath
            """, new UTF8Encoding(true));
        var info = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
        foreach (var arg in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-LauncherPid", Environment.ProcessId.ToString() }) info.ArgumentList.Add(arg);
        Process.Start(info);
    }
}
