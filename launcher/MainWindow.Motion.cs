using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PlutoniumLauncher;

public partial class MainWindow
{
    private readonly DispatcherTimer _activityTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private DownloadItem? _displayedDownload;
    private void InitializeActivity()
    {
        _activityTimer.Tick += (_, _) => RefreshActivity();
        Loaded += (_, _) => { _activityTimer.Start(); RefreshActivity(); };
        Closed += (_, _) => _activityTimer.Stop();
    }
    private void RefreshActivity()
    {
        var active = _downloads.Items.FirstOrDefault(i => i.Active);
        var item = active ?? _downloads.Items.FirstOrDefault();
        var spinning = active?.IsTransferring == true || (active is null && _busy);
        DownloadSpinner.IsRunning = FooterSpinner.IsRunning = spinning;
        DownloadSpinner.Visibility = FooterSpinner.Visibility = spinning ? Visibility.Visible : Visibility.Collapsed;
        if (item is null)
        {
            DownloadTitle.Text = _busy ? "Getting things ready" : "All caught up";
            DownloadDetail.Text = _busy ? ActivityText.Text : "Downloads appear here as they happen.";
            TransferText.Text = _busy ? ActivityText.Text : "PLUTONIUM / READY WHEN YOU ARE";
            DownloadProgress.Value = 0;
            return;
        }
        DownloadTitle.Text = item.Name;
        DownloadDetail.Text = item.Status + " · " + item.Detail;
        TransferText.Text = _busy && active is null ? ActivityText.Text : item.Status + " · " + item.Name + (active is not null && item.Percent > 0 ? $" · {item.Percent:0}%" : "");
        var value = Math.Clamp(item.Percent, 0, 100);
        var previous = DownloadProgress.Value;
        if (Math.Abs(previous - value) > .05)
        {
            DownloadProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
            DownloadProgress.Value = value;
            if (ReferenceEquals(item, _displayedDownload) && SystemParameters.ClientAreaAnimation)
                DownloadProgress.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty,
                    new DoubleAnimation(previous, value, TimeSpan.FromMilliseconds(150)) { FillBehavior = FillBehavior.Stop });
        }
        _displayedDownload = item;
    }
}
