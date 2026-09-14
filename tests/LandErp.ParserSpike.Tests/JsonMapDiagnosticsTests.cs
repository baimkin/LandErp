using System.Text;
using System.Text.Json;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class JsonMapDiagnosticsTests
{
    [TestMethod]
    public void ItemsKeepAllVerifiedIdsAndConvertCoordinateStrings()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"totalCount":4,"items":[
              {"id":12345678,"urlPath":"/korolev/uchastok_12345678","coords":{"lat":"55.9","lng":"37.8","precision":100},"userId":987654321},
              {"id":12345679,"urlPath":"/korolev/uchastok_12345679","coords":{"lat":55.91,"lng":37.81}},
              {"id":12345680,"urlPath":"/korolev/uchastok_12345680","coords":{"lat":55.92,"lng":37.82}},
              {"id":12345681,"urlPath":"/korolev/uchastok_12345681","coords":{"lat":55.93,"lng":37.83}},
              {"id":22222222,"urlPath":"/korolev/uchastok_99999999","coords":{"lat":55.9,"lng":37.8}}]}
            """);
        MapResponseSummary summary = JsonResponseDiagnostics.SummarizeMap(document.RootElement, "https://www.avito.ru/js/1/map/items")!;
        Assert.AreEqual(4, summary.Listings.Length);
        Assert.AreEqual(55.9m, summary.Listings[0].Latitude);
        Assert.AreEqual(37.8m, summary.Listings[0].Longitude);
        Assert.AreEqual(100m, summary.Listings[0].Precision);
        Assert.IsFalse(JsonSerializer.Serialize(summary).Contains("987654321", StringComparison.Ordinal));
    }
    [TestMethod]
    public void InvalidOrIncompleteCoordinatesCannotPlaceListing()
    {
        using JsonDocument document = JsonDocument.Parse("""
            {"items":[{"id":12345678,"urlPath":"/uchastok_12345678","coords":{"lat":"91","lng":"37.8"}},
            {"id":12345679,"urlPath":"/uchastok_12345679","coords":{"lat":"55.9","lng":"NaN"}}]}
            """);
        MapResponseSummary summary = JsonResponseDiagnostics.SummarizeMap(document.RootElement, "https://www.avito.ru/js/1/map/items")!;
        Assert.AreEqual(2, summary.Listings.Length);
        Assert.IsTrue(summary.Listings.All(p => p.Latitude is null && p.Longitude is null));
        Assert.IsNull(JsonResponseDiagnostics.SummarizeMap(document.RootElement, "https://evil.test/js/1/map/items"));
        Assert.IsNull(JsonResponseDiagnostics.SummarizeMap(document.RootElement, "https://www.avito.ru/web/1/profile"));
    }
    [TestMethod]
    public void GeometryExportsOnlyKnownGeographyAndMarkersAreNotListings()
    {
        string encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes("""
            {"type":"Polygon","coordinates":[[[37.8,55.9],[37.9,55.9],[37.8,55.9]]],
            "token":"private-test-secret","userId":987654321,"properties":{"email":"private@example.test"}}
            """));
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            markers = new[] { new { id = "cluster:12", itemsCount = 7, coords = new { lat = "55.9", lng = 37.8 }, isOutGeo = false } },
            drawAreaBase64 = Uri.EscapeDataString(encoded)
        }));
        MapResponseSummary summary = JsonResponseDiagnostics.SummarizeMap(document.RootElement, "https://www.avito.ru/web/1/map/markers")!;
        Assert.AreEqual("DecodedJson", summary.DrawOutcome);
        Assert.AreEqual(0, summary.Listings.Length);
        Assert.AreEqual(7, summary.Markers.Single().ItemsCount);
        Assert.AreEqual(55.9m, summary.Markers.Single().Latitude);
        Assert.IsNull(summary.Markers.Single().MarkerId);
        string text = JsonSerializer.Serialize(summary);
        StringAssert.Contains(text, "Polygon");
        Assert.IsFalse(text.Contains("private", StringComparison.Ordinal));
        Assert.IsFalse(text.Contains("987654321", StringComparison.Ordinal));
    }
    [TestMethod]
    public void MissingAndMalformedGeometryAreExplicitWithoutRawPayload()
    {
        using JsonDocument missing = JsonDocument.Parse("{}");
        Assert.AreEqual("Absent", JsonResponseDiagnostics.SummarizeMap(missing.RootElement, "https://www.avito.ru/web/1/map/markers")!.DrawOutcome);
        using JsonDocument invalid = JsonDocument.Parse("""{"drawAreaBase64":"private-invalid-payload!"}""");
        MapResponseSummary summary = JsonResponseDiagnostics.SummarizeMap(invalid.RootElement, "https://www.avito.ru/web/1/map/markers")!;
        StringAssert.StartsWith(summary.DrawOutcome, "CannotDecode_");
        Assert.IsNull(summary.DrawGeometry);
        Assert.IsFalse(JsonSerializer.Serialize(summary).Contains("private-invalid-payload", StringComparison.Ordinal));
    }
}
