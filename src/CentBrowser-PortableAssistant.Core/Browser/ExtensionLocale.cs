using System.Text.Json.Nodes;

namespace CentBrowser_PortableAssistant.Core.Browser;

/// <summary>把 manifest 里 __MSG_xxx__ 形式的名称查成真实文字，中文优先。</summary>
public static class ExtensionLocale
{
    public static string Resolve(string raw, string versionDir, JsonObject? manifest)
    {
        if (!raw.StartsWith("__MSG_", StringComparison.Ordinal) || !raw.EndsWith("__", StringComparison.Ordinal)) return raw;
        var key = raw[6..^2];

        var defaultLocale = manifest?["default_locale"]?.GetValue<string>();
        string?[] locales = ["zh_CN", "zh", defaultLocale, "en", "en_US"];
        foreach (var locale in locales.Where(l => !string.IsNullOrEmpty(l)).Distinct())
        {
            if (ProfileReader.ReadJson(Path.Combine(versionDir, "_locales", locale!, "messages.json")) is not JsonObject messages) continue;
            foreach (var (k, v) in messages)
            {
                if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase) && v?["message"]?.GetValue<string>() is { Length: > 0 } text)
                    return text;
            }
        }
        return raw;
    }
}
