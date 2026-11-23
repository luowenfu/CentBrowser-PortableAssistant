using System.Diagnostics;
using System.IO;
using System.Reflection;
using CentBrowser_PortableAssistant.Core.Schedule;
using CentBrowser_PortableAssistant.Core.Update;

namespace CentBrowser_PortableAssistant.Services;

/// <summary>检查新版本的结果。启动时查一次，之后每次打开主页面再查。界面只读 <see cref="Offer"/>。</summary>
public sealed class UpdateService
{
    private int _running;
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

    public void CheckOnLaunch() => Start();

    public void CheckOnShow() => Start();

    private void Start()
    {
        if (Debugger.IsAttached) return;
        var exe = Environment.ProcessPath;
        if (!ExeSwap.CanCheck(exe)) return;
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0) return;
        _ = RunAsync(exe!);
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
        if (!outcome.AnySucceeded) return;
        if (DialogOpen) return;
        long length;
        try { length = new FileInfo(exe).Length; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return; }
        Offer = UpdateOffer.Choose(Current().Version, PackageNames.SelfContained(length), _gitee, _github);
        Changed?.Invoke();
    }

    public static AppVersion RunningVersion() => Current().Version;

    public static string RunningLabel() => Current().Label;

    /// <summary>
    /// 文件名上的版本号和正在运行的程序不一致时，改成当前版本再重新打开。
    /// 同名文件如果已经是更新的程序，不盖掉。开机启动带来的托盘参数会带过去。
    /// </summary>
    public static bool RelaunchIfNameStale(bool tray)
    {
        if (Debugger.IsAttached) return false;
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe) || !ExeSwap.CanCheck(exe)) return false;
        var (version, label) = Current();
        if (label.Length == 0) return false;
        var final = VersionedName.WithVersion(exe, version, label);
        if (string.Equals(final, exe, StringComparison.OrdinalIgnoreCase)) return false;
        if (File.Exists(final) && IsNewerBuild(final, version)) return false;
        if (!ExeSwap.TryRename(exe, final)) return false;
        AutoStart.Retarget(final);
        var more = tray ? AutoStart.TrayArgument : null;
        if (ExeSwap.TryStart(final, Environment.ProcessId, exe, more)) return true;
        ExeSwap.TryRename(final, exe);
        return false;
    }

    private static bool IsNewerBuild(string path, AppVersion current)
    {
        try
        {
            var text = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (!AppVersion.TryParse(text, out var found, out _)) return false;
            return found.CompareTo(current) > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static (AppVersion Version, string Label) Current()
    {
        var raw = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "";
        var text = raw.Split('+')[0];
        return AppVersion.TryParse(text, out var version, out var label) ? (version, label) : (default, "");
    }
}
