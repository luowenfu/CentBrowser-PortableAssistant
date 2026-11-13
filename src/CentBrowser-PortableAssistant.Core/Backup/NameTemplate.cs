using System.Text;
using System.Text.RegularExpressions;

namespace CentBrowser_PortableAssistant.Core.Backup;

public sealed record NameValues(
    string Prefix,
    string Plan,
    string PlanSlug,
    DateTime Time,
    bool IncludesProgram,
    string BrowserVersion,
    string Computer);

/// <summary>按模板生成备份文件名，扩展名另外加。拼出来最长 <see cref="MaxLength"/> 个字符。</summary>
public static partial class NameTemplate
{
    public const int MaxLength = 80;

    public static readonly string[] Variables = ["{前缀}", "{方案}", "{日期}", "{时间}", "{类型}", "{浏览器版本}", "{电脑名}"];

    /// <summary>返回 null 表示可用，否则返回给用户看的原因。</summary>
    public static string? Validate(string template)
    {
        if (string.IsNullOrWhiteSpace(template)) return "文件名模板不能为空";
        if (!template.Contains("{时间}")) return "模板里要有 {时间}，否则同一天的备份会重名";
        var unknown = VariablePattern().Matches(template).Select(m => m.Value).FirstOrDefault(v => !Variables.Contains(v));
        return unknown is null ? null : $"不认识的变量 {unknown}";
    }

    public static string Render(string template, NameValues v, bool asciiOnly)
    {
        var plan = asciiOnly ? v.PlanSlug : v.Plan;
        var time = v.Time.ToString("HHmmss");
        var result = template
            .Replace("{前缀}", v.Prefix)
            .Replace("{方案}", plan)
            .Replace("{日期}", v.Time.ToString("yyyyMMdd"))
            .Replace("{时间}", time)
            .Replace("{类型}", v.IncludesProgram ? "Full" : "Data")
            .Replace("{浏览器版本}", v.BrowserVersion)
            .Replace("{电脑名}", v.Computer);
        return Fit(Sanitize(result, asciiOnly), time);
    }

    /// <summary>超长从前面截。时间不在最后一段里时，结果改以时间为结尾，避免同一秒的备份重名。</summary>
    private static string Fit(string name, string time)
    {
        if (name.Length <= MaxLength) return name;
        var at = name.LastIndexOf(time, StringComparison.Ordinal);
        var cut = at < 0 || at >= name.Length - MaxLength
            ? name[^MaxLength..]
            : name[Math.Max(0, at + time.Length - MaxLength)..(at + time.Length)];
        var trimmed = cut.Trim('.', '_');
        return trimmed.Length == 0 ? time : trimmed;
    }

    public static string Sanitize(string name, bool asciiOnly)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var bad = invalid.Contains(c) || char.IsWhiteSpace(c) || (asciiOnly && c > 127);
            sb.Append(bad ? '_' : c);
        }
        var s = sb.ToString().Trim('.', '_');
        return s.Length == 0 ? "backup" : s;
    }

    /// <summary>从中文方案名生成英文标识，取不出字母数字时用 Plan。</summary>
    public static string MakeSlug(string name)
    {
        var ascii = new string(name.Where(c => c < 128 && char.IsLetterOrDigit(c)).ToArray());
        return ascii.Length > 0 ? ascii : "Plan";
    }

    [GeneratedRegex(@"\{[^{}]*\}")]
    private static partial Regex VariablePattern();
}
