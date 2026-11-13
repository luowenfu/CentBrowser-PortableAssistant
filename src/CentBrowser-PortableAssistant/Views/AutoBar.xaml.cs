using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;
using CentBrowser_PortableAssistant.Services;

namespace CentBrowser_PortableAssistant.Views;

/// <summary>主窗口底栏：开机启动、自动备份、居中的备份状态，以及待上传。</summary>
public partial class AutoBar : UserControl
{
    private readonly DispatcherTimer _statusHold = new() { Interval = TimeSpan.FromSeconds(8) };
    private bool _loading;
    private string? _dismissed;
    private string? _holdText;
    private UploadProgress? _progress;
    private DateTime _lastRefresh;

    public event Action? QueueRequested;

    public AutoBar()
    {
        InitializeComponent();
        foreach (var option in SpeedLimits.Options)
            LimitBox.Items.Add(new ComboBoxItem { Content = option.Label });
        App.Services.ConfigChanged += () => Dispatcher.BeginInvoke(Load);
        App.Status.Changed += () => Dispatcher.BeginInvoke(ShowStatus);
        var queue = App.Services.Queue;
        queue.Changed += () => Dispatcher.BeginInvoke(UpdateQueue);
        queue.Progress += p => Dispatcher.BeginInvoke(() =>
        {
            _progress = p;
            if (DateTime.UtcNow - _lastRefresh >= TimeSpan.FromMilliseconds(500)) UpdateQueue();
        });
        App.Backup.CancelStateChanged += () => Dispatcher.BeginInvoke(ShowCancel);
        _statusHold.Tick += (_, _) =>
        {
            _statusHold.Stop();
            if (App.Status.Text != _holdText) return;
            _dismissed = _holdText;
            StatusText.Text = "";
            StatusText.ToolTip = null;
        };
        Load();
        UpdateQueue();
        ShowStatus();
        ShowCancel();
    }

    private void ShowCancel() =>
        CancelBackupButton.Visibility = App.Backup.CanCancel ? Visibility.Visible : Visibility.Collapsed;

    private async void OnCancelBackup(object sender, RoutedEventArgs e)
    {
        CancelBackupButton.IsEnabled = false;
        try
        {
            await App.Backup.CancelAsync();
        }
        finally
        {
            CancelBackupButton.IsEnabled = true;
        }
    }

    private void Load()
    {
        _loading = true;
        var c = App.Services.Config;
        StartSwitch.IsChecked = c.StartWithWindows;
        AutoSwitch.IsChecked = c.AutoEnabled;
        FrequencyBox.SelectedIndex = (int)c.Frequency;
        LimitBox.SelectedIndex = SpeedLimits.IndexOf(c.SlowUploadKbps);
        UpdateEnabled();
        _loading = false;
    }

    private void UpdateEnabled()
    {
        var startup = StartSwitch.IsChecked == true;
        AutoPanel.IsEnabled = startup;
        AutoPanel.Opacity = startup ? 1 : 0.4;
        FrequencyBox.IsEnabled = AutoSwitch.IsChecked == true;
    }

    private void UpdateQueue()
    {
        _lastRefresh = DateTime.UtcNow;
        var queue = App.Services.Queue;
        var jobs = queue.Snapshot();
        QueueText.Foreground = (Brush)FindResource("Text2");
        if (jobs.Count == 0)
            QueueText.Text = "没有待上传的文件";
        else if (queue.IsBusy && _progress is { Total: > 0 } p)
            QueueText.Text = $"{jobs.Count} 个文件上传中";
        else if (jobs.FirstOrDefault(j => j.LastError.Length > 0) is { } job)
        {
            QueueText.Inlines.Clear();
            QueueText.Inlines.Add(new Run($"{jobs.Count} 个文件待上传 · "));
            QueueText.Inlines.Add(new Run($"{job.NextAttemptUtc.ToLocalTime():HH:mm} 重试") { Foreground = (Brush)FindResource("Danger") });
        }
        else
            QueueText.Text = $"{jobs.Count} 个文件待上传 · {Format.Size(jobs.Sum(j => j.Size))}";
    }

    private void OnStartToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        var on = StartSwitch.IsChecked == true;
        if (!on)
        {
            _loading = true;
            AutoSwitch.IsChecked = false;
            _loading = false;
        }
        ((App)Application.Current).SetStartWithWindows(on);
        UpdateEnabled();
    }

    private void OnAutoToggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ((App)Application.Current).SetAutoEnabled(AutoSwitch.IsChecked == true);
        UpdateEnabled();
    }

    /// <summary>关掉窗口时，已经结束的那句不再留在底栏。正在提取、打包或上传的留下，再打开还能看到。</summary>
    public void ReleaseSettledStatus()
    {
        var status = App.Status;
        if (status.Phase != StatusPhase.Idle) return;
        _dismissed = status.Text;
        StatusText.Text = "";
        StatusText.ToolTip = null;
    }

    /// <summary>已送达的目的地用普通颜色，失败那一行单独用红色。</summary>
    private void ShowStatus()
    {
        var status = App.Status;
        if (_dismissed is not null)
        {
            if (status.Phase == StatusPhase.Idle && status.Text == _dismissed)
            {
                StatusText.Text = "";
                StatusText.ToolTip = null;
                return;
            }
            _dismissed = null;
        }
        var names = App.Services.Config.Plans.Select(p => p.Name);
        if (status.Text.Length == 0)
        {
            StatusText.Text = "";
            StatusText.ToolTip = null;
            HoldStatus("");
            return;
        }
        if (status.Arrived.Count > 0 && status.DeliveryError is { } failure)
        {
            StatusText.Inlines.Clear();
            StatusText.Inlines.Add(new Run(PlanNameText.ClipWithin(status.Text, names)) { Foreground = (Brush)FindResource("Text2") });
            StatusText.Inlines.Add(new Run("\n" + failure) { Foreground = (Brush)FindResource("Danger") });
            StatusText.ToolTip = status.Text + "\n" + failure;
            HoldStatus(status.Text);
            return;
        }
        StatusText.Text = PlanNameText.ClipWithin(status.Text, names);
        StatusText.ToolTip = status.Text;
        StatusText.Foreground = (Brush)FindResource(status.IsError ? "Danger" : "Text2");
        HoldStatus(status.Text);
    }

    /// <summary>取消和备份完成的那句只留 8 秒。进行中的进度会把倒计时停掉。</summary>
    private void HoldStatus(string text)
    {
        var done = text == "已取消"
            || text.Contains("已送达", StringComparison.Ordinal)
            || text.Contains("备份完成", StringComparison.Ordinal);
        _statusHold.Stop();
        if (!done) return;
        _holdText = text;
        _statusHold.Start();
    }

    private void OnFrequencyChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FrequencyBox.SelectedIndex < 0) return;
        App.Services.Config.Frequency = (AutoFrequency)FrequencyBox.SelectedIndex;
        App.Services.SaveConfig();
    }

    private void OnLimitChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || LimitBox.SelectedIndex < 0) return;
        App.Services.Config.SlowUploadKbps = SpeedLimits.Options[LimitBox.SelectedIndex].Kbps;
        App.Services.SaveConfig();
    }

    private void OnOpenQueue(object sender, RoutedEventArgs e) => QueueRequested?.Invoke();
}
