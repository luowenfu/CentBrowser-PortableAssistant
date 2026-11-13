namespace CentBrowser_PortableAssistant.Core.Backup;

/// <summary>一次备份的飞行位置。提取占 20%，打包占 50%，上传占 30%。</summary>
public static class FlightProgress
{
    public static double Stage(BackupStage stage, int percent)
    {
        var unit = percent < 0 ? 0d : Math.Clamp(percent, 0, 100) / 100d;
        return stage switch
        {
            BackupStage.Collecting => 0.20 * unit,
            BackupStage.Packing => 0.20 + 0.50 * unit,
            _ => 0.70,
        };
    }

    public static double Upload(long sent, long total)
    {
        var unit = total <= 0 ? 0d : Math.Clamp(sent / (double)total, 0d, 1d);
        return 0.70 + 0.30 * unit;
    }

    /// <summary>图片左缘。0 时机头刚到起点，1 时左缘刚离开终点。</summary>
    public static double Left(double progress, double track, double image)
    {
        var unit = double.IsFinite(progress) ? Math.Clamp(progress, 0d, 1d) : 0d;
        return -image + unit * (track + image);
    }
}
