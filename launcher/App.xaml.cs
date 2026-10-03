using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace PlutoniumLauncher;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            if(e.Args.Contains("--uninstall")) { AppInstaller.Uninstall(); Shutdown(); return; }
            if(AppInstaller.EnsureInstalled()) { Shutdown(); return; }
        }
        catch(Exception ex)
        {
            MessageBox.Show("Plutonium could not install. Close any older Plutonium launcher and try again.\n\n" + ErrorReport.Redact(ex.Message), "Installation needs attention", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(); return;
        }
        base.OnStartup(e);
    }
	public App()
	{
		DispatcherUnhandledException += (_, args) =>
		{
			WriteCrashLog(args.Exception);
			args.Handled = false;
		};
		AppDomain.CurrentDomain.UnhandledException += (_, args) =>
			WriteCrashLog(args.ExceptionObject as Exception ?? new Exception(args.ExceptionObject?.ToString()));
	}

	private static void WriteCrashLog(Exception exception)
	{
		try
		{
			var root = Environment.GetEnvironmentVariable("PLUTONIUM_LAUNCHER_HOME")
				?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Plutonium Client");
			Directory.CreateDirectory(root);
			File.AppendAllText(Path.Combine(root, "launcher-crash.log"), ErrorReport.Redact($"{DateTimeOffset.UtcNow:O}{Environment.NewLine}{exception}{Environment.NewLine}"));
		}
		catch { }
	}
}
