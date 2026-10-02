using CmlLib.Core.Auth;
using Microsoft.Win32;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Media;

namespace PlutoniumLauncher;

public partial class MainWindow : Window
{
    private readonly BootstrapService _bootstrap = new();
    private readonly MinecraftLaunchService _game = new();
    private readonly UpdateService _updates = new();
    private LauncherConfig? _config;
    private InstallationInfo? _installation;
    private JavaRuntime? _java;
    private MSession? _session;
    private Process? _gameProcess;
    private bool _fabricSelected;
    private bool _busy;
    private CancellationTokenSource? _operation;
    private AccountService? _accounts;

    public MainWindow() => InitializeComponent();

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await InitializeAsync();

    private async Task InitializeAsync()
    {
        SetBusy(true);
        SetStage("CHECKING", "Checking this PC for Plutonium and Java 21");
        try
        {
            _config = await LauncherConfig.LoadAsync();
            _accounts = new AccountService(_config.DataDirectory);
            RefreshAccounts();
            _installation = InstallationDiscovery.Detect(_config);
            _config.MinecraftDirectory = _installation.MinecraftDirectory;
            _config.StandaloneGameDirectory = _installation.StandaloneGameDirectory;
            MinecraftPath.Text = _installation.MinecraftDirectory;
            SelectProfile(_config.SelectedProfile == "fabric", persist: false);
            await _bootstrap.InstallOrRepairAsync(_config, new Progress<string>(SetInstallProgress));
            await EnsureJavaAsync();
            await _config.SaveAsync();
            _installation = InstallationDiscovery.Detect(_config);
            InstallStatus.Text = _installation.HasLegacyQuirkSettings
                ? "Legacy Quirk settings and worlds detected and preserved."
                : "Plutonium files verified. Existing worlds, profiles, and mods are untouched.";
            InstallIndicator.Fill = (Brush)FindResource("GreenBrush");
            await RefreshUpdateStatusAsync();
            SetStage("READY", "Ready to play");
            var smokeProfile = GetSmokeLaunchProfile();
            if (smokeProfile is not null)
            {
                await RunOfflineLaunchSmokeAsync(smokeProfile == "fabric");
                Close();
                return;
            }
            if (IsSmokeTestRun)
            {
                var installation = _installation ?? throw new InvalidOperationException("Install discovery did not complete.");
                var java = _java ?? throw new InvalidOperationException("Java detection did not complete.");
                var resultPath = Path.Combine(_config.DataDirectory, "smoke-result.txt");
                await File.WriteAllTextAsync(resultPath, $"PASS\nMinecraft={installation.MinecraftDirectory}\nJava={java.DisplayVersion}\nStandalone={installation.HasStandaloneFiles}\nFabric={installation.HasFabricFiles}\n");
                Close();
            }
        }
        catch (Exception ex)
        {
            InstallStatus.Text = ex.Message;
            InstallIndicator.Fill = (Brush)FindResource("RedBrush");
            SetStage("ACTION NEEDED", "Setup could not finish");
            if (IsSmokeTestRun && _config is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(_config.DataDirectory, "smoke-result.txt"), "FAIL\n" + ex);
                Close();
            }
        }
        finally
        {
            SetBusy(false);
            if (_java is null) PlayButton.IsEnabled = false;
        }
    }

    private async Task EnsureJavaAsync(CancellationToken cancellationToken = default)
    {
        if (_config is null) return;
        _java = Environment.GetEnvironmentVariable("PLUTONIUM_FORCE_JAVA_INSTALL") == "1"
            ? null
            : await Task.Run(() => JavaRuntimeDetector.FindAsync(_config.JavaPath, cancellationToken), cancellationToken);
        if (_java is null)
        {
            SetStage("SETTING UP", "Installing a verified Java 21 runtime");
            Progress.Visibility = Visibility.Visible;
            var percent = new Progress<double>(value => Progress.Value = value * 100);
            _java = await new JavaRuntimeInstaller(dataDirectory: _config.DataDirectory).InstallAsync(percent, cancellationToken);
        }
        _config.JavaPath = _java.ExecutablePath;
        JavaVersion.Text = _java.DisplayVersion;
        await _config.SaveAsync(cancellationToken);
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        RefreshAccounts();
        AccountsOverlay.Visibility = Visibility.Visible;
    }

    private async void Play_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        if (_config is null) throw new InvalidOperationException("Launcher settings have not loaded.");
        SetStage("CHECKING", "Verifying Plutonium and game files");
        _installation = await _bootstrap.InstallOrRepairAsync(_config, new Progress<string>(SetInstallProgress), OperationToken);
        await EnsureJavaAsync(OperationToken);

        if (_accounts is null) throw new InvalidOperationException("Accounts have not loaded.");
        if (string.IsNullOrEmpty(_config.SelectedAccountId))
        {
            SetStage("SIGNING IN", "Continue securely in your browser");
            var signedIn = await _accounts.AddAsync(OperationToken);
            _config.SelectedAccountId = signedIn.Account.Id;
            _session = signedIn.Session;
            await _config.SaveAsync(OperationToken);
        }
        else
        {
            SetStage("SIGNING IN", "Refreshing your Minecraft session");
            _session = await _accounts.AuthenticateAsync(_config.SelectedAccountId, OperationToken);
        }
        ShowSignedInAccount(_session);

        SetStage("UPDATING", "Checking configured Plutonium updates");
        await RefreshUpdateStatusAsync();
        SetStage("LAUNCHING", "Preparing Minecraft 1.21.11");
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        var gameProgress = new Progress<string>(SetInstallProgress);
        var byteProgress = new Progress<double>(value => Progress.Value = value * 100);
        _gameProcess = await _game.PrepareAndLaunchAsync(_config, _java!, _session, _fabricSelected,
            gameProgress, byteProgress, OperationToken);
        MonitorGameProcess(_gameProcess);
        SetStage("PLAYING", "Minecraft is running");
        PlayButton.Content = "PLAYING";
    });

    private void ShowSignedInAccount(MSession session)
    {
        AccountName.Text = session.Username;
        AccountMark.Text = string.IsNullOrWhiteSpace(session.Username) ? "P" : session.Username[..1].ToUpperInvariant();
        AccountState.Text = "Microsoft account connected";
        AccountButton.Content = "ACCOUNT";
    }

    private async void Repair_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        if (_config is null) return;
        SetStage("REPAIRING", "Verifying and repairing Plutonium files");
        _installation = await _bootstrap.InstallOrRepairAsync(_config, new Progress<string>(SetInstallProgress), OperationToken);
        await EnsureJavaAsync(OperationToken);
        await RefreshUpdateStatusAsync(applyUpdates: true);
        InstallStatus.Text = "Plutonium files repaired and verified.";
        InstallIndicator.Fill = (Brush)FindResource("GreenBrush");
        SetStage("READY", "Repair complete");
    });

    private async void Updates_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        UpdatesOverlay.Visibility = Visibility.Visible;
        SetStage("CHECKING", "Checking launcher and client release feeds");
        await RefreshUpdateStatusAsync();
        if (_gameProcess is null) SetStage("READY", "Update check complete");
    });

    private void MonitorGameProcess(Process process)
    {
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) => Dispatcher.InvokeAsync(() =>
        {
            if (!IsLoaded) return;
            _gameProcess = null;
            SetBusy(false);
            PlayButton.Content = "PLAY";
            PlayButton.IsEnabled = _java is not null;
            Progress.Visibility = Visibility.Collapsed;
            SetStage("READY", "Minecraft closed");
        });
        if (process.HasExited)
        {
            _gameProcess = null;
            SetBusy(false);
            PlayButton.Content = "PLAY";
            PlayButton.IsEnabled = _java is not null;
            SetStage("READY", "Minecraft closed");
        }
    }

    private async Task RunOperationAsync(Func<Task> operation)
    {
        if (_busy || _gameProcess is not null) return;
        _operation = new CancellationTokenSource();
        SetBusy(true);
        try
        {
            await operation();
        }
        catch (OperationCanceledException)
        {
            SetStage("READY", "Operation cancelled");
        }
        catch (Exception ex)
        {
            InstallStatus.Text = ex.Message;
            SetStage("ERROR", ex.Message);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false);
            if (_gameProcess is not null && !_gameProcess.HasExited)
            {
                PlayButton.Content = "PLAYING";
                PlayButton.IsEnabled = false;
            }
            else if (_java is not null)
            {
                PlayButton.Content = "PLAY";
                PlayButton.IsEnabled = true;
            }
            Progress.Visibility = Visibility.Collapsed;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        AccountButton.IsEnabled = !busy;
        UpdateButton.IsEnabled = !busy;
        PlayButton.IsEnabled = !busy && _java is not null && _gameProcess is null;
        StandaloneButton.IsEnabled = FabricButton.IsEnabled = !busy && _gameProcess is null;
        RepairButton.IsEnabled = SettingsButton.IsEnabled = !busy && _gameProcess is null;
        AccountButton.IsEnabled = UpdateButton.IsEnabled = !busy && _gameProcess is null;
        AccountActions.IsEnabled = !busy;
        UpdateActions.IsEnabled = !busy;
        CancelOperationButton.IsEnabled = true;
        CancelOperationButton.Visibility = busy && _operation is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SetStage(string state, string detail)
    {
        StatusBadge.Text = state;
        ActivityText.Text = detail;
        var colorKey = state switch
        {
            "ERROR" or "ACTION NEEDED" => "RedBrush",
            "UPDATING" or "LAUNCHING" or "CHECKING" or "SETTING UP" or "REPAIRING" => "BlueBrush",
            _ => "GreenBrush"
        };
        StatusBadge.Foreground = (Brush)FindResource(colorKey);
        ActivityDot.Fill = (Brush)FindResource(colorKey);
    }

    private void SetInstallProgress(string value)
    {
        InstallStatus.Text = value;
        if (_busy) ActivityText.Text = value;
    }

    private void SelectProfile(bool fabric, bool persist = true)
    {
        _fabricSelected = fabric;
        var active = new SolidColorBrush(Color.FromRgb(42, 42, 42));
        var inactive = new SolidColorBrush(Color.FromRgb(18, 18, 18));
        StandaloneButton.Background = fabric ? inactive : active;
        StandaloneButton.BorderBrush = fabric ? (Brush)FindResource("StrokeBrush") : Brushes.White;
        FabricButton.Background = fabric ? active : inactive;
        FabricButton.BorderBrush = fabric ? Brushes.White : (Brush)FindResource("StrokeBrush");
        ProfileName.Text = fabric ? "Plutonium Client + Fabric" : "Plutonium Client";
        ProfileDetails.Text = fabric ? "Fabric  ·  Minecraft 1.21.11" : "Standalone  ·  Minecraft 1.21.11";
        ModsButton.Visibility = fabric ? Visibility.Visible : Visibility.Collapsed;
        if (persist && _config is not null)
        {
            _config.SelectedProfile = fabric ? "fabric" : "standalone";
            _ = _config.SaveAsync();
        }
    }

    private async void Standalone_Click(object sender, RoutedEventArgs e) { SelectProfile(false); await RunOperationAsync(() => RefreshUpdateStatusAsync()); }
    private async void Fabric_Click(object sender, RoutedEventArgs e) { SelectProfile(true); await RunOperationAsync(() => RefreshUpdateStatusAsync()); }
    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        _operation?.Cancel();
        base.OnClosed(e);
    }

    private void Mods_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        var mods = Path.Combine(_config.MinecraftDirectory, "mods");
        Directory.CreateDirectory(mods);
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{mods}\"") { UseShellExecute = true });
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        var show = SettingsOverlay.Visibility != Visibility.Visible;
        if (show && _config is not null)
        {
            GameDirectoryInput.Text = _config.MinecraftDirectory;
            MemoryInput.Text = _config.MaximumMemoryGb.ToString();
            ResolutionInput.Text = $"{_config.ScreenWidth}x{_config.ScreenHeight}";
            FullscreenInput.IsChecked = _config.Fullscreen;
            ClientManifestInput.Text = _config.ClientManifestUrl;
            LauncherManifestInput.Text = _config.LauncherManifestUrl;
        }
        SettingsOverlay.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog { Title = "Select Minecraft game directory", FolderName = GameDirectoryInput.Text };
        if (dialog.ShowDialog(this) == true) GameDirectoryInput.Text = dialog.FolderName;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        try
        {
            var resolution = ResolutionInput.Text.Split('x', 'X');
            if (resolution.Length != 2 || !int.TryParse(resolution[0], out var width) || !int.TryParse(resolution[1], out var height))
                throw new FormatException("Enter the resolution as width x height.");
            if (!int.TryParse(MemoryInput.Text, out var memory)) throw new FormatException("Enter memory as a whole number of gigabytes.");
            if (string.IsNullOrWhiteSpace(GameDirectoryInput.Text)) throw new FormatException("Choose a Minecraft directory.");
            UpdateService.ValidateManifestUrl(ClientManifestInput.Text.Trim());
            UpdateService.ValidateManifestUrl(LauncherManifestInput.Text.Trim());

            _config.MinecraftDirectory = Path.GetFullPath(GameDirectoryInput.Text);
            _config.MaximumMemoryGb = memory;
            _config.ScreenWidth = width;
            _config.ScreenHeight = height;
            _config.Fullscreen = FullscreenInput.IsChecked == true;
            _config.ClientManifestUrl = ClientManifestInput.Text.Trim();
            _config.LauncherManifestUrl = LauncherManifestInput.Text.Trim();
            _installation = InstallationDiscovery.Detect(_config);
            _config.StandaloneGameDirectory = _installation.StandaloneGameDirectory;
            await _config.SaveAsync();
            MinecraftPath.Text = _config.MinecraftDirectory;
            SettingsOverlay.Visibility = Visibility.Collapsed;
            await RefreshUpdateStatusAsync();
        }
        catch (Exception ex)
        {
            InstallStatus.Text = ex.Message;
        }
    }

    private void ScheduleLauncherUpdate(string stagedPath)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable)) throw new InvalidOperationException("Could not determine the launcher executable path.");
        LauncherUpdateHandoff.Start(stagedPath, executable);
        Close();
    }

    private static string CurrentLauncherVersion =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    private static bool IsSmokeTestRun =>
        Environment.GetCommandLineArgs().Contains("--smoke-test", StringComparer.OrdinalIgnoreCase);

    private static string? GetSmokeLaunchProfile()
    {
        const string prefix = "--smoke-launch=";
        var value = Environment.GetCommandLineArgs()
            .FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..]
            .ToLowerInvariant();
        return value is "standalone" or "fabric" ? value : null;
    }

    private async Task RunOfflineLaunchSmokeAsync(bool fabric)
    {
        if (_config is null || _java is null) throw new InvalidOperationException("Launcher setup did not finish.");
        var profileName = fabric ? "fabric" : "standalone";
        var gameDirectory = fabric ? _config.MinecraftDirectory : _config.StandaloneGameDirectory;
        Process? game = null;
        try
        {
            var session = MSession.CreateOfflineSession("PlutoniumSmoke");
            game = await _game.PrepareAndLaunchAsync(_config, _java, session, fabric,
                new Progress<string>(SetInstallProgress), new Progress<double>(value => Progress.Value = value * 100));
            var logPath = Path.Combine(gameDirectory, "logs", "latest.log");
            var started = false;
            for (var attempt = 0; attempt < 120 && !game.HasExited; attempt++)
            {
                await Task.Delay(500);
                if (game.MainWindowHandle != IntPtr.Zero)
                {
                    started = true;
                    break;
                }
            }
            var exitCode = game.HasExited ? game.ExitCode : (int?)null;
            if (!game.HasExited)
            {
                game.Kill(entireProcessTree: true);
                await game.WaitForExitAsync();
            }
            if (!started)
            {
                var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "Minecraft did not create a log.";
                throw new InvalidOperationException($"{profileName} launch did not reach the game startup marker. Exit code: {exitCode?.ToString() ?? "running"}. {log[^Math.Min(log.Length, 2500)..]}");
            }
            await File.WriteAllTextAsync(Path.Combine(_config.DataDirectory, "launch-smoke-" + profileName + ".txt"),
                $"PASS\nProfile={profileName}\nMinecraft={gameDirectory}\nJava={_java.DisplayVersion}\n");
        }
        catch (Exception ex)
        {
            if (game is { HasExited: false })
            {
                game.Kill(entireProcessTree: true);
                await game.WaitForExitAsync();
            }
            await File.WriteAllTextAsync(Path.Combine(_config.DataDirectory, "launch-smoke-" + profileName + ".txt"), "FAIL\n" + ex);
        }
    }
}
