using CentBrowser_PortableAssistant.Core.Browser;

namespace CentBrowser_PortableAssistant.Core.Schedule;

/// <summary>浏览器开着时每 1 秒看一次，全部退出后每 5 秒看一次。从有一个开着变成全部退出时触发 Closed。</summary>
public sealed class BrowserWatcher : IDisposable
{
    private static readonly TimeSpan Fast = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan Slow = TimeSpan.FromSeconds(5);

    private readonly Func<IReadOnlyCollection<BrowserInstall>> _installs;
    private readonly Timer _timer;
    private TimeSpan _period = Fast;
    private bool? _wasRunning;

    public event Action? Closed;
    public event Action? Started;

    public BrowserWatcher(Func<IReadOnlyCollection<BrowserInstall>> installs)
    {
        _installs = installs;
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsRunning => _wasRunning == true;

    public void Start() => _timer.Change(TimeSpan.Zero, Fast);

    private void Tick()
    {
        var installs = _installs();
        if (installs.Count == 0) return;
        var running = installs.Any(BrowserProcess.IsRunning);
        var was = _wasRunning;
        _wasRunning = running;
        SetPeriod(running ? Fast : Slow);
        if (was == true && !running) Closed?.Invoke();
        if (was == false && running) Started?.Invoke();
    }

    private void SetPeriod(TimeSpan period)
    {
        if (_period == period) return;
        _period = period;
        _timer.Change(period, period);
    }

    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _timer.Dispose();
    }
}
