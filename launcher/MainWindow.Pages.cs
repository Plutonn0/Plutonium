using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PlutoniumLauncher;

public partial class MainWindow
{
    private void Navigate(string page)
    {
        var pages = new Dictionary<string, FrameworkElement> { ["play"] = PlayPage, ["account"] = AccountsOverlay,
            ["installed"] = UpdatesOverlay, ["files"] = FilesPage, ["theme"] = ThemePage };
        foreach (var pair in pages) pair.Value.Visibility = pair.Key == page ? Visibility.Visible : Visibility.Collapsed;
        foreach (var button in Navigation.Children.OfType<Button>())
            button.BorderBrush = Equals(button.Tag, page) ? Brushes.White : (Brush)FindResource("StrokeBrush");
        pages[page].BeginAnimation(OpacityProperty, new DoubleAnimation(.3, 1, TimeSpan.FromMilliseconds(180)));
    }
    private void Navigate_Click(object sender, RoutedEventArgs e) { RefreshAccounts(); Navigate((string)((Button)sender).Tag); }
    private void Website_Click(object sender, RoutedEventArgs e) => OpenLocation("https://plutonium-lime.vercel.app/");
    private void OpenLocation(string location)
    {
        try { Process.Start(new ProcessStartInfo(location) { UseShellExecute = true }); }
        catch (Exception ex) { ShowError(ex); }
    }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        try
        {
            var root = _fabricSelected ? _config.MinecraftDirectory : _config.StandaloneGameDirectory;
            var category = (string)((Button)sender).Tag;
            var path = category == "game" ? root : Path.Combine(root, category);
            Directory.CreateDirectory(path); OpenLocation(path);
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void Shortcut_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!File.Exists(AppInstaller.InstalledExecutable)) throw new IOException("Run the downloaded launcher once to install Plutonium first.");
            AppInstaller.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Plutonium.lnk"));
            ActivityText.Text = "Desktop shortcut created";
        }
        catch (Exception ex) { ShowError(ex); }
    }
    private void ApplyPreferences()
    {
        if (_config is null) return;
        var contrast = _config.Theme == "Contrast"; var graphite = _config.Theme == "Graphite";
        Resources["CanvasBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(graphite ? "#191919" : contrast ? "#000000" : "#090909"));
        Resources["PanelBrush"] = new SolidColorBrush((Color)ColorConverter.ConvertFromString(graphite ? "#242424" : contrast ? "#080808" : "#111111"));
        ThemeLabel.Text = "Current theme: " + _config.Theme;
        AutoUpdatesInput.IsChecked = _config.AutomaticUpdates;
        ShowHeadInput.IsChecked = _config.ShowPlayerHead;
    }
    private async void Theme_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        _config.Theme = (string)((Button)sender).Tag; ApplyPreferences();
        try { await _config.SaveAsync(); } catch (Exception ex) { ShowError(ex); }
    }
    private async void Preferences_Click(object sender, RoutedEventArgs e)
    {
        if (_config is null) return;
        _config.AutomaticUpdates = AutoUpdatesInput.IsChecked == true;
        _config.ShowPlayerHead = ShowHeadInput.IsChecked == true;
        RefreshAccounts();
        try { await _config.SaveAsync(); } catch (Exception ex) { ShowError(ex); }
    }
    private async Task RefreshHeadAsync(SavedAccount? account)
    {
        AccountHead.Source = null;
        if (_config?.ShowPlayerHead != true || account is null) return;
        try
        {
            var head = await PlayerHead.LoadAsync(account.SkinUrl, _config.DataDirectory);
            if (_config.SelectedAccountId == account.Id && _config.ShowPlayerHead) AccountHead.Source = head;
        }
        catch { /* A skin download must not prevent account sign-in or play. */ }
    }
    private void ShowError(Exception ex)
    {
        ErrorSummary.Text = ErrorReport.Redact(ex.Message);
        ErrorPreview.Text = ErrorReport.Create(ex, CurrentLauncherVersion, _fabricSelected ? "Fabric" : "Standalone");
        ErrorOverlay.Visibility = Visibility.Visible;
    }
    private void SendError_Click(object sender, RoutedEventArgs e)
    {
        try { Process.Start(new ProcessStartInfo(ErrorReport.MailTo(ErrorPreview.Text)) { UseShellExecute = true }); }
        catch { ErrorSummary.Text = "No email app opened. Copy the report and email it to " + ErrorReport.Recipient + "."; }
    }
    private void CopyError_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(ErrorPreview.Text); } catch { ErrorSummary.Text = "Clipboard unavailable. Select and copy the report below."; }
    }
    private void CloseError_Click(object sender, RoutedEventArgs e) => ErrorOverlay.Visibility = Visibility.Collapsed;
}
