using System.Globalization;

namespace LandErp.Server.Components.Procurement;

public static class TimeEntry
{
    public static bool Invalid(string value) => value.Length > 0 && !TimeOnly.TryParseExact(value,"HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out _);

    // Four digits are a complete HHmm entry; partial input remains editable.
    public static string Normalize(string value)
    {
        string text = value.Trim();
        return text.Length == 4 && text.All(char.IsAsciiDigit) ? text.Insert(2, ":") : text;
    }
}
