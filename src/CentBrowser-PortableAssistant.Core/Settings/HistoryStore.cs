namespace CentBrowser_PortableAssistant.Core.Settings;

/// <summary>history.json 会被上传线程和界面线程同时改，统一在这里加锁。</summary>
public sealed class HistoryStore
{
    private readonly object _gate = new();
    private readonly BackupHistory _history;

    public HistoryStore()
    {
        _history = JsonFile.Read<BackupHistory>(AppPaths.HistoryFile) ?? new BackupHistory();
    }

    public T Read<T>(Func<BackupHistory, T> read)
    {
        lock (_gate) return read(_history);
    }

    public void Update(Action<BackupHistory> change)
    {
        lock (_gate)
        {
            change(_history);
            JsonFile.Write(AppPaths.HistoryFile, _history);
        }
    }
}
