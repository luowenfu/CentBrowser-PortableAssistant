using System.Text.RegularExpressions;

namespace CentBrowser_PortableAssistant.Core.Update;

/// <summary>版本号按数字逐段比。缺的段当作 0，所以 1.1 和 1.1.0 是同一个版本。</summary>
public readonly record struct AppVersion(int Major, int Minor, int Build, int Revision) : IComparable<AppVersion>
{
    public static bool TryParse(string? raw, out AppVersion version, out string label)
    {
        version = default;
        label = "";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var text = raw.Trim();
        if (text[0] is 'v' or 'V') text = text[1..];
        var plus = text.IndexOf('+');
        if (plus >= 0) text = text[..plus];
        if (text.Length == 0) return false;

        var parts = text.Split('.');
        if (parts.Length is < 1 or > 4) return false;
        var nums = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Length == 0 || !parts[i].All(char.IsAsciiDigit)) return false;
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out nums[i])) return false;
        }
        version = new AppVersion(nums[0], nums[1], nums[2], nums[3]);
        label = text;
        return true;
    }

    public int CompareTo(AppVersion other)
    {
        var major = Major.CompareTo(other.Major);
        if (major != 0) return major;
        var minor = Minor.CompareTo(other.Minor);
        if (minor != 0) return minor;
        var build = Build.CompareTo(other.Build);
        if (build != 0) return build;
        return Revision.CompareTo(other.Revision);
    }
}

/// <summary>按正在运行的 exe 有多大，决定下哪一种包。不看文件名。</summary>
public static class PackageNames
{
    public const long SelfContainedFrom = 20L * 1024 * 1024;

    public static bool SelfContained(long bytes) => bytes >= SelfContainedFrom;

    public static string FileName(string label, bool selfContained) => selfContained
        ? $"CentBrowser-PortableAssistant-v{label}-selfcontained.exe"
        : $"CentBrowser-PortableAssistant-v{label}.exe";
}

/// <summary>文件名里等于当前版本的那一段，换成新版本号。没有这段就保持原名。</summary>
public static class VersionedName
{
    private static readonly Regex Token = new(
        @"(?<![0-9.])(?<token>v?(?<num>\d+(?:\.\d+){0,3}))(?!\d|\.\d)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static string WithVersion(string exePath, AppVersion current, string newLabel)
    {
        if (string.IsNullOrWhiteSpace(newLabel)) return exePath;
        var name = Path.GetFileName(exePath);
        var updated = Token.Replace(name, match =>
        {
            if (!AppVersion.TryParse(match.Groups["num"].Value, out var parsed, out _)) return match.Value;
            if (parsed.CompareTo(current) != 0) return match.Value;
            var head = match.Groups["token"].Value;
            var prefix = head.Length > 0 && (head[0] is 'v' or 'V') ? head[..1] : "";
            return prefix + newLabel;
        });
        if (updated == name) return exePath;
        var dir = Path.GetDirectoryName(exePath);
        return string.IsNullOrEmpty(dir) ? updated : Path.Combine(dir, updated);
    }
}
