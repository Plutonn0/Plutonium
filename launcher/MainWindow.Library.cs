using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PlutoniumLauncher;

public partial class MainWindow
{
    private readonly DownloadManager _downloads = new();
    private readonly ModrinthService _modrinth = new();
    private ModDetection? _modDetection;
    private List<DetectedMod> _detectedMods = [];
    private ScrollViewer _modResultsScroll = null!, _modDetailsScroll = null!;
    private FrameworkElement _modHeader = null!, _modFooter = null!;
    private readonly Grid _modsPage = new() { Margin = new Thickness(28, 30, 28, 24), Visibility = Visibility.Collapsed };
    private readonly Grid _libraryPage = new() { Margin = new Thickness(28, 30, 28, 24), Visibility = Visibility.Collapsed };
    private TextBox _modQuery = null!;
    private ComboBox _modSort = null!, _modCategory = null!;
    private CheckBox _modPreviews = null!;
    private StackPanel _modResults = null!, _modDetails = null!;
    private TextBlock _modStatus = null!;
    private Button _modPrevious = null!, _modNext = null!;
    private CancellationTokenSource? _modSearch;
    private CancellationTokenSource? _modPlan;
    private int _modOffset;
    private bool _installedMods;
    private string? _serverToJoin;

    private static TextBlock Label(string text, double size = 12, bool muted = false) => new()
    { Text = text, FontSize = size, Foreground = muted ? Brushes.DarkGray : Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
    private Button ActionButton(string text, Func<Task> action)
    {
        var button = new Button { Content = text, Style = (Style)FindResource("QuietButton"), Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 9, 12, 9) };
        var running = false;
        button.Click += async (_, _) => { if (running) return; running = true; button.IsHitTestVisible = false; try { await action(); } catch (OperationCanceledException) { } catch (Exception ex) { ShowError(ex); } finally { running = false; button.IsHitTestVisible = true; } };
        return button;
    }
    private static ScrollViewer Scroll(UIElement content) => new() { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 12, 0) };
    private Border Card(UIElement content)
    {
        var border = new Border { Child = content, Padding = new Thickness(16), BorderBrush = new SolidColorBrush(Color.FromRgb(43, 43, 43)), BorderThickness = new Thickness(1), Margin = new Thickness(0, 0, 0, 10) };
        border.SetResourceReference(Border.BackgroundProperty, "PanelBrush"); return border;
    }
    private TextBox Input(string text, string hint) => new() { Text = text, ToolTip = hint, Style = (Style)FindResource("DarkTextBox"), Margin = new Thickness(0, 0, 8, 10), MinWidth = 100 };
    private ComboBox Choices(params string[] values)
    {
        var box = new ComboBox { ItemsSource = values, SelectedIndex = 0, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(8), MinWidth = 115, Background = Brushes.Black, Foreground = Brushes.White };
        box.Style = (Style)FindResource("DarkComboBox");
        return box;
    }
    private async Task MutateLibraryAsync(Func<Task> action)
    {
        if (_config is null) throw new InvalidOperationException("Wait for launcher setup to finish.");
        if (_busy || _gameProcess is not null) throw new InvalidOperationException("Finish the current operation and close Minecraft before changing installed files.");
        await RunOperationAsync(action, affectsClientInstallation: false);
    }
    private void InitializeLibraryPages()
    {
        var shell = (Grid)PlayPage.Parent;
        Grid.SetColumn(_modsPage, 1); Grid.SetColumn(_libraryPage, 1); shell.Children.Add(_modsPage); shell.Children.Add(_libraryPage);
        _modsPage.RowDefinitions.Add(new() { Height = GridLength.Auto }); _modsPage.RowDefinitions.Add(new()); _modsPage.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var header = new StackPanel(); header.Children.Add(Label("Mods", 28)); header.Children.Add(Label("Discover on Modrinth · Minecraft 1.21.11 / Fabric · installs to your Fabric profile", 11, true));
        header.Children.Add(Label("SEARCH MODS BY NAME", 10, true));
        var toolbar = new WrapPanel(); _modQuery = Input("", "Search Modrinth mods"); _modQuery.Width = 260;
        System.Windows.Automation.AutomationProperties.SetName(_modQuery, "Search mods");
        toolbar.Children.Add(_modQuery); toolbar.Children.Add(ActionButton("SEARCH", async () => { _installedMods = false; _modOffset = 0; await SearchModsAsync(); }));
        toolbar.Children.Add(ActionButton("INSTALLED", ShowInstalledModsAsync));
        header.Children.Add(toolbar);
        var filters = new WrapPanel(); _modSort = Choices("Relevance", "Downloads", "Newest", "Updated"); _modCategory = Choices("All categories", "Optimization", "Utility", "Worldgen", "Adventure", "Decoration", "Technology");
        _modPreviews = new CheckBox { Content = "Include beta / alpha", Foreground = Brushes.LightGray, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 10) };
        filters.Children.Add(_modSort); filters.Children.Add(_modCategory); filters.Children.Add(_modPreviews); header.Children.Add(filters);
        _modHeader = header; _modsPage.Children.Add(header);
        _modResults = new(); _modDetails = new();
        _modResultsScroll = Scroll(_modResults); Grid.SetRow(_modResultsScroll, 1); _modsPage.Children.Add(_modResultsScroll);
        _modDetailsScroll = Scroll(_modDetails); Grid.SetRowSpan(_modDetailsScroll, 3); _modDetailsScroll.Visibility = Visibility.Collapsed; _modsPage.Children.Add(_modDetailsScroll);
        var footer = new DockPanel { Margin = new Thickness(0, 10, 0, 0) }; Grid.SetRow(footer, 2); var paging = new StackPanel { Orientation = Orientation.Horizontal };
        _modPrevious = ActionButton("PREVIOUS", async () => { _modOffset = Math.Max(0, _modOffset - 20); await SearchModsAsync(); });
        _modNext = ActionButton("NEXT", async () => { _modOffset += 20; await SearchModsAsync(); }); paging.Children.Add(_modPrevious); paging.Children.Add(_modNext); DockPanel.SetDock(paging, Dock.Right); footer.Children.Add(paging);
        _modStatus = Label("Powered by Modrinth", 11, true); footer.Children.Add(_modStatus); _modFooter = footer; _modsPage.Children.Add(footer);
        _modQuery.TextChanged += async (_, _) => { if (_installedMods) return; _modOffset = 0; await SearchModsAsync(debounce: true); };
        _modSort.SelectionChanged += async (_, _) => { _installedMods = false; _modOffset = 0; await SearchModsAsync(); };
        _modCategory.SelectionChanged += async (_, _) => { _installedMods = false; _modOffset = 0; await SearchModsAsync(); };
        _modPreviews.Click += (_, _) => { _modPlan?.Cancel(); _modDetails.Children.Clear(); _modDetails.Children.Add(Label("Release channel changed. Select a mod to refresh its install plan.", 12, true)); };
    }
    private async Task SearchModsAsync(bool debounce = false)
    {
        _modSearch?.Cancel(); _modSearch = new(); var token = _modSearch.Token;
        try
        {
            if (debounce) await Task.Delay(350, token);
            ShowModBrowser();
            _modStatus.Text = "Searching Modrinth…"; _modPrevious.IsEnabled = _modNext.IsEnabled = false;
            var query = _modQuery.Text.Trim(); var category = _modCategory.SelectedIndex == 0 ? "" : ((string)_modCategory.SelectedItem).ToLowerInvariant();
            var result = await _modrinth.SearchAsync(query, ((string)_modSort.SelectedItem).ToLowerInvariant(), category, _modOffset, token);
            var detection = _config is null ? new ModDetectionResult([], null) : await (_modDetection ??= new(_modrinth)).ScanAsync(_config.MinecraftDirectory, token);
            _detectedMods = detection.Mods;
            token.ThrowIfCancellationRequested(); _modResults.Children.Clear();
            foreach (var mod in result.Hits)
            {
                var body = new StackPanel(); var heading = new DockPanel();
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(20, 0, 0, 0) };
                var existing = _detectedMods.FirstOrDefault(m => m.ProjectId == mod.ProjectId);
                var install = ActionButton(existing is null ? "INSTALL" : "INSTALLED", () => InstallBrowserModAsync(mod));
                install.IsEnabled = existing is null; install.ToolTip = existing is null ? "Install the compatible version and required dependencies" : existing.Version + (existing.Enabled ? " · enabled" : " · disabled");
                actions.Children.Add(install); actions.Children.Add(ActionButton("VIEW", () => ShowModAsync(mod)));
                DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
                if (Uri.TryCreate(mod.IconUrl, UriKind.Absolute, out var icon) && icon.Scheme == "https" && icon.Host == "cdn.modrinth.com")
                {
                    var bitmap = new BitmapImage(); bitmap.BeginInit(); bitmap.UriSource = icon; bitmap.DecodePixelWidth = 96; bitmap.EndInit();
                    var picture = new Image { Source = bitmap, Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 8) };
                    picture.ImageFailed += (_, e) => { picture.Source = null; e.Handled = true; };
                    DockPanel.SetDock(picture, Dock.Left); heading.Children.Add(picture);
                }
                var title = new StackPanel(); title.Children.Add(Label(mod.Title, 16)); title.Children.Add(Label($"by {mod.Author} · {mod.Downloads:N0} downloads", 10, true)); heading.Children.Add(title); body.Children.Add(heading);
                var description = Label(mod.Description, 11, true); description.MaxHeight = 54; description.TextTrimming = TextTrimming.CharacterEllipsis; body.Children.Add(description);
                _modResults.Children.Add(Card(body));
            }
            if (result.Hits.Count == 0) _modResults.Children.Add(Label("No compatible mods found. Try another name or category.", 16, true));
            _modStatus.Text = result.TotalHits == 0 ? "No results" : $"{_modOffset + 1}–{_modOffset + result.Hits.Count} of {result.TotalHits:N0} compatible mods";
            if (detection.Warning is not null) _modStatus.Text += " · " + detection.Warning;
            _modPrevious.IsEnabled = _modOffset > 0; _modNext.IsEnabled = _modOffset + result.Hits.Count < result.TotalHits;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) { _modStatus.Text = "Search unavailable · press Search to retry"; _modResults.Children.Clear(); _modResults.Children.Add(Label(ErrorReport.Redact(ex.Message), 12, true)); } }
    }
    private void ShowModBrowser()
    {
        _modPlan?.Cancel();
        _modHeader.Visibility = _modFooter.Visibility = _modResultsScroll.Visibility = Visibility.Visible;
        _modDetailsScroll.Visibility = Visibility.Collapsed;
    }
    private async Task InstallBrowserModAsync(ModProject mod) => await MutateLibraryAsync(async () =>
    {
        _modStatus.Text = "Resolving " + mod.Title + " and its required dependencies…";
        var plan = await _modrinth.PlanAsync(mod.ProjectId, mod.Title, _modPreviews.IsChecked == true, OperationToken);
        await InstallModPlanAsync(plan);
        await SearchModsAsync();
    });
    private async Task InstallModPlanAsync(List<ModPlanEntry> plan)
    {
        await new ModLibrary(_config!.MinecraftDirectory, _downloads).InstallAsync(plan, OperationToken);
        SelectProfile(true, false); _config.SelectedProfile = "fabric"; await _config.SaveAsync();
    }
    private async Task ShowModAsync(ModProject mod)
    {
        _modSearch?.Cancel(); _modPlan?.Cancel(); _modPlan = new(); var token = _modPlan.Token;
        _modHeader.Visibility = _modFooter.Visibility = _modResultsScroll.Visibility = Visibility.Collapsed;
        _modDetailsScroll.Visibility = Visibility.Visible; _modDetailsScroll.ScrollToTop();
        _modDetails.Children.Clear();
        _modDetails.Children.Add(ActionButton("← BACK TO MODS", async () => { if (_installedMods) await ShowInstalledModsAsync(); else await SearchModsAsync(); }));
        _modDetails.Children.Add(Label(mod.Title, 30)); _modDetails.Children.Add(Label(mod.Description, 13, true));
        var installPanel = new StackPanel(); var status = Label("Loading mod details…", 12, true); installPanel.Children.Add(status); _modDetails.Children.Add(Card(installPanel));
        try
        {
            var project = await _modrinth.ProjectAsync(mod.ProjectId, token); token.ThrowIfCancellationRequested();
            _modDetails.Children.Add(Label($"{project.Downloads:N0} downloads · {string.Join(" / ", project.Categories)} · {project.License.Name ?? project.License.Id}", 11, true));
            var gallery = project.Gallery.FirstOrDefault(g => g.Featured) ?? project.Gallery.FirstOrDefault();
            if (gallery is not null && Uri.TryCreate(gallery.Url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "cdn.modrinth.com")
            {
                var picture = new Image { Source = new BitmapImage(uri), MaxHeight = 280, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 20) };
                picture.ImageFailed += (_, e) => { picture.Visibility = Visibility.Collapsed; e.Handled = true; }; _modDetails.Children.Add(picture);
            }
            _modDetails.Children.Add(Label("ABOUT THIS MOD", 11, true));
            var document = ModDescription.Render(project.Body); document.FontFamily = (FontFamily)FindResource("Lato");
            _modDetails.Children.Add(new FlowDocumentScrollViewer { Document = document, IsToolBarVisible = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontFamily = (FontFamily)FindResource("Lato") });
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex) { if (!token.IsCancellationRequested) _modDetails.Children.Add(Label("Description unavailable: " + ErrorReport.Redact(ex.Message), 12, true)); }
        try
        {
            var plan = await _modrinth.PlanAsync(mod.ProjectId, mod.Title, _modPreviews.IsChecked == true, token); token.ThrowIfCancellationRequested();
            var detection = _config is null ? new ModDetectionResult([], null) : await (_modDetection ??= new(_modrinth)).ScanAsync(_config.MinecraftDirectory, token);
            var installed = detection.Mods.FirstOrDefault(m => m.ProjectId == mod.ProjectId);
            var current = _config is null ? null : (await new ModLibrary(_config.MinecraftDirectory, _downloads).LoadAsync()).FirstOrDefault(m => m.ProjectId == mod.ProjectId);
            token.ThrowIfCancellationRequested();
            status.Text = $"FABRIC 1.21.11 · {plan.Count} {(plan.Count == 1 ? "mod" : "mods")} including dependencies · {plan.Sum(p => p.File.Size) / 1048576d:0.0} MB";
            foreach (var entry in plan) installPanel.Children.Add(Label($"{entry.Title} · {entry.Version.VersionNumber} · {entry.Version.VersionType}", 11, true));
            var upToDate = installed?.VersionId == plan.First(p => p.Version.ProjectId == mod.ProjectId).Version.Id;
            if (installed is not null) installPanel.Children.Add(Label($"Installed: {installed.Version} · {(installed.Enabled ? "Enabled" : "Disabled")}" + (installed.Managed ? "" : " · detected in your mods folder"), 12));
            var actions = new WrapPanel();
            var install = ActionButton(installed is null ? "INSTALL" : upToDate ? "INSTALLED" : "UPDATE", () => MutateLibraryAsync(async () =>
            {
                await InstallModPlanAsync(plan); await ShowModAsync(mod);
            }));
            install.IsEnabled = !upToDate && (installed is null || installed.Managed); actions.Children.Add(install);
            if (current is not null) actions.Children.Add(ActionButton("REINSTALL / REPAIR", () => MutateLibraryAsync(async () => { await InstallModPlanAsync(plan); await ShowModAsync(mod); })));
            if (installed is { Managed: false }) installPanel.Children.Add(Label("This local mod is preserved. Use the Installed view to locate its file.", 11, true));
            installPanel.Children.Add(actions);
            installPanel.Children.Add(Label("Required dependencies are included automatically. Installs use the Fabric profile.", 11, true));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!token.IsCancellationRequested) status.Text = ErrorReport.Redact(ex.Message); }
    }
    private async Task ShowInstalledModsAsync()
    {
        if (_config is null) return;
        ShowModBrowser();
        _installedMods = true; _modSearch?.Cancel(); _modResults.Children.Clear(); _modNext.IsEnabled = _modPrevious.IsEnabled = false;
        var library = new ModLibrary(_config.MinecraftDirectory, _downloads); var installed = await library.LoadAsync();
        var detected = await (_modDetection ??= new(_modrinth)).ScanAsync(_config.MinecraftDirectory, default);
        _modStatus.Text = $"{installed.Count} managed mods · Fabric 1.21.11";
        _modResults.Children.Add(ActionButton("BROWSE MODS", async () => { _installedMods = false; _modOffset = 0; await SearchModsAsync(); }));
        foreach (var mod in installed.OrderBy(m => m.Title))
        {
            var body = new StackPanel(); body.Children.Add(Label(mod.Title, 16)); body.Children.Add(Label($"{mod.Version} · {(File.Exists(library.PathFor(mod)) ? mod.Enabled ? "Enabled" : "Disabled" : "File missing")}", 11, true));
            var actions = new WrapPanel();
            actions.Children.Add(ActionButton(mod.Enabled ? "DISABLE" : "ENABLE", () => MutateLibraryAsync(async () => { await library.ChangeAsync(mod.ProjectId, false); await ShowInstalledModsAsync(); })));
            actions.Children.Add(ActionButton("CHECK UPDATE", () => ShowModAsync(new(mod.ProjectId, mod.ProjectId, mod.Title, "Choose Install / Update to install the latest compatible version and its required dependencies.", "", 0, null, "optional"))));
            actions.Children.Add(ActionButton("REMOVE", () => MutateLibraryAsync(async () => { await library.ChangeAsync(mod.ProjectId, true); await ShowInstalledModsAsync(); })));
            body.Children.Add(actions); _modResults.Children.Add(Card(body));
        }
        var managedPaths = installed.Select(library.PathFor).ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (Directory.Exists(library.DirectoryPath))
            foreach (var path in Directory.EnumerateFiles(library.DirectoryPath).Where(p => (p.EndsWith(".jar", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".jar.disabled", StringComparison.OrdinalIgnoreCase)) && !managedPaths.Contains(p)))
            {
                var local = detected.Mods.FirstOrDefault(m => m.Path.Equals(path, StringComparison.OrdinalIgnoreCase));
                var body = new StackPanel(); var heading = new DockPanel();
                if (local is not null)
                {
                    var actions = new WrapPanel(); var installedButton = ActionButton("INSTALLED", () => Task.CompletedTask); installedButton.IsEnabled = false; actions.Children.Add(installedButton);
                    actions.Children.Add(ActionButton("VIEW", () => ShowModAsync(new(local.ProjectId, local.ProjectId, local.Title, "Detected from your existing mod file.", "", 0, null, "optional"))));
                    DockPanel.SetDock(actions, Dock.Right); heading.Children.Add(actions);
                }
                heading.Children.Add(Label(local?.Title ?? Path.GetFileName(path), 15)); body.Children.Add(heading);
                body.Children.Add(Label(local is null ? "Local / bundled mod · not identified by Modrinth" : local.Version + (local.Enabled ? " · Enabled" : " · Disabled"), 11, true));
                body.Children.Add(Label(Path.GetFileName(path), 10, true)); _modResults.Children.Add(Card(body));
            }
        if (installed.Count == 0) _modResults.Children.Add(Label("Your Modrinth library is empty. Browse mods to add your first one.", 13, true));
    }
    private async Task ShowLibraryPageAsync(string page)
    {
        _libraryPage.Tag = page;
        _libraryPage.Children.Clear();
        var content = new StackPanel(); _libraryPage.Children.Add(Scroll(content));
        if (page != "settings") content.Children.Add(ActionButton("← SETTINGS", async () => { Navigate("settings"); await ShowLibraryPageAsync("settings"); }));
        if (page == "settings")
        {
            content.Children.Add(Label("Settings", 28)); content.Children.Add(Label("Your launcher, in one place.", 12, true));
            var general = new StackPanel(); general.Children.Add(Label("Game & appearance", 18)); general.Children.Add(Label("Memory, resolution, fullscreen, folders and your preferred theme.", 12, true));
            var controls = new WrapPanel(); controls.Children.Add(ActionButton("GAME SETTINGS", () => { Settings_Click(this, new()); return Task.CompletedTask; }));
            controls.Children.Add(ActionButton("APPEARANCE", () => { Navigate("theme"); return Task.CompletedTask; })); general.Children.Add(controls); content.Children.Add(Card(general));
            foreach (var section in new[] { ("servers", "Server favorites", "Saved servers with quick joining."), ("downloads", "Downloads", "Transfer progress, pause, resume and cancel."), ("installed", "Updates", "Installed versions and available client or launcher updates."), ("history", "Update history & rollback", "Restore a saved version or review recent update activity."), ("health", "Installation health", "Check Java, game files and your mod library.") })
            {
                var body = new DockPanel(); var button = ActionButton("OPEN", async () => { Navigate(section.Item1); if (section.Item1 == "installed") await RunOperationAsync(() => RefreshUpdateStatusAsync()); else await ShowLibraryPageAsync(section.Item1); });
                DockPanel.SetDock(button, Dock.Right); body.Children.Add(button);
                var labels = new StackPanel(); labels.Children.Add(Label(section.Item2, 16)); labels.Children.Add(Label(section.Item3, 11, true)); body.Children.Add(labels); content.Children.Add(Card(body));
            }
            return;
        }
        if (page == "downloads")
        {
            content.Children.Add(Label("Downloads", 28)); content.Children.Add(Label("Mods, client updates and Java · verified before installation · automatic network retries", 11, true));
            content.Children.Add(ActionButton("CLEAR FINISHED", async () => { foreach (var item in _downloads.Items.Where(i => !i.Active).ToList()) _downloads.Items.Remove(item); await ShowLibraryPageAsync("downloads"); }));
            foreach (var item in _downloads.Items)
            {
                var body = new StackPanel(); body.Children.Add(Label(item.Name, 15)); var state = Label("", 11, true); state.SetBinding(TextBlock.TextProperty, new Binding(nameof(item.Status)) { Source = item }); body.Children.Add(state);
                var bar = new ProgressBar { Height = 3, Maximum = 100, Foreground = Brushes.White, Background = Brushes.DimGray, Margin = new Thickness(0, 4, 0, 12) }; bar.SetBinding(ProgressBar.ValueProperty, new Binding(nameof(item.Percent)) { Source = item }); body.Children.Add(bar);
                var detail = Label("", 11, true); detail.SetBinding(TextBlock.TextProperty, new Binding(nameof(item.Detail)) { Source = item }); body.Children.Add(detail);
                var actions = new WrapPanel();
                if (item.CanPause)
                {
                    var pause = ActionButton("PAUSE / RESUME", () => { item.TogglePause(); return Task.CompletedTask; });
                    pause.SetBinding(IsEnabledProperty, new Binding(nameof(item.Active)) { Source = item }); actions.Children.Add(pause);
                }
                var cancel = ActionButton("CANCEL", () => { item.Cancel(); return Task.CompletedTask; });
                cancel.SetBinding(IsEnabledProperty, new Binding(nameof(item.Active)) { Source = item }); actions.Children.Add(cancel);
                body.Children.Add(actions); content.Children.Add(Card(body));
            }
            if (_downloads.Items.Count == 0) content.Children.Add(Label("No downloads yet. New transfers appear here automatically.", 15, true));
            return;
        }
        if (_config is null) { content.Children.Add(Label("Wait for launcher setup to finish.")); return; }
        if (page == "servers")
        {
            content.Children.Add(Label("Server favorites", 28)); content.Children.Add(Label("Save a server, then launch straight into it using your selected profile.", 11, true));
            var name = Input("", "Server name"); var address = Input("", "Hostname or IP, optionally :port");
            content.Children.Add(Label("NAME", 10, true)); content.Children.Add(name); content.Children.Add(Label("ADDRESS", 10, true)); content.Children.Add(address);
            content.Children.Add(ActionButton("ADD FAVORITE", async () =>
            {
                var favorite = ServerFavorite.Create(name.Text, address.Text);
                if (_config.Servers.Any(s => s.Address.Equals(favorite.Address, StringComparison.OrdinalIgnoreCase))) throw new InvalidOperationException("This server is already saved.");
                _config.Servers.Add(favorite); await _config.SaveAsync(); await ShowLibraryPageAsync("servers");
            }));
            foreach (var server in _config.Servers.ToList())
            {
                var body = new StackPanel(); body.Children.Add(Label(server.Name, 18)); body.Children.Add(Label(server.Address, 12, true)); var actions = new WrapPanel();
                actions.Children.Add(ActionButton("JOIN", () =>
                {
                    if (_busy || _gameProcess is not null) throw new InvalidOperationException("Close Minecraft and finish the current operation before joining a server.");
                    _serverToJoin = ServerFavorite.Create(server.Name, server.Address).Address; Navigate("play"); Play_Click(this, new()); return Task.CompletedTask;
                }));
                actions.Children.Add(ActionButton("COPY ADDRESS", () => { Clipboard.SetText(server.Address); return Task.CompletedTask; }));
                actions.Children.Add(ActionButton("REMOVE", async () => { _config.Servers.Remove(server); await _config.SaveAsync(); await ShowLibraryPageAsync("servers"); })); body.Children.Add(actions); content.Children.Add(Card(body));
            }
        }
        else if (page == "health")
        {
            content.Children.Add(Label("Installation health", 28)); content.Children.Add(Label("Check Java, client archives, writable folders, disk space, mod checksums and duplicate mod IDs.", 11, true));
            var findings = new StackPanel();
            content.Children.Add(ActionButton("RUN CHECK", () => MutateLibraryAsync(async () =>
            {
                findings.Children.Clear(); findings.Children.Add(Label("Checking installation…"));
                var results = await Task.Run(() => InstallationHealth.CheckAsync(_config, OperationToken)); findings.Children.Clear();
                foreach (var result in results) { var body = new StackPanel(); body.Children.Add(Label(result.Status + " · " + result.Name, 14)); body.Children.Add(Label(result.Detail, 11, true)); findings.Children.Add(Card(body)); }
            })));
            content.Children.Add(ActionButton("REPAIR CLIENT & JAVA", () => MutateLibraryAsync(async () => { await _bootstrap.InstallOrRepairAsync(_config, cancellationToken: OperationToken); await EnsureJavaAsync(OperationToken); _needsRepair = false; findings.Children.Clear(); findings.Children.Add(Label("Repair finished. Run the check again to verify. Mod problems can be repaired from the Mods tab.")); })));
            content.Children.Add(findings);
        }
        else if (page == "history")
        {
            content.Children.Add(Label("Update history & rollback", 28)); content.Children.Add(Label("Saved versions from before an update. Restoring disables automatic updates so your chosen version stays installed.", 11, true));
            var history = new UpdateHistory(_config.DataDirectory); var snapshots = await history.LoadAsync();
            content.Children.Add(Label($"Current · Launcher {CurrentLauncherVersion} / Fabric {_config.FabricClientVersion} / Standalone {_config.StandaloneClientVersion}", 11, true));
            var events = await history.EventsAsync();
            foreach (var entry in events.Take(30))
            {
                var body = new StackPanel(); body.Children.Add(Label($"{entry.Component} · {entry.FromVersion} → {entry.ToVersion}", 14));
                body.Children.Add(Label($"{entry.Created:dd MMM yyyy HH:mm} · {entry.Status}", 11, true)); content.Children.Add(Card(body));
            }
            content.Children.Add(Label("Restore a saved version", 20));
            foreach (var snapshot in snapshots)
            {
                var body = new StackPanel(); body.Children.Add(Label(snapshot.Component + " · " + snapshot.Version, 17)); body.Children.Add(Label($"Saved {snapshot.Created:dd MMM yyyy HH:mm}\n{snapshot.Target}", 11, true));
                body.Children.Add(ActionButton("RESTORE THIS VERSION", () => MutateLibraryAsync(async () =>
                {
                    var expected = snapshot.Component == "Launcher" ? Environment.ProcessPath! : snapshot.Component == "Fabric" ? InstallationDiscovery.FabricModPath(_config.MinecraftDirectory) : Path.Combine(_config.StandaloneGameDirectory, "versions", "plutonium-1.21.11", "plutonium-1.21.11.jar");
                    if (!Path.GetFullPath(expected).Equals(snapshot.Target, StringComparison.OrdinalIgnoreCase)) throw new IOException("This snapshot belongs to a different installation folder.");
                    var staged = await history.PrepareRestoreAsync(snapshot, OperationToken);
                    await history.CaptureAsync(snapshot.Component, snapshot.Component == "Launcher" ? CurrentLauncherVersion : snapshot.Component == "Fabric" ? _config.FabricClientVersion : _config.StandaloneClientVersion, expected, OperationToken);
                    _config.AutomaticUpdates = false;
                    if (snapshot.Component == "Launcher") { await _config.SaveAsync(); await history.RecordAsync("Launcher", CurrentLauncherVersion, snapshot.Version, "Rollback scheduled on exit"); ScheduleLauncherUpdate(staged); return; }
                    try { AtomicFile.Replace(staged, expected); } finally { if (File.Exists(staged)) File.Delete(staged); }
                    if (snapshot.Component == "Fabric") { _config.FabricClientVersion = snapshot.Version; _config.FabricRollbackHash = snapshot.Sha256; }
                    else { _config.StandaloneClientVersion = snapshot.Version; _config.StandaloneRollbackHash = snapshot.Sha256; }
                    await _config.SaveAsync(); await history.RecordAsync(snapshot.Component, "Saved backup", snapshot.Version, "Restored · automatic updates disabled"); ApplyPreferences(); await RefreshUpdateStatusAsync(); await ShowLibraryPageAsync("history");
                })));
                content.Children.Add(Card(body));
            }
            if (snapshots.Count == 0) content.Children.Add(Label("No saved versions yet. A verified backup is created before each future client or launcher update.", 15, true));
        }
    }
}
