using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class FlightProgressTests
{
    [Fact]
    public void StagesMeetAtTheSeams()
    {
        Assert.Equal(0, FlightProgress.Stage(BackupStage.Collecting, -1));
        Assert.Equal(0.20, FlightProgress.Stage(BackupStage.Collecting, 100), 5);
        Assert.Equal(0.20, FlightProgress.Stage(BackupStage.Packing, 0), 5);
        Assert.Equal(0.70, FlightProgress.Stage(BackupStage.Packing, 100), 5);
        Assert.Equal(0.70, FlightProgress.Upload(0, 10), 5);
        Assert.Equal(0.85, FlightProgress.Upload(5, 10), 5);
        Assert.Equal(1, FlightProgress.Upload(10, 10), 5);
    }

    [Fact]
    public void NoseEntersAtTheStartAndThePackageLeavesAtTheEnd()
    {
        Assert.Equal(-40, FlightProgress.Left(0, 200, 40));
        Assert.Equal(200, FlightProgress.Left(1, 200, 40));
    }

    [Fact]
    public void FliesOnePlanForwardAndIgnoresAnother()
    {
        var meter = new FlightMeter();
        meter.Begin();
        meter.Stage("甲", BackupStage.Collecting, 100);
        meter.Stage("甲", BackupStage.Packing, 100);
        Assert.Equal(0.70, meter.Position, 5);

        var job = new UploadJob
        {
            PlanName = "甲",
            BatchId = "b1",
            Size = 100,
            TargetIds = ["t1", "t2"],
            PendingTargetIds = ["t1", "t2"],
        };
        meter.Arm([job]);
        meter.Advance([job], new UploadProgress(job, "甲地", 50, 100));
        Assert.Equal(0.775, meter.Position, 5);

        meter.Advance([job], new UploadProgress(job, "甲地", 0, 100));
        Assert.Equal(0.775, meter.Position, 5);

        meter.Stage("乙", BackupStage.Collecting, 50);
        Assert.Equal(0.775, meter.Position, 5);

        meter.Begin();
        Assert.Equal(0, meter.Flight);
        meter.End();
        Assert.Null(meter.Flight);
    }
}
