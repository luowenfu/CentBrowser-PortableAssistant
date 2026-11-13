using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Tests;

public class NameTemplateTests
{
    private static readonly NameValues Values = new("CentBrowser", "工作 用", "Work", new DateTime(2026, 11, 10, 22, 5, 9), true, "5.3.1184.31", "PC01");

    [Fact]
    public void DefaultTemplateRendersReadableName()
    {
        var name = NameTemplate.Render(NameTemplateDefaults.Template, Values, asciiOnly: false);
        Assert.Equal("CentBrowser_工作_用_20261110-220509_Full", name);
    }

    [Fact]
    public void AsciiOnlyUsesSlugAndStripsChinese()
    {
        var name = NameTemplate.Render("{前缀}-{方案}-{日期}-{电脑名}", Values, asciiOnly: true);
        Assert.Equal("CentBrowser-Work-20261110-PC01", name);
        Assert.Equal("abc", NameTemplate.Sanitize("备份abc", asciiOnly: true));
    }

    [Theory]
    [InlineData("{方案}_{类型}")]
    [InlineData("{方案}_{日期}")]
    [InlineData("")]
    [InlineData("{日期}_{未知}")]
    public void InvalidTemplatesAreRejected(string template) => Assert.NotNull(NameTemplate.Validate(template));

    [Fact]
    public void TemplateWithTimeIsValid() => Assert.Null(NameTemplate.Validate("{方案}_{时间}"));

    [Fact]
    public void LongRenderedNameIsRejectedWithoutCutting()
    {
        var plan = new string('名', 100);
        var name = NameTemplate.Render("{方案}_{时间}", Values with { Plan = plan }, asciiOnly: false);
        Assert.StartsWith(plan, name);
        Assert.Equal($"拼出来的文件名最长 {NameTemplate.MaxLength} 个字符", NameTemplate.LengthError(name));
        Assert.Null(NameTemplate.LengthError(name[..NameTemplate.MaxLength]));
    }

    [Fact]
    public void LongTemplateIsAllowedWhenRenderedNameFits()
    {
        var template = "{时间}" + new string('.', 100);
        Assert.Null(NameTemplate.Validate(template));
        var name = NameTemplate.Render(template, Values, asciiOnly: false);
        Assert.Equal("220509", name);
        Assert.Null(NameTemplate.LengthError(name));
    }
}
