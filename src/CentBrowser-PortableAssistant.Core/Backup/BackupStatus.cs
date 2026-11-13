namespace CentBrowser_PortableAssistant.Core.Backup;

public enum BackupStage
{
    Collecting,
    Packing,
    Queued,
}

public sealed record BackupStatus(string PlanName, BackupStage Stage, int Percent);

public sealed record BackupResult(string PlanName, IReadOnlyList<string> Files, long TotalSize, TimeSpan Elapsed);
