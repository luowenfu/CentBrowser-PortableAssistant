using System.Diagnostics;
using System.IO;
using System.Reflection;
using CentBrowser_PortableAssistant.Core.Update;

namespace CentBrowser_PortableAssistant.Services;

/// <summary>检查新版本的节奏和结果。界面只读 <see cref="Offer"/>。</summary>
public sealed class UpdateService
{
    private static readonly TimeSpan Gap = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(12);

    private int _running;
    private DateTime _lastAttempt = DateTime.MinValue;
    private DateTime? _lastSuccess;
    private bool _lastFailed = true;
    private DateTime? _giteeBlocked;
    private DateTime? _githubBlocked;
    private ReleaseInfo? _gitee;
    private ReleaseInfo? _github;

    public UpdateOffer? Offer { get; private set; }

    /// <summary>确认窗开着时，不拿新的检查结果去换它。</summary>
    public bool DialogOpen { get; set; }

    public event Action? Changed;

    public Action? CancelDownload { get; set; }

    public void StopDownload() => CancelDownload?.Invoke();

    public void CheckOnLaunch() => Start(scheduled: false);

    public void CheckOnShow() => Start(scheduled: true);

    private void Start(bool scheduled)
    {
        if (Debugger.IsAttached) return;
        var exe = Environment.ProcessPath;
        if (!ExeSwap.CanCheck(exe)) return;
        if (scheduled && !Due()) return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        _lastAttempt = DateTime.UtcNow;
        _ = RunAsync(exe!);
    }

    private bool Due()
    {
        if (DateTime.UtcNow - _lastAttempt < Gap) return false;
        if (_lastFailed || _lastSuccess is null) return true;
        return DateTime.UtcNow - _lastSuccess.Value > Fresh;
    }

    private async Task RunAsync(string exe)
    {
        try
        {
            var outcome = await UpdateChecker.FetchAsync(_giteeBlocked, _githubBlocked, CancellationToken.None);
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null || dispatcher.HasShutdownStarted) return;
            await dispatcher.InvokeAsync(() => Apply(outcome, exe));
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private void Apply(FetchOutcome outcome, string exe)
    {
        if (outcome.GiteeBlockedUntil is { } giteeUntil) _giteeBlocked = giteeUntil;
        if (outcome.GitHubBlockedUntil is { } githubUntil) _githubBlocked = githubUntil;
        if (!outcome.GiteeFailed) _gitee = outcome.Gitee;
        if (!outcome.GitHubFailed) _github = outcome.GitHub;
        _lastFailed = outcome.GiteeFailed || outcome.GitHubFailed;
        if (!outcome.AnySucceeded) return;
        _lastSuccess = DateTime.UtcNow;
        if (DialogOpen) return;
        long length;
        try { length = new FileInfo(exe).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        Offer = UpdateOffer.Choose(Current(), PackageNames.SelfContained(length), _gitee, _github);
        Changed?.Invoke();
    }

    public static AppVersion RunningVersion() => Current();

    private static AppVersion Current()
    {
        var raw = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var text = raw.Split('+')[0];
        return AppVersion.TryParse(text, out var version, out _) ? version : default;
    }
}
