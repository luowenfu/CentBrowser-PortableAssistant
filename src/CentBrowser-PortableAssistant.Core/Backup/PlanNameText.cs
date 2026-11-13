namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>主页面上方案名的可见长度。存下来的名字和打包文件名不动。</summary>
public static class PlanNameText
{
    public const int VisibleLength = 8;

    public static string Clip(string name) =>
        name.Length <= VisibleLength ? name : name[..VisibleLength] + "…";

    /// <summary>句子里出现的方案名收到可见长度。长名字先替换，避免它的开头被另一个方案名截断。</summary>
    public static string ClipWithin(string text, IEnumerable<string> names)
    {
        foreach (var name in names.Where(n => n.Length > VisibleLength).Distinct().OrderByDescending(n => n.Length))
            text = text.Replace(name, Clip(name));
        return text;
    }
}
