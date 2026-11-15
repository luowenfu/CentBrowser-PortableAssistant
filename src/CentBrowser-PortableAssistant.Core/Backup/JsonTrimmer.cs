using System.Text.Json.Nodes;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>精简 Local State 和 Preferences，去掉换电脑后无用或不该带走的部分。</summary>
public static class JsonTrimmer
{
    private static readonly int[] ComponentLocations = [5, 10];

    /// <summary>删掉本机加密密钥，用户列表只留选中的。写上当前版本，避免解压后弹出选用 Chrome 的提示。</summary>
    /// <param name="keepOsCrypt">机密信息要靠这把钥匙在本机还原，勾了才留下。</param>
    public static JsonNode TrimLocalState(JsonNode localState, IReadOnlyCollection<string> keepProfiles, string version, bool keepOsCrypt = false)
    {
        var root = localState.AsObject();
        if (!keepOsCrypt) root.Remove("os_crypt");
        SuppressDefaultBrowserPrompt(root, version);

        if (root["profile"] is not JsonObject profile) return root;
        bool Keep(string dir) => keepProfiles.Contains(dir, StringComparer.OrdinalIgnoreCase);

        if (profile["info_cache"] is JsonObject cache)
        {
            foreach (var key in cache.Select(kv => kv.Key).Where(k => !Keep(k)).ToList()) cache.Remove(key);
        }
        FilterArray(profile, "profiles_order", Keep);
        FilterArray(profile, "last_active_profiles", Keep);

        var lastUsed = profile["last_used"]?.GetValue<string>();
        if (lastUsed is null || !Keep(lastUsed))
        {
            var first = keepProfiles.FirstOrDefault();
            if (first is null) profile.Remove("last_used");
            else profile["last_used"] = first;
        }
        return root;
    }

    /// <summary>
    /// keepExtensions 为空集合表示不带任何扩展。settingsIncluded 为 false 时只留扩展登记，
    /// 其余设置交给浏览器在新电脑上按默认值生成。
    /// </summary>
    public static JsonNode TrimPreferences(JsonNode prefs, ISet<string> keepExtensions, bool settingsIncluded)
    {
        var root = prefs.AsObject();
        if (root["extensions"]?["settings"] is JsonObject settings)
        {
            var drop = settings
                .Where(kv => !IsComponent(kv.Value) && !keepExtensions.Contains(kv.Key))
                .Select(kv => kv.Key)
                .ToList();
            foreach (var id in drop) settings.Remove(id);

            if (root["protection"]?["macs"]?["extensions"]?["settings"] is JsonObject macs)
            {
                foreach (var id in drop) macs.Remove(id);
            }
        }

        if (settingsIncluded) return root;

        var slim = new JsonObject();
        if (root["extensions"]?["settings"] is JsonObject kept)
        {
            slim["extensions"] = new JsonObject { ["settings"] = kept.DeepClone() };
        }
        return slim;
    }

    /// <summary>
    /// 浏览器不是系统默认浏览器时会弹「Choose Chrome」。
    /// 这项和程序版本一致就不再弹。记在 Local State 里，不跟某个用户走。
    /// </summary>
    private static void SuppressDefaultBrowserPrompt(JsonObject root, string version)
    {
        if (string.IsNullOrWhiteSpace(version)) return;
        if (root["browser"] is not JsonObject browser)
        {
            browser = new JsonObject();
            root["browser"] = browser;
        }
        browser["suppress_default_browser_prompt_for_version"] = version;
    }

    private static bool IsComponent(JsonNode? ext) =>
        ext?["location"] is JsonValue v && v.TryGetValue<int>(out var loc) && ComponentLocations.Contains(loc);

    private static void FilterArray(JsonObject parent, string name, Func<string, bool> keep)
    {
        if (parent[name] is not JsonArray arr) return;
        var kept = arr.Select(n => n?.GetValue<string>()).Where(s => s is not null && keep(s)).ToList();
        var replacement = new JsonArray();
        foreach (var s in kept) replacement.Add(JsonValue.Create(s));
        parent[name] = replacement;
    }
}
