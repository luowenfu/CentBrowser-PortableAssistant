namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>记录里的方案名。方案还在就用现在的名字，不在了就用当时记下的。</summary>
public static class RecordPlanName
{
    public static string Show(string? current, string saved) => current ?? saved;
}
