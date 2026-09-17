using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LandErp.Application.Modules.Collection.Contracts;

namespace LandErp.Server.Foundation;

public sealed class SafeExceptionHandler(IProblemDetailsService problems, ILogger<SafeExceptionHandler> logger) : IExceptionHandler
{
    private static readonly Action<ILogger, string, string, Exception?> Failure = LoggerMessage.Define<string, string>(
        LogLevel.Warning, new EventId(101, "RequestFailure"), "Request failed: {ExceptionType}; correlation {CorrelationId}");
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        Failure(logger, exception.GetType().Name, httpContext.TraceIdentifier, null);
        (int status, string code, string title) = exception switch
        {
            CollectorProtocolException protocol => (protocol.Code is "AGENT_UNAUTHORIZED" or "ACTIVATION_INVALID" ? 401 : protocol.Code == "WORK_NOT_ALLOWED" ? 403 : 409, protocol.Code, "Collector request rejected"),
            AccessDeniedException => (403, "FORBIDDEN", "Нет доступа"),
            DbUpdateConcurrencyException => (409, "CONFLICT", "Данные уже изменены"),
            ArgumentException => (400, "VALIDATION_FAILED", "Проверьте введённые данные"),
            _ => (500, "REQUEST_FAILED", "Не удалось выполнить действие")
        };
        httpContext.Response.StatusCode = status;
        return await problems.TryWriteAsync(new() { HttpContext = httpContext,
            ProblemDetails = new ProblemDetails { Status = status, Title = title,
                Extensions = { ["code"] = code, ["correlationId"] = httpContext.TraceIdentifier,
                    ["retryable"] = exception is CollectorProtocolException collectorFailure && collectorFailure.Retryable } } });
    }
}
