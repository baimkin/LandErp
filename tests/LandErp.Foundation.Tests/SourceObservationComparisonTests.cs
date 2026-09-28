using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Collector.Contracts.V1;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class SourceObservationComparisonTests
{
    private static readonly DateTimeOffset Start = new(2026, 9, 27, 8, 0, 0, TimeSpan.Zero);
    private static ListingData Data(int minute) => new() { Source = ListingSource.Avito, ExternalId = "10001",
        Url = "https://www.avito.ru/10001", AdapterVersion = "test", Provenance = "test", ObservedAt = Start.AddMinutes(minute) };
    private static ObservationView Observation(ListingData data, int recorded, params string[] changes) =>
        new(Guid.CreateVersion7(), Guid.Empty, data.ObservedAt, Start.AddMinutes(recorded), data, changes);

    [TestMethod]
    public void SourceValuesCarryAcrossAbsentFieldsAndIgnoreLateObservations()
    {
        var initial = Data(0) with { Price = new(FieldPresence.Present, "2000000", 2_000_000), Description = new(FieldPresence.Present, "Старое описание"), PhotoUrls = ["https://images.test/a.png"] };
        var absent = Data(2) with { Price = new(FieldPresence.Absent, null, null), Description = new(FieldPresence.Empty, "") };
        var late = Data(1) with { Price = new(FieldPresence.Present, "999", 999) };
        var changed = Data(3) with { Price = new(FieldPresence.Present, "1900000.5", 1_900_000.5m), Description = new(FieldPresence.Present, "Новое\nописание"), PhotoUrls = ["https://images.test/b.png"] };
        var result = SourceObservationComparison.Build([
            Observation(initial, 0, "цена", "описание", "фотографии"), Observation(absent, 2),
            Observation(late, 3), Observation(changed, 4, "цена", "описание", "фотографии")], true);
        Assert.HasCount(1, result);
        Assert.AreEqual(2_000_000m, result[0].Fields.Single(x => x.Name == "цена").Before!.Number);
        Assert.AreEqual("Старое описание", result[0].Fields.Single(x => x.Name == "описание").Before!.Text);
        Assert.AreEqual("https://images.test/a.png", result[0].Fields.Single(x => x.Name == "фотографии").Before!.Photos!.Single());
        Assert.AreEqual(1_900_000.5m, result[0].Fields.Single(x => x.Name == "цена").After!.Number);
    }

    [TestMethod]
    public void MissingEarlierFieldAndIncompleteHistoryRemainUnknown()
    {
        var changed = Data(1) with { Description = new(FieldPresence.Present, "Первое известное описание") };
        var result = SourceObservationComparison.Build([Observation(Data(0), 0), Observation(changed, 1, "описание")], true);
        Assert.IsNull(result.Single().Fields.Single().Before);
        var gap = SourceObservationComparison.Build([Observation(changed, 1, "описание")], false);
        Assert.IsNull(gap.Single().Fields.Single().Before);
        Assert.AreEqual("Первое известное описание", gap.Single().Fields.Single().After!.Text);
    }

    [TestMethod]
    public void EmptyPhotosDoNotBecomeRemovalAndSourcesHaveSeparateState()
    {
        var initial = Data(0) with { PhotoUrls = ["https://images.test/a.png"] };
        var result = SourceObservationComparison.Build([Observation(initial, 0, "фотографии"), Observation(Data(1), 1)], true);
        Assert.HasCount(0, result);
        var otherSource = Data(2) with { Source = ListingSource.Cian, ExternalId = "20001", Price = new(FieldPresence.Present, "12", 12) };
        var separate = SourceObservationComparison.Build([Observation(otherSource, 2, "цена")], false);
        Assert.IsNull(separate.Single().Fields.Single().Before);
    }

    [TestMethod]
    public void PhotoUrlRotationAndReencodingDoNotBecomeSourceChanges()
    {
        const string oldA = "https://old.test/a.jpg";
        const string oldB = "https://old.test/b.jpg";
        const string newA = "https://new.test/a.webp?token=2";
        const string newB = "https://new.test/b.webp?token=2";
        var fingerprints = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [oldA] = 0L,
            [newA] = 3L,
            [oldB] = -1L,
            [newB] = -1L
        };
        var initial = Data(0) with { PhotoUrls = [oldA, oldB] };
        var rotated = Data(1) with { PhotoUrls = [newB, newA] };

        var result = SourceObservationComparison.Build(
            [Observation(initial, 0, "фотографии"), Observation(rotated, 1, "фотографии")],
            true, fingerprints, 8);

        Assert.HasCount(0, result);
    }

    [TestMethod]
    public void PhotoReorderingWithStableUrlsIsNotAChangeWithoutFingerprints()
    {
        var initial = Data(0) with { PhotoUrls = ["https://images.test/a.png", "https://images.test/b.png"] };
        var reordered = Data(1) with { PhotoUrls = ["https://images.test/b.png", "https://images.test/a.png"] };

        var result = SourceObservationComparison.Build(
            [Observation(initial, 0, "фотографии"), Observation(reordered, 1, "фотографии")], true);

        Assert.HasCount(0, result);
    }

    [TestMethod]
    public void PhotoComparisonReportsOnlyTrulyAddedImage()
    {
        const string oldA = "https://old.test/a.jpg";
        const string oldB = "https://old.test/b.jpg";
        const string newA = "https://new.test/a.jpg";
        const string newB = "https://new.test/b.jpg";
        const string added = "https://new.test/c.jpg";
        var fingerprints = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [oldA] = 0L,
            [newA] = 1L,
            [oldB] = -1L,
            [newB] = -1L,
            [added] = unchecked((long)0xAAAAAAAAAAAAAAAAUL)
        };
        var initial = Data(0) with { PhotoUrls = [oldA, oldB] };
        var changed = Data(1) with { PhotoUrls = [newA, newB, added] };

        var field = SourceObservationComparison.Build(
                [Observation(initial, 0, "фотографии"), Observation(changed, 1, "фотографии")],
                true, fingerprints, 8)
            .Single().Fields.Single();

        CollectionAssert.AreEqual(Array.Empty<string>(), field.RemovedPhotos!);
        CollectionAssert.AreEqual(new[] { added }, field.AddedPhotos!);
    }

    [TestMethod]
    public void PhotoComparisonReportsOnlyTrulyRemovedImage()
    {
        const string oldA = "https://old.test/a.jpg";
        const string oldB = "https://old.test/b.jpg";
        const string oldC = "https://old.test/c.jpg";
        const string newA = "https://new.test/a.jpg";
        const string newC = "https://new.test/c.jpg";
        var fingerprints = new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [oldA] = 0L,
            [newA] = 1L,
            [oldB] = unchecked((long)0xAAAAAAAAAAAAAAAAUL),
            [oldC] = -1L,
            [newC] = -1L
        };
        var initial = Data(0) with { PhotoUrls = [oldA, oldB, oldC] };
        var changed = Data(1) with { PhotoUrls = [newA, newC] };

        var field = SourceObservationComparison.Build(
                [Observation(initial, 0, "фотографии"), Observation(changed, 1, "фотографии")],
                true, fingerprints, 8)
            .Single().Fields.Single();

        CollectionAssert.AreEqual(new[] { oldB }, field.RemovedPhotos!);
        CollectionAssert.AreEqual(Array.Empty<string>(), field.AddedPhotos!);
    }
}
