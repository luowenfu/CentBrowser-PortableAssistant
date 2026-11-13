using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Browser;
using CentBrowser_PortableAssistant.Core.Schedule;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;
using CentBrowser_PortableAssistant.Core.Update;
using CentBrowser_PortableAssistant.Services;
using CentBrowser_PortableAssistant.Tray;

namespace CentBrowser_PortableAssistant;

public partial class App : Application
{
    private const string InstanceName = "Local\\CentBrowser-PortableAssistant";
    private Mutex? _mutex;
    private EventWaitHandle? _showSignal;
    private TrayIcon? _tray;
    private MainWindow? _window;
    private bool _exiting;
    private bool _due;
    private DateTime _hoverAt;
    private DispatcherTimer? _refreshTimer;
    private DispatcherTimer? _countdownTimer;

    /// <summary>已经在退出，关窗口时不要再保存，也不要再进一次退出。</summary>
    public bool IsExiting => _exiting;

    public static AppServices Services { get; private set; } = null!;

    public static BackupService Backup { get; private set; } = null!;

    public static AutoBackupService Auto { get; private set; } = null!;

    public static UpdateService Updates { get; private set; } = null!;

    /// <summary>托盘和主界面共用的状态文案。</summary>
    public static StatusLine Status { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ExeSwap.WaitForPrevious(e.Args);
        _mutex = new Mutex(true, InstanceName, out var first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, InstanceName + ".Show");
        if (!first)
        {
            // 已经在运行，叫醒那个实例显示主界面
            _showSignal.Set();
            Shutdown();
            return;
        }
        ListenForShowSignal();

        Services = new AppServices();
        Backup = new BackupService(Services);
        Backup.WatchQueue();
        Auto = new AutoBackupService(Services, Backup);
        Status = new StatusLine(Services, Backup, Auto);
        Updates = new UpdateService();
        AutoStart.Set(Services.Config.StartWithWindows);

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += (_, _) =>
        {
            if (Auto.StartsIn is null) _countdownTimer?.Stop();
            Refresh();
        };
        Auto.PendingChanged += () => Dispatcher.BeginInvoke(SyncCountdown);

        _tray = new TrayIcon();
        WireTray(_tray);
        Services.Queue.Start();
        Services.Watcher.Start();
        Auto.OnStartup();
        Refresh();
        Updates.CheckOnLaunch();

        if (!e.Args.Contains(AutoStart.TrayArgument)) ShowMainWindow();
        ExeSwap.DeleteStale(Environment.ProcessPath, ExeSwap.ReplacedPath(e.Args));
    }

    private void WireTray(TrayIcon tray)
    {
        void SyncMenu()
        {
            tray.SetPlans(Services.Config.Plans.Select(p => (p.Id, p.Name)));
            tray.SetLimit(Services.Config.SlowUploadKbps);
        }
        SyncMenu();
        Services.ConfigChanged += () => Dispatcher.BeginInvoke(() =>
        {
            SyncMenu();
            Refresh();
        });

        tray.OpenRequested += ShowMainWindow;
        tray.RecordsRequested += ShowRecords;
        tray.ExitRequested += ExitApp;
        tray.LimitRequested += kbps =>
        {
            Services.Config.SlowUploadKbps = kbps;
            Services.SaveConfig();
        };
        tray.BackupRequested += (id, fast) => _ = BackupFromTray(id, fast);
        tray.Hovering += () =>
        {
            // 鼠标一动就来一次，一秒刷一回就够
            if (DateTime.UtcNow - _hoverAt < TimeSpan.FromSeconds(1)) return;
            _hoverAt = DateTime.UtcNow;
            Refresh();
        };

        Status.Changed += ScheduleRefresh;
        Services.Changes.Updated += ScheduleRefresh;
        Services.Queue.Changed += ScheduleRefresh;
        Backup.BusyChanged += ScheduleRefresh;
    }

    /// <summary>连着来的事件合并成一次刷新。上传每涨 1% 都会报一次，不合并会白刷。</summary>
    private void ScheduleRefresh() => Dispatcher.BeginInvoke(() =>
    {
        _refreshTimer?.Stop();
        _refreshTimer?.Start();
    });

    private void Refresh()
    {
        RefreshHover();
        ApplyMark();
    }

    /// <summary>托盘悬停第一行。版本号跟窗口标题同一套，没有版本时只留名字。</summary>
    internal static string TrayTitle()
    {
        var raw = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var version = raw.Split('+')[0];
        return string.IsNullOrEmpty(version) ? "Cent Browser 便携助手" : $"Cent Browser 便携助手 v{version}";
    }

    private void RefreshHover()
    {
        if (_tray is null) return;
        var config = Services.Config;
        var now = DateTime.Now;
        var pending = Services.Queue.Snapshot().Select(j => j.PlanId).ToHashSet();
        var joined = config.Plans.Where(p => p.AutoBackup).ToList();
        var visible = new List<HoverPlan>();
        if (config.AutoEnabled)
        {
            foreach (var plan in joined)
            {
                if (Backup.IsBusy && plan.Id == Backup.ActivePlanId) continue;
                var state = Services.History.Read(h => h.For(plan.Id));
                // 变化与否由后台算好缓存着，界面线程不碰指纹
                var unchanged = !Services.Changes.Changed(plan);
                var mark = HoverText.Mark(state.LastSuccessLocal, now, config.Frequency, pending.Contains(plan.Id), unchanged);
                var when = mark == HoverMark.Done && state.LastSuccessLocal is { } at ? Format.When(at) : null;
                visible.Add(new HoverPlan(plan.Name, mark, when));
            }
        }
        _due = visible.Any(p => p.Mark == HoverMark.Changed);
        var status = Status.Text;
        _tray.SetTip(HoverText.Compose(TrayTitle(), status.Length == 0 ? null : status,
            config.AutoEnabled, config.Frequency, joined.Count > 0, visible, HoverFailures(), LastManual(), Auto.StartsIn));
    }

    /// <summary>自动备份关着才写。正在进行、又没有更早的手动备份时留空，避免写成还没有。</summary>
    private string? LastManual()
    {
        if (Services.Config.AutoEnabled) return null;
        var jobs = Services.Queue.Snapshot();
        var pending = jobs.Where(j => !j.FromAuto && j.BatchId.Length > 0).Select(j => j.BatchId).ToHashSet();
        var latest = Services.History.Read(h => HoverText.LatestManual(h, pending));
        if (latest is not { } backup)
        {
            var inFlight = Backup.IsBusy || Status.Phase != StatusPhase.Idle || pending.Count > 0;
            return inFlight ? null : HoverText.NoManual;
        }
        var current = Services.Config.Plans.FirstOrDefault(p => p.Id == backup.PlanId)?.Name;
        return HoverText.ManualLine(Format.When(backup.UploadedUtc.ToLocalTime()), RecordPlanName.Show(current, backup.PlanName), backup.Result);
    }

    private void ApplyMark()
    {
        if (_tray is null) return;
        var mark = Status.Phase switch
        {
            StatusPhase.Collect => TrayMark.Collect,
            StatusPhase.Pack or StatusPhase.Upload => TrayMark.Busy,
            _ when HasFailure() => TrayMark.Fail,
            _ when _due => TrayMark.Due,
            _ => TrayMark.None,
        };
        _tray.SetMark(mark);
    }

    private bool HasFailure() =>
        Status.RunFailure.Length > 0 || Services.Queue.Snapshot().Any(j => j.LastError.Length > 0);

    /// <summary>和红点同一套原因。正在提取、打包或上传时红点让位，悬停也不写失败。</summary>
    private List<string> HoverFailures()
    {
        if (Status.Phase != StatusPhase.Idle) return [];
        return Services.Queue.Snapshot()
            .Where(j => j.LastError.Length > 0)
            .Select(j => StatusLine.Failure(j, Services.Config))
            .Where(f => f != Status.Text)
            .ToList();
    }

    public void SetStartWithWindows(bool enabled)
    {
        Services.Config.StartWithWindows = enabled;
        if (!enabled)
        {
            Services.Config.AutoEnabled = false;
            Auto.Disarm();
        }
        AutoStart.Set(enabled);
        Services.SaveConfig();
    }

    public void SetAutoEnabled(bool enabled)
    {
        Services.Config.AutoEnabled = Services.Config.StartWithWindows && enabled;
        Services.SaveConfig();
        if (Services.Config.AutoEnabled) Auto.Arm();
        else Auto.Disarm();
    }

    /// <summary>倒计时走着就每秒改一次悬停。停下后不再刷。</summary>
    private void SyncCountdown()
    {
        if (_countdownTimer is null) return;
        if (Auto.StartsIn is null) _countdownTimer.Stop();
        else if (!_countdownTimer.IsEnabled) _countdownTimer.Start();
        Refresh();
    }

    /// <summary>托盘里点的备份。一个都备不了时打开主窗口，不启动这次飞行。</summary>
    private async Task BackupFromTray(string? id, bool fast)
    {
        Services.RefreshInstalls();
        var plans = Services.Config.Plans.Where(p => id is null || p.Id == id).ToList();
        if (plans.Count == 0) return;
        (BackupPlan Plan, string Reason)? blocked = null;
        var ready = false;
        foreach (var plan in plans)
        {
            if (BlockedReason(plan) is { } reason) blocked ??= (plan, reason);
            else ready = true;
        }
        if (!ready && blocked is { } hit)
        {
            ShowMainWindow();
            _window!.ShowPlan(hit.Plan.Id);
            Status.Note(hit.Reason, error: true);
            return;
        }

        // 跑起来之后才出错（打包失败、提取被打断）也把主窗口打开。不改当前方案，免得飞机飞到另一张牌上
        void OnNotice(string title, string text, bool error)
        {
            if (error) Dispatcher.BeginInvoke(new Action(ShowMainWindow));
        }
        Backup.Notice += OnNotice;
        try
        {
            await Backup.RunAsync(plans, foreground: fast);
        }
        finally
        {
            Backup.Notice -= OnNotice;
        }
    }

    /// <summary>和主界面点快速备份时拦下的是同一类问题。没有则可以开备。</summary>
    private static string? BlockedReason(BackupPlan plan)
    {
        if (Services.InstallFor(plan) is not { } install)
            return $"找不到浏览器：「{plan.Name}」在第 1 步手动选择浏览器目录";
        if (BackupRunner.Validate(plan, Services.Config, install) is { } problem) return problem;
        if (BrowserProcess.IsRunning(install)) return "Cent Browser 还开着，关掉后再备份";
        return null;
    }

    private void ShowRecords()
    {
        // 菜单的 Click 还没结束就开模态窗口，关掉记录时主窗口会跟着收到关闭
        Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
        {
            ShowMainWindow();
            _window!.OpenQueue();
            if (!_window.IsVisible) ShowMainWindow();
        }));
    }

    public void ShowMainWindow()
    {
        _window ??= new MainWindow();
        _window.Show();
        if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
        _window.Activate();
        Updates.CheckOnShow();
    }

    /// <summary>开着开机启动，或这次备份、上传还在进行，就留在托盘。失败后干等重试不算。</summary>
    public bool ShouldStayInTray() =>
        Services.Config.StartWithWindows || Backup.IsBusy || Services.Queue.IsBusy;

    public void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;
        Updates?.StopDownload();
        _tray?.Dispose();
        Services.Dispose();
        Shutdown();
    }

    /// <summary>卸载确认后立刻结束进程，不再走关窗口时的保存和二次释放。</summary>
    public void ExitNow()
    {
        _exiting = true;
        Updates?.StopDownload();
        _tray?.Dispose();
        Services.Dispose();
        Environment.Exit(0);
    }

    private void ListenForShowSignal()
    {
        var thread = new Thread(() =>
        {
            while (_showSignal!.WaitOne()) Dispatcher.Invoke(ShowMainWindow);
        })
        { IsBackground = true };
        thread.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
