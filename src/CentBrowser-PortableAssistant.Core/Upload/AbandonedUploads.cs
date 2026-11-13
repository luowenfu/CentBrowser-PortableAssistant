using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Upload;

public static class AbandonedUploads
{
    public static void Add(ICollection<AbandonedTarget> list, IEnumerable<UploadJob> jobs, Func<string, string?> liveName)
    {
        foreach (var job in jobs)
        {
            foreach (var id in job.PendingTargetIds.Distinct())
            {
                if (list.Any(a => a.BatchId == job.BatchId && a.TargetId == id &&
                                  string.Equals(a.FileName, job.FileName, StringComparison.OrdinalIgnoreCase)))
                    continue;
                var name = liveName(id);
                if (string.IsNullOrEmpty(name))
                {
                    var colon = job.LastError.IndexOf('：');
                    name = colon > 0 ? job.LastError[..colon] : "";
                }
                if (string.IsNullOrEmpty(name)) continue;
                list.Add(new AbandonedTarget
                {
                    BatchId = job.BatchId,
                    FileName = job.FileName,
                    TargetId = id,
                    TargetName = name,
                    PlanId = job.PlanId,
                    PlanName = job.PlanName,
                    CreatedUtc = job.CreatedUtc,
                    FromAuto = job.FromAuto,
                    Size = job.Size,
                });
            }
        }
    }
}
