using LandErp.Server.Components.Procurement;

namespace LandErp.Foundation.Tests;

[TestClass]
public sealed class TimeEntryTests
{
    [TestMethod]
    [DataRow("1200", "12:00")]
    [DataRow("0930", "09:30")]
    [DataRow("0000", "00:00")]
    [DataRow("2359", "23:59")]
    [DataRow("12:00", "12:00")]
    [DataRow("", "")]
    [DataRow("12", "12")]
    [DataRow("12:", "12:")]
    [DataRow("9999", "99:99")]
    public void FourDigitsFormatWithoutChangingPartialOrExistingInput(string value, string expected)
        => Assert.AreEqual(expected, TimeEntry.Normalize(value));
}
