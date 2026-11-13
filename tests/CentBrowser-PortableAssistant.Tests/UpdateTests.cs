using CentBrowser_PortableAssistant.Core.Update;

namespace CentBrowser_PortableAssistant.Tests;

public class UpdateTests
{
    [Fact]
    public void SameVersionIgnoresMissingZeros()
    {
        Assert.True(AppVersion.TryParse("v1.1", out var plain, out var plainLabel));
        Assert.True(AppVersion.TryParse("1.1.0", out var three, out _));
        Assert.True(AppVersion.TryParse("1.1.0.0+abc", out var four, out _));
        Assert.Equal(0, plain.CompareTo(three));
        Assert.Equal(0, plain.CompareTo(four));
        Assert.Equal("1.1", plainLabel);
    }

    [Fact]
    public void NumbersCompareByValue()
    {
        Assert.True(AppVersion.TryParse("1.10", out var ten, out _));
        Assert.True(AppVersion.TryParse("1.9", out var nine, out _));
        Assert.True(ten.CompareTo(nine) > 0);
    }

    [Fact]
    public void NonNumericTagIsRejected() =>
        Assert.False(AppVersion.TryParse("v1.2-beta", out _, out _));

    [Fact]
    public void HigherGiteeWinsAndTiesUseGiteeNotes()
    {
        var gitee = Release("Gitee", "v1.2", "来自码云", "https://gitee.com/dfsss/CentBrowser-PortableAssistant/releases/download/v1.2/CentBrowser-PortableAssistant-v1.2.exe");
        var github = Release("GitHub", "v1.1", "来自 GitHub", "https://github.com/luowenfu/CentBrowser-PortableAssistant/releases/download/v1.1/CentBrowser-PortableAssistant-v1.1.exe", 10, "sha256:" + new string('a', 64));
        AppVersion.TryParse("1.0", out var current, out _);
        var offer = UpdateOffer.Choose(current, selfContained: false, gitee, github);
        Assert.NotNull(offer);
        Assert.Equal("1.2", offer.Label);
        Assert.Equal("来自码云", offer.Notes);
        Assert.Single(offer.Downloads);
        Assert.Equal(UpdateSource.Gitee, offer.Downloads[0].Source);
    }

    [Fact]
    public void EqualVersionsPreferGiteeNotesAndKeepBothDownloads()
    {
        var gitee = Release("Gitee", "v1.2", "码云说明", "https://gitee.com/dfsss/CentBrowser-PortableAssistant/releases/download/v1.2/CentBrowser-PortableAssistant-v1.2.exe");
        var github = Release("GitHub", "v1.2", "GitHub 说明", "https://github.com/luowenfu/CentBrowser-PortableAssistant/releases/download/v1.2/CentBrowser-PortableAssistant-v1.2.exe", 10, "sha256:" + new string('b', 64));
        AppVersion.TryParse("1.1", out var current, out _);
        var offer = UpdateOffer.Choose(current, false, gitee, github);
        Assert.NotNull(offer);
        Assert.Equal("码云说明", offer.Notes);
        Assert.Equal(UpdateSource.Gitee, offer.Downloads[0].Source);
        Assert.Equal(UpdateSource.GitHub, offer.Downloads[1].Source);
        Assert.Equal(new string('b', 64), offer.GitHubDigest);
        Assert.StartsWith("https://gitee.com/", offer.PageUrl);
    }

    [Fact]
    public void MissingPackageStillOffersThePage()
    {
        var github = Release("GitHub", "v1.3", "只有说明", "https://example.com/not-the-package.exe");
        AppVersion.TryParse("1.0", out var current, out _);
        var offer = UpdateOffer.Choose(current, false, null, github);
        Assert.NotNull(offer);
        Assert.Empty(offer.Downloads);
        Assert.Contains("/releases/tag/v1.3", offer.PageUrl);
    }

    [Fact]
    public void PackageNameFollowsFileSize()
    {
        Assert.False(PackageNames.SelfContained(4 * 1024 * 1024));
        Assert.True(PackageNames.SelfContained(70 * 1024 * 1024));
        Assert.Equal("CentBrowser-PortableAssistant-v1.2.exe", PackageNames.FileName("1.2", false));
        Assert.Equal("CentBrowser-PortableAssistant-v1.2-selfcontained.exe", PackageNames.FileName("1.2", true));
    }

    [Fact]
    public void DownloadUrlMustStayOnTheRelease()
    {
        Assert.True(DownloadUrls.IsAllowed("https://github.com/luowenfu/CentBrowser-PortableAssistant/releases/download/v1.2/a.exe", UpdateSource.GitHub));
        Assert.True(DownloadUrls.IsAllowed("https://gitee.com/dfsss/CentBrowser-PortableAssistant/releases/download/v1.2/a.exe", UpdateSource.Gitee));
        Assert.False(DownloadUrls.IsAllowed("http://github.com/luowenfu/CentBrowser-PortableAssistant/releases/download/v1.2/a.exe", UpdateSource.GitHub));
        Assert.False(DownloadUrls.IsAllowed("https://example.com/a.exe", UpdateSource.GitHub));
    }

    [Fact]
    public void DevAndDotnetHostsAreNotReplaced()
    {
        Assert.False(ExeSwap.CanCheck(@"C:\work\bin\Debug\app.exe"));
        Assert.False(ExeSwap.CanCheck(@"C:\work\bin\Release\app.exe"));
        Assert.False(ExeSwap.CanCheck(@"C:\work\obj\app.exe"));
        Assert.False(ExeSwap.CanCheck(@"C:\Program Files\dotnet\dotnet.exe"));
        Assert.True(ExeSwap.CanCheck(@"D:\Tools\CentBrowser-PortableAssistant.exe"));
    }

    [Fact]
    public void FileNameVersionIsReplacedAndOtherNamesStay()
    {
        AppVersion.TryParse("1.2.1", out var patch, out _);
        AppVersion.TryParse("1.2", out var minor, out _);
        Assert.Equal(
            @"D:\Tools\CentBrowser-PortableAssistant-v1.3.exe",
            VersionedName.WithVersion(@"D:\Tools\CentBrowser-PortableAssistant-v1.2.1.exe", patch, "1.3"));
        Assert.Equal(
            @"D:\Tools\CentBrowser-PortableAssistant-v1.3-selfcontained.exe",
            VersionedName.WithVersion(@"D:\Tools\CentBrowser-PortableAssistant-v1.2-selfcontained.exe", minor, "1.3"));
        Assert.Equal(@"D:\Tools\助手.exe", VersionedName.WithVersion(@"D:\Tools\助手.exe", minor, "1.3"));
        Assert.Equal(@"D:\Tools\v1.20.exe", VersionedName.WithVersion(@"D:\Tools\v1.20.exe", minor, "1.3"));
    }

    [Fact]
    public void UnreadableTagFailsThatSide()
    {
        var read = ReleaseInfo.Read("""{"tag_name":"v1.2-beta","prerelease":false,"assets":[]}""", UpdateSource.GitHub);
        Assert.True(read.Failed);
        Assert.Null(read.Release);
    }

    private static ReleaseInfo Release(string source, string tag, string notes, string url, long? size = null, string? digest = null)
    {
        var kind = source == "GitHub" ? UpdateSource.GitHub : UpdateSource.Gitee;
        var json = $$"""
            {"tag_name":"{{tag}}","prerelease":false,"body":"{{notes}}","assets":[
              {"name":"CentBrowser-PortableAssistant-v{{tag[1..]}}.exe","browser_download_url":"{{url}}"{{Size(size)}}{{Digest(digest)}}}
            ]}
            """;
        var read = ReleaseInfo.Read(json, kind);
        Assert.False(read.Failed);
        return read.Release!;
    }

    private static string Size(long? size) => size is null ? "" : $",\"size\":{size}";

    private static string Digest(string? digest) => digest is null ? "" : $",\"digest\":\"{digest}\"";
}
