using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class BackupRecordsTests
{
    private static RemoteRecord R(string batch, int hour, string file, long size, string target = "t1", string plan = "p1") => new()
    {
        PlanId = plan,
        TargetId = target,
        BatchId = batch,
        RemotePath = file,
        Size = size,
        UploadedUtc = new DateTime(2026, 11, 12, hour, 0, 0, DateTimeKind.Utc),
    };

    [Fact]
    public void OneBackupIsOneRowAndEachFileListsItsDestinations()
    {
        var records = new[]
        {
            R("b", 1, @"D:\备份\a.7z", 100, "t1"),
            R("b", 2, "dir/a.zip", 50, "t1"),
            R("b", 3, @"D:\备份\a.7z", 100, "t2"),
        };

        var row = Assert.Single(BackupRecords.Delivered(records));

        Assert.Equal("p1", row.PlanId);
        Assert.Equal(["a.7z", "a.zip"], row.FileNames);
        Assert.Equal(["t1", "t2"], row.Files.First(f => f.Name == "a.7z").TargetIds);
        Assert.Equal(["t1"], row.Files.First(f => f.Name == "a.zip").TargetIds);
        Assert.Equal(150, row.Size);
        Assert.Equal(new long?[] { 100, 50 }, row.Files.Select(f => f.Size).ToArray());
        Assert.Equal(new DateTime(2026, 11, 12, 3, 0, 0, DateTimeKind.Utc), row.UploadedUtc);
    }

    [Fact]
    public void RecordTimeIsWhenTheBackupEnteredTheQueue()
    {
        var slow = R("b", 3, "a.7z", 10, "webdav");
        var local = R("b", 1, "a.7z", 10, "disk");
        var queued = slow.UploadedUtc.AddMinutes(-27);
        slow.QueuedUtc = queued;
        local.QueuedUtc = queued;

        var row = Assert.Single(BackupRecords.Delivered([slow, local]));

        Assert.Equal(queued, row.UploadedUtc);
    }

    [Fact]
    public void EncodedWebDavNameShowsAsChinese()
    {
        var path = "https://nas/backup/CentBrowser_%E9%BB%98%E8%AE%A4%E6%96%B9%E6%A1%88_Data.exe";
        var row = Assert.Single(BackupRecords.Delivered([R("b", 1, path, 10)]));
        Assert.Equal("CentBrowser_默认方案_Data.exe", row.FileNames[0]);
    }

    [Fact]
    public void ArrivedDestinationShowsWhileAnotherIsStillRetrying()
    {
        var records = new[]
        {
            R("old", 1, "old.7z", 10),
            R("now", 5, "now.7z", 20),
            R("wait", 6, "wait.7z", 30, "t1"),
        };

        var rows = BackupRecords.Delivered(records);

        Assert.Equal(["wait.7z", "now.7z", "old.7z"], rows.Select(r => r.FileNames[0]));
        Assert.Equal(["t1"], rows[0].Files[0].TargetIds);
        Assert.Equal("wait", rows[0].BatchId);
        Assert.Null(rows[0].FromAuto);
    }

    [Fact]
    public void KeepsOnlyTheNewestHundredBackups()
    {
        var records = Enumerable.Range(0, 101)
            .Select(i => R("b" + i, 1, "a.7z", 10))
            .ToList();
        for (var i = 0; i < records.Count; i++)
            records[i].UploadedUtc = new DateTime(2026, 1, 1).AddHours(i);

        Assert.Equal(1, BackupRecords.Trim(records));
        Assert.Equal(100, BackupRecords.Delivered(records).Count);
        Assert.DoesNotContain(records, r => r.BatchId == "b0");
    }

    [Fact]
    public void DeliveryKeepsTheDestinationNameFromThatTime()
    {
        var record = R("b", 1, "a.7z", 10);
        record.TargetName = "群晖";

        var file = Assert.Single(Assert.Single(BackupRecords.Delivered([record])).Files);

        Assert.Equal(["群晖"], file.TargetNames);
    }

    [Fact]
    public void DeliveryKeepsPaceAndElapsed()
    {
        var record = R("b", 2, "a.7z", 10);
        record.Slow = false;
        record.StartedUtc = record.UploadedUtc.AddMinutes(-1).AddSeconds(-12);
        record.TargetName = "群晖";

        var file = Assert.Single(Assert.Single(BackupRecords.Delivered([record])).Files);

        Assert.Equal([false], file.Slow);
        Assert.Equal([TimeSpan.FromSeconds(72)], file.Elapsed);
    }

    [Fact]
    public void ElapsedIsThisDestinationOnly()
    {
        var record = R("b", 2, "a.7z", 10);
        record.StartedUtc = record.UploadedUtc.AddMinutes(-3);
        record.UploadStartedUtc = record.UploadedUtc.AddSeconds(-8);

        var file = Assert.Single(Assert.Single(BackupRecords.Delivered([record])).Files);

        Assert.Equal([TimeSpan.FromSeconds(8)], file.Elapsed);
    }

    [Fact]
    public void OldDeliveryHasNoPace()
    {
        var file = Assert.Single(Assert.Single(BackupRecords.Delivered([R("b", 1, "a.7z", 10)])).Files);

        Assert.Equal([null], file.Slow);
        Assert.Equal([null], file.Elapsed);
    }

    [Fact]
    public void AutoBackupStaysOnTheDelivery()
    {
        var record = R("b", 1, "a.7z", 10);
        record.FromAuto = true;

        Assert.True(Assert.Single(BackupRecords.Delivered([record])).FromAuto);
    }

    [Fact]
    public void MissingSizeCanBeFilledWithoutTouchingKnownOnes()
    {
        var records = new List<RemoteRecord> { R("b", 1, @"D:\a.7z", 0), R("b", 1, @"D:\b.7z", 20) };

        var filled = BackupRecords.ApplyLengths(records, new Dictionary<string, long> { [@"D:\a.7z"] = 40 });

        Assert.Equal(1, filled);
        Assert.Equal(40, records[0].Size);
        Assert.Equal(20, records[1].Size);
        Assert.Equal(60, Assert.Single(BackupRecords.Delivered(records)).Size);
    }

    [Fact]
    public void MissingSizeLeavesTheRowWithoutASize()
    {
        var records = new[] { R("b", 1, "a.7z", 100), R("b", 1, "a.zip", 0) };

        Assert.Null(Assert.Single(BackupRecords.Delivered(records)).Size);
    }

    [Fact]
    public void DeliveryKeepsThePlanNameFromThatTime()
    {
        var record = R("b", 1, "a.7z", 10);
        record.PlanName = "工作";

        Assert.Equal("工作", Assert.Single(BackupRecords.Delivered([record])).PlanName);
    }

    [Fact]
    public void OldDeliveryHasNoPlanName()
    {
        Assert.Equal("", Assert.Single(BackupRecords.Delivered([R("b", 1, "a.7z", 10)])).PlanName);
    }
}
