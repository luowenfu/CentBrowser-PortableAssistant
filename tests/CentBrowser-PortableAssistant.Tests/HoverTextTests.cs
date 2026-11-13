using CentBrowser_PortableAssistant.Core.Schedule;
using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class HoverTextTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 21, 0, 0);

    [Fact]
    public void PendingUploadSaysUploading()
    {
        Assert.Equal(HoverMark.Uploading, HoverText.Mark(Now, Now, AutoFrequency.Daily, pending: true, unchanged: false));
        var text = HoverText.Compose("助手", null, true, AutoFrequency.Daily, true, [new HoverPlan("工作", HoverMark.Uploading)]);
        Assert.Contains("工作 · 正在上传", text);
    }

    [Fact]
    public void UnchangedSkipSaysNoChange()
    {
        Assert.Equal(HoverMark.Unchanged, HoverText.Mark(Now.AddDays(-3), Now, AutoFrequency.Daily, pending: false, unchanged: true));
        var text = HoverText.Compose("助手", null, true, AutoFrequency.Daily, true, [new HoverPlan("工作", HoverMark.Unchanged)]);
        Assert.Contains("工作 · 今天没变化", text);
    }

    [Fact]
    public void SuccessThisPeriodStaysBackedUp()
    {
        Assert.Equal(HoverMark.Done, HoverText.Mark(Now.AddHours(-2), Now, AutoFrequency.Daily, pending: false, unchanged: true));
        var text = HoverText.Compose("助手", null, true, AutoFrequency.Daily, true, [new HoverPlan("工作", HoverMark.Done, "今天 19:00")]);
        Assert.Contains("工作 · 今天 19:00 已备份", text);
    }

    [Fact]
    public void UploadThisWeekCountsAsDone() =>
        Assert.Equal(HoverMark.Done, HoverText.Mark(new DateTime(2026, 11, 9, 8, 0, 0), Now, AutoFrequency.Weekly, pending: false, unchanged: false));

    [Fact]
    public void PreviousWeekWithChangesIsNotBackedUp()
    {
        Assert.Equal(HoverMark.Changed, HoverText.Mark(new DateTime(2026, 11, 8, 23, 0, 0), Now, AutoFrequency.Weekly, pending: false, unchanged: false));
        var text = HoverText.Compose("助手", null, true, AutoFrequency.Weekly, true, [new HoverPlan("工作", HoverMark.Changed)]);
        Assert.Contains("工作 · 待备份", text);
        Assert.DoesNotContain("后开始备份", text);
    }

    [Fact]
    public void DuePlanShowsWhenBackupStarts()
    {
        Assert.Equal("45秒", HoverText.Countdown(TimeSpan.FromSeconds(45)));
        Assert.Equal("1分钟", HoverText.Countdown(TimeSpan.FromSeconds(59.1)));
        Assert.Equal("2分钟", HoverText.Countdown(TimeSpan.FromMinutes(2)));
        Assert.Equal("1分30秒", HoverText.Countdown(TimeSpan.FromSeconds(90)));
        Assert.Equal("1小时", HoverText.Countdown(TimeSpan.FromHours(1)));
        Assert.Equal("1小时5分", HoverText.Countdown(TimeSpan.FromHours(1) + TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(10)));
        var text = HoverText.Compose("助手", null, true, AutoFrequency.Weekly, true,
            [new HoverPlan("工作", HoverMark.Changed)], startsIn: TimeSpan.FromSeconds(45));
        Assert.Contains("工作 · 待备份 • 45秒后开始备份", text);
    }

    [Fact]
    public void AutoOffIsOneLine()
    {
        var text = HoverText.Compose("Cent Browser 便携助手", "正在打包", false, AutoFrequency.Daily, true, [new HoverPlan("工作", HoverMark.Changed)]);
        Assert.Equal("Cent Browser 便携助手\n正在打包\n自动备份未开启", text);
    }

    [Fact]
    public void AutoOffAddsTheLastManualLine()
    {
        var text = HoverText.Compose("Cent Browser 便携助手", null, false, AutoFrequency.Daily, false, [],
            lastManual: HoverText.ManualLine("今天 01:51", "方案1", ManualResult.Delivered));
        Assert.Equal("Cent Browser 便携助手\n上次手动：今天 01:51 · 方案1 · 已送达\n自动备份未开启", text);
        Assert.Equal("上次手动：今天 01:51 · 方案1 · 没送完", HoverText.ManualLine("今天 01:51", "方案1", ManualResult.Incomplete));
        Assert.Equal("上次手动：今天 01:51 · 方案1 · 没送达", HoverText.ManualLine("今天 01:51", "方案1", ManualResult.Missed));
        Assert.Equal("上次手动：今天 02:09 · 啊实打实的阿萨德… · 已送达",
            HoverText.ManualLine("今天 02:09", "啊实打实的阿萨德爱仕达阿萨德", ManualResult.Delivered));
    }

    [Fact]
    public void LatestManualSkipsAutoAndUnfinished()
    {
        var history = new BackupHistory
        {
            Records =
            [
                Record("auto", "自动方案", true, Now.AddHours(1)),
                Record("unknown", "旧的", null, Now.AddHours(2)),
                Record("old", "方案1", false, Now.AddHours(-2)),
                Record("live", "方案2", false, Now),
            ],
        };
        var picked = HoverText.LatestManual(history, new HashSet<string> { "live" });
        Assert.Equal("方案1", picked?.PlanName);
        Assert.Equal(ManualResult.Delivered, picked?.Result);

        history.Records.Clear();
        Assert.Null(HoverText.LatestManual(history, new HashSet<string>()));
    }

    private static RemoteRecord Record(string batch, string plan, bool? fromAuto, DateTime uploaded) => new()
    {
        BatchId = batch,
        PlanId = batch,
        PlanName = plan,
        TargetId = "t",
        TargetName = "群晖",
        UploadedUtc = uploaded,
        FromAuto = fromAuto,
    };

    [Fact]
    public void NoJoinedPlanHasItsOwnLine()
    {
        var text = HoverText.Compose("助手", null, true, AutoFrequency.Daily, false, []);
        Assert.Contains("没有方案加入自动备份", text);
    }

    [Fact]
    public void UndonePlansStayWhenTextIsTight()
    {
        var huge = new string('名', 200);
        var plans = new[] { new HoverPlan(huge, HoverMark.Done), new HoverPlan("工作", HoverMark.Changed) };
        var text = HoverText.Compose("助手", null, true, AutoFrequency.Weekly, true, plans);
        Assert.Contains("工作 · 待备份", text);
        Assert.DoesNotContain(huge, text);
        Assert.True(text.Length <= HoverText.Limit);
    }

    [Fact]
    public void FailureComesBeforeThePlanAndExtraOnesCollapse()
    {
        var text = HoverText.Compose("Cent Browser 便携助手", null, true, AutoFrequency.Weekly, true,
            [new HoverPlan("默认方案", HoverMark.Changed)],
            ["「群晖webdav」上一级目录不存在，19:50 重试", "「另一处」连接失败，19:51 重试"]);

        Assert.Equal("Cent Browser 便携助手\n「群晖webdav」上一级目录不存在，19:50 重试\n另有 1 处失败\n默认方案 · 待备份", text);
    }

    [Fact]
    public void LongFailureKeepsTheReasonAndDropsThePlan()
    {
        var text = HoverText.Compose("Cent Browser 便携助手", null, true, AutoFrequency.Weekly, true,
            [new HoverPlan("默认方案", HoverMark.Changed)], [new string('错', 120)]);

        Assert.StartsWith("Cent Browser 便携助手\n错", text);
        Assert.DoesNotContain("默认方案", text);
        Assert.True(text.Length <= HoverText.Limit);
    }
}
