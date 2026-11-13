using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

/// <summary>按 history.json 的记录删掉更早的备份，只删本程序传上去的文件。</summary>
public static class RetentionCleaner
{
    /// <summary>返回需要删除的记录，同一批次的文件算一份备份。keep 为 0 表示全部保留。</summary>
    public static IReadOnlyList<RemoteRecord> Expired(IEnumerable<RemoteRecord> records, string planId, string targetId, int keep)
    {
        if (keep <= 0) return [];
        return records
            .Where(r => r.PlanId == planId && r.TargetId == targetId && !r.FileRemoved)
            .GroupBy(r => r.BatchId)
            .OrderByDescending(g => g.Max(BackupRecords.ListedUtc))
            .Skip(keep)
            .SelectMany(g => g)
            .ToList();
    }

    public static async Task CleanAsync(IUploader uploader, HistoryStore history, string planId, string targetId, int keep, CancellationToken ct)
    {
        var expired = history.Read(h => Expired(h.Records, planId, targetId, keep));
        foreach (var record in expired)
        {
            // 同名被后一次盖住时，旧记录和新记录指向同一个文件，不能把还在保留里的那份删掉
            var stillKept = history.Read(h => h.Records.Any(r =>
                !r.FileRemoved &&
                r.TargetId == record.TargetId &&
                r.BatchId != record.BatchId &&
                string.Equals(r.RemotePath, record.RemotePath, StringComparison.OrdinalIgnoreCase) &&
                !expired.Any(e => e.BatchId == r.BatchId && e.TargetId == r.TargetId && e.RemotePath == r.RemotePath)));
            if (!stillKept) await uploader.DeleteAsync(record.RemotePath, ct);
            history.Update(h =>
            {
                foreach (var item in h.Records)
                {
                    if (item.TargetId == record.TargetId && item.BatchId == record.BatchId && item.RemotePath == record.RemotePath)
                        item.FileRemoved = true;
                }
            });
        }
    }
}
