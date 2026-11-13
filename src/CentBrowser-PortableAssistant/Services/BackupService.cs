using System.IO;
using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Services;

/// <summary>一次只跑一个备份，进度和结果通过事件报给托盘和界面。</summary>
public sealed class BackupService(AppServices app)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _state = new();
    private readonly HashSet<string> _runBatches = [];
    private CancellationTokenSource _cancel = new();
    private bool _runOpen;
    private bool _canCancel;

    public bool IsBusy => _gate.CurrentCount == 0;

    /// <summary>这次备份还在提取、打包，或打出来的文件还在上传。</summary>
    public bool CanCancel
    {
        get { lock (_state) return _canCancel; }
    }

    public event Action? CancelStateChanged;

    /// <summary>上传队列空了就收起取消按钮。创建后调用一次。</summary>
    public void WatchQueue() => app.Queue.Changed += TouchCancel;

    /// <summary>提取和打包的进度。空的表示这次跑完了。</summary>
    public event Action<BackupStatus?>? Status;

    /// <summary>一句状态：标题、正文、是否是错误。</summary>
    public event Action<string, string, bool>? Notice;

    /// <summary>正在提取或打包的方案。悬停时这一行改由进度显示。</summary>
    public string? ActivePlanId { get; private set; }

    /// <summary>这次在后台跑，让着浏览器。状态前缀写成慢速。</summary>
    public bool Slow { get; private set; }

    public event Action? BusyChanged;

    private int _updateHold;

    /// <summary>只试一下。占不到说明正在备份，不会把正在跑的备份停掉。</summary>
    public bool TryHoldForUpdate()
    {
        if (!_gate.Wait(0)) return false;
        _updateHold = 1;
        return true;
    }

    /// <summary>放开 <see cref="TryHoldForUpdate"/> 占住的名额。没占过就什么也不做。</summary>
    public void ReleaseUpdateHold()
    {
        if (_updateHold == 0) return;
        _updateHold = 0;
        _gate.Release();
    }

    /// <summary>停掉正在跑的备份，并占住名额，直到 <see cref="Resume"/>。提取和打包都会停。</summary>
    public async Task QuiesceAsync()
    {
        _cancel.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
    }

    /// <summary>放开 <see cref="QuiesceAsync"/> 占住的名额，之后的备份可以再跑。</summary>
    public void Resume()
    {
        _cancel = new CancellationTokenSource();
        _gate.Release();
    }

    /// <param name="foreground">主界面和托盘里选快速的全速打包；自动备份和托盘里选慢速的降低优先级。</param>
    /// <param name="fromAuto">这次是自动备份。主界面的快速备份和托盘菜单里选的都不是。</param>
    public async Task RunAsync(IReadOnlyList<BackupPlan> plans, bool foreground = false, CancellationToken ct = default, bool fromAuto = false)
    {
        if (plans.Count == 0) return;
        if (!await _gate.WaitAsync(0, ct))
        {
            Notice?.Invoke("正在备份", "上一个备份还没完成，请稍后再试", false);
            return;
        }
        if (_cancel.IsCancellationRequested) _cancel = new CancellationTokenSource();
        Slow = !foreground;
        lock (_state) _runOpen = true;
        TouchCancel();
        BusyChanged?.Invoke();
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct, _cancel.Token);
        try
        {
            app.RefreshInstalls();
            foreach (var plan in plans)
            {
                if (run.IsCancellationRequested) return;
                ActivePlanId = plan.Id;
                if (app.InstallFor(plan) is not { } install)
                {
                    Notice?.Invoke("找不到浏览器", $"「{plan.Name}」在第 1 步手动选择浏览器目录", true);
                    continue;
                }
                Notice?.Invoke("开始备份", $"「{plan.Name}」正在提取数据", false);
                var progress = new Progress<BackupStatus>(s => Status?.Invoke(s));
                using var browserStarted = new CancellationTokenSource();
                void OnStarted()
                {
                    // 监视器在计时器线程上触发，可能晚于这里的释放
                    try { browserStarted.Cancel(); }
                    catch (ObjectDisposedException) { }
                }
                app.Watcher.Started += OnStarted;
                try
                {
                    var result = await app.Runner.RunAsync(install, plan, app.Config, progress, run.Token, browserStarted.Token, lowPriority: !foreground, fromAuto: fromAuto, onBatch: NoteBatch);
                    var summary = $"「{plan.Name}」{Format.Size(result.TotalSize)}";
                    // 本地文件夹复制很快，走到这里时可能已经传完了
                    if (app.Queue.Snapshot().Any(j => result.Files.Contains(j.LocalPath)))
                        Notice?.Invoke("打包完成", $"{summary}，正在上传", false);
                    else
                        Notice?.Invoke("备份完成", $"{summary}，已送达全部目的地", false);
                }
                catch (BackupException ex)
                {
                    if (!ex.StoppedForBrowser) Notice?.Invoke("备份没有完成", ex.Message, true);
                    if (ex.BrowserRunning) return;
                }
                catch (OperationCanceledException)
                {
                    return;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Notice?.Invoke("备份出错", $"「{plan.Name}」{ex.Message}", true);
                }
                finally
                {
                    app.Watcher.Started -= OnStarted;
                }
            }
        }
        finally
        {
            ActivePlanId = null;
            lock (_state) _runOpen = false;
            _gate.Release();
            Status?.Invoke(null);
            BusyChanged?.Invoke();
            TouchCancel();
        }
    }

    /// <summary>停下这次备份，并删掉暂存、本地包和这次已经传到目的地的文件。</summary>
    public async Task CancelAsync()
    {
        string[] batches;
        lock (_state)
        {
            if (!_canCancel) return;
            _canCancel = false;
        }
        CancelStateChanged?.Invoke();
        _cancel.Cancel();
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_state)
            {
                batches = [.. _runBatches];
                _runBatches.Clear();
                _runOpen = false;
            }
            SweepStaging();
            await app.Queue.DropBatchesAsync(batches).ConfigureAwait(false);
            Notice?.Invoke("已取消", "", false);
        }
        finally
        {
            _gate.Release();
            TouchCancel();
        }
    }

    private void NoteBatch(string batchId)
    {
        lock (_state) _runBatches.Add(batchId);
        TouchCancel();
    }

    private void TouchCancel()
    {
        bool changed;
        lock (_state)
        {
            var queued = app.Queue.Snapshot();
            if (!_runOpen)
                _runBatches.RemoveWhere(id => queued.All(j => j.BatchId != id));
            var on = _runOpen || _runBatches.Count > 0;
            changed = on != _canCancel;
            _canCancel = on;
        }
        if (changed) CancelStateChanged?.Invoke();
    }

    private static void SweepStaging()
    {
        if (!Directory.Exists(AppPaths.StagingDir)) return;
        foreach (var dir in Directory.EnumerateDirectories(AppPaths.StagingDir))
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (Exception ex) when (ex is (IOException or UnauthorizedAccessException))
            {
            }
        }
    }
}
