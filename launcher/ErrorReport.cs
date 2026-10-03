using System.Text.RegularExpressions;

namespace PlutoniumLauncher;

public static class ErrorReport
{
    public const string Recipient = "justquirk.business@gmail.com";
    public static string Redact(string value)
    {
        value = Regex.Replace(value, @"(?i)\bBearer\s+[^\s,""'}]+", "Bearer [redacted]");
        value = Regex.Replace(value, @"(?i)(access.?token|refresh.?token|authorization|bearer|password|client.?secret|session.?id)([\s=:""']+)[^\s,""'}]+", "$1$2[redacted]");
        value = Regex.Replace(value, @"(?i)[A-Z]:\\Users\\[^\\\r\n]+", @"C:\Users\[user]");
        value = Regex.Replace(value, @"\b[A-Za-z0-9_-]{64,}\b", "[redacted]");
        value = Regex.Replace(value, @"[\w.+-]+@[\w.-]+\.[A-Za-z]{2,}", "[email]");
        return value;
    }
    public static string Create(Exception error, string version, string profile) =>
        Redact($"Plutonium launcher {version}\nProfile: {profile}\nWindows: {Environment.OSVersion.Version}\nTime: {DateTimeOffset.UtcNow:O}\n\n{error.GetType().Name}: {error.Message}\n{error.StackTrace}");
    public static string MailTo(string report) => $"mailto:{Recipient}?subject=Plutonium%20error%20report&body={Uri.EscapeDataString(report[..Math.Min(report.Length, 1600)])}";
}
