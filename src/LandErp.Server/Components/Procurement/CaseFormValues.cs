using System.Globalization;
using LandErp.Application.Foundation;

namespace LandErp.Server.Components.Procurement;

public static class CaseFormValues
{
    public static DateTimeOffset? ParseLocal(string value)
    {
        if(string.IsNullOrWhiteSpace(value))return null;
        string[] formats=["yyyy-MM-ddTHH:mm","yyyy-MM-ddTHH:mm:ss","yyyy-MM-ddTHH:mm:ss.FFFFFFF"];
        if(!DateTime.TryParseExact(value,formats,CultureInfo.InvariantCulture,DateTimeStyles.None,out DateTime local))
            throw new ArgumentException("Проверьте дату и время: укажите корректное значение по Москве.");
        var zone=TimeZoneInfo.FindSystemTimeZoneById(DataConventions.BusinessTimeZoneId);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),zone));
    }
}
