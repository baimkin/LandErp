using LandErp.ParserSpike.Desktop;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class ManualAttentionTests
{
    private static CollectionNotice Notice(string id = "job", SourceSite source = SourceSite.Avito, string reason = "Captcha")
        => new(source, id, "https://www.avito.ru/moskva", "worker", reason);

    [TestMethod]
    public void DuplicateDoesNotResetTimeAndReminderIsSentOnlyOnceAfterFiveMinutes()
    {
        var state = new ManualAttentionState();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        Assert.IsTrue(state.Add(Notice(), start));
        Assert.IsFalse(state.Add(Notice(), start.AddMinutes(4)));
        Assert.AreEqual(start, state.Pending.Single().SinceUtc);
        Assert.IsEmpty(state.Reminders(start.AddMinutes(5).AddTicks(-1)));
        Assert.HasCount(1, state.Reminders(start.AddMinutes(5)));
        Assert.IsEmpty(state.Reminders(start.AddHours(1)));
    }

    [TestMethod]
    public void EachSourceRemainsVisibleUntilItsOwnWorkIsResolved()
    {
        var state = new ManualAttentionState();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        state.Add(Notice("avito"), start);
        state.Add(Notice("cian", SourceSite.Cian, "AuthenticationRequired"), start.AddMinutes(1));
        state.RemoveResolved(id => id == "cian");
        Assert.AreEqual(SourceSite.Cian, state.Pending.Single().Notice.Source);
        Assert.IsEmpty(state.Reminders(start.AddMinutes(5)));
        Assert.HasCount(1, state.Reminders(start.AddMinutes(6)));
        state.RemoveResolved(_ => false);
        Assert.IsEmpty(state.Pending);
        Assert.IsEmpty(state.Reminders(start.AddDays(1)));
    }

    [TestMethod]
    public void NewReasonOrNewBlockStartsANewReminder()
    {
        var state = new ManualAttentionState();
        DateTimeOffset start = DateTimeOffset.UtcNow;
        state.Add(Notice(), start);
        state.Reminders(start.AddMinutes(5));
        Assert.IsTrue(state.Add(Notice(reason: "AuthenticationRequired"), start.AddMinutes(6)));
        Assert.IsEmpty(state.Reminders(start.AddMinutes(10)));
        Assert.HasCount(1, state.Reminders(start.AddMinutes(11)));
        state.RemoveResolved(_ => false);
        Assert.IsTrue(state.Add(Notice(), start.AddMinutes(12)));
        Assert.HasCount(1, state.Reminders(start.AddMinutes(17)));
    }
}
