namespace LandErp.Server.Foundation;

public sealed class CorrelationMiddleware(RequestDelegate next, ILogger<CorrelationMiddleware> logger)
{
    public const string HeaderName = "X-Correlation-ID";

    public static bool IsValid(string? value) => value is { Length: > 0 and <= 64 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');

    public async Task InvokeAsync(HttpContext context)
    {
        string incoming = context.Request.Headers[HeaderName].ToString();
        string correlationId = IsValid(incoming) ? incoming : Guid.CreateVersion7().ToString();
        context.TraceIdentifier = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        using IDisposable? scope = logger.BeginScope(new Dictionary<string, object>
        {
            ["correlationId"] = correlationId,
            ["service"] = "Server"
        });
        await next(context);
    }
}
