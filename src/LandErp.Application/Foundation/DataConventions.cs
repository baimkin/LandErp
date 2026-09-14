namespace LandErp.Application.Foundation;

public static class DataConventions
{
    public const string BusinessTimeZoneId = "Europe/Moscow";
    public const int DecimalPrecision = 19;
    public const int DecimalScale = 4;

    public static Guid NewId() => Guid.CreateVersion7();

    public static decimal RoundRubles(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.ToEven);
}
