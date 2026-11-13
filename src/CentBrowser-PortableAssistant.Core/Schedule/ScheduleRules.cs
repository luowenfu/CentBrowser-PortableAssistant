using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Core.Schedule;

public enum PlanDecision
{
    /// <summary>不用管，例如今天已经备份过。</summary>
    None,
    /// <summary>该备份了，但数据和上次一样，跳过并提示。</summary>
    Unchanged,
    /// <summary>该备份了。</summary>
    Backup,
}

/// <summary>关闭浏览器后某个方案要不要备份。纯逻辑，方便测试。</summary>
public static class ScheduleRules
{
    public static bool IsDue(DateTime? lastSuccess, DateTime now, AutoFrequency frequency)
    {
        if (lastSuccess is not { } last) return true;
        return frequency switch
        {
            AutoFrequency.Daily => last.Date < now.Date,
            AutoFrequency.Weekly => WeekStart(last) < WeekStart(now),
            AutoFrequency.Monthly => last.Year != now.Year || last.Month != now.Month,
            _ => false,
        };
    }

    /// <summary>按频率到期才备份。数据没变化就跳过。</summary>
    public static PlanDecision Decide(AutoFrequency frequency, DateTime now, PlanState state, bool changed)
    {
        if (!IsDue(state.LastSuccessLocal, now, frequency)) return PlanDecision.None;
        return changed ? PlanDecision.Backup : PlanDecision.Unchanged;
    }

    /// <summary>"数据未变化，已跳过"每个方案每天只提示一次。</summary>
    public static bool ShouldNotifyUnchanged(PlanState state, DateTime now) =>
        state.LastUnchangedNotice != DateOnly.FromDateTime(now);

    /// <summary>周一到周日算一周。周日和次日周一不是同一周。</summary>
    public static DateTime WeekStart(DateTime time)
    {
        var date = time.Date;
        var sinceMonday = ((int)date.DayOfWeek + 6) % 7;
        return date.AddDays(-sinceMonday);
    }
}
