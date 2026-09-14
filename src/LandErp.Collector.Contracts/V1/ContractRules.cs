namespace LandErp.Collector.Contracts.V1;

public static class ContractRules
{
    public static bool IsSourceUrl(string? value, ListingSource source) => Enum.IsDefined(source)
        && Uri.TryCreate(value, UriKind.Absolute, out Uri? url) && url.Scheme == Uri.UriSchemeHttps
        && url.UserInfo.Length == 0 && url.IsDefaultPort
        && (url.Host.Equals(source == ListingSource.Avito ? "avito.ru" : "cian.ru", StringComparison.OrdinalIgnoreCase)
            || url.Host.EndsWith(source == ListingSource.Avito ? ".avito.ru" : ".cian.ru", StringComparison.OrdinalIgnoreCase));

    public static void Validate(ListingData data)
    {
        if (data == null || string.IsNullOrWhiteSpace(data.ExternalId) || data.ExternalId.Length > 32
            || !data.ExternalId.All(char.IsAsciiDigit) || !IsSourceUrl(data.Url, data.Source)
            || data.ObservedAt.Offset != TimeSpan.Zero || data.ObservedAt < DateTimeOffset.UnixEpoch
            || data.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(5) || data.Currency != "RUB"
            || string.IsNullOrWhiteSpace(data.AdapterVersion) || data.AdapterVersion.Length > 32
            || string.IsNullOrWhiteSpace(data.Provenance) || data.Provenance.Length > 100
            || data.PhotoUrls == null || data.PhotoUrls.Length > 100 || data.Warnings == null || data.Warnings.Length > 50)
            throw new ArgumentException("COLLECTION_FIELD_INVALID");
        foreach (TextField field in new[] { data.Title, data.Location, data.Description, data.SellerName })
        {
            if (field == null || !Enum.IsDefined(field.Presence) || field.Raw?.Length > 20000
                || field.Presence == FieldPresence.Present && string.IsNullOrWhiteSpace(field.Raw))
                throw new ArgumentException("COLLECTION_TEXT_INVALID");
        }
        foreach (DecimalField field in new[] { data.Price, data.AreaSquareMeters })
        {
            if (field == null || !Enum.IsDefined(field.Presence) || field.Raw?.Length > 512
                || field.Parsed is < 0 or > 999999999999999.9999m
                || field.Parsed != null && field.Presence != FieldPresence.Present
                || field.Presence == FieldPresence.Present && field.Parsed == null)
                throw new ArgumentException("COLLECTION_NUMBER_INVALID");
        }
        if (data.PhotoUrls.Any(value => value.Length > 2000 || !Uri.TryCreate(value, UriKind.Absolute, out Uri? url)
            || url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length != 0 || url.IsLoopback)
            || data.Warnings.Any(value => value.Length > 1000))
            throw new ArgumentException("COLLECTION_MEDIA_INVALID");
    }
}
