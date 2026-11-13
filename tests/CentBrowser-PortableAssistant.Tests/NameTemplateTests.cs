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
}
