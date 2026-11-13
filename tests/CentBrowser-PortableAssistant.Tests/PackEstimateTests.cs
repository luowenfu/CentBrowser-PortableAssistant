using CentBrowser_PortableAssistant.Core.Backup;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Tests;

public class PackEstimateTests
{
    [Fact]
    public void ZipIsLargerThan7zByDefault()
    {
        var none = new Dictionary<string, double>();
        Assert.True(PackEstimate.For(ArchiveFormats.Zip, 1000_000, none) > PackEstimate.For(ArchiveFormats.SevenZip, 1000_000, none));
        Assert.True(PackEstimate.For(ArchiveFormats.Sfx, 1000_000, none) > PackEstimate.For(ArchiveFormats.SevenZip, 1000_000, none));
    }

    [Fact]
    public void RecordsRatioPerFormat()
    {
        var dir = Directory.CreateTempSubdirectory().FullName;
        try
        {
            var sevenZip = Path.Combine(dir, "a.7z");
            var zip = Path.Combine(dir, "a.zip");
            File.WriteAllBytes(sevenZip, new byte[200]);
            File.WriteAllBytes(zip, new byte[300]);
            var ratios = new Dictionary<string, double>();

            PackEstimate.Record(ratios, [sevenZip, zip], 1000);

            Assert.Equal(0.2, ratios[PackEstimate.SevenZipKey], 3);
            Assert.Equal(0.3, ratios[PackEstimate.ZipKey], 3);
            Assert.Equal(300, PackEstimate.For(ArchiveFormats.Zip, 1000, ratios));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }
}
