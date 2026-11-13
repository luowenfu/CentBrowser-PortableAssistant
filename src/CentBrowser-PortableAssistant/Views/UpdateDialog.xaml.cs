using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using CentBrowser_PortableAssistant.Core.Schedule;
using CentBrowser_PortableAssistant.Core.Update;
using CentBrowser_PortableAssistant.Services;

namespace CentBrowser_PortableAssistant.Views;

public partial class UpdateDialog : Window
{
    private static UpdateDialog? _open;

    private readonly UpdateOffer _offer;
    private CancellationTokenSource? _cts;
    private bool _downloading;
    private bool _applying;
    private bool _ready;
    private bool _swapped;
    private string? _launchPath;
    private bool _failed;
    private bool _closing;

    private UpdateDialog(Window? owner, UpdateOffer offer)
    {
        InitializeComponent();
        TitleBarIcon.Remove(this);
        Owner = owner;
        _offer = offer;
        MessageText.Text = "发现 v" + offer.Label;
        NotesText.Text = string.IsNullOrWhiteSpace(offer.Notes) ? "没有更新说明" : offer.Notes;
        App.Updates.CancelDownload = StopDownload;
        App.Backup.BusyChanged += OnBusy;
        App.Services.Queue.Changed += OnBusy;
        RefreshChrome();
    }

    public static void Show(Window owner, UpdateOffer offer)
    {
        if (_open is { IsVisible: true } existing)
        {
            existing.Activate();
            return;
        }
        var dialog = new UpdateDialog(owner, offer);
        _open = dialog;
        App.Updates.DialogOpen = true;
        dialog.ShowDialog();
    }

    private void OnBusy() => Dispatcher.BeginInvoke(RefreshChrome);

    private async void OnOk(object sender, RoutedEventArgs e)
    {
        if (_offer.Downloads.Count == 0)
        {
            OpenPage();
            return;
        }
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe)) return;
        if (_swapped)
        {
            if (_launchPath is not null && ExeSwap.TryStart(_launchPath, Environment.ProcessId, exe))
                ((App)Application.Current).ExitNow();
            return;
        }
        if (_downloading || _applying) return;
        OkButton.IsEnabled = false;
        if (!_ready)
        {
            if (!ExeSwap.CanWrite(exe))
            {
                Fail();
                return;
            }
            _downloading = true;
            SetStatus("正在下载 0%");
            var progress = new Progress<int>(percent =>
            {
                if (_downloading) SetStatus($"正在下载 {percent}%");
            });
            try
            {
                foreach (var source in _offer.Downloads)
                {
                    _cts = new CancellationTokenSource();
                    var outcome = await UpdateDownloader.DownloadAsync(
                        source.Url, ExeSwap.NewPath(exe), source.Size ?? _offer.GitHubSize,
                        _offer.GitHubDigest, _offer.Label, progress, _cts.Token);
                    if (outcome == DownloadOutcome.Canceled || _closing) return;
                    if (outcome == DownloadOutcome.Done)
                    {
                        _ready = true;
                        break;
                    }
                }
            }
            finally
            {
                _downloading = false;
            }
            if (_closing) return;
            if (!_ready)
            {
                Fail();
                return;
            }
        }

        _applying = true;
        var held = false;
        var started = false;
        try
        {
            if (!App.Backup.TryHoldForUpdate())
            {
                _applying = false;
                RefreshChrome();
                return;
            }
            held = true;
            if (Owner is MainWindow main) main.Plans.Flush();
            if (!ExeSwap.TrySwap(exe))
            {
                Fail();
                return;
            }
            var final = VersionedName.WithVersion(exe, UpdateService.RunningVersion(), _offer.Label);
            if (!string.Equals(final, exe, StringComparison.OrdinalIgnoreCase) && !ExeSwap.TryRename(exe, final))
                final = exe;
            _launchPath = final;
            _swapped = true;
            AutoStart.Retarget(final);
            SetStatus("下载完成、即将更新");
            started = ExeSwap.TryStart(final, Environment.ProcessId, exe);
            if (started) ((App)Application.Current).ExitNow();
        }
        finally
        {
            if (held) App.Backup.ReleaseUpdateHold();
            _applying = false;
        }
        if (!started && held) OkButton.IsEnabled = true;
    }

    private void Fail()
    {
        _failed = true;
        _applying = false;
        _downloading = false;
        if (!_ready)
        {
            var exe = Environment.ProcessPath;
            if (exe is not null) ExeSwap.DeleteNew(exe);
        }
        SetStatus("下载没完成，没有替换");
        OkButton.Content = "立即更新";
        OkButton.IsEnabled = true;
    }

    private void RefreshChrome()
    {
        if (_downloading || _applying) return;
        var web = _offer.Downloads.Count == 0;
        OkButton.Content = web ? "打开网页" : "立即更新";
        var busy = !web && (App.Backup.IsBusy || App.Services.Queue.IsBusy);
        if (busy)
        {
            SetStatus("正在备份中，完成后才能更新");
            OkButton.IsEnabled = false;
            return;
        }
        if (_failed)
        {
            SetStatus("下载没完成，没有替换");
            OkButton.IsEnabled = true;
            return;
        }
        StatusText.Visibility = Visibility.Collapsed;
        OkButton.IsEnabled = true;
    }

    private void SetStatus(string text)
    {
        StatusText.Text = text;
        StatusText.Visibility = Visibility.Visible;
    }

    private void StopDownload()
    {
        try { _cts?.Cancel(); }
        catch (ObjectDisposedException) { }
        var exe = Environment.ProcessPath;
        if (!_swapped && exe is not null) ExeSwap.DeleteNew(exe);
    }

    private void OpenPage() =>
        Process.Start(new ProcessStartInfo(_offer.PageUrl) { UseShellExecute = true });

    protected override void OnClosing(CancelEventArgs e)
    {
        _closing = true;
        StopDownload();
        App.Updates.DialogOpen = false;
        App.Updates.CancelDownload = null;
        App.Backup.BusyChanged -= OnBusy;
        App.Services.Queue.Changed -= OnBusy;
        if (ReferenceEquals(_open, this)) _open = null;
        base.OnClosing(e);
    }
}
