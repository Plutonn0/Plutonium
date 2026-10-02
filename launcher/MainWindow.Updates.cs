using System.IO;
using System.Windows;

namespace PlutoniumLauncher;

public partial class MainWindow
{
    private ClientUpdateManifest? _clientRelease;
    private LauncherUpdateManifest? _launcherRelease;
    private bool _clientUpdateNeeded;

    private async Task RefreshUpdateStatusAsync(bool applyUpdates = false)
    {
        if (_config is null) return;
        _clientRelease = null;
        _launcherRelease = null;
        _clientUpdateNeeded = false;
        InstallClientUpdateButton.IsEnabled = InstallLauncherUpdateButton.IsEnabled = false;
        var installed = _fabricSelected ? _config.FabricClientVersion : _config.StandaloneClientVersion;
        ClientInstalledVersion.Text = $"{(_fabricSelected ? "Fabric" : "Standalone")} client · {installed}";
        LauncherInstalledVersion.Text = $"Launcher · {CurrentLauncherVersion}";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(OperationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var (status, manifest) = await _updates.CheckClientAsync(_config.ClientManifestUrl, installed, timeout.Token);
            ClientUpdateDetail.Text = status.Status;
            if (manifest is not null)
            {
                var asset = _fabricSelected ? manifest.Fabric : manifest.Standalone;
                bool matches = await UpdateService.FileMatchesSha256Async(ClientUpdateTarget, asset.Sha256, timeout.Token);
                _clientUpdateNeeded = status.Available || (manifest.Version == installed && !matches);
                _clientRelease = manifest;
                if (_clientUpdateNeeded && !status.Available) ClientUpdateDetail.Text = "Repair available for installed version";
            }
        }
        catch (Exception ex) when (!OperationToken.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            ClientUpdateDetail.Text = "Could not check client updates. Your installed version is still available.";
        }
        using var launcherTimeout = CancellationTokenSource.CreateLinkedTokenSource(OperationToken);
        launcherTimeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            var (status, manifest) = await _updates.CheckLauncherAsync(_config.LauncherManifestUrl, CurrentLauncherVersion, launcherTimeout.Token);
            LauncherUpdateDetail.Text = status.Status;
            if (status.Available) _launcherRelease = manifest;
        }
        catch (Exception ex) when (!OperationToken.IsCancellationRequested && ex is not OutOfMemoryException)
        {
            LauncherUpdateDetail.Text = "Could not check launcher updates. Try again later.";
        }
        OperationToken.ThrowIfCancellationRequested();
        if (applyUpdates && _clientUpdateNeeded) await InstallClientUpdateAsync();
        InstallClientUpdateButton.IsEnabled = _clientUpdateNeeded;
        InstallLauncherUpdateButton.IsEnabled = _launcherRelease is not null;
        UpdateButton.Content = _clientUpdateNeeded || _launcherRelease is not null ? "UPDATES" : "CHECK";
        UpdateStatus.Text = $"Client {(_fabricSelected ? _config.FabricClientVersion : _config.StandaloneClientVersion)} · Launcher {CurrentLauncherVersion}\n"
            + (_clientUpdateNeeded || _launcherRelease is not null ? "Update available" : ClientUpdateDetail.Text);
        VersionsFooter.Text = $"LAUNCHER {CurrentLauncherVersion} / CLIENT {(_fabricSelected ? _config.FabricClientVersion : _config.StandaloneClientVersion)}";
    }

    private string ClientUpdateTarget => _fabricSelected
        ? InstallationDiscovery.FabricModPath(_config!.MinecraftDirectory)
        : Path.Combine(_config!.StandaloneGameDirectory, "versions", "plutonium-1.21.11", "plutonium-1.21.11.jar");

    private async Task InstallClientUpdateAsync()
    {
        if (_config is null || _clientRelease is null || !_clientUpdateNeeded) return;
        var release = _clientRelease;
        SetStage("UPDATING", $"Installing client {release.Version}");
        Progress.Visibility = Visibility.Visible;
        Progress.Value = 0;
        await _updates.DownloadVerifiedAsync(_fabricSelected ? release.Fabric : release.Standalone,
            ClientUpdateTarget, new Progress<double>(value => Progress.Value = value * 100), OperationToken);
        if (_fabricSelected) _config.FabricClientVersion = release.Version;
        else _config.StandaloneClientVersion = release.Version;
        await _config.SaveAsync();
        _clientUpdateNeeded = false;
        ClientUpdateDetail.Text = $"Client {release.Version} installed and verified";
        ClientInstalledVersion.Text = $"{(_fabricSelected ? "Fabric" : "Standalone")} client · {release.Version}";
        InstallClientUpdateButton.IsEnabled = false;
        SetStage("READY", "Client update installed");
    }

    private async void InstallClientUpdate_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        await InstallClientUpdateAsync();
        await RefreshUpdateStatusAsync();
    });

    private async void InstallLauncherUpdate_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        if (_launcherRelease is null || _config is null) return;
        SetStage("UPDATING", $"Downloading launcher {_launcherRelease.Version}");
        var staged = Path.Combine(_config.DataDirectory, "updates", "Plutonium Client.exe.next");
        Progress.Visibility = Visibility.Visible;
        await _updates.DownloadVerifiedAsync(_launcherRelease.Executable, staged,
            new Progress<double>(value => Progress.Value = value * 100), OperationToken);
        OperationToken.ThrowIfCancellationRequested();
        ScheduleLauncherUpdate(staged);
    });

    private void CloseUpdates_Click(object sender, RoutedEventArgs e) => UpdatesOverlay.Visibility = Visibility.Collapsed;
}
