using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Contracts;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Server.Components.Pages;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class CollectionSearchFormTests
{
    [TestMethod]
    public void DailyTimesAreSortedAndDuplicatesOrBlankRowsAreRejected()
    {
        CollectionSearchForm form = new() { ScheduleKind = CollectionScheduleKind.FixedTimes, Times = [new("16:00"), new("09:00")] };
        Assert.AreEqual("09:00,16:00", string.Join(",", form.Schedule().FixedTimes!));
        form.Times.Add(new("09:00"));
        Assert.ThrowsExactly<ArgumentException>(() => form.Schedule());
        form.Times = [new("")];
        Assert.ThrowsExactly<ArgumentException>(() => form.Schedule());
    }

    [TestMethod]
    public void EditedBrowserTimesAreBoundAndCanonical()
    {
        CollectionSearchForm form = new()
        {
            ScheduleKind = CollectionScheduleKind.FixedTimes,
            Times = [new("08:30"), new("16:45")]
        };
        CollectionSchedule schedule = form.Schedule();
        Assert.AreEqual("08:30,16:45", string.Join(",", schedule.FixedTimes!));

        form.Times = [new("8:30")];
        ArgumentException format = Assert.ThrowsExactly<ArgumentException>(() => form.Schedule());
        StringAssert.Contains(format.Message, "ЧЧ:ММ");

        string markup = File.ReadAllText(Path.Combine(FoundationTests.RepositoryRoot(),
            "src", "LandErp.Server", "Components", "Pages", "Collectors.razor"));
        StringAssert.Contains(markup, """value="@row.Value" @oninput="async e => { row.Value = e.Value?.ToString()""");
        StringAssert.Contains(markup, "await UpdatePreviewAsync();",
            "An edited browser time must update the form and schedule preview.");
    }

    [TestMethod]
    public void LegacyWholeMinuteTimesAreNormalizedWhenEditing()
    {
        var search = new SearchView(Guid.NewGuid(), "Name", CatalogSource.Avito, "https://www.avito.ru", 10, null, "",
            "", CollectionScheduleKind.FixedTimes, null, ["16:00:00", "08:00:00.0000000"], true, null, 1, null);

        CollectionSearchForm edited = CollectionSearchForm.From(search);

        Assert.AreEqual("16:00", edited.Times[0].Value);
        Assert.AreEqual("08:00", edited.Times[1].Value);
        Assert.AreEqual("08:00,16:00", string.Join(",", edited.Schedule().FixedTimes!));

        edited.Times = [new("08:00:30")];
        ArgumentException format = Assert.ThrowsExactly<ArgumentException>(() => edited.Schedule());
        StringAssert.Contains(format.Message, "ЧЧ:ММ");
    }

    [TestMethod]
    public void IntervalUnitsAreBoundedAndEditingPreservesOddMinuteIntervals()
    {
        CollectionSearchForm form = new() { ScheduleKind = CollectionScheduleKind.Interval, IntervalValue = 2, IntervalUnit = 60 };
        Assert.AreEqual(120, form.Schedule().IntervalMinutes);
        form.IntervalValue = int.MaxValue;
        Assert.ThrowsExactly<ArgumentException>(() => form.Schedule());
        var search = new SearchView(Guid.NewGuid(), "Name", CatalogSource.Avito, "https://www.avito.ru", 10, null, "",
            "", CollectionScheduleKind.Interval, 95, [], true, null, 1, null);
        var edited = CollectionSearchForm.From(search);
        Assert.AreEqual(95, edited.Schedule().IntervalMinutes);
        Assert.IsFalse(edited.RunImmediately);
    }
}
