using Go2HDR.Services;
using System.Text.Json;
using Xunit;

namespace Go2HDR.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("https://github.com/Rayekkk/Go2HDR/releases/tag/2.1.2", true)]
    [InlineData("https://github.com/rayekkk/go2hdr/releases/latest", true)]
    [InlineData("http://github.com/Rayekkk/Go2HDR/releases/tag/2.1.2", false)]
    [InlineData("https://github.com/Other/Go2HDR/releases/tag/2.1.2", false)]
    [InlineData("https://user@github.com/Rayekkk/Go2HDR/releases/tag/2.1.2", false)]
    [InlineData("https://github.com:444/Rayekkk/Go2HDR/releases/tag/2.1.2", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("not a URL", false)]
    public void TrustedReleaseUrl_IsRestrictedToOfficialHttpsReleases(string url, bool expected)
    {
        Assert.Equal(expected, UpdateService.TryGetTrustedReleaseUri(url, out _));
    }

    [Theory]
    [InlineData("2.1", "2.1.0.0")]
    [InlineData("2.1.2", "2.1.2.0")]
    [InlineData("2.1.2.3", "2.1.2.3")]
    public void NormalizeVersion_FillsMissingComponents(string input, string expected)
    {
        Assert.Equal(Version.Parse(expected), UpdateService.NormalizeVersion(Version.Parse(input)));
    }

    [Theory]
    [InlineData("v2.1.3", "2.1.2", true)]
    [InlineData("2.1.2", "2.1.2.0", false)]
    [InlineData("2.1.1", "2.1.2", false)]
    public void ReleaseResponse_IsParsedAndCompared(string tag, string current, bool isNewer)
    {
        using JsonDocument document = JsonDocument.Parse($$"""
            { "tag_name": "{{tag}}", "html_url": "https://github.com/Rayekkk/Go2HDR/releases/tag/{{tag}}" }
            """);

        Assert.True(UpdateService.TryCreateResult(
            document.RootElement, Version.Parse(current), out UpdateResult? result));
        Assert.NotNull(result);
        Assert.Equal(isNewer, result.IsNewer);
    }

    [Fact]
    public void ReleaseResponse_RejectsUntrustedUrl()
    {
        using JsonDocument document = JsonDocument.Parse("""
            { "tag_name": "9.9.9", "html_url": "https://example.com/installer.exe" }
            """);

        Assert.False(UpdateService.TryCreateResult(
            document.RootElement, new Version(2, 1, 2), out UpdateResult? result));
        Assert.Null(result);
    }
}
