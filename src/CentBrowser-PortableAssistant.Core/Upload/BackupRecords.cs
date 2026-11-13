using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>一次送达里的一个文件。大小没记过就是 null。目的地只含真正收到它的。</summary>
/// <param name="Slow">和目的地一一对应。早前的记录没有，是 null。</param>
/// <param name="Elapsed">送到这个目的地用了多久。早前的记录没有。</param>
public sealed record DeliveredFile(
    string Name,
    long? Size,
    IReadOnlyList<string> TargetIds,
    IReadOnlyList<string> TargetNames,
    IReadOnlyList<bool?>? Slow = null,
    IReadOnlyList<TimeSpan?>? Elapsed = null);

/// <summary>一次已经送达的备份。同一次打出的多个文件算一块，大小不按目的地重复计算。</summary>
/// <param name="UploadedUtc">放进上传队列的时间。早前没有这份时间的记录，用最晚一次送达。</param>
public sealed record DeliveredBackup(
    string PlanId,
    DateTime UploadedUtc,
    IReadOnlyList<string> FileNames,
    IReadOnlyList<string> TargetIds,
    long? Size,
    IReadOnlyList<DeliveredFile> Files,
    bool? FromAuto,
    string BatchId,
    string PlanName);

public static class BackupRecords
{
    public const int MaxRows = 100;

    /// <summary>一个目的地先送到就先列出。一个都没送到、后来被新备份顶掉的也留在记录里。最多 100 次。</summary>
    public static IReadOnlyList<DeliveredBackup> Delivered(IEnumerable<RemoteRecord> records, IEnumerable<AbandonedTarget>? abandoned = null)
    {
        var extra = (abandoned ?? []).Where(a => a.BatchId.Length > 0).GroupBy(a => a.BatchId).ToDictionary(g => g.Key, g => g.ToList());
        var rows = records
            .Where(r => r.BatchId.Length > 0)
            .GroupBy(r => r.BatchId)
            .Select(g => WithAbandonedFiles(ToBackup(g), extra.GetValueOrDefault(g.Key)))
            .ToList();
        var seen = rows.Select(r => r.BatchId).ToHashSet();
        foreach (var pair in extra)
        {
            if (!seen.Contains(pair.Key)) rows.Add(FromAbandoned(pair.Value));
        }
        return rows.OrderByDescending(b => b.UploadedUtc).Take(MaxRows).ToList();
    }

    /// <summary>送达记录和没送成的记录合在一起，只留最近 100 次。</summary>
    public static void TrimHistory(BackupHistory history)
    {
        var times = new Dictionary<string, DateTime>();
        foreach (var record in history.Records)
            Touch(times, record.BatchId, ListedUtc(record));
        foreach (var left in history.Abandoned)
            Touch(times, left.BatchId, left.CreatedUtc);
        var keep = times.OrderByDescending(p => p.Value).Take(MaxRows).Select(p => p.Key).ToHashSet();
        history.Records.RemoveAll(r => r.BatchId.Length > 0 && !keep.Contains(r.BatchId));
        history.Abandoned.RemoveAll(a => a.BatchId.Length == 0 || !keep.Contains(a.BatchId));
    }

    private static void Touch(Dictionary<string, DateTime> times, string batchId, DateTime when)
    {
        if (batchId.Length == 0) return;
        if (!times.TryGetValue(batchId, out var known) || when > known) times[batchId] = when;
    }

    /// <summary>只留最近 100 次备份。返回删掉的条数。</summary>
    public static int Trim(List<RemoteRecord> records)
    {
        var keep = records
            .Where(r => r.BatchId.Length > 0)
            .GroupBy(r => r.BatchId)
            .OrderByDescending(g => BatchWhen(g))
            .Take(MaxRows)
            .Select(g => g.Key)
            .ToHashSet();
        return records.RemoveAll(r => r.BatchId.Length > 0 && !keep.Contains(r.BatchId));
    }

    private static DeliveredBackup ToBackup(IGrouping<string, RemoteRecord> batch)
    {
        var pieces = batch
            .GroupBy(r => DisplayName(r.RemotePath), StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g =>
            {
                var arrived = g.OrderBy(r => r.UploadedUtc)
                    .GroupBy(r => r.TargetId)
                    .Select(t =>
                    {
                        var first = t.First();
                        var started = first.UploadStartedUtc.Year >= 2000 ? first.UploadStartedUtc : first.StartedUtc;
                        var elapsed = started.Year >= 2000 && first.UploadedUtc >= started
                            ? first.UploadedUtc - started
                            : (TimeSpan?)null;
                        return (
                            Id: t.Key,
                            Name: t.Select(r => r.TargetName).FirstOrDefault(n => n is { Length: > 0 }) ?? "",
                            first.Slow,
                            Elapsed: elapsed);
                    })
                    .ToList();
                return new DeliveredFile(
                    g.Key,
                    g.Max(r => r.Size) is var size && size > 0 ? size : null,
                    arrived.Select(t => t.Id).ToList(),
                    arrived.Select(t => t.Name).ToList(),
                    arrived.Select(t => t.Slow).ToList(),
                    arrived.Select(t => t.Elapsed).ToList());
            })
            .ToList();
        var targets = batch
            .OrderBy(r => r.UploadedUtc)
            .Select(r => r.TargetId)
            .Distinct()
            .ToList();
        return new DeliveredBackup(
            batch.First().PlanId,
            BatchWhen(batch),
            pieces.Select(f => f.Name).ToList(),
            targets,
            pieces.All(f => f.Size is not null) ? pieces.Sum(f => f.Size) : null,
            pieces,
            batch.Select(r => r.FromAuto).FirstOrDefault(v => v is not null),
            batch.Key,
            RememberedName(batch.Select(r => r.PlanName)));
    }

    /// <summary>这次还有文件一个目的地都没送到，补进行李牌，状态由调用方写成失败。</summary>
    private static DeliveredBackup WithAbandonedFiles(DeliveredBackup backup, List<AbandonedTarget>? extra)
    {
        if (extra is null || extra.Count == 0) return backup;
        var planName = backup.PlanName.Length > 0 ? backup.PlanName : RememberedName(extra.Select(a => a.PlanName));
        var names = backup.Files.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var added = extra.Where(a => a.FileName.Length > 0 && !names.Contains(a.FileName))
            .GroupBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeliveredFile(g.Key, g.Max(a => a.Size) is var size && size > 0 ? size : null, [], []))
            .ToList();
        if (added.Count == 0) return planName == backup.PlanName ? backup : backup with { PlanName = planName };
        var files = backup.Files.Concat(added).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        return backup with
        {
            Files = files,
            FileNames = files.Select(f => f.Name).ToList(),
            Size = files.All(f => f.Size is not null) ? files.Sum(f => f.Size) : null,
            PlanName = planName,
        };
    }

    private static DeliveredBackup FromAbandoned(List<AbandonedTarget> items)
    {
        var files = items.Where(a => a.FileName.Length > 0)
            .GroupBy(a => a.FileName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => new DeliveredFile(g.Key, g.Max(a => a.Size) is var size && size > 0 ? size : null, [], []))
            .ToList();
        return new DeliveredBackup(
            items.Select(a => a.PlanId).FirstOrDefault(id => id.Length > 0) ?? "",
            items.Max(a => a.CreatedUtc),
            files.Select(f => f.Name).ToList(),
            [],
            files.Count > 0 && files.All(f => f.Size is not null) ? files.Sum(f => f.Size) : null,
            files,
            items.Select(a => a.FromAuto).FirstOrDefault(v => v is not null),
            items[0].BatchId,
            RememberedName(items.Select(a => a.PlanName)));
    }

    /// <summary>有进队列的时间就用它，否则用这次送达的时间。</summary>
    public static DateTime ListedUtc(RemoteRecord record) =>
        record.QueuedUtc.Year >= 2000 ? record.QueuedUtc : record.UploadedUtc;

    /// <summary>同一次备份的时间。几个目的地都记下了进队列时间时，用最早的那个。</summary>
    private static DateTime BatchWhen(IEnumerable<RemoteRecord> batch)
    {
        DateTime? queued = null;
        var uploaded = DateTime.MinValue;
        foreach (var record in batch)
        {
            if (record.UploadedUtc > uploaded) uploaded = record.UploadedUtc;
            if (record.QueuedUtc.Year < 2000) continue;
            if (queued is null || record.QueuedUtc < queued) queued = record.QueuedUtc;
        }
        return queued ?? uploaded;
    }

    private static string RememberedName(IEnumerable<string> names) =>
        names.FirstOrDefault(n => n.Length > 0) ?? "";

    /// <summary>早前的记录没有大小。按路径补上已知的字节数，已有的不动。</summary>
    public static int ApplyLengths(IEnumerable<RemoteRecord> records, IReadOnlyDictionary<string, long> lengths)
    {
        var filled = 0;
        foreach (var record in records)
        {
            if (record.Size > 0 || record.RemotePath.Length == 0) continue;
            if (!lengths.TryGetValue(record.RemotePath, out var size) || size <= 0) continue;
            record.Size = size;
            filled++;
        }
        return filled;
    }

    /// <summary>地址里的中文是百分号编码，给人看时译回来。删文件仍用原来的地址。</summary>
    private static string DisplayName(string remotePath)
    {
        var name = Path.GetFileName(remotePath.TrimEnd('/', '\\'));
        if (name.Length == 0) name = remotePath;
        try { return Uri.UnescapeDataString(name); }
        catch (UriFormatException) { return name; }
    }
}
