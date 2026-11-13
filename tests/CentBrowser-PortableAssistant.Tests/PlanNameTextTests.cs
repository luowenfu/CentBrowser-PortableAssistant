using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Tests;

public class PlanNameTextTests
{
    [Fact]
    public void ShortNameStaysWhole() => Assert.Equal("日常备份", PlanNameText.Clip("日常备份"));

    [Fact]
    public void LongNameKeepsEightCharacters() =>
        Assert.Equal("一二三四五六七八…", PlanNameText.Clip("一二三四五六七八九十"));

    [Fact]
    public void SentenceKeepsTheRestAndClipsTheName()
    {
        var text = PlanNameText.ClipWithin(
            "备份完成：「一二三四五六七八九十」4 MB，用时 3 秒",
            ["一二三四五六七八九十", "日常备份"]);
        Assert.Equal("备份完成：「一二三四五六七八…」4 MB，用时 3 秒", text);
    }

    [Fact]
    public void LongerNameIsReplacedBeforeItsPrefix()
    {
        var text = PlanNameText.ClipWithin(
            "正在提取「一二三四五六七八九十」",
            ["一二三四五六七八九", "一二三四五六七八九十"]);
        Assert.Equal("正在提取「一二三四五六七八…」", text);
    }
}

public class RecordPlanNameTests
{
    [Fact]
    public void LivingPlanUsesItsCurrentName() =>
        Assert.Equal("工作", RecordPlanName.Show("工作", "旧名字"));

    [Fact]
    public void DeletedPlanKeepsTheNameFromThatTime() =>
        Assert.Equal("工作（已删除）", RecordPlanName.Show(null, "工作"));

    [Fact]
    public void DeletedPlanWithoutASavedNameSaysSo() =>
        Assert.Equal("已删除的方案", RecordPlanName.Show(null, ""));
}
