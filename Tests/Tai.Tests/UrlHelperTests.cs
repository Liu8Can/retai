using Core.Librarys.Browser;
using Xunit;

namespace Tai.Tests
{
    /// <summary>
    /// Tests for UrlHelper — pure-function URL parsing logic.
    /// These guard the web-browsing data pipeline (used by WebData.cs).
    /// </summary>
    public class UrlHelperTests
    {
        [Theory]
        [InlineData("https://www.google.com/search?q=test", "www.google.com")]
        [InlineData("http://github.com/repo/page", "github.com")]
        [InlineData("https://translate.google.com/", "translate.google.com")]
        [InlineData("ftp://files.example.com/dir/file.txt", "files.example.com")]
        [InlineData("www.bilibili.com/video/BV123", "www.bilibili.com")]
        public void GetDomain_StripsProtocolAndPath(string url, string expected)
        {
            var result = UrlHelper.GetDomain(url);
            Assert.Equal(expected, result);
        }

        [Fact]
        public void GetDomain_EmptyString_ReturnsEmpty()
        {
            var result = UrlHelper.GetDomain("");
            Assert.Equal("", result);
        }

        [Fact]
        public void GetDomain_NullString_ReturnsNull()
        {
            var result = UrlHelper.GetDomain(null);
            Assert.Null(result);
        }

        [Fact]
        public void GetDomain_KeepProtocol_WhenRequested()
        {
            var result = UrlHelper.GetDomain("https://www.google.com/search", isRemovePH_: false);
            Assert.Equal("https://www.google.com", result);
        }

        [Theory]
        [InlineData("https://www.google.com/")]
        [InlineData("https://www.google.com")]
        public void IsIndexUrl_RootUrl_ReturnsTrue(string url)
        {
            Assert.True(UrlHelper.IsIndexUrl(url));
        }

        [Theory]
        [InlineData("https://www.google.com/search?q=test")]
        [InlineData("https://github.com/repo")]
        public void IsIndexUrl_UrlWithPath_ReturnsFalse(string url)
        {
            Assert.False(UrlHelper.IsIndexUrl(url));
        }

        [Theory]
        [InlineData("https://www.google.com/search", "Google")]
        [InlineData("https://github.com/repo", "Github")]
        [InlineData("https://v2ex.com/t/123", "V2EX")]
        [InlineData("https://www.bilibili.com/video/BV1", "哔哩哔哩")]
        public void GetName_KnownDomain_ReturnsFriendlyName(string url, string expected)
        {
            var result = UrlHelper.GetName(url);
            Assert.Equal(expected, result);
        }

        [Theory]
        [InlineData("https://example.com/page", "Example")]
        [InlineData("https://test.org", "Test")]
        public void GetName_UnknownTwoPartDomain_ReturnsCapitalizedSecondLevel(string url, string expected)
        {
            var result = UrlHelper.GetName(url);
            Assert.Equal(expected, result);
        }
    }
}
