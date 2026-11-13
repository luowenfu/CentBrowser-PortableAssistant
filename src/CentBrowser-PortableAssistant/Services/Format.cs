namespace CentBrowser_PortableAssistant.Services;

public static class Format
{
    public static string Size(long bytes) => bytes switch
    {
        >= 1L << 30 => $"{bytes / (double)(1L << 30):0.0} GB",
        >= 1L << 20 => $"{bytes / (double)(1L << 20):0} MB",
        >= 1L << 10 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes} B",
    };

    /// <summary>不到一分钟用秒，不到一小时用分，再长用小时。零头是 0 的单位不写。</summary>
    public static string Duration(TimeSpan t)
    {
        if (t < TimeSpan.Zero) t = TimeSpan.Zero;
        if (t.TotalHours >= 1)
        {
            var hours = (int)t.TotalHours;
            return t.Minutes > 0 ? $"{hours} 小时 {t.Minutes} 分" : $"{hours} 小时";
        }
        if (t.TotalMinutes >= 1)
        {
            var minutes = (int)t.TotalMinutes;
            return t.Seconds > 0 ? $"{minutes} 分 {t.Seconds} 秒" : $"{minutes} 分";
        }
        return $"{Math.Max(1, (int)t.TotalSeconds)} 秒";
    }

    public static string When(DateTime? local, bool seconds = false)
    {
        if (local is not { } t) return "还没备份过";
        var today = DateTime.Today;
        var clock = seconds ? "HH:mm:ss" : "HH:mm";
        if (t.Date == today) return $"今天 {t.ToString(clock)}";
        if (t.Date == today.AddDays(-1)) return $"昨天 {t.ToString(clock)}";
        return t.ToString(seconds ? "yyyy-MM-dd HH:mm:ss" : "yyyy-MM-dd HH:mm");
    }
}
