using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>这一次备份的飞机。只往前走，上传只认当前这一批。</summary>
public sealed class FlightMeter
{
    private string _plan = "";
    private string _batch = "";
    private long _total;

    public bool Active { get; private set; }

    public double Position { get; private set; }

    public string Plan => _plan;

    public string BatchId => _batch;

    public double? Flight => Active ? Position : null;

    public void Begin()
    {
        Active = true;
        Position = 0;
        _plan = "";
        _batch = "";
        _total = 0;
    }

    public void End() => Active = false;

    public void Stage(string planName, BackupStage stage, int percent)
    {
        if (!Active) return;
        if (_plan.Length == 0) _plan = planName;
        if (planName != _plan || _batch.Length > 0) return;
        if (stage is BackupStage.Collecting or BackupStage.Packing)
            Move(FlightProgress.Stage(stage, percent));
    }

    public void Arm(IReadOnlyList<UploadJob> batch)
    {
        if (!Active || _batch.Length > 0 || batch.Count == 0) return;
        _batch = batch[0].BatchId;
        if (_plan.Length == 0) _plan = batch[0].PlanName;
        long total = 0;
        foreach (var job in batch)
        {
            var targets = job.TargetIds.Count;
            if (targets < 1) targets = 1;
            total += job.Size * targets;
        }
        _total = total;
        Move(FlightProgress.Upload(0, _total));
    }

    public void Advance(IReadOnlyList<UploadJob> queued, UploadProgress? current)
    {
        if (!Active || _batch.Length == 0) return;
        if (current is not null && current.Job.BatchId != _batch) return;
        long remaining = 0;
        foreach (var job in queued)
        {
            if (job.BatchId != _batch) continue;
            remaining += job.PendingTargetIds.Count * job.Size;
            if (current?.Job.Id == job.Id)
                remaining -= Math.Clamp(current.Sent, 0, job.Size);
        }
        if (remaining < 0) remaining = 0;
        var sent = _total - remaining;
        if (sent < 0) sent = 0;
        Move(FlightProgress.Upload(sent, _total));
    }

    public bool Pending(IReadOnlyList<UploadJob> queued)
    {
        if (_batch.Length == 0) return false;
        foreach (var job in queued)
            if (job.BatchId == _batch) return true;
        return false;
    }

    private void Move(double next)
    {
        if (next > Position) Position = next;
    }
}
