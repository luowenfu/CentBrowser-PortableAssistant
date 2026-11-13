namespace CentBrowser_PortableAssistant.Core.Settings;

public enum AutoFrequency
{
    Daily,
    Weekly,
    Monthly,
}

public sealed class AppConfig
{
    /// <summary>旧版全局的浏览器目录，读配置时搬到各方案的 BrowserRoot 后清空。</summary>
    public string BrowserRoot { get; set; } = "";

    /// <summary>开机自启动，关闭窗口后留在托盘。不管自动备份。</summary>
    public bool StartWithWindows { get; set; }

    /// <summary>关掉浏览器后静默备份。不管开机启动和托盘。</summary>
    public bool AutoEnabled { get; set; }

    public AutoFrequency Frequency { get; set; } = AutoFrequency.Daily;

    /// <summary>慢速备份时的上传限速，单位 KB/s，0 表示不限。本机文件夹不受限。</summary>
    public int SlowUploadKbps { get; set; } = 1024;

    public List<BackupPlan> Plans { get; set; } = [];

    public List<UploadTarget> Targets { get; set; } = [];

    public static AppConfig CreateDefault()
    {
        var local = new UploadTarget
        {
            Name = "默认目的地",
            Kind = TargetKind.LocalFolder,
            Address = AppPaths.DefaultLocalBackupDir,
            RemoteDir = "",
        };
        var plan = new BackupPlan { TargetIds = [local.Id] };
        return new AppConfig { Plans = [plan], Targets = [local] };
    }
}
