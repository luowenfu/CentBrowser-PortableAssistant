using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class RetentionCleanerTests
{
    private static RemoteRecord R(string batch, int day, string file, string target = "t1") => new()
    {
        PlanId = "p1",
        TargetId = target,
        BatchId = batch,
        RemotePath = file,
        UploadedUtc = new DateTime(2026, 11, day),
    };

    [Fact]
    public void KeepsNewestBatchesAndCountsFilesOfOneBackupTogether()
    {
        var records = new[]
        {
            R("a", 1, "a.exe"), R("a", 1, "a.7z"),
            R("b", 2, "b.exe"),
            R("c", 3, "c.exe"), R("c", 3, "c.7z"),
            R("old", 1, "other-target.exe", target: "t2"),
        };

        var expired = RetentionCleaner.Expired(records, "p1", "t1", keep: 2);

        Assert.Equal(["a.exe", "a.7z"], expired.Select(r => r.RemotePath));
    }

    [Fact]
    public void ZeroKeepsEverything() =>
        Assert.Empty(RetentionCleaner.Expired([R("a", 1, "a.exe"), R("b", 2, "b.exe")], "p1", "t1", keep: 0));

    [Fact]
    public void AlreadyRemovedFileIsNotDeletedAgain()
    {
        var gone = R("old", 1, "old.exe");
        gone.FileRemoved = true;

        var expired = RetentionCleaner.Expired([gone, R("now", 2, "now.exe")], "p1", "t1", keep: 1);

        Assert.Empty(expired);
    }
}
