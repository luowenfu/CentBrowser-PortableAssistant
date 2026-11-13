using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Schedule;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Services;

/// <summary>自动备份：关掉浏览器 2 分钟后静默备份，这期间又打开浏览器就取消。</summary>
public sealed class AutoBackupService
{
    private static readonly TimeSpan AutoDelay = TimeSpan.FromMinutes(2);

    private readonly object _gate = new();
    private readonly AppServices _app;
    private readonly BackupService _backup;
    private CancellationTokenSource? _pending;
    private int _generation;
    private long _dueTicks;

    public event Action<string, string>? Notice;

    /// <summary>等待开始或取消了。托盘据此刷新倒计时。</summary>
    public event Action? PendingChanged;

    /// <summary>离自动备份开始还要多久。没在等则为 null。</summary>
    public TimeSpan? StartsIn
    {
        get
        {
            var ticks = Volatile.Read(ref _dueTicks);
            if (ticks == 0) return null;
            var left = new DateTime(ticks, DateTimeKind.Utc) - DateTime.UtcNow;
            return left < TimeSpan.Zero ? TimeSpan.Zero : left;
        }
    }

    public AutoBackupService(AppServices app, BackupService backup)
    {
        _app = app;
        _backup = backup;
        app.Watcher.Closed += OnClosed;
        app.Watcher.Started += Disarm;
    }

    /// <summary>程序启动时浏览器没开着也检查一次，补上关机前没来得及做的备份。</summary>
    public void OnStartup() => Arm();

    /// <summary>自动备份刚打开。浏览器已经关着就等 2 分钟，开着就等它关掉。</summary>
    public void Arm()
    {
        if (!_app.Config.AutoEnabled || BrowserOpen()) return;
        ScheduleAuto();
    }

    /// <summary>自动备份关掉，或浏览器又开了。进行中的等待取消。</summary>
    public void Disarm()
    {
        var notify = false;
        lock (_gate)
        {
            if (_pending is null && _dueTicks == 0) return;
            _pending?.Cancel();
            _pending = null;
            Volatile.Write(ref _dueTicks, 0);
            _generation++;
            notify = true;
        }
        if (notify) PendingChanged?.Invoke();
    }

    private void OnClosed()
    {
        if (!_app.Config.AutoEnabled) return;
        ScheduleAuto();
    }

    private void ScheduleAuto()
    {
        CancellationTokenSource cts;
        int generation;
        lock (_gate)
        {
            _pending?.Cancel();
            cts = new CancellationTokenSource();
            _pending = cts;
            generation = ++_generation;
            Volatile.Write(ref _dueTicks, (DateTime.UtcNow + AutoDelay).Ticks);
        }
        PendingChanged?.Invoke();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(AutoDelay, cts.Token); }
            catch (OperationCanceledException) { return; }
            lock (_gate)
            {
                if (generation != _generation) return;
                _pending = null;
                Volatile.Write(ref _dueTicks, 0);
            }
            PendingChanged?.Invoke();
            if (BrowserOpen()) return;
            lock (_gate)
            {
                if (generation != _generation) return;
            }
            var plans = Evaluate();
            if (plans.Count > 0) await _backup.RunAsync(plans, fromAuto: true);
        });
    }

    private bool BrowserOpen() =>
        _app.Config.Plans.Where(p => p.AutoBackup)
            .Select(p => _app.InstallFor(p))
            .OfType<BrowserInstall>()
            .DistinctBy(i => i.RootDir, StringComparer.OrdinalIgnoreCase)
            .Any(BrowserProcess.IsRunning);

    /// <summary>挑出该备份且勾选的用户数据有变化的方案。没变化的直接跳过。</summary>
    private List<BackupPlan> Evaluate()
    {
        var config = _app.Config;
        if (_backup.IsBusy) return [];

        var now = DateTime.Now;
        var result = new List<BackupPlan>();
        foreach (var plan in config.Plans.Where(p => p.AutoBackup))
        {
            if (_app.Queue.Snapshot().Any(j => j.PlanId == plan.Id)) continue;
            if (_app.InstallFor(plan) is not { } install) continue;
            var state = _app.History.Read(h => h.For(plan.Id));
            var problem = BackupRunner.Validate(plan, config, install);
            var changed = problem is null && _app.Changes.Compute(plan, install);
            var decision = ScheduleRules.Decide(config.Frequency, now, state, changed || problem is not null);
            if (decision == PlanDecision.None) continue;

            if (problem is not null)
            {
                NotifyOncePerDay(plan, state, now, "无法自动备份", problem);
                continue;
            }
            if (decision == PlanDecision.Unchanged) continue;
            result.Add(plan);
        }
        return result;
    }

    private void NotifyOncePerDay(BackupPlan plan, PlanState state, DateTime now, string title, string text)
    {
        if (!ScheduleRules.ShouldNotifyUnchanged(state, now)) return;
        _app.History.Update(h => h.For(plan.Id).LastUnchangedNotice = DateOnly.FromDateTime(now));
        Notice?.Invoke(title, text);
    }
}
