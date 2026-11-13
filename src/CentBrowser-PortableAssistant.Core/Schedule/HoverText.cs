using CentBrowser_PortableAssistant.Core.Settings;
using CentBrowser_PortableAssistant.Core.Upload;

namespace CentBrowser_PortableAssistant.Core.Schedule;

public enum HoverMark
{
    /// <summary>还在上传。</summary>
    Uploading,
    /// <summary>数据有变化，但这一周期还没备份成功。</summary>
    Changed,
    /// <summary>勾选的用户数据没变化，所以没备。</summary>
    Unchanged,
    /// <summary>这一周期已经备份成功。</summary>
    Done,
}

/// <param name="When">已备份时的时间，例如「今天 13:18」。其他状态不用。</param>
public readonly record struct HoverPlan(string Name, HoverMark Mark, string? When = null);

public enum ManualResult
{
    Delivered,
    Incomplete,
    Missed,
}

/// <summary>最近一次已经结束的手动备份。还在队列里的不算。</summary>
public readonly record struct ManualBackup(string PlanId, string PlanName, DateTime UploadedUtc, ManualResult Result);

/// <summary>托盘悬停里每个方案这一周期的实际情况。纯逻辑，方便测试。</summary>
public static class HoverText
{
    public const int Limit = 127;

    /// <summary>
    /// 还在上传优先。然后看这一周期有没有成功备份。没有的话，没变化和有变化各写各的。
    /// </summary>
    public static HoverMark Mark(DateTime? uploaded, DateTime now, AutoFrequency frequency, bool pending, bool unchanged)
    {
        if (pending) return HoverMark.Uploading;
        if (!ScheduleRules.IsDue(uploaded, now, frequency)) return HoverMark.Done;
        return unchanged ? HoverMark.Unchanged : HoverMark.Changed;
    }

    public static string Period(AutoFrequency frequency) => frequency switch
    {
        AutoFrequency.Weekly => "本周",
        AutoFrequency.Monthly => "本月",
        _ => "今天",
    };

    public const string NoManual = "还没有手动备份";

    /// <summary>方案名超过这个字数就省略，免得托盘提示被拉得很长。</summary>
    public const int PlanNameLimit = 8;

    /// <summary>自动备份关着时的那一行。方案名用现在的，删了就用当时记下的。</summary>
    public static string ManualLine(string when, string plan, ManualResult result)
    {
        var word = result switch
        {
            ManualResult.Missed => "没送达",
            ManualResult.Incomplete => "没送完",
            _ => "已送达",
        };
        if (plan.Length > PlanNameLimit) plan = plan[..PlanNameLimit] + "…";
        return $"上次手动：{when} · {plan} · {word}";
    }

    public static ManualResult Judge(bool arrived, bool missing) =>
        arrived ? missing ? ManualResult.Incomplete : ManualResult.Delivered : ManualResult.Missed;

    /// <summary>分不出手动还是自动的旧记录不算。正在上传的那次留给进度那一行。</summary>
    public static ManualBackup? LatestManual(BackupHistory history, IReadOnlySet<string> pending)
    {
        var abandoned = history.Abandoned.Select(a => a.BatchId).ToHashSet();
        var latest = BackupRecords.Delivered(history.Records, history.Abandoned)
            .Where(b => b.FromAuto == false && !pending.Contains(b.BatchId))
            .MaxBy(b => b.UploadedUtc);
        if (latest is null) return null;
        var arrived = latest.TargetIds.Any(id => id.Length > 0) || latest.Files.Any(f => f.TargetIds.Count > 0);
        var missing = abandoned.Contains(latest.BatchId) || latest.Files.Any(f => f.TargetIds.Count == 0);
        return new ManualBackup(latest.PlanId, latest.PlanName, latest.UploadedUtc, Judge(arrived, missing));
    }

    /// <param name="anyJoined">有没有方案加入自动备份。正在提取或打包的方案不出现在 <paramref name="visible"/> 里，但不能因此说成没人加入。</param>
    /// <param name="failures">红点对应的失败。放在周期前面，字数不够时先丢掉周期。</param>
    /// <param name="lastManual">自动备份关着时，最近一次手动备份。空就不要这行。</param>
    /// <param name="startsIn">离自动备份开始还要多久。没在等就不写倒计时。</param>
    public static string Compose(string title, string? progress, bool autoOn, AutoFrequency frequency, bool anyJoined, IReadOnlyList<HoverPlan> visible, IReadOnlyList<string>? failures = null, string? lastManual = null, TimeSpan? startsIn = null)
    {
        var lines = new List<string> { title };
        if (!string.IsNullOrEmpty(progress)) lines.Add(progress);
        if (failures is { Count: > 0 })
        {
            lines.Add(failures[0]);
            if (failures.Count > 1) lines.Add($"另有 {failures.Count - 1} 处失败");
        }
        if (!autoOn && !string.IsNullOrEmpty(lastManual)) lines.Add(lastManual);
        if (!autoOn) lines.Add("自动备份未开启");
        else if (!anyJoined) lines.Add("没有方案加入自动备份");

        var text = Fit(lines);
        if (!autoOn || !anyJoined) return text;

        foreach (var plan in visible.OrderBy(p => p.Mark).ThenBy(p => p.Name, StringComparer.Ordinal))
        {
            var suffix = Suffix(plan.Mark, frequency, startsIn, plan.When);
            var line = plan.Name + suffix;
            if (text.Length + 1 + line.Length <= Limit)
            {
                text += "\n" + line;
                continue;
            }
            var room = Limit - text.Length - 1;
            var nameRoom = room - suffix.Length;
            if (nameRoom < 2) break;
            var name = plan.Name.Length > nameRoom ? plan.Name[..(nameRoom - 1)] + "…" : plan.Name;
            text += "\n" + name + suffix;
            break;
        }
        return text.Length <= Limit ? text : text[..Limit];
    }

    /// <summary>倒计时。不到一分钟用秒，整分用分钟，有零头写成几分几秒。再长用小时，至少 1 秒。</summary>
    public static string Countdown(TimeSpan left)
    {
        var total = (int)Math.Ceiling(left.TotalSeconds);
        if (total < 1) total = 1;
        var hours = total / 3600;
        var minutes = total % 3600 / 60;
        var seconds = total % 60;
        if (hours > 0) return minutes > 0 ? $"{hours}小时{minutes}分" : $"{hours}小时";
        if (minutes > 0) return seconds > 0 ? $"{minutes}分{seconds}秒" : $"{minutes}分钟";
        return $"{seconds}秒";
    }

    private static string Suffix(HoverMark mark, AutoFrequency frequency, TimeSpan? startsIn, string? when) => mark switch
    {
        HoverMark.Uploading => " · 正在上传",
        HoverMark.Unchanged => $" · {Period(frequency)}没变化",
        HoverMark.Changed => startsIn is { } left ? $" · 待备份 • {Countdown(left)}后开始备份" : " · 待备份",
        _ => string.IsNullOrEmpty(when) ? $" · {Period(frequency)}已备份" : $" · {when} 已备份",
    };

    /// <summary>一行一行往上加。某一行放不下就截断，后面的不要了。</summary>
    private static string Fit(List<string> lines)
    {
        var text = "";
        foreach (var line in lines)
        {
            var next = text.Length == 0 ? line : text + "\n" + line;
            if (next.Length <= Limit)
            {
                text = next;
                continue;
            }
            var room = Limit - (text.Length == 0 ? 0 : text.Length + 1);
            if (room >= 2) text = text.Length == 0 ? line[..(room - 1)] + "…" : text + "\n" + line[..(room - 1)] + "…";
            break;
        }
        return text;
    }
}
