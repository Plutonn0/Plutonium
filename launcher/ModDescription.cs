using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PlutoniumLauncher;

// Native document rendering: project descriptions never execute HTML, scripts, or embedded web content.
public static class ModDescription
{
    public static FlowDocument Render(string markdown)
    {
        var document = new FlowDocument { Background = Brushes.Transparent, Foreground = Brushes.LightGray,
            FontSize = 13, PagePadding = new Thickness(0), ColumnWidth = double.PositiveInfinity };
        markdown = Regex.Replace(markdown, @"<(script|style)\b[^>]*>[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"</?(?:p|div|h[1-6]|br|li|summary|details)\b[^>]*>", "\n", RegexOptions.IgnoreCase);
        markdown = WebUtility.HtmlDecode(Regex.Replace(markdown, "<[^>]+>", ""));
        var code = false;
        foreach (var raw in markdown.Replace("\r", "").Split('\n'))
        {
            if (raw.TrimStart().StartsWith("```")) { code = !code; continue; }
            var text = raw.Trim(); if (text.Length == 0) continue;
            var heading = Regex.Match(text, @"^(#{1,6})\s+(.+)$");
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, heading.Success ? 14 : 9), LineHeight = 21 };
            if (heading.Success) { text = heading.Groups[2].Value; paragraph.FontSize = Math.Max(15, 26 - heading.Groups[1].Length * 2); paragraph.FontWeight = FontWeights.SemiBold; paragraph.Foreground = Brushes.White; paragraph.Margin = new Thickness(0, 15, 0, 12); }
            if (code) { paragraph.FontFamily = new FontFamily("Consolas"); paragraph.Background = new SolidColorBrush(Color.FromRgb(25, 25, 25)); paragraph.Inlines.Add(new Run(raw)); }
            else
            {
                text = Regex.Replace(text, @"!\[([^\]]*)\]\([^)]*\)", "$1");
                text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]*\)", "$1");
                text = Regex.Replace(text, @"^[-*+]\s+", "• ");
                var start = 0;
                foreach (Match match in Regex.Matches(text, @"\*\*(.+?)\*\*|`([^`]+)`"))
                {
                    paragraph.Inlines.Add(new Run(text[start..match.Index]));
                    if (match.Groups[1].Success) paragraph.Inlines.Add(new Bold(new Run(match.Groups[1].Value)));
                    else paragraph.Inlines.Add(new Run(match.Groups[2].Value) { FontFamily = new FontFamily("Consolas"), Foreground = Brushes.White });
                    start = match.Index + match.Length;
                }
                paragraph.Inlines.Add(new Run(text[start..]));
            }
            document.Blocks.Add(paragraph);
        }
        return document;
    }
}
