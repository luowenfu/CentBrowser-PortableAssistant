using System.Net.NetworkInformation;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

public sealed record UploadProgress(UploadJob Job, string TargetName, long Sent, long Total);

/// <summary>
/// 本地待传队列，存在 Pending\queue.json，程序重启后接着传。
/// 失败按退避时间重试，网络恢复时立即重试。
/// </summary>
public sealed class UploadQueue : IDisposable
{
    private readonly object _gate = new();
    private readonly List<UploadJob> _jobs;
    private readonly Func<AppConfig> _config;
    private readonly HistoryStore _history;
    private readonly SemaphoreSlim _signal = new(0);
    private readonly CancellationTokenSource _stop = new();
    private CancellationTokenSource _attempt = new();
    private Task? _loop;
    private int _generation;
    private int _inProcess;
    private TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public event Action? Changed;
    public event Action<UploadProgress>? Progress;
    public event Action<UploadJob, string>? TargetDone;
    public event Action<UploadJob>? JobDone;
    public event Action<UploadJob, string>? JobFailed;

    public UploadQueue(Func<AppConfig> config, HistoryStore history)
    {
        _config = config;
        _history = history;
        _jobs = JsonFile.Read<List<UploadJob>>(AppPaths.QueueFile) ?? [];
        _jobs.RemoveAll(j => !File.Exists(j.LocalPath));
        NetworkChange.NetworkAvailabilityChanged += OnNetworkChanged;
    }

    public IReadOnlyList<UploadJob> Snapshot()
    {
        lock (_gate) return [.. _jobs];
    }

    public bool IsBusy { get; private set; }

    /// <summary>有任务已经失败，并且还没到下一次重试。用来避免旧的“正在上传”盖住错误。</summary>
    public bool HasRetryPending()
    {
        var now = DateTime.UtcNow;
        lock (_gate) return _jobs.Any(j => j.LastError.Length > 0 && j.NextAttemptUtc > now);
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_stop.Token));

    /// <summary>丢掉还没传完的任务。正在传的会停下，并且不再写进备份记录。</summary>
    public async Task DropPendingAsync()
    {
        CancellationTokenSource previous;
        Task idle;
        lock (_gate)
        {
            Interlocked.Increment(ref _generation);
            previous = _attempt;
            _attempt = new CancellationTokenSource();
            _jobs.Clear();
            Save();
            idle = _inProcess == 0 ? Task.CompletedTask : _idle.Task;
        }
        previous.Cancel();
        Changed?.Invoke();
        await idle.ConfigureAwait(false);
    }

    /// <summary>同一方案出了新包时，队列里没传完的旧包作废。</summary>
    public void Enqueue(IReadOnlyList<UploadJob> batch)
    {
        if (batch.Count == 0) return;
        var planId = batch[0].PlanId;
        var batchId = batch[0].BatchId;
        List<UploadJob> dropped;
        lock (_gate)
        {
            dropped = _jobs.Where(j => j.PlanId == planId && j.BatchId != batchId).ToList();
            foreach (var old in dropped)
            {
                DeleteLocal(old);
                _jobs.Remove(old);
            }
            _jobs.AddRange(batch);
            Save();
        }
        if (dropped.Count > 0)
        {
            var config = _config();
            foreach (var job in dropped)
            {
                if (job.PlanName.Length > 0) continue;
                var live = config.Plans.FirstOrDefault(p => p.Id == job.PlanId)?.Name;
                if (!string.IsNullOrEmpty(live)) job.PlanName = live;
            }
            _history.Update(h =>
            {
                AbandonedUploads.Add(h.Abandoned, dropped, id => config.Targets.FirstOrDefault(t => t.Id == id)?.Name);
            });
        }
        Changed?.Invoke();
        _signal.Release();
    }

    public void RetryNow()
    {
        lock (_gate)
        {
            foreach (var j in _jobs) j.NextAttemptUtc = DateTime.UtcNow;
            Save();
        }
        Changed?.Invoke();
        Nudge();
    }

    /// <summary>配置变了就醒一次。停用的目的地重新启用后，排着的包可以接着传。</summary>
    public void Nudge()
    {
        try { _signal.Release(); }
        catch (ObjectDisposedException) { }
    }

    public void Remove(string jobId)
    {
        lock (_gate)
        {
            var job = _jobs.FirstOrDefault(j => j.Id == jobId);
            if (job is null) return;
            DeleteLocal(job);
            _jobs.Remove(job);
            Save();
        }
        Changed?.Invoke();
    }

    private void OnNetworkChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        if (e.IsAvailable) RetryNow();
    }

    private async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            UploadJob? job;
            TimeSpan wait;
            lock (_gate)
            {
                var now = DateTime.UtcNow;
                var config = _config();
                job = _jobs.Where(j => j.NextAttemptUtc <= now && HasOpenTarget(j, config)).OrderBy(j => j.CreatedUtc).FirstOrDefault();
                var next = _jobs.Count == 0 ? now.AddMinutes(10) : _jobs.Min(j => j.NextAttemptUtc);
                wait = next > now ? next - now : TimeSpan.Zero;
                // 到点的包都停着，不能空转。启用目的地时会把等待叫醒
                if (job is null && wait == TimeSpan.Zero) wait = TimeSpan.FromMinutes(10);
            }

            CancellationToken attempt;
            int generation;
            lock (_gate)
            {
                attempt = _attempt.Token;
                generation = _generation;
            }

            if (job is null)
            {
                try { await _signal.WaitAsync(wait < TimeSpan.FromMinutes(10) ? wait : TimeSpan.FromMinutes(10), ct); }
                catch (OperationCanceledException) { return; }
                continue;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, attempt);
            lock (_gate)
            {
                if (generation != _generation || !_jobs.Contains(job)) continue;
                if (_inProcess++ == 0) _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }

            IsBusy = true;
            Changed?.Invoke();
            try
            {
                try { await ProcessAsync(job, linked.Token, generation); }
                catch (OperationCanceledException) when (!ct.IsCancellationRequested) { }
                catch (OperationCanceledException) { return; }
            }
            finally
            {
                lock (_gate)
                {
                    if (--_inProcess == 0) _idle.TrySetResult();
                }
                IsBusy = false;
                Changed?.Invoke();
            }
        }
    }

    private async Task ProcessAsync(UploadJob job, CancellationToken ct, int generation)
    {
        var config = _config();
        var plan = config.Plans.FirstOrDefault(p => p.Id == job.PlanId);
        string? failure = null;
        foreach (var targetId in job.PendingTargetIds.ToList())
        {
            if (generation != Volatile.Read(ref _generation)) return;
            var target = config.Targets.FirstOrDefault(t => t.Id == targetId);
            if (target is null)
            {
                if (!Finish(job, targetId, generation)) return;
                continue;
            }
            if (target.Disabled) continue;

            try
            {
                await using var uploader = UploaderFactory.Create(target, job.Slow ? config.SlowUploadKbps : 0);
                var progress = new Progress<long>(sent => Progress?.Invoke(new UploadProgress(job, target.Name, sent, job.Size)));
                var uploadStartedUtc = DateTime.UtcNow;
                var remote = await uploader.UploadAsync(job.LocalPath, job.FileName, progress, ct);
                if (generation != Volatile.Read(ref _generation)) return;

                _history.Update(h =>
                {
                    h.Records.Add(new RemoteRecord
                    {
                        PlanId = job.PlanId,
                        PlanName = plan is { Name.Length: > 0 } ? plan.Name : job.PlanName,
                        TargetId = targetId,
                        TargetName = target.Name,
                        BatchId = job.BatchId,
                        RemotePath = remote,
                        Size = job.Size,
                        UploadedUtc = DateTime.UtcNow,
                        QueuedUtc = job.CreatedUtc,
                        FromAuto = job.FromAuto,
                        Slow = job.Slow,
                        StartedUtc = job.StartedUtc,
                        UploadStartedUtc = uploadStartedUtc,
                    });
                    h.Abandoned.RemoveAll(a => a.BatchId == job.BatchId && a.TargetId == targetId &&
                                               string.Equals(a.FileName, job.FileName, StringComparison.OrdinalIgnoreCase));
                    BackupRecords.TrimHistory(h);
                });
                if (!Finish(job, targetId, generation)) return;
                TargetDone?.Invoke(job, target.Name);

                if (plan is not null) await TryCleanAsync(uploader, job, targetId, plan.KeepCount, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested && UploadErrors.IsExpected(ex))
            {
                // 这个没传上，剩下的目的地继续。失败几次也只算这一轮一次
                failure = $"{target.Name}：{UploadErrors.Brief(ex)}";
            }
        }
        if (failure is null) return;
        lock (_gate)
        {
            if (generation != _generation || !_jobs.Contains(job)) return;
            job.Attempts++;
            job.NextAttemptUtc = DateTime.UtcNow + UploadJob.Backoff(job.Attempts);
            job.LastError = failure;
            Save();
        }
        JobFailed?.Invoke(job, failure);
    }

    private async Task TryCleanAsync(IUploader uploader, UploadJob job, string targetId, int keep, CancellationToken ct)
    {
        try
        {
            await RetentionCleaner.CleanAsync(uploader, _history, job.PlanId, targetId, keep, ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && UploadErrors.IsExpected(ex))
        {
            // 删旧备份失败不影响本次上传，下次上传成功时会再清理一次
        }
    }

    /// <summary>删掉的目的地要清掉；停用的先留着，重新启用再传。</summary>
    private static bool HasOpenTarget(UploadJob job, AppConfig config) =>
        job.PendingTargetIds.Any(id => config.Targets.FirstOrDefault(t => t.Id == id) is not { Disabled: true });

    private bool Finish(UploadJob job, string targetId, int generation)
    {
        var allDone = false;
        lock (_gate)
        {
            if (generation != _generation) return false;
            job.PendingTargetIds.Remove(targetId);
            if (job.PendingTargetIds.Count == 0)
            {
                DeleteLocal(job);
                _jobs.Remove(job);
                allDone = true;
            }
            Save();
        }
        Changed?.Invoke();
        if (!allDone) return true;
        JobDone?.Invoke(job);
        TryRecord(job, generation);
        return true;
    }

    /// <summary>还在的目的地都有送达记录才写下上次成功和指纹。旧任务没有指纹，不覆盖正式记录。</summary>
    private void TryRecord(UploadJob job, int generation)
    {
        if (generation != Volatile.Read(ref _generation)) return;
        if (job.Fingerprint.Length == 0) return;
        if (Snapshot().Any(j => j.BatchId == job.BatchId)) return;

        var living = _config().Targets.Select(t => t.Id).ToHashSet();
        var wanted = job.TargetIds.Where(living.Contains).ToHashSet();
        var delivered = _history.Read(h => h.Records.Where(r => r.BatchId == job.BatchId).Select(r => r.TargetId).ToHashSet());
        if (delivered.Count == 0 || wanted.Any(id => !delivered.Contains(id))) return;

        var when = DateTime.Now;
        _history.Update(h =>
        {
            var state = h.For(job.PlanId);
            state.LastSuccessLocal = when;
            state.Fingerprint = job.Fingerprint;
        });
    }

    private void Save() => JsonFile.Write(AppPaths.QueueFile, _jobs);

    private static void DeleteLocal(UploadJob job)
    {
        File.Delete(job.LocalPath);
        var dir = Path.GetDirectoryName(job.LocalPath);
        if (dir is not null && Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir);
    }

    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        NetworkChange.NetworkAvailabilityChanged -= OnNetworkChanged;
        _stop.Cancel();
        _stop.Dispose();
        _signal.Dispose();
    }
}
