using System.Text.Json;
using System.Text.Json.Nodes;

namespace CentBrowser_PortableAssistant.Core.Browser;

public static class ProfileReader
{
    /// <summary>只列 Local State 里登记过的用户，没登记的遗留目录浏览器不认。</summary>
    public static IReadOnlyList<ProfileInfo> ReadProfiles(string userDataDir)
    {
        var localState = ReadJson(Path.Combine(userDataDir, "Local State"));
        if (localState?["profile"]?["info_cache"] is not JsonObject cache) return [];

        var order = localState["profile"]?["profiles_order"] is JsonArray arr
            ? arr.Select(n => n?.GetValue<string>() ?? "").ToList()
            : [];

        return cache
            .Where(kv => Directory.Exists(Path.Combine(userDataDir, kv.Key)))
            .Select(kv => new ProfileInfo(kv.Key, kv.Value?["name"]?.GetValue<string>() ?? kv.Key,
                kv.Value?["user_name"]?.GetValue<string>() ?? "",
                kv.Value?["active_time"] is JsonValue t && t.TryGetValue<double>(out var seconds) && seconds > 0
                    ? DateTimeOffset.FromUnixTimeMilliseconds((long)(seconds * 1000)).LocalDateTime
                    : null))
            .OrderBy(p => order.IndexOf(p.DirName) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(p => p.DirName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>最近有人用过的那一天，当天用过的用户都算上。谁都没有使用时间时，退回最后一次用的那个。</summary>
    public static IReadOnlyList<string> ReadRecentProfiles(string userDataDir)
    {
        var profiles = ReadProfiles(userDataDir);
        var dated = profiles.Where(p => p.LastActive is not null).ToList();
        if (dated.Count > 0)
        {
            var latest = dated.Max(p => p.LastActive!.Value.Date);
            return dated.Where(p => p.LastActive!.Value.Date == latest).Select(p => p.DirName).ToList();
        }

        var last = ReadJson(Path.Combine(userDataDir, "Local State"))?["profile"]?["last_used"]?.GetValue<string>();
        return !string.IsNullOrEmpty(last) && profiles.Any(p => p.DirName.Equals(last, StringComparison.OrdinalIgnoreCase))
            ? [last]
            : [];
    }

    /// <summary>
    /// 只列扩展 id，不解析名字也不统计大小。算指纹时用这个，比 <see cref="ReadExtensions"/> 快得多。
    /// </summary>
    public static IReadOnlyList<string> ReadExtensionIds(string profileDir)
    {
        var list = new List<string>();
        foreach (var (id, node) in MergeSettings(profileDir))
        {
            if (node is not JsonObject ext) continue;
            if (ext["location"]?.GetValue<int>() is 4 or 5 or 10) continue;
            if (!Directory.Exists(Path.Combine(profileDir, ProfilePaths.ExtensionsDir, id))) continue;
            list.Add(id);
        }
        return list;
    }

    /// <summary>列出用户安装的扩展，跳过浏览器内置组件和本地解压加载的扩展。</summary>
    public static IReadOnlyList<ExtensionInfo> ReadExtensions(string profileDir)
    {
        var settings = MergeSettings(profileDir);
        var list = new List<ExtensionInfo>();
        foreach (var (id, node) in settings)
        {
            if (node is not JsonObject ext) continue;
            var location = ext["location"]?.GetValue<int>() ?? 0;
            if (location is 4 or 5 or 10) continue;

            var extDir = Path.Combine(profileDir, ProfilePaths.ExtensionsDir, id);
            if (!Directory.Exists(extDir)) continue;

            var manifest = ext["manifest"] as JsonObject;
            var version = manifest?["version"]?.GetValue<string>() ?? "";
            var versionDir = ResolveVersionDir(extDir, version, ext["path"]?.GetValue<string>());
            manifest ??= versionDir is null ? null : ReadJson(Path.Combine(versionDir, "manifest.json")) as JsonObject;

            var rawName = manifest?["name"]?.GetValue<string>() ?? id;
            var name = versionDir is null ? rawName : ExtensionLocale.Resolve(rawName, versionDir, manifest);
            var dataSize = ProfilePaths.ExtensionData(id).Sum(p => ProfilePaths.SizeOf(Path.Combine(profileDir, p)));

            list.Add(new ExtensionInfo(id, name, version, IsEnabled(ext), ProfilePaths.SizeOf(extDir), dataSize));
        }
        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static Dictionary<string, JsonNode?> MergeSettings(string profileDir)
    {
        var merged = new Dictionary<string, JsonNode?>();
        foreach (var file in new[] { ProfilePaths.SecurePreferences, ProfilePaths.Preferences })
        {
            if (ReadJson(Path.Combine(profileDir, file))?["extensions"]?["settings"] is not JsonObject s) continue;
            foreach (var (id, node) in s) merged[id] = node;
        }
        return merged;
    }

    private static string? ResolveVersionDir(string extDir, string version, string? relPath)
    {
        if (!string.IsNullOrEmpty(relPath))
        {
            var fromPath = Path.Combine(Path.GetDirectoryName(extDir)!, relPath);
            if (Directory.Exists(fromPath)) return fromPath;
        }
        var byVersion = Path.Combine(extDir, version + "_0");
        if (Directory.Exists(byVersion)) return byVersion;
        return Directory.GetDirectories(extDir).OrderByDescending(d => d).FirstOrDefault();
    }

    private static bool IsEnabled(JsonObject ext)
    {
        switch (ext["disable_reasons"])
        {
            case JsonArray a when a.Count > 0:
                return false;
            case JsonValue v when v.TryGetValue<int>(out var bits) && bits != 0:
                return false;
        }
        return ext["state"] is not JsonValue state || !state.TryGetValue<int>(out var s) || s != 0;
    }

    internal static JsonNode? ReadJson(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonNode.Parse(fs);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
