using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Services;

public enum StatusPhase
{
    Idle,
    Collect,
    Pack,
    Upload,
}

/// <summary>当前这句状态。托盘悬停和主界面底栏都从这里取，口径一致。</summary>
public sealed class StatusLine
{
    private readonly AppServices _app;
    private readonly BackupService _backup;
    private readonly object _gate = new();
    private readonly List<string> _arrived = [];
    private StatusPhase _backupPhase;
    private TextSource _source;
    private long _percent = -1;
    private string _batch = "";
    private DateTime _runStartedUtc;
    private readonly FlightMeter _flight = new();

    /// <summary>这句话是谁写的。进行中的那句跑完要收走，结论留着。</summary>
    private enum TextSource { Result, Backup, Upload }

    public event Action? Changed;

    public StatusLine(AppServices app, BackupService backup, AutoBackupService auto)
    {
        _app = app;
        _backup = backup;
        backup.Status += OnStatus;
        backup.Notice += OnNotice;
        auto.Notice += (title, text) => OnNotice(title, text, title == "无法自动备份");
        app.Queue.Progress += OnProgress;
        app.Queue.TargetDone += OnTargetDone;
        app.Queue.JobFailed += OnJobFailed;
        app.Queue.Changed += OnQueueChanged;
    }

    public string Text { get; private set; } = "";

    public bool IsError { get; private set; }

    /// <summary>这一批已经送达的目的地。主界面把它和失败分两种颜色显示。</summary>
    public IReadOnlyList<string> Arrived
    {
        get { lock (_gate) return [.. _arrived]; }
    }

    /// <summary>这一批最后一次上传失败。</summary>
    public string? DeliveryError { get; private set; }

    /// <summary>最近一次备份失败。队列里没有失败任务时，悬停用它。</summary>
    public string RunFailure { get; private set; } = "";

    public StatusPhase Phase =>
        _backupPhase != StatusPhase.Idle ? _backupPhase :
        _app.Queue.IsBusy ? StatusPhase.Upload : StatusPhase.Idle;

    /// <summary>0 到 1 是这一次备份的飞机位置。没有则不飞。</summary>
    public double? Flight
    {
        get { lock (_gate) return _flight.Flight; }
    }

    /// <summary>界面上临时说一句，例如浏览器还开着。</summary>
    public void Note(string text, bool error) => Set(text, error, TextSource.Result);

    /// <summary>速度写在句尾。快速是全速，慢速让着别的程序。</summary>
    public static string Pace(bool slow, string text) => text + " · " + (slow ? "慢速" : "快速");

    /// <summary>失败那句：「目的地」原因，几点重试。</summary>
    public static string Failure(UploadJob job, AppConfig config)
    {
        var error = job.LastError;
        var colon = error.IndexOf('：');
        var reason = colon >= 0 ? error[(colon + 1)..] : error;
        var name = config.Targets.FirstOrDefault(t => t.Id == job.PendingTargetIds.FirstOrDefault())?.Name;
        if (string.IsNullOrEmpty(name) && colon >= 0) name = error[..colon];
        var head = string.IsNullOrEmpty(name) ? "" : $"「{name}」";
        return $"{head}{reason}，{job.NextAttemptUtc.ToLocalTime():HH:mm} 重试";
    }

    private void OnStatus(BackupStatus? status)
    {
        if (status is null)
        {
            ClearBackupSentence();
            return;
        }

        var queued = status.Stage == BackupStage.Queued;
        var retrying = queued && _app.Queue.HasRetryPending();
        var text = status.Stage switch
        {
            BackupStage.Collecting => Pace(_backup.Slow, status.Percent < 0
                ? $"正在提取「{status.PlanName}」"
                : $"正在提取「{status.PlanName}」{status.Percent}%"),
            BackupStage.Packing => Pace(_backup.Slow, $"正在打包「{status.PlanName}」{status.Percent}%"),
            _ when !retrying => Pace(_backup.Slow, $"「{status.PlanName}」已放进上传队列"),
            _ => null,
        };
        var phase = status.Stage switch
        {
            BackupStage.Collecting => StatusPhase.Collect,
            BackupStage.Packing => StatusPhase.Pack,
            _ => StatusPhase.Idle,
        };
        var changed = false;
        lock (_gate)
        {
            if (queued && _backupPhase != StatusPhase.Idle)
            {
                _backupPhase = StatusPhase.Idle;
                changed = true;
            }
            // 上传、失败或送达已经写上时，迟到的打包进度不能盖住，否则结束时会连失败一起清掉
            if (_source == TextSource.Backup && text is not null)
            {
                _backupPhase = phase;
                Text = text;
                IsError = false;
                _source = TextSource.Backup;
                changed = true;
            }
            if (FlyStage(status)) changed = true;
        }
        if (changed) Changed?.Invoke();
    }

    /// <summary>打包结束只收走自己的那句。上传已经写了进度、失败或送达，就留着。</summary>
    private void ClearBackupSentence()
    {
        lock (_gate)
        {
            _backupPhase = StatusPhase.Idle;
            if (_source == TextSource.Backup)
            {
                Text = "";
                IsError = false;
                _source = TextSource.Result;
            }
            SettleFlight();
        }
        Changed?.Invoke();
    }

    private void OnNotice(string title, string text, bool error)
    {
        if (title == "已取消")
        {
            _backupPhase = StatusPhase.Idle;
            RunFailure = "";
            lock (_gate) _flight.End();
            Set("已取消", error: false, TextSource.Result);
            return;
        }
        if (title == "开始备份")
        {
            ResetDelivery();
            _runStartedUtc = DateTime.UtcNow;
            RunFailure = "";
            lock (_gate) _flight.Begin();
            // 来源记成打包，后面的提取进度才能接上。失败和送达是结论，进度不能倒回去盖
            Set(Pace(_backup.Slow, $"{title}：{text}"), error: false, TextSource.Backup);
            return;
        }
        if (error)
        {
            _backupPhase = StatusPhase.Idle;
            RunFailure = $"{title}：{text}";
            lock (_gate) _flight.End();
            Set(RunFailure, error: true, TextSource.Result);
            return;
        }
        RunFailure = "";
        // 本地文件夹传得快，备份完成时可能已经送达，这时留着送达那句
        if (text.Contains("已送达全部目的地", StringComparison.Ordinal) && _arrived.Count > 0)
        {
            ShowDelivery(_backup.Slow, _runStartedUtc);
            return;
        }
        if (_app.Queue.HasRetryPending()) return;
        var line = $"{title}：{text}";
        if (title is "打包完成" or "备份完成") line = Pace(_backup.Slow, line);
        if (title == "备份完成") line = WithTime(line, _runStartedUtc);
        SetKeepingFailure(line);
    }

    /// <summary>打包完成这句不能盖住正在上传，也不能盖住已经写出的失败。</summary>
    private void SetKeepingFailure(string line)
    {
        var wrote = false;
        lock (_gate)
        {
            if (IsError || _source == TextSource.Upload || _arrived.Count > 0 || DeliveryError is not null) return;
            Text = line;
            IsError = false;
            _source = TextSource.Result;
            wrote = true;
        }
        if (wrote) Changed?.Invoke();
    }

    private void OnProgress(UploadProgress p)
    {
        lock (_gate) FlyUpload(p);
        var percent = p.Total > 0 ? p.Sent * 100 / p.Total : 0;
        if (Interlocked.Exchange(ref _percent, percent) == percent) return;
        if (_app.Queue.HasRetryPending()) return;
        Set(Pace(p.Job.Slow, $"正在上传到「{p.TargetName}」{percent}%"), error: false, TextSource.Upload);
    }

    private void OnTargetDone(UploadJob job, string target)
    {
        Interlocked.Exchange(ref _percent, -1);
        lock (_gate)
        {
            StartBatch(job.BatchId);
            if (!_arrived.Contains(target)) _arrived.Add(target);
            if (DeliveryError is { } e && e.StartsWith($"「{target}」", StringComparison.Ordinal)) DeliveryError = null;
            if (_flight.Active && job.BatchId == _flight.BatchId && !_flight.Pending(_app.Queue.Snapshot()))
                _flight.End();
        }
        ShowDelivery(job.Slow, job.StartedUtc);
    }

    private void OnJobFailed(UploadJob job, string error)
    {
        Interlocked.Exchange(ref _percent, -1);
        lock (_gate)
        {
            StartBatch(job.BatchId);
            DeliveryError = Failure(job, _app.Config);
        }
        ShowDelivery(job.Slow, job.StartedUtc);
    }

    /// <summary>队列空了就把"正在上传"收走，别一直挂着。</summary>
    private void OnQueueChanged()
    {
        var ended = false;
        lock (_gate)
        {
            if (_flight.Active && _flight.BatchId.Length > 0 && !_flight.Pending(_app.Queue.Snapshot()))
            {
                _flight.End();
                ended = true;
            }
        }
        if (_source != TextSource.Upload)
        {
            if (ended) Changed?.Invoke();
            return;
        }
        var queue = _app.Queue;
        if (queue.IsBusy || queue.Snapshot().Count > 0)
        {
            if (ended) Changed?.Invoke();
            return;
        }
        Set("", error: false, TextSource.Result);
    }

    /// <summary>提取、打包往前走。放进队列后记下这一批的总字节。</summary>
    private bool FlyStage(BackupStatus status)
    {
        if (!_flight.Active) return false;
        var before = _flight.Position;
        _flight.Stage(status.PlanName, status.Stage, status.Percent);
        if (status.Stage == BackupStage.Queued) ArmFlight(status.PlanName);
        return _flight.Position > before;
    }

    private bool FlyUpload(UploadProgress progress)
    {
        if (!_flight.Active || progress.Job.StartedUtc < _runStartedUtc) return false;
        if (_flight.Plan.Length > 0 && progress.Job.PlanName != _flight.Plan) return false;
        var before = _flight.Position;
        var queued = _app.Queue.Snapshot();
        if (_flight.BatchId.Length == 0)
            _flight.Arm(queued.Where(j => j.BatchId == progress.Job.BatchId).ToList());
        _flight.Advance(queued, progress);
        return _flight.Position > before;
    }

    private void ArmFlight(string planName)
    {
        if (_flight.BatchId.Length > 0) return;
        var queued = _app.Queue.Snapshot();
        var mine = queued.Where(j => j.PlanName == planName && j.StartedUtc >= _runStartedUtc).ToList();
        if (mine.Count == 0) return;
        var batch = mine.MaxBy(j => j.CreatedUtc)!.BatchId;
        _flight.Arm(mine.Where(j => j.BatchId == batch).ToList());
        _flight.Advance(queued, null);
    }

    /// <summary>提取打包结束。这一批还在队列里就接着飞，否则收起。</summary>
    private void SettleFlight()
    {
        if (!_flight.Active) return;
        var queued = _app.Queue.Snapshot();
        if (_flight.BatchId.Length == 0)
        {
            var mine = queued.Where(j => j.StartedUtc >= _runStartedUtc).ToList();
            if (mine.Count > 0)
            {
                var batch = mine.MaxBy(j => j.CreatedUtc)!.BatchId;
                _flight.Arm(mine.Where(j => j.BatchId == batch).ToList());
                _flight.Advance(queued, null);
            }
        }
        if (!_flight.Pending(queued)) _flight.End();
    }

    private void StartBatch(string batchId)
    {
        if (_batch == batchId) return;
        _batch = batchId;
        _arrived.Clear();
        DeliveryError = null;
    }

    private void ResetDelivery()
    {
        lock (_gate)
        {
            _batch = "";
            _arrived.Clear();
            DeliveryError = null;
        }
    }

    private void ShowDelivery(bool slow, DateTime startedUtc)
    {
        string? arrived;
        lock (_gate) arrived = _arrived.Count > 0 ? "已送达" + string.Concat(_arrived.Select(n => $"「{n}」")) : null;
        var error = DeliveryError;
        if (arrived is null && error is null) return;
        var line = Pace(slow, arrived ?? error!);
        // 这一批都送完才算完成。还在传别的目的地时不写用时
        var pending = _batch.Length > 0 && _app.Queue.Snapshot().Any(j => j.BatchId == _batch);
        if (arrived is not null && !pending) line = WithTime(line, startedUtc);
        Set(line, error: arrived is null, TextSource.Result);
    }

    /// <summary>完成后的用时。早前的任务没有开始时间，就不写。</summary>
    private static string WithTime(string text, DateTime startedUtc)
    {
        if (startedUtc.Year < 2000) return text;
        return text + " · 用时 " + Format.Duration(DateTime.UtcNow - startedUtc);
    }

    private void Set(string text, bool error, TextSource source)
    {
        lock (_gate)
        {
            Text = text;
            IsError = error;
            _source = source;
        }
        Changed?.Invoke();
    }
}
