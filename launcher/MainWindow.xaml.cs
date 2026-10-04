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
    private bool _needsRepair;
    private System.Windows.Threading.DispatcherTimer? _gameMonitor;
    private bool _gameWindowVisible;
    private bool _stopRequested;

    public MainWindow()
    {
        InitializeComponent(); InitializeLibraryPages(); _updates.Downloads = _downloads; _game.Downloads = _downloads;
        _downloads.Items.CollectionChanged += async (_, _) =>
        {
            if (_libraryPage.Visibility == Visibility.Visible && _libraryPage.Tag as string == "downloads")
                await ShowLibraryPageAsync("downloads");
        };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await InitializeAsync();

    private async Task InitializeAsync()
    {
        SetBusy(true);
        SetStage("CHECKING", "Checking this PC for Plutonium and Java 21");
        try
        {
            _config = await LauncherConfig.LoadAsync();
            ApplyPreferences();
            Navigate("play");
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
            await RefreshUpdateStatusAsync(applyUpdates: _config.AutomaticUpdates);
            if (await ApplyAutomaticLauncherUpdateAsync()) return;
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
            _needsRepair = true;
            ShowError(ex);
            if (IsSmokeTestRun && _config is not null)
            {
                await File.WriteAllTextAsync(Path.Combine(_config.DataDirectory, "smoke-result.txt"), "FAIL\n" + ex);
                Close();
            }
        }
        finally
        {
            SetBusy(false);
            if (_needsRepair) { PlayButton.Content = "REPAIR & PLAY"; SetStage("REPAIR NEEDED", "Setup failed. Repair the installation to continue."); }
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
            _java = await new JavaRuntimeInstaller(dataDirectory: _config.DataDirectory, downloads: _downloads).InstallAsync(percent, cancellationToken);
        }
        _config.JavaPath = _java.ExecutablePath;
        JavaVersion.Text = _java.DisplayVersion;
        await _config.SaveAsync(cancellationToken);
    }

    private void Account_Click(object sender, RoutedEventArgs e)
    {
        RefreshAccounts();
        Navigate("account");
    }

    private async void Play_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        var serverToJoin = _serverToJoin; _serverToJoin = null;
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
        await RefreshUpdateStatusAsync(applyUpdates: _config.AutomaticUpdates);
        if (await ApplyAutomaticLauncherUpdateAsync()) return;
        SetStage("LAUNCHING", "Preparing Minecraft 1.21.11");
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        var gameProgress = new Progress<string>(SetInstallProgress);
        var byteProgress = new Progress<double>(value => Progress.Value = value * 100);
        _gameProcess = await _game.PrepareAndLaunchAsync(_config, _java!, _session, _fabricSelected,
            gameProgress, byteProgress, OperationToken, serverToJoin);
        _needsRepair = false;
        MonitorGameProcess(_gameProcess);
        if (_gameProcess is null) return;
        SetStage("STARTING", "Waiting for the Minecraft window");
        PlayButton.Content = "STARTING";
    });

    private void ShowSignedInAccount(MSession session)
    {
        RefreshAccounts();
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
        _needsRepair = false;
        SetStage("READY", "Repair complete");
    });

    private async void Updates_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        Navigate("installed");
        SetStage("CHECKING", "Checking launcher and client release feeds");
        await RefreshUpdateStatusAsync();
        if (_gameProcess is null) SetStage(_needsRepair ? "REPAIR NEEDED" : "READY", "Update check complete");
    });

    private void MonitorGameProcess(Process process)
    {
        var handled = false;
        var started = DateTime.UtcNow;
        var warned = false;
        _gameWindowVisible = false;
        _stopRequested = false;
        StopGameButton.Visibility = Visibility.Visible;
        void Ended()
        {
            if (handled || !IsLoaded) return;
            handled = true;
            _gameMonitor?.Stop();
            StopGameButton.Visibility = Visibility.Collapsed;
            var code = _stopRequested ? 0 : process.ExitCode;
            _gameProcess = null;
            _needsRepair = code != 0;
            SetBusy(false);
            PlayButton.Content = _needsRepair ? "REPAIR & PLAY" : "PLAY";
            Progress.Visibility = Visibility.Collapsed;
            if (_needsRepair)
            {
                var error = new InvalidOperationException($"Minecraft stopped with exit code {code}. Try Repair in the Play page.\n\n" + _game.RecentOutput);
                SetStage("REPAIR NEEDED", $"Minecraft stopped unexpectedly (exit code {code}).");
                InstallStatus.Text = "Minecraft failed to run. Repair and try again; the error report includes recent game output.";
                ShowError(error);
            }
            else SetStage("READY", "Minecraft closed");
            if (GetSmokeLaunchProfile() is null) process.Dispose();
        }
        process.Exited += (_, _) => Dispatcher.InvokeAsync(Ended);
        process.EnableRaisingEvents = true;
        if (process.HasExited) Ended();
        if (handled) return;
        _gameMonitor = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _gameMonitor.Tick += (_, _) =>
        {
            if (handled) return;
            process.Refresh();
            if (process.HasExited) { Ended(); return; }
            _gameWindowVisible = process.MainWindowHandle != IntPtr.Zero;
            PlayButton.Content = _gameWindowVisible ? "PLAYING" : "STARTING";
            SetStage(_gameWindowVisible ? "PLAYING" : "STARTING", _gameWindowVisible ? "Minecraft is running" : "Java is running; waiting for the Minecraft window");
            if (!_gameWindowVisible && !warned && DateTime.UtcNow - started > TimeSpan.FromSeconds(90))
            {
                warned = true;
                ShowError(new TimeoutException("Minecraft has not opened a window after 90 seconds. Use Stop Minecraft, then Repair and retry.\n\n" + _game.RecentOutput));
            }
        };
        _gameMonitor.Start();
    }

    private void StopGame_Click(object sender, RoutedEventArgs e)
    {
        try { if (_gameProcess is { HasExited: false }) { _stopRequested = true; _gameProcess.Kill(entireProcessTree: true); } }
        catch (Exception ex) { ShowError(ex); }
    }

    private async Task RunOperationAsync(Func<Task> operation, bool affectsClientInstallation = true)
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
            SetStage(_needsRepair ? "REPAIR NEEDED" : "READY", "Operation cancelled");
        }
        catch (Exception ex)
        {
            InstallStatus.Text = ErrorReport.Redact(ex.Message);
            if (affectsClientInstallation && ex is (IOException or InvalidDataException)) _needsRepair = true;
            SetStage(_needsRepair ? "REPAIR NEEDED" : "ERROR", InstallStatus.Text);
            ShowError(ex);
        }
        finally
        {
            _operation.Dispose();
            _operation = null;
            SetBusy(false);
            if (_gameProcess is not null && !_gameProcess.HasExited)
            {
                PlayButton.Content = _gameWindowVisible ? "PLAYING" : "STARTING";
                PlayButton.IsEnabled = false;
            }
            else if (_config is not null)
            {
                PlayButton.Content = _needsRepair ? "REPAIR & PLAY" : "PLAY";
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
        PlayButton.IsEnabled = !busy && _config is not null && _gameProcess is null;
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
        _modSearch?.Cancel(); _modPlan?.Cancel();
        _gameMonitor?.Stop();
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
            MemoryInput.Value = _config.MaximumMemoryGb;
            var resolution = $"{_config.ScreenWidth}x{_config.ScreenHeight}";
            if (!ResolutionInput.Items.OfType<System.Windows.Controls.ComboBoxItem>().Any(item => Equals(item.Tag, resolution)))
                ResolutionInput.Items.Add(new System.Windows.Controls.ComboBoxItem { Content = "Custom · " + resolution, Tag = resolution });
            ResolutionInput.SelectedValue = resolution;
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
            var resolution = ((string?)ResolutionInput.SelectedValue ?? "1920x1080").Split('x', 'X');
            if (resolution.Length != 2 || !int.TryParse(resolution[0], out var width) || !int.TryParse(resolution[1], out var height))
                throw new FormatException("Enter the resolution as width x height.");
            var memory = (int)Math.Round(MemoryInput.Value);
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
            InstallStatus.Text = ErrorReport.Redact(ex.Message);
            ShowError(ex);
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
            _gameProcess = game;
            MonitorGameProcess(game);
            var logPath = Path.Combine(gameDirectory, "logs", "latest.log");
            var started = false;
            for (var attempt = 0; attempt < 120 && !game.HasExited; attempt++)
            {
                await Task.Delay(500);
                game.Refresh();
                if (attempt >= 20 && _gameWindowVisible && PlayButton.Content?.ToString() == "PLAYING")
                {
                    started = true;
                    break;
                }
            }
            var exitCode = game.HasExited ? game.ExitCode : (int?)null;
            if (!game.HasExited)
            {
                _stopRequested = true;
                game.Kill(entireProcessTree: true);
                await game.WaitForExitAsync();
            }
            await Task.Delay(200);
            if (!started)
            {
                var log = File.Exists(logPath) ? await File.ReadAllTextAsync(logPath) : "Minecraft did not create a log.";
                throw new InvalidOperationException($"{profileName} launch did not reach the game startup marker. Exit code: {exitCode?.ToString() ?? "running"}. {log[^Math.Min(log.Length, 2500)..]}");
            }
            if (_gameProcess is not null || PlayButton.Content?.ToString() != "PLAY")
                throw new InvalidOperationException("Launcher did not clear its Playing state after Minecraft exited.");
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
