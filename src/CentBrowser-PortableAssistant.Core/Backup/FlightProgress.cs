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

    /// <summary>
    /// 机头位置。0–0.20 在提取两条线之间，0.20–0.70 在打包两条线之间，0.70–1 在目的地左线和终点之间。
    /// 这些数只标出哪一段，线段本身的像素长短另算。
    /// </summary>
    public static double Nose(double progress, double extractStart, double extractEnd, double packEnd, double uploadEnd)
    {
        var unit = double.IsFinite(progress) ? Math.Clamp(progress, 0d, 1d) : 0d;
        if (unit <= 0.20) return Between(extractStart, extractEnd, unit / 0.20);
        if (unit <= 0.70) return Between(extractEnd, packEnd, (unit - 0.20) / 0.50);
        return Between(packEnd, uploadEnd, (unit - 0.70) / 0.30);
    }

    private static double Between(double start, double end, double unit) => start + (end - start) * unit;
}
