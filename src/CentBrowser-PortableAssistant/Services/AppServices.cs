using System.IO;
using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Schedule;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Services;

/// <summary>全程序共用的状态：配置、记录、上传队列、浏览器位置。</summary>
public sealed class AppServices : IDisposable
{
    public AppServices()
    {
        Config = ConfigStore.Load();
        History = new HistoryStore();
        Queue = new UploadQueue(() => Config, History);
        Runner = new BackupRunner(History, Queue);
        Changes = new ChangeProbe(this);
        Watcher = new BrowserWatcher(() => Config.Plans.Where(p => p.AutoBackup)
            .Select(InstallFor).OfType<BrowserInstall>().DistinctBy(i => i.RootDir, StringComparer.OrdinalIgnoreCase).ToList());
    }

    public AppConfig Config { get; private set; }

    public HistoryStore History { get; }

    public UploadQueue Queue { get; }

    public BackupRunner Runner { get; }

    /// <summary>方案数据有没有变化，后台算好缓存着。</summary>
    public ChangeProbe Changes { get; }

    public BrowserWatcher Watcher { get; }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BrowserInstall?> _installs = new(StringComparer.OrdinalIgnoreCase);

    public event Action? ConfigChanged;

    public void SaveConfig()
    {
        ConfigStore.Save(Config);
        // 勾选的内容也算进指纹，改了设置就重算
        Changes.Invalidate();
        Queue.Nudge();
        ConfigChanged?.Invoke();
    }

    /// <summary>方案用的浏览器：手动选的目录，或自动识别。结果按目录缓存。</summary>
    public BrowserInstall? InstallFor(BackupPlan plan) => _installs.GetOrAdd(plan.BrowserRoot, BrowserLocator.Locate);

    /// <summary>浏览器可能升级或挪了位置，重新识别。</summary>
    public void RefreshInstalls() => _installs.Clear();

    /// <summary>重置：删掉设置和还没传完的文件，回到默认。备份记录和 7-Zip 留着。</summary>
    public void Reset()
    {
        AutoStart.Set(false);
        DeleteDir(AppPaths.PendingDir);
        DeleteDir(AppPaths.StagingDir);
        DeleteFile(AppPaths.ConfigFile);
        History.Update(h => h.Plans.Clear());
        Config = ConfigStore.Load();
        Changes.Invalidate();
        RefreshInstalls();
        ConfigChanged?.Invoke();
    }

    /// <summary>卸载：重置要删的之外，再删掉备份记录和释放的 7-Zip。调用前先丢掉上传队列。</summary>
    public void Uninstall()
    {
        AutoStart.Set(false);
        Queue.Dispose();
        DeleteDir(AppPaths.LocalDir);
        DeleteDir(AppPaths.ConfigDir);
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void DeleteDir(string path)
    {
        if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
    }

    public void Dispose()
    {
        Watcher.Dispose();
        Queue.Dispose();
    }
}
