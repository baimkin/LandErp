using System.Globalization;
using LandErp.ParserSpike.LocalCollection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LandErp.ParserSpike.Tests;

[TestClass]
public sealed class CianCoordinateIdentityTests
{
    private const string Precise = "55.61444583439937,37.16068413239134,55.67851595205049,37.30316307526244";
    private const string Rounded = "55.6144458344,37.1606841324,55.6785159521,37.3031630753";
    private static string Search(string bbox, bool second = false) =>
        $"https://{(second ? "www" : "fixture")}.cian.ru/cat.php?bbox={Uri.EscapeDataString(bbox)}&deal_type=sale&offer_type=suburban"
        + $"&in_polygon%5B{(second ? 1 : 0)}%5D=37%2055%2C38%2055%2C37%2055&object_type%5B0%5D=3"
        + (second ? "&p=2" : "&center=55.65%2C37.23&zoom=13");

    [TestMethod]
    [DataRow(Precise, Rounded)]
    [DataRow("55.60776646875543,37.89821222885736,55.67184755112399,38.03313807114251", "55.6077664688,37.8982122289,55.6718475511,38.0331380711")]
    [DataRow("-55.61444583439937,-37.30316307526244,-55.52752092417583,-37.16068413239134", "-55.6144458344,-37.3031630753,-55.5275209242,-37.1606841324")]
    [DataRow("55.12345678905,37.12345678905,56.12345678905,38.12345678905", "55.1234567891,37.1234567891,56.1234567891,38.1234567891")]
    [DataRow("55.600000000000,37.500000000000,55.900000000000,38.400000000000", "55.6,37.5,55.9,38.4")]
    public void RoundingIsAcceptedInBothDirectionsWithoutRewritingSavedUrl(string precise, string rounded)
    {
        string first = Search(precise), second = Search(rounded, true);
        Assert.IsTrue(SearchUrls.SameSearch(first, second, SourceSite.Cian));
        Assert.IsTrue(SearchUrls.SameSearch(second, first, SourceSite.Cian));
        Assert.AreEqual(0, SearchUrls.DiagnosticIdentityDiff(first, second, SourceSite.Cian).Length);
        StringAssert.Contains(SearchUrls.Normalize(first).Url, Uri.EscapeDataString(precise));
        Assert.AreEqual(2, SearchUrls.PageNumber(SearchUrls.SafePage(second, SourceSite.Cian)));
    }

    [TestMethod]
    [DataRow("55.6144458345,37.1606841324,55.6785159521,37.3031630753")]
    [DataRow("55.6144458344,37.1606841325,55.6785159521,37.3031630753")]
    [DataRow("55.6144458344,37.1606841324,55.6785159522,37.3031630753")]
    [DataRow("55.6144458344,37.1606841324,55.6785159521,37.3031630754")]
    [DataRow("55.7,37.1606841324,55.8,37.3031630753")]
    public void EveryBoundingCoordinateRemainsSignificant(string changed)
    {
        Assert.IsFalse(SearchUrls.SameSearch(Search(Precise), Search(changed, true), SourceSite.Cian));
        Assert.IsTrue(SearchUrls.DiagnosticIdentityDiff(Search(Precise), Search(changed, true), SourceSite.Cian).Any(x => x.StartsWith("bbox:", StringComparison.Ordinal)));
    }

    [TestMethod]
    [DataRow("deal_type=sale", "deal_type=rent")]
    [DataRow("offer_type=suburban", "offer_type=flat")]
    [DataRow("object_type%5B0%5D=3", "object_type%5B0%5D=2")]
    [DataRow("37%2055%2C38%2055%2C37%2055", "37%2055%2C38.0000000001%2055%2C37%2055")]
    public void RoundedBboxDoesNotHideChangedPolygonOrOtherFilters(string before, string after)
    {
        Assert.IsFalse(SearchUrls.SameSearch(Search(Precise), Search(Rounded, true).Replace(before, after, StringComparison.Ordinal), SourceSite.Cian));
    }

    [TestMethod]
    [DataRow("maxprice")]
    [DataRow("region")]
    [DataRow("unknown_filter")]
    public void AddedRemovedOrChangedFiltersStillFail(string key)
    {
        string first = Search(Precise), second = Search(Rounded, true);
        Assert.IsFalse(SearchUrls.SameSearch(first, second + $"&{key}=1", SourceSite.Cian));
        Assert.IsFalse(SearchUrls.SameSearch(first + $"&{key}=1", second, SourceSite.Cian));
        Assert.IsFalse(SearchUrls.SameSearch(first + $"&{key}=1", second + $"&{key}=2", SourceSite.Cian));
    }

    [TestMethod]
    [DataRow("91.12345678901,37,92,38", "91.1234567890,37,92,38")]
    [DataRow("55,181.12345678901,56,182", "55,181.1234567890,56,182")]
    [DataRow("NaN,37,56,38", "nan,37,56,38")]
    [DataRow("55,37,56", "55.0,37,56")]
    [DataRow("55,37,56,38,39", "55.0,37,56,38,39")]
    [DataRow("5.5e1,37,56,38", "55,37,56,38")]
    [DataRow("-79228162514264337593543950335,37,56,38", "55,37,56,38")]
    public void MalformedOrOutOfRangeCoordinatesAreNotNormalized(string left, string right)
    {
        Assert.IsFalse(SearchUrls.SameSearch(Search(left), Search(right, true), SourceSite.Cian));
    }

    [TestMethod]
    [DataRow("ru-RU")]
    [DataRow("en-US")]
    public void CoordinatesAreCultureIndependentAndMissingBboxIsNotIgnored(string culture)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            string first = Search(Precise), second = Search(Rounded, true);
            Assert.IsTrue(SearchUrls.SameSearch(first, second, SourceSite.Cian));
            string withoutBbox = "https://www.cian.ru/cat.php?" + string.Join('&', second.Split('&').Skip(1));
            Assert.IsFalse(SearchUrls.SameSearch(first, withoutBbox, SourceSite.Cian));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }
}
