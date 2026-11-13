namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>能直接给用户看的备份错误。</summary>
public sealed class BackupException(string message, bool browserRunning = false, bool stoppedForBrowser = false) : Exception(message)
{
    public bool BrowserRunning { get; } = browserRunning;

    /// <summary>提取数据时浏览器打开了，主动让路，不算出错。</summary>
    public bool StoppedForBrowser { get; } = stoppedForBrowser;
}
