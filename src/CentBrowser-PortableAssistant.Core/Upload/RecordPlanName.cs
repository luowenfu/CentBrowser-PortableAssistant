namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>记录里的方案名。方案还在就用现在的名字；删了用当时记下的，并标已删除。</summary>
public static class RecordPlanName
{
    public static string Show(string? current, string saved) =>
        current is not null ? current
        : saved.Length > 0 ? $"{saved}（已删除）"
        : "已删除的方案";
}
