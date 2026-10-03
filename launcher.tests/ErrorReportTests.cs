using PlutoniumLauncher;
using Xunit;

namespace PlutoniumLauncher.Tests;

public class ErrorReportTests
{
    [Theory]
    [InlineData("Authorization: Bearer short-secret", "short-secret")]
    [InlineData("--accessToken abc.def.ghi", "abc.def.ghi")]
    [InlineData("{\"refresh_token\":\"secret-value\"}", "secret-value")]
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
}
