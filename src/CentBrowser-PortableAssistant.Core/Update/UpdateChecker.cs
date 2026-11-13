namespace CentBrowser_PortableAssistant.Core.Update;

public sealed record FetchOutcome(
    ReleaseInfo? Gitee,
    ReleaseInfo? GitHub,
    bool GiteeFailed,
    bool GitHubFailed,
    DateTime? GiteeBlockedUntil,
    DateTime? GitHubBlockedUntil)
{
    public bool AnySucceeded => !GiteeFailed || !GitHubFailed;
}

/// <summary>同时问 GitHub 和 Gitee 的最新正式版。一边失败不影响另一边。</summary>
public static class UpdateChecker
{
    public const string GitHubLatest = "https://api.github.com/repos/luowenfu/CentBrowser-PortableAssistant/releases/latest";
    public const string GiteeLatest = "https://gitee.com/api/v5/repos/dfsss/CentBrowser-PortableAssistant/releases/latest";

    public static async Task<FetchOutcome> FetchAsync(
        DateTime? giteeBlockedUntil,
        DateTime? githubBlockedUntil,
        CancellationToken ct)
    {
        var gitee = FetchOneAsync(UpdateSource.Gitee, GiteeLatest, giteeBlockedUntil, ct);
        var github = FetchOneAsync(UpdateSource.GitHub, GitHubLatest, githubBlockedUntil, ct);
        await Task.WhenAll(gitee, github);
        var giteeResult = await gitee;
        var githubResult = await github;
        return new FetchOutcome(
            giteeResult.Release, githubResult.Release,
            giteeResult.Failed, githubResult.Failed,
            giteeResult.BlockedUntil, githubResult.BlockedUntil);
    }

    private static async Task<OneResult> FetchOneAsync(UpdateSource source, string url, DateTime? blockedUntil, CancellationToken ct)
    {
        if (blockedUntil is { } until && until > DateTime.UtcNow)
            return new OneResult(null, true, blockedUntil);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (source == UpdateSource.GitHub)
            {
                request.Headers.Accept.ParseAdd("application/vnd.github+json");
                request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");
            }
            using var response = await UpdateHttp.SendAsync(request, TimeSpan.FromSeconds(15), ct);
            if (response.StatusCode is System.Net.HttpStatusCode.Forbidden or System.Net.HttpStatusCode.TooManyRequests)
                return new OneResult(null, true, DateTime.UtcNow.AddHours(1));
            if (!response.IsSuccessStatusCode) return new OneResult(null, true, null);
            var json = await response.Content.ReadAsStringAsync(ct);
            var read = ReleaseInfo.Read(json, source);
            return new OneResult(read.Release, read.Failed, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new OneResult(null, true, null);
        }
    }

    private sealed record OneResult(ReleaseInfo? Release, bool Failed, DateTime? BlockedUntil);
}
