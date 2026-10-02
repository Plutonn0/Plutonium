using System.Windows;

namespace PlutoniumLauncher;

public partial class MainWindow
{
    private CancellationToken OperationToken => _operation?.Token ?? CancellationToken.None;

    private void RefreshAccounts()
    {
        if (_accounts is null || _config is null) return;
        var saved = _accounts.GetAccounts();
        if (saved.Count > 0 && !saved.Any(a => a.Id == _config.SelectedAccountId))
            _config.SelectedAccountId = saved[0].Id;
        AccountList.ItemsSource = saved;
        AccountList.SelectedItem = saved.FirstOrDefault(a => a.Id == _config.SelectedAccountId) ?? saved.FirstOrDefault();
        var active = saved.FirstOrDefault(a => a.Id == _config.SelectedAccountId);
        AccountName.Text = active?.Name ?? "Microsoft account";
        AccountMark.Text = active is null ? "?" : active.Name[..1].ToUpperInvariant();
        AccountState.Text = active is null ? "Not signed in" : "Remembered on this PC";
        AccountButton.Content = "ACCOUNTS";
        AccountEmpty.Visibility = saved.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void AddAccount_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        if (_accounts is null || _config is null) return;
        SetStage("SIGNING IN", "Choose a Microsoft account in your browser");
        var result = await _accounts.AddAsync(OperationToken);
        _config.SelectedAccountId = result.Account.Id;
        await _config.SaveAsync(OperationToken);
        _session = result.Session;
        RefreshAccounts();
        ShowSignedInAccount(_session);
        SetStage("READY", "Account added");
    });

    private async void UseAccount_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        if (_config is null || AccountList.SelectedItem is not SavedAccount selected) return;
        _config.SelectedAccountId = selected.Id;
        await _config.SaveAsync(OperationToken);
        _session = null;
        RefreshAccounts();
        AccountsOverlay.Visibility = Visibility.Collapsed;
        SetStage("READY", $"Using {selected.Name}");
    });

    private async void RemoveAccount_Click(object sender, RoutedEventArgs e) => await RunOperationAsync(async () =>
    {
        if (_config is null || _accounts is null || AccountList.SelectedItem is not SavedAccount selected) return;
        await _accounts.RemoveAsync(selected.Id, OperationToken);
        if (_config.SelectedAccountId == selected.Id)
        {
            _config.SelectedAccountId = string.Empty;
            _session = null;
            await _config.SaveAsync(OperationToken);
        }
        RefreshAccounts();
        SetStage("READY", "Account signed out of this launcher");
    });

    private void CloseAccounts_Click(object sender, RoutedEventArgs e) => AccountsOverlay.Visibility = Visibility.Collapsed;
    private void CancelOperation_Click(object sender, RoutedEventArgs e)
    {
        _operation?.Cancel();
        CancelOperationButton.IsEnabled = false;
        ActivityText.Text = "Cancelling…";
    }
}
