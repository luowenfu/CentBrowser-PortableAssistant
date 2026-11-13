using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;
using CentBrowser_PortableAssistant.Services;

namespace CentBrowser_PortableAssistant.Views;

public partial class QueuePage : UserControl
{
    private sealed record Row(string Id, string FileName, string Status, string Size, double Percent, bool Failed, string When, string Plan, string Trigger)
    {
        public Visibility ProgressVisibility => Percent > 0 && !Failed ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Head 是放不下时留下的部分，Tail 是用时，空表示这句没有后半段。</summary>
    private sealed record WhereLine(string Text, string Head, string Tail, bool Failed)
    {
        public static WhereLine Of(string text, bool failed) => new(text, text, "", failed);

        public static WhereLine Delivered(string name, TimeSpan? elapsed)
        {
            var head = $"已送达「{name}」";
            var tail = elapsed is { } time ? " · 用时 " + Format.Duration(time) : "";
            return new(head + tail, head, tail, false);
        }
    }

    private sealed record TagFile(string Name, string Size, IReadOnlyList<WhereLine> Lines);

    private sealed record RecordRow(IReadOnlyList<TagFile> Files, string Plan, string When, string Trigger, string Pace);

    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private UploadProgress? _progress;
    private DateTime _lastRefresh;

    public QueuePage()
    {
        InitializeComponent();
        _refreshTimer.Tick += (_, _) =>
        {
            _refreshTimer.Stop();
            Refresh();
        };
        var queue = App.Services.Queue;
        // 一次上传结束时队列会连着变好几回，两张表都要重建，合并成一次
        queue.Changed += () => Dispatcher.BeginInvoke(ScheduleRefresh);
        queue.Progress += p => Dispatcher.BeginInvoke(() =>
        {
            _progress = p;
            if (DateTime.UtcNow - _lastRefresh < TimeSpan.FromMilliseconds(250)) return;
            Refresh();
        });
        App.Services.ConfigChanged += () => Dispatcher.BeginInvoke(ScheduleRefresh);
        Loaded += async (_, _) =>
        {
            Refresh();
            ScrollRecordsToTop();
            await FillSizesAsync();
        };
        Refresh();
    }

    private void ScheduleRefresh()
    {
        _refreshTimer.Stop();
        _refreshTimer.Start();
    }

    private void ScrollRecordsToTop() =>
        Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, () => RecordScroll.ScrollToVerticalOffset(0));

    private void Refresh()
    {
        _lastRefresh = DateTime.UtcNow;
        var config = App.Services.Config;
        var jobs = App.Services.Queue.Snapshot();
        Jobs.ItemsSource = jobs.Select(j =>
        {
            var pending = j.PendingTargetIds.Select(id => config.Targets.FirstOrDefault(t => t.Id == id)).ToList();
            var names = pending.Select(PendingName).ToList();
            var active = _progress is { } p && p.Job.Id == j.Id && App.Services.Queue.IsBusy;
            var percent = active && _progress!.Total > 0 ? _progress.Sent * 100.0 / _progress.Total : 0;
            var failed = !active && j.LastError.Length > 0;
            var allPaused = pending.Count > 0 && pending.All(t => t is { Disabled: true });
            var status = active
                ? $"正在传到「{_progress!.TargetName}」{percent:0}%"
                : allPaused
                    ? $"已停用，等重新启用后再传到{Quote(pending.Select(t => t!.Name))}"
                    : failed
                        ? $"{CurrentError(j.LastError, names.FirstOrDefault())}，{j.NextAttemptUtc.ToLocalTime():HH:mm} 重试（第 {j.Attempts} 次失败）"
                        : $"等待上传到{Quote(names)}";
            return new Row(j.Id, j.FileName, status, Format.Size(Bytes(j)), percent, failed, JobWhen(j), PlanLabel(config, j.PlanId, j.PlanName), TriggerName(j.FromAuto));
        }).ToList();

        var hasJobs = jobs.Count > 0;
        EmptyText.Visibility = hasJobs ? Visibility.Collapsed : Visibility.Visible;
        JobScroll.Visibility = hasJobs ? Visibility.Visible : Visibility.Collapsed;
        RetryButton.Visibility = hasJobs ? Visibility.Visible : Visibility.Collapsed;
        var plans = jobs.Select(j => j.PlanId).Distinct().Count();
        Subtitle.Text = hasJobs
            ? $"{plans} 个方案，{jobs.Count} 个文件，共 {Format.Size(jobs.Sum(Bytes))}，传完后自动从本地删除。每个方案只留最近一次的「待上传」"
            : "打好的包传完后会自动从本地删除。每个方案只留最近一次的「待上传」";

        if (App.Services.History.Read(h => h.Records.Select(r => r.BatchId).Concat(h.Abandoned.Select(a => a.BatchId)).Where(id => id.Length > 0).Distinct().Count() > BackupRecords.MaxRows))
            App.Services.History.Update(BackupRecords.TrimHistory);
        var delivered = App.Services.History.Read(h => BackupRecords.Delivered(h.Records, h.Abandoned));
        var waiting = jobs.ToLookup(j => j.BatchId);
        var abandoned = App.Services.History.Read(h => h.Abandoned.ToLookup(a => a.BatchId));
        var seen = delivered.Select(r => r.BatchId).ToHashSet();
        var rows = delivered.Select(row => (row.UploadedUtc, Row: new RecordRow(
            row.Files.Select(f => TagFor(f, DeliveryLines(config, f, waiting[row.BatchId], abandoned[row.BatchId]))).ToList(),
            PlanLabel(config, row.PlanId, row.PlanName),
            Format.When(row.UploadedUtc.ToLocalTime(), seconds: true),
            TriggerName(row.FromAuto),
            PaceLabel(row.Files, waiting[row.BatchId])))).ToList();
        foreach (var batch in waiting)
        {
            if (batch.Key.Length == 0 || seen.Contains(batch.Key)) continue;
            var job = batch.OrderBy(j => j.CreatedUtc).First();
            rows.Add((job.CreatedUtc, new RecordRow(
                batch.Select(j => TagFor(PendingFile(j), DeliveryLines(config, PendingFile(j), [j], []))).ToList(),
                PlanLabel(config, job.PlanId, job.PlanName),
                JobWhen(job),
                TriggerName(job.FromAuto),
                PaceLabel([], batch))));
        }
        Records.ItemsSource = rows.OrderByDescending(r => r.Item1).Select(r => r.Row).ToList();
        var hasRecords = rows.Count > 0;
        RecordEmpty.Visibility = hasRecords ? Visibility.Collapsed : Visibility.Visible;
        RecordScroll.Visibility = hasRecords ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>待上传用打包时间。早前的队列没记下时间，就用本地文件的修改时间。</summary>
    private static string JobWhen(UploadJob job)
    {
        var utc = job.CreatedUtc;
        if (utc.Year < 2000) utc = WriteTimeUtc(job.LocalPath);
        return utc.Year < 2000 ? "" : Format.When(utc.ToLocalTime(), seconds: true);
    }

    private static DateTime WriteTimeUtc(string path)
    {
        if (path.Length == 0) return default;
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc : default;
        }
        catch (IOException)
        {
            return default;
        }
        catch (UnauthorizedAccessException)
        {
            return default;
        }
    }

    /// <summary>队列里偶尔没写上大小，本地文件还在就用文件长度。</summary>
    private static long Bytes(UploadJob job)
    {
        if (job.Size > 0) return job.Size;
        return LengthOf(job.LocalPath);
    }

    /// <summary>旧的送达记录没有大小。按目的地上的文件补一次，写回记录。</summary>
    private async Task FillSizesAsync()
    {
        var paths = App.Services.History.Read(h => h.Records
            .Where(r => r.Size <= 0 && r.RemotePath.Length > 0)
            .Select(r => r.RemotePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList());
        if (paths.Count == 0) return;
        var found = await Task.Run(() => Measure(paths));
        if (found.Count == 0) return;
        var changed = false;
        App.Services.History.Update(h => changed = BackupRecords.ApplyLengths(h.Records, found) > 0);
        if (changed) Refresh();
    }

    private static Dictionary<string, long> Measure(IReadOnlyList<string> paths)
    {
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            var length = LengthOf(path);
            if (length > 0) map[path] = length;
        }
        return map;
    }

    private static long LengthOf(string path)
    {
        if (path.Length == 0) return 0;
        try
        {
            var info = new FileInfo(path);
            return info.Exists ? info.Length : 0;
        }
        catch (IOException)
        {
            return 0;
        }
        catch (UnauthorizedAccessException)
        {
            return 0;
        }
    }

    /// <summary>一次备份只用一种速度。目的地再多也只写一次。早前的记录没有，就留空。</summary>
    private static string PaceLabel(IEnumerable<DeliveredFile> files, IEnumerable<UploadJob> jobs)
    {
        foreach (var file in files)
        {
            if (file.Slow is not { } paces) continue;
            foreach (var pace in paces)
                if (pace is bool slow) return slow ? "慢速" : "快速";
        }
        foreach (var job in jobs)
            return job.Slow ? "慢速" : "快速";
        return "";
    }

    private static string TriggerName(bool? fromAuto) => fromAuto switch
    {
        true => "自动备份",
        false => "手动备份",
        _ => "",
    };

    /// <summary>一个文件一张行李牌，几个目的地都写在右边。没有可显示的目的地时也留着，文件名还在。</summary>
    private static TagFile TagFor(DeliveredFile file, IReadOnlyList<WhereLine> lines)
    {
        var size = file.Size is { } n ? Format.Size(n) : "";
        return new TagFile(file.Name, size, lines);
    }

    private static DeliveredFile PendingFile(UploadJob job) =>
        new(job.FileName, job.Size > 0 ? job.Size : null, [], []);

    /// <summary>还在待传名单里的：这次失败过就每个都写重试，还没轮到才写等待。顶掉之后才写失败。</summary>
    private static IReadOnlyList<WhereLine> DeliveryLines(AppConfig config, DeliveredFile file, IEnumerable<UploadJob> waiting, IEnumerable<AbandonedTarget> abandoned)
    {
        var lines = new List<WhereLine>();
        for (var i = 0; i < file.TargetIds.Count; i++)
        {
            var stored = i < file.TargetNames.Count ? file.TargetNames[i] : "";
            var name = stored.Length > 0 ? stored : LiveName(config, file.TargetIds[i]);
            if (string.IsNullOrEmpty(name)) continue;
            var elapsed = file.Elapsed is { } times && i < times.Count ? times[i] : null;
            lines.Add(WhereLine.Delivered(name, elapsed));
        }
        var pending = new HashSet<string>();
        foreach (var job in waiting)
        {
            if (!string.Equals(job.FileName, file.Name, StringComparison.OrdinalIgnoreCase)) continue;
            var failed = job.LastError.Length > 0;
            foreach (var id in job.PendingTargetIds)
            {
                if (config.Targets.FirstOrDefault(t => t.Id == id) is { Disabled: true }) continue;
                if (LiveName(config, id) is not { } name) continue;
                pending.Add(id);
                lines.Add(failed
                    ? WhereLine.Of($"{job.NextAttemptUtc.ToLocalTime():HH:mm} 重试「{name}」", true)
                    : WhereLine.Of($"等待上传「{name}」", false));
            }
        }
        foreach (var left in abandoned)
        {
            if (!string.Equals(left.FileName, file.Name, StringComparison.OrdinalIgnoreCase) || pending.Contains(left.TargetId)) continue;
            var name = left.TargetName.Length > 0 ? left.TargetName : LiveName(config, left.TargetId);
            if (string.IsNullOrEmpty(name)) continue;
            lines.Add(WhereLine.Of($"失败「{name}」", true));
        }
        return lines;
    }

    private static string? LiveName(AppConfig config, string? id) =>
        string.IsNullOrEmpty(id) ? null : config.Targets.FirstOrDefault(t => t.Id == id)?.Name;

    private static string PlanLabel(AppConfig config, string id, string saved)
    {
        var plan = config.Plans.FirstOrDefault(p => p.Id == id);
        return RecordPlanName.Show(plan is null ? null : plan.Name, saved);
    }

    private static string Quote(IEnumerable<string> names) => string.Concat(names.Select(n => $"「{n}」"));

    private static string PendingName(UploadTarget? target) => target switch
    {
        null => "已删除的目的地",
        { Disabled: true } => $"{target.Name}（已停用）",
        _ => target.Name,
    };

    private static string TargetName(AppConfig config, string id) =>
        config.Targets.FirstOrDefault(t => t.Id == id)?.Name ?? "已删除的目的地";

    /// <summary>失败记录里写的是当时的目的地名。改名之后换成现在的名字。</summary>
    private static string CurrentError(string error, string? name)
    {
        var colon = error.IndexOf('：');
        var reason = colon >= 0 ? error[(colon + 1)..] : error;
        if (string.IsNullOrEmpty(name) && colon >= 0) name = error[..colon];
        return string.IsNullOrEmpty(name) ? reason : $"「{name}」{reason}";
    }

    private void OnStatusLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock block) return;
        block.DataContextChanged -= OnStatusContext;
        block.DataContextChanged += OnStatusContext;
        FitStatus(block);
    }

    private void OnStatusSize(object sender, SizeChangedEventArgs e) => FitStatus(sender as TextBlock);

    private void OnStatusContext(object sender, DependencyPropertyChangedEventArgs e) => FitStatus(sender as TextBlock);

    /// <summary>整句放得下就全显示。放不下只留已送达「目的地」，后面换成省略号。</summary>
    private static void FitStatus(TextBlock? block)
    {
        if (block?.DataContext is not WhereLine line) return;
        var width = block.ActualWidth;
        if (width <= 0 || double.IsInfinity(width)) return;
        var shown = line.Tail.Length == 0 || Fits(block, line.Text, width) ? line.Text : line.Head + "…";
        if (block.Text != shown) block.Text = shown;
    }

    private static bool Fits(TextBlock block, string text, double width)
    {
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentUICulture,
            FlowDirection.LeftToRight,
            new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch),
            block.FontSize,
            Brushes.Black,
            VisualTreeHelper.GetDpi(block).PixelsPerDip);
        return formatted.Width <= width + 1;
    }

    private void OnRetry(object sender, RoutedEventArgs e) => App.Services.Queue.RetryNow();

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string id }) return;
        var name = App.Services.Queue.Snapshot().FirstOrDefault(j => j.Id == id)?.FileName;
        var message = string.IsNullOrEmpty(name) ? "删除这个待上传文件" : $"删除「{name}」";
        if (!MessageDialog.Confirm(Window.GetWindow(this), "删除待上传文件", message,
                "不再上传，本地的包也一起删掉", "删除", danger: true)) return;
        App.Services.Queue.Remove(id);
    }
}
