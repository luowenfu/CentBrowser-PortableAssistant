using CentBrowser_PortableAssistant.Core.Update;

namespace CentBrowser_PortableAssistant.Tests;

public class ReleaseMarkdownTests
{
    [Fact]
    public void HeadingsAndListsMatchReleaseNotes()
    {
        var blocks = ReleaseMarkdown.Parse("""
            ### 新增

            - 有新版本时可以查看说明
            - 可建多个方案

            ### 修复

            1. 窗口加大
            """);

        Assert.Equal(4, blocks.Count);
        var added = Assert.IsType<MdHeading>(blocks[0]);
        Assert.Equal(3, added.Level);
        Assert.Equal("新增", added.Runs[0].Text);
        var features = Assert.IsType<MdList>(blocks[1]);
        Assert.False(features.Ordered);
        Assert.Equal(2, features.Items.Count);
        var fixedTitle = Assert.IsType<MdHeading>(blocks[2]);
        Assert.Equal("修复", fixedTitle.Runs[0].Text);
        var fixes = Assert.IsType<MdList>(blocks[3]);
        Assert.True(fixes.Ordered);
        Assert.Equal("窗口加大", fixes.Items[0][0].Text);
    }

    [Fact]
    public void InlinesKeepBoldCodeAndLinks()
    {
        var runs = ReleaseMarkdown.ParseInlines("见 **说明** 和 `版本`，打开 [项目](https://github.com/luowenfu/CentBrowser-PortableAssistant)");
        Assert.Equal(6, runs.Count);
        Assert.Equal("见 ", runs[0].Text);
        Assert.True(runs[1].Bold);
        Assert.Equal("说明", runs[1].Text);
        Assert.True(runs[3].Code);
        Assert.Equal("版本", runs[3].Text);
        Assert.Equal("项目", runs[5].Text);
        Assert.Equal("https://github.com/luowenfu/CentBrowser-PortableAssistant", runs[5].Link);
    }

    [Fact]
    public void BlankNotesProduceNothing() =>
        Assert.Empty(ReleaseMarkdown.Parse("  \n"));
}
