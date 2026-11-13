using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>按格式估算打包后的大小。压缩比优先用这个方案上次实际打包的结果。</summary>
public static class PackEstimate
{
    public const string SevenZipKey = "7z";
    public const string ZipKey = "zip";

    /// <summary>没备份过时的估计：浏览器程序加用户数据，7z 约压到四分之一，zip 大三成左右。</summary>
    private static readonly Dictionary<string, double> Defaults = new() { [SevenZipKey] = 0.25, [ZipKey] = 0.33 };

    /// <summary>自解压 exe = 7z.sfx 模块 + .7z 数据。</summary>
    private const long SfxOverhead = 212 * 1024;

    public static long For(ArchiveFormats format, long rawSize, IReadOnlyDictionary<string, double> ratios)
    {
        double Ratio(string key) => ratios.TryGetValue(key, out var r) && r > 0 ? r : Defaults[key];
        return format switch
        {
            ArchiveFormats.Sfx => (long)(rawSize * Ratio(SevenZipKey)) + SfxOverhead,
            ArchiveFormats.SevenZip => (long)(rawSize * Ratio(SevenZipKey)),
            ArchiveFormats.Zip => (long)(rawSize * Ratio(ZipKey)),
            _ => 0,
        };
    }

    /// <summary>从打出来的文件反推压缩比，写进记录供下次估算。</summary>
    public static void Record(IDictionary<string, double> ratios, IEnumerable<string> files, long rawSize)
    {
        if (rawSize <= 0) return;
        var list = files.ToList();
        var has7z = list.Any(f => f.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));
        foreach (var f in list)
        {
            var size = new FileInfo(f).Length;
            switch (Path.GetExtension(f).ToLowerInvariant())
            {
                case ".7z": ratios[SevenZipKey] = size / (double)rawSize; break;
                case ".exe" when !has7z: ratios[SevenZipKey] = Math.Max(0, size - SfxOverhead) / (double)rawSize; break;
                case ".zip": ratios[ZipKey] = size / (double)rawSize; break;
            }
        }
    }
}
