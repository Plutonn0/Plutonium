using System.Windows;
using System.Windows.Threading;
using System.IO;

namespace PlutoniumLauncher;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        SplashWindow? splash = null;
        try
        {
            if(e.Args.Contains("--uninstall")) { AppInstaller.Uninstall(); Shutdown(); return; }
            var smoke = e.Args.Any(a => a.StartsWith("--smoke")) && !e.Args.Contains("--smoke-startup");
            if (!smoke)
            {
                splash = new SplashWindow(); splash.Show();
                await Dispatcher.Yield(DispatcherPriority.Background);
            }
            // Keep file copying and Windows registration off the animation dispatcher.
            if(await InstallAsync()) { Shutdown(); return; }
            if (Dispatcher.HasShutdownStarted) return;
            var window = new MainWindow(); MainWindow = window;
            window.Closed += (_, _) => Shutdown();
            if (smoke) { window.Show(); return; }
            Action<string> progress = text => splash?.SetStatus(text);
            window.StartupProgress += progress;
            await window.InitializeForStartupAsync();
            window.StartupProgress -= progress;
            if (Dispatcher.HasShutdownStarted) return;
            window.Show(); splash?.Close();
            if (SystemParameters.ClientAreaAnimation)
                window.BeginAnimation(UIElement.OpacityProperty, new System.Windows.Media.Animation.DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)));
            if (e.Args.Contains("--smoke-startup"))
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                var home = Environment.GetEnvironmentVariable("PLUTONIUM_LAUNCHER_HOME");
                if (home is not null) await File.AppendAllTextAsync(Path.Combine(home, "smoke-result.txt"), $"SplashClosed={splash is not null && !splash.IsVisible}\nDashboardVisible={window.IsVisible}\n");
                window.Close();
            }
        }
        catch(Exception ex)
        {
            MessageBox.Show("Plutonium could not install. Close any older Plutonium launcher and try again.\n\n" + ErrorReport.Redact(ex.Message), "Installation needs attention", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(); return;
        }
    }
    private static Task<bool> InstallAsync()
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            try { completed.SetResult(AppInstaller.EnsureInstalled()); }
            catch (Exception ex) { completed.SetException(ex); }
        }) { IsBackground = true, Name = "Plutonium installation" };
        worker.SetApartmentState(ApartmentState.STA); worker.Start(); return completed.Task;
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
