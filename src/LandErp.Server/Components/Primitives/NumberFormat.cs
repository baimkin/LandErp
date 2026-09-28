using System.Globalization;
namespace LandErp.Server.Components.Primitives;
public static class NumberFormat
{
    public static string Money(decimal value) => decimal.Round(value,0,MidpointRounding.AwayFromZero).ToString("N0",CultureInfo.GetCultureInfo("ru-RU")).Replace('\u00a0',' ');
    public static string Normalize(string text) => text.Replace(" ","",StringComparison.Ordinal).Replace("\u00a0","",StringComparison.Ordinal).Replace("\u202f","",StringComparison.Ordinal).Replace("₽","",StringComparison.Ordinal).Replace(',','.');
}
