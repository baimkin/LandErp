namespace LandErp.Infrastructure.Foundation.Files;

public sealed class YandexDiskOptions
{
    public string Token { get; set; } = "";
    public string ConnectionId { get; set; } = "";
    public string Root { get; set; } = "app:/LandErp/development";
    public int MaxFileBytes { get; set; } = 8 * 1024 * 1024;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Token) || Token.Any(char.IsWhiteSpace))
            throw new InvalidOperationException("Настройте секрет доступа Яндекс Диска.");
        if (!Guid.TryParseExact(ConnectionId, "N", out Guid id) || id == Guid.Empty)
            throw new InvalidOperationException("Настройте уникальный идентификатор подключения хранилища.");
        if (!Root.StartsWith("app:/LandErp/", StringComparison.Ordinal) || Root.Length > 100
            || Root[5..].Split('/').Any(part => !ValidSegment(part)))
            throw new InvalidOperationException("Корень хранилища должен находиться в app:/LandErp/ и содержать только безопасные имена.");
        if (MaxFileBytes is < 1 or > 8 * 1024 * 1024)
            throw new InvalidOperationException("Размер файла должен быть ограничен 8 МБ.");
    }

    internal static bool ValidSegment(string value) => value.Length > 0 && value is not ("." or "..")
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}
