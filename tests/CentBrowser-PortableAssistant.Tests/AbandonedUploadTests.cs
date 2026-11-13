using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class AbandonedUploadTests
{
    [Fact]
    public void SupersededDestinationIsRememberedByItsLiveName()
    {
        var job = new UploadJob
        {
            BatchId = "b1",
            FileName = "a.exe",
            PlanName = "工作",
            PendingTargetIds = ["nas"],
            LastError = "旧名字：上一级目录不存在",
        };
        var list = new List<AbandonedTarget>();

        AbandonedUploads.Add(list, [job], id => id == "nas" ? "群晖webdav" : null);

        var left = Assert.Single(list);
        Assert.Equal("群晖webdav", left.TargetName);
        Assert.Equal("工作", left.PlanName);
        Assert.Equal("a.exe", left.FileName);
    }

    [Fact]
    public void FailedDestinationsStayARecordWithoutAnySuccess()
    {
        var when = new DateTime(2026, 11, 12, 10, 58, 0, DateTimeKind.Utc);
        var abandoned = new[]
        {
            new AbandonedTarget { BatchId = "b", PlanId = "p", PlanName = "工作", FileName = "a.exe", TargetId = "t1", TargetName = "甲", CreatedUtc = when, Size = 4, FromAuto = false },
            new AbandonedTarget { BatchId = "b", PlanId = "p", FileName = "a.exe", TargetId = "t2", TargetName = "乙", CreatedUtc = when, Size = 4, FromAuto = false },
            new AbandonedTarget { BatchId = "b", PlanId = "p", FileName = "a.exe", TargetId = "t3", TargetName = "丙", CreatedUtc = when, Size = 4, FromAuto = false },
        };

        var row = Assert.Single(BackupRecords.Delivered([], abandoned));

        Assert.Equal("p", row.PlanId);
        Assert.Equal("工作", row.PlanName);
        Assert.Equal("a.exe", Assert.Single(row.Files).Name);
        Assert.Empty(row.Files[0].TargetIds);
        Assert.False(row.FromAuto);
        Assert.Equal(when, row.UploadedUtc);
    }

    [Fact]
    public void FailureOnlyBatchCountsTowardTheNewestHundred()
    {
        var history = new BackupHistory();
        for (var i = 0; i < 100; i++)
            history.Records.Add(new RemoteRecord { BatchId = "b" + i, PlanId = "p", RemotePath = "a.7z", Size = 1, UploadedUtc = new DateTime(2026, 1, 1).AddHours(i) });
        history.Abandoned.Add(new AbandonedTarget { BatchId = "new", PlanId = "p", FileName = "a.7z", TargetName = "群晖", CreatedUtc = new DateTime(2026, 6, 1) });

        BackupRecords.TrimHistory(history);

        Assert.Contains(history.Abandoned, a => a.BatchId == "new");
        Assert.DoesNotContain(history.Records, r => r.BatchId == "b0");
        Assert.Equal(100, BackupRecords.Delivered(history.Records, history.Abandoned).Count);
    }
}
