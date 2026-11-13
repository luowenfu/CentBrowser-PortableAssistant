using CentBrowser_PortableAssistant.Core.Schedule;
using CentBrowser_PortableAssistant.Core.Settings;

namespace CentBrowser_PortableAssistant.Tests;

public class ScheduleRulesTests
{
    private static readonly DateTime Now = new(2026, 11, 10, 21, 0, 0);

    [Theory]
    [InlineData(AutoFrequency.Daily, "2026-11-09 23:00", true)]
    [InlineData(AutoFrequency.Daily, "2026-11-10 08:00", false)]
        [InlineData(AutoFrequency.Weekly, "2026-11-09 08:00", false)]
        [InlineData(AutoFrequency.Weekly, "2026-11-08 08:00", true)]
        [InlineData(AutoFrequency.Weekly, "2026-11-04 08:00", true)]
    [InlineData(AutoFrequency.Monthly, "2026-11-01 08:00", false)]
    [InlineData(AutoFrequency.Monthly, "2026-10-31 08:00", true)]
    public void FrequencyDecidesWhenDue(AutoFrequency frequency, string last, bool due) =>
        Assert.Equal(due, ScheduleRules.IsDue(DateTime.Parse(last), Now, frequency));

    [Fact]
    public void NeverBackedUpIsDue() => Assert.True(ScheduleRules.IsDue(null, Now, AutoFrequency.Monthly));

    [Fact]
    public void NotBackedUpAgainTheSameDay()
    {
        var state = new PlanState { LastSuccessLocal = Now.AddHours(-2) };
        Assert.Equal(PlanDecision.None, ScheduleRules.Decide(AutoFrequency.Daily, Now, state, changed: true));
    }

    [Fact]
    public void BackupFollowsFrequency()
    {
        var sameWeek = new PlanState { LastSuccessLocal = new DateTime(2026, 11, 9, 8, 0, 0) };
        Assert.Equal(PlanDecision.None, ScheduleRules.Decide(AutoFrequency.Weekly, Now, sameWeek, changed: true));
        var previousWeek = new PlanState { LastSuccessLocal = new DateTime(2026, 11, 8, 8, 0, 0) };
        Assert.Equal(PlanDecision.Backup, ScheduleRules.Decide(AutoFrequency.Weekly, Now, previousWeek, changed: true));
    }

    [Fact]
    public void UnchangedDataIsSkipped()
    {
        var state = new PlanState { LastSuccessLocal = Now.AddDays(-3) };
        Assert.Equal(PlanDecision.Unchanged, ScheduleRules.Decide(AutoFrequency.Daily, Now, state, changed: false));
        Assert.Equal(PlanDecision.Backup, ScheduleRules.Decide(AutoFrequency.Daily, Now, state, changed: true));
    }

    [Fact]
    public void UnchangedNoticeOncePerDay()
    {
        var state = new PlanState { LastUnchangedNotice = DateOnly.FromDateTime(Now) };
        Assert.False(ScheduleRules.ShouldNotifyUnchanged(state, Now));
        Assert.True(ScheduleRules.ShouldNotifyUnchanged(state, Now.AddDays(1)));
    }
}
