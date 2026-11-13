namespace CentBrowser_PortableAssistant;

/// <summary>慢速备份的上传限速。0 是不限速。底栏和托盘共用同一套档位。</summary>
public static class SpeedLimits
{
    public static readonly (int Kbps, string Label)[] Options =
    [
        (0, "不限速"),
        (512, "限速 512 KB/s"),
        (1024, "限速 1 MB/s"),
        (2048, "限速 2 MB/s"),
        (5120, "限速 5 MB/s"),
        (10240, "限速 10 MB/s"),
        (20480, "限速 20 MB/s"),
    ];

    /// <summary>配置里的值不在档位里时，落到最接近的一档。</summary>
    public static int IndexOf(int kbps)
    {
        var exact = Array.FindIndex(Options, o => o.Kbps == kbps);
        if (exact >= 0) return exact;
        var best = 0;
        for (var i = 1; i < Options.Length; i++)
            if (Math.Abs(Options[i].Kbps - kbps) < Math.Abs(Options[best].Kbps - kbps)) best = i;
        return best;
    }
}
