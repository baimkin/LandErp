using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using LandErp.Application.Modules.IdentityAccess.Contracts;

namespace LandErp.Server.Foundation;

// Only structural metadata is allowed here. Provider Message/Detail/SQL and form values never enter a log.
public sealed record WorkspaceFailureDetails(string Code, string InnerFailureType, string? SqlState = null,
    string? Constraint = null, string? Table = null, string? Column = null, bool Transient = false);

public static partial class WorkspaceFailureDiagnostics
{
    [GeneratedRegex("^[A-Za-z_][A-Za-z0-9_]{0,62}$", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
    [GeneratedRegex("^[0-9A-Z]{5}$", RegexOptions.CultureInvariant)]
    private static partial Regex SqlStatePattern();

    public static WorkspaceFailureDetails Inspect(Exception exception)
    {
        List<Exception> chain = [];
        for (Exception? current = exception; current != null && chain.Count < 12; current = current.InnerException)
            chain.Add(current);
        string innerType = chain[^1].GetType().Name;
        if (chain.OfType<PostgresException>().FirstOrDefault() is { } postgres)
            return new(SqlStatePattern().IsMatch(postgres.SqlState) ? "PG_" + postgres.SqlState : "PG_ERROR",
                innerType, SqlStatePattern().IsMatch(postgres.SqlState) ? postgres.SqlState : null,
                Identifier(postgres.ConstraintName), Identifier(postgres.TableName), Identifier(postgres.ColumnName), postgres.IsTransient);
        bool database = chain.Any(item => item is NpgsqlException);
        string code = exception switch
        {
            DbUpdateConcurrencyException => "DB_CONCURRENCY",
            AccessDeniedException => "ACCESS_DENIED",
            ArgumentException => "VALIDATION",
            _ when chain.Any(item => item is TimeoutException) => database ? "DB_TIMEOUT" : "TIMEOUT",
            _ when chain.Any(item => item is SocketException) => database ? "DB_CONNECTION" : "NETWORK_CONNECTION",
            _ when database => "DB_PROVIDER",
            _ when chain.Any(item => item is OperationCanceledException) => "CANCELLED",
            _ when chain.Any(item => item is UnauthorizedAccessException) => "IO_ACCESS_DENIED",
            _ when chain.Any(item => item is IOException) => "IO_FAILURE",
            InvalidOperationException => "INVALID_OPERATION",
            _ => "UNEXPECTED_ERROR"
        };
        return new(code, innerType, Transient: chain.OfType<NpgsqlException>().Any(item => item.IsTransient));
    }
    private static string? Identifier(string? value) => value != null && IdentifierPattern().IsMatch(value) ? value : null;
}

internal static partial class WorkspaceFailureLog
{
    [LoggerMessage(EventId = 1101, EventName = "WORKSPACE_OPERATION_FAILED", Level = LogLevel.Warning,
        Message = "Workspace operation {OperationId}; phase {Phase}; component {Component}; action {Action}; failure type {FailureType}; code {DiagnosticCode}; inner {InnerFailureType}; SQLSTATE {SqlState}; constraint {Constraint}; table {Table}; column {Column}; transient {Transient}; elapsed ms {ElapsedMs}; command {CommandId}")]
    internal static partial void Write(ILogger logger, string operationId, string phase, string component, string action,
        string failureType, string diagnosticCode, string innerFailureType, string? sqlState, string? constraint,
        string? table, string? column, bool transient, long? elapsedMs, Guid? commandId);
}
