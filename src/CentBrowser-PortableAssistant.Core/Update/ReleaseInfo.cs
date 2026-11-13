using System.Text.Json;

namespace CentBrowser_PortableAssistant.Core.Update;

public enum UpdateSource
{
    Gitee,
    GitHub,
}

public readonly record struct ReleaseRead(ReleaseInfo? Release, bool Failed);

public sealed record ReleaseAsset(string Name, long? Size, string? Digest, string Url);

/// <summary>一个来源查到的最新正式版。标签读不出数字版本时不会产生这一项。</summary>
public sealed record ReleaseInfo(UpdateSource Source, string Tag, string Label, AppVersion Version, string Notes, IReadOnlyList<ReleaseAsset> Assets)
{
    public string PageUrl => Source switch
    {
        UpdateSource.GitHub => $"https://github.com/luowenfu/CentBrowser-PortableAssistant/releases/tag/{Tag}",
        _ => $"https://gitee.com/dfsss/CentBrowser-PortableAssistant/releases/tag/{Tag}",
    };

    /// <summary>预发布不算失败，只是没有可用版本。标签不是数字版本算这一边失败。</summary>
    public static ReleaseRead Read(string json, UpdateSource source)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { return new ReleaseRead(null, true); }
        using (doc)
        {
            var root = doc.RootElement;
            if (Flag(root, "prerelease") || Flag(root, "draft")) return new ReleaseRead(null, false);
            if (!root.TryGetProperty("tag_name", out var tagProp)) return new ReleaseRead(null, true);
            var tag = tagProp.GetString() ?? "";
            if (!AppVersion.TryParse(tag, out var version, out var label)) return new ReleaseRead(null, true);
            var notes = root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.String
                ? body.GetString() ?? ""
                : "";
            var assets = new List<ReleaseAsset>();
            if (root.TryGetProperty("assets", out var list) && list.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in list.EnumerateArray())
                {
                    var name = Text(item, "name");
                    var url = Text(item, "browser_download_url");
                    if (name.Length == 0 || url.Length == 0) continue;
                    long? size = item.TryGetProperty("size", out var sizeProp) && sizeProp.TryGetInt64(out var bytes) ? bytes : null;
                    var digest = DigestOf(Text(item, "digest"));
                    assets.Add(new ReleaseAsset(name, size, digest, url));
                }
            }
            return new ReleaseRead(new ReleaseInfo(source, tag, label, version, notes.Trim(), assets), false);
        }
    }

    private static bool Flag(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;

    private static string Text(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? ""
            : "";

    private static string? DigestOf(string raw)
    {
        const string prefix = "sha256:";
        if (!raw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
        var hex = raw[prefix.Length..].Trim().ToLowerInvariant();
        return hex.Length == 64 && hex.All(Uri.IsHexDigit) ? hex : null;
    }
}

public sealed record DownloadSource(UpdateSource Source, string Url, long? Size);

/// <summary>两边合并后给界面用的结果。下载列表已经按先 Gitee 后 GitHub 排好。</summary>
public sealed record UpdateOffer(
    string Label,
    string Notes,
    string PageUrl,
    IReadOnlyList<DownloadSource> Downloads,
    long? GitHubSize,
    string? GitHubDigest)
{
    /// <summary>没有比当前更高的版本时返回 null。有更高版本但没有对应安装包时，下载列表是空的。</summary>
    public static UpdateOffer? Choose(AppVersion current, bool selfContained, ReleaseInfo? gitee, ReleaseInfo? github)
    {
        var found = new[] { gitee, github }.Where(item => item is not null).Cast<ReleaseInfo>().ToList();
        if (found.Count == 0) return null;
        var best = found.Select(item => item.Version).Max();
        if (best.CompareTo(current) <= 0) return null;

        var same = found.Where(item => item.Version.CompareTo(best) == 0).ToList();
        var notes = same.FirstOrDefault(item => item.Source == UpdateSource.Gitee) ?? same[0];
        var githubRelease = same.FirstOrDefault(item => item.Source == UpdateSource.GitHub);
        ReleaseAsset? githubAsset = null;
        if (githubRelease is not null)
            githubAsset = Find(githubRelease, selfContained);

        var downloads = new List<DownloadSource>();
        foreach (var source in new[] { UpdateSource.Gitee, UpdateSource.GitHub })
        {
            var release = same.FirstOrDefault(item => item.Source == source);
            if (release is null) continue;
            var asset = Find(release, selfContained);
            if (asset is null || !DownloadUrls.IsAllowed(asset.Url, source)) continue;
            downloads.Add(new DownloadSource(source, asset.Url, asset.Size));
        }
        return new UpdateOffer(notes.Label, notes.Notes, notes.PageUrl, downloads, githubAsset?.Size, githubAsset?.Digest);
    }

    private static ReleaseAsset? Find(ReleaseInfo release, bool selfContained)
    {
        var name = PackageNames.FileName(release.Label, selfContained);
        return release.Assets.FirstOrDefault(asset => asset.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }
}

public static class DownloadUrls
{
    public static bool IsAllowed(string url, UpdateSource source)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps) return false;
        var prefix = source == UpdateSource.GitHub
            ? "https://github.com/luowenfu/CentBrowser-PortableAssistant/releases/download/"
            : "https://gitee.com/dfsss/CentBrowser-PortableAssistant/releases/download/";
        return url.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
