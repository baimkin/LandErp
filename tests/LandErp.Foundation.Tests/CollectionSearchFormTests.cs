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
