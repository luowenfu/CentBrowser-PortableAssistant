using System.Collections.Concurrent;
using System.IO;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Services;

/// <summary>
/// 方案勾选的数据有没有变化。算指纹要把要带走的文件全扫一遍，几百毫秒到几秒不等，
/// 所以只在后台算，界面线程一律读缓存。
/// </summary>
public sealed class ChangeProbe(AppServices app)
{
    private static readonly TimeSpan Fresh = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, (bool Changed, DateTime At)> _cache = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private int _queued;

    /// <summary>后台算完了，悬停文字和图标可以刷新。</summary>
    public event Action? Updated;

    public void Invalidate() => _cache.Clear();

    public void Invalidate(string planId) => _cache.TryRemove(planId, out _);

    /// <summary>读缓存。没有或者过期就排一次后台计算，这次先按"有变化"算。</summary>
    public bool Changed(BackupPlan plan)
    {
        var hit = _cache.TryGetValue(plan.Id, out var entry);
        if (!hit || DateTime.UtcNow - entry.At > Fresh) Refresh();
        return !hit || entry.Changed;
    }

    /// <summary>后台现算一次并写进缓存。自动备份决定要不要跑时用这个。</summary>
    public bool Compute(BackupPlan plan, BrowserInstall install)
    {
        var changed = app.Runner.HasChanged(install, plan, out _);
        _cache[plan.Id] = (changed, DateTime.UtcNow);
        return changed;
    }

    /// <summary>把加入自动备份的方案算一遍。同时只排一次，算完通知一次。</summary>
    private void Refresh()
    {
        if (Interlocked.Exchange(ref _queued, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            Interlocked.Exchange(ref _queued, 0);
            try
            {
                foreach (var plan in app.Config.Plans.Where(p => p.AutoBackup).ToList())
                {
                    if (app.InstallFor(plan) is not { } install) continue;
                    try
                    {
                        Compute(plan, install);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 浏览器正在写文件或者目录没权限，这次算不出来，下次再说
                    }
                }
            }
            finally
            {
                _gate.Release();
            }
            Updated?.Invoke();
        });
    }
}
