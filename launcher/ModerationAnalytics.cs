using System.Globalization;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace PlutoniumLauncher;

// A read-only view: data and permissions still come exclusively from the moderation API.
internal static class ModerationAnalytics
{
    private static TextBlock Text(string value, double size = 12, bool muted = false) => new()
    { Text = value, FontSize = size, Foreground = muted ? Brushes.DarkGray : Brushes.WhiteSmoke, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };
    private static Border Panel(UIElement child) => new()
    { Child = child, Padding = new Thickness(20), Margin = new Thickness(0, 0, 12, 12), CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(Color.FromRgb(20, 21, 25)), BorderBrush = new SolidColorBrush(Color.FromRgb(47, 49, 56)), BorderThickness = new Thickness(1) };
    private static Border Metric(string title, string value, string detail)
    {
        var body = new StackPanel(); body.Children.Add(Text(title.ToUpperInvariant(), 10, true));
        body.Children.Add(Text(value, 32)); body.Children.Add(Text(detail, 11, true));
        var panel = Panel(body); panel.MinHeight = 140; return panel;
    }
    internal static string Growth(int current, int previous) => previous == 0
        ? (current == 0 ? "No activity in either period" : "No previous baseline")
        : $"{(current - previous) * 100.0 / previous:+0.0;-0.0;0}% vs previous 7 days";
    internal static FrameworkElement Build(JsonElement data)
    {
        var root = new StackPanel();
        var a = data.GetProperty("accounts"); var installs = data.GetProperty("installs"); var weekly = data.GetProperty("weekly");
        var cards = new UniformGrid { Columns = 3 };
        cards.SizeChanged += (_, _) => { var columns=cards.ActualWidth >= 720 ? 3 : cards.ActualWidth >= 440 ? 2 : 1; if(cards.Columns!=columns)cards.Columns=columns; };
        cards.Children.Add(Metric("Active now", a.GetProperty("active").ToString(), "Accounts seen in the last 5 minutes"));
        cards.Children.Add(Metric("Playing", a.GetProperty("playing").ToString(), "Client heartbeat in the last 2 minutes"));
        cards.Children.Add(Metric("Weekly active", weekly.GetProperty("this_week").ToString(), Growth(weekly.GetProperty("this_week").GetInt32(), weekly.GetProperty("last_week").GetInt32())));
        cards.Children.Add(Metric("Reported installs", installs.GetProperty("total").ToString(), $"{installs.GetProperty("this_week")} new · {Growth(installs.GetProperty("this_week").GetInt32(), installs.GetProperty("last_week").GetInt32())}"));
        cards.Children.Add(Metric("Accounts seen", a.GetProperty("total").ToString(), $"{a.GetProperty("disabled")} currently restricted"));
        cards.Children.Add(Metric("Awaiting review", data.GetProperty("pendingAppeals").ToString(), "Pending account appeals"));
        root.Children.Add(cards);
        if (data.TryGetProperty("daily", out var daily))
        {
            var chartBody = new StackPanel(); chartBody.Children.Add(Text("Activity over time", 18));
            chartBody.Children.Add(Text("Last 14 UTC days · white: active accounts · gray: new reported installs", 11, true));
            var points = daily.EnumerateArray().ToArray(); var maximum = Math.Max(1, points.Select(p => Math.Max(p.GetProperty("active").GetInt32(), p.GetProperty("installs").GetInt32())).DefaultIfEmpty(0).Max());
            chartBody.Children.Add(Text($"Shared count scale: 0 – {maximum}",10,true));
            var chart = new UniformGrid { Rows = 1, Columns = Math.Max(1, points.Length), Height = 180, Margin = new Thickness(0, 12, 0, 0) };
            foreach (var point in points)
            {
                var active = point.GetProperty("active").GetInt32(); var added = point.GetProperty("installs").GetInt32();
                var day = point.GetProperty("day").GetString()!;
                var column = new Grid { Margin = new Thickness(2, 0, 2, 0), ToolTip = $"{day} UTC\n{active} active accounts\n{added} reported installs" };
                column.RowDefinitions.Add(new RowDefinition()); column.RowDefinitions.Add(new RowDefinition { Height = new GridLength(28) });
                var bars = new UniformGrid { Rows = 1, Columns = 2, VerticalAlignment = VerticalAlignment.Bottom };
                bars.Children.Add(new Border { Height = Math.Max(1, 145.0 * active / maximum), Background = Brushes.WhiteSmoke, Margin = new Thickness(1), CornerRadius = new CornerRadius(2, 2, 0, 0), VerticalAlignment = VerticalAlignment.Bottom });
                bars.Children.Add(new Border { Height = Math.Max(1, 145.0 * added / maximum), Background = Brushes.DimGray, Margin = new Thickness(1), CornerRadius = new CornerRadius(2, 2, 0, 0), VerticalAlignment = VerticalAlignment.Bottom });
                column.Children.Add(bars); var label = Text(DateTime.Parse(day, CultureInfo.InvariantCulture).ToString("dd", CultureInfo.InvariantCulture), 10, true); label.HorizontalAlignment = HorizontalAlignment.Center; label.Margin = new Thickness(0, 8, 0, 0); Grid.SetRow(label, 1); column.Children.Add(label); chart.Children.Add(column);
                System.Windows.Automation.AutomationProperties.SetName(column, column.ToolTip.ToString());
            }
            chartBody.Children.Add(chart);
            chartBody.Children.Add(Text(points.Length == 0 || points.All(p => p.GetProperty("active").GetInt32() == 0 && p.GetProperty("installs").GetInt32() == 0) ? "No activity recorded in this period yet." : $"{points[0].GetProperty("day").GetString()} — {points[^1].GetProperty("day").GetString()} · Today is still in progress · Hover a day for counts", 11, true));
            root.Children.Add(Panel(chartBody));
        }
        var bottom = new WrapPanel();
        var versions = new StackPanel(); versions.Children.Add(Text("Launcher versions", 17)); versions.Children.Add(Text("Accounts active in the latest 7 UTC days", 11, true));
        if (data.TryGetProperty("versions", out var items) && items.GetArrayLength() > 0)
            foreach (var item in items.EnumerateArray()) versions.Children.Add(Text($"v{item.GetProperty("version").GetString()}     {item.GetProperty("count")} accounts", 13));
        else versions.Children.Add(Text("No version data reported yet.", 12, true));
        var versionPanel = Panel(versions); versionPanel.Width = 330; bottom.Children.Add(versionPanel);
        if (data.TryGetProperty("reviews", out var reviews))
        {
            var body = new StackPanel(); body.Children.Add(Text("Review outcomes", 17)); body.Children.Add(Text("Latest 7 UTC days", 11, true));
            body.Children.Add(Text($"{reviews.GetProperty("accepted")} accepted   /   {reviews.GetProperty("rejected")} rejected", 20));
            body.Children.Add(Text("Accepted appeals restore client access.", 11, true));
            var reviewPanel = Panel(body); reviewPanel.Width = 330; bottom.Children.Add(reviewPanel);
        }
        root.Children.Add(bottom); root.Children.Add(Text(data.GetProperty("definition").GetString() ?? "", 11, true));
        return root;
    }
}
