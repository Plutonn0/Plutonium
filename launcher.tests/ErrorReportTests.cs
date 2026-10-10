using PlutoniumLauncher;
using System.IO;
using Xunit;

namespace PlutoniumLauncher.Tests;

public class ErrorReportTests
{
    [Theory]
    [InlineData("Authorization: Bearer short-secret", "short-secret")]
    [InlineData("--accessToken abc.def.ghi", "abc.def.ghi")]
    [InlineData("{\"refresh_token\":\"secret-value\"}", "secret-value")]
    [InlineData("device_code=private-device", "private-device")]
    [InlineData("deviceToken: private-device-token", "private-device-token")]
    [InlineData("id_token=private-id", "private-id")]
    [InlineData("user_code: ABCD1234", "ABCD1234")]
    [InlineData(@"C:\Users\PrivateName\game\logs", "PrivateName")]
    [InlineData("account person@example.com failed", "person@example.com")]
    public void ReportsRemoveCredentialsAndPersonalPaths(string input, string secret)
    {
        Assert.DoesNotContain(secret, ErrorReport.Redact(input));
    }

    [Fact]
    public void EmailDraftEscapesContentAndUsesOnlyCreatorRecipient()
    {
        var uri = ErrorReport.MailTo("Failure &bcc=someone@example.com\nUnicode: ł");
        Assert.StartsWith("mailto:justquirk.business@gmail.com?subject=", uri);
        Assert.DoesNotContain("&bcc=", uri);
        Assert.Contains("%26bcc%3D", uri);
    }

    [Fact]
    public void ReportsIncludeNestedFailuresWithCredentialsRedacted()
    {
        var error = new InvalidOperationException("Could not restore files",
            new AggregateException(new IOException("Original operation failed"),
                new IOException("Recovery failed with device_token=secret-token")));
        string report = ErrorReport.Create(error, "test", "Fabric");
        Assert.Contains("Original operation failed", report);
        Assert.Contains("Recovery failed", report);
        Assert.DoesNotContain("secret-token", report);
    }
}
