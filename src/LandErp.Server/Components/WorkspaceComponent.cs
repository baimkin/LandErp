using System.Runtime.CompilerServices;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Foundation.Files;
using LandErp.Server.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace LandErp.Server.Components;

public enum WorkspaceWriteOutcome { None, NotSent, Rejected, Unknown, Committed }

public abstract class WorkspaceComponent : ComponentBase
{
    private static readonly Action<ILogger, string, string, string, string, string, Exception?> LogFailure =
        LoggerMessage.Define<string, string, string, string, string>(LogLevel.Warning,
            new EventId(1101, "WORKSPACE_OPERATION_FAILED"),
            "Workspace operation {OperationId}; phase {Phase}; component {Component}; action {Action}; failure type {FailureType}");

    [Inject] protected AuthenticationStateProvider Authentication { get; set; } = default!;
    [Inject] protected ILogger<WorkspaceComponent> Logger { get; set; } = NullLogger<WorkspaceComponent>.Instance;
    protected Subject CurrentSubject { get; private set; } = new(Guid.Empty, false);
    protected bool Loading { get; private set; } = true;
    protected bool Busy { get; private set; }
    protected string? Error { get; private set; }
    protected bool Forbidden { get; private set; }
    protected string? Success { get; set; }
    protected string OperationId { get; private set; } = "";
    protected WorkspaceWriteOutcome WriteOutcome { get; private set; }
    protected bool RefreshRequired { get; private set; }

    protected abstract Task ReadAsync();

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    protected async Task ReloadAsync()
    {
        // A read must not replace form state or the command result while a write is in flight.
        if (Busy || Loading && OperationId.Length > 0) return;
        Loading = true; Error = null; Forbidden = false;
        if (!RefreshRequired) Success = null;
        OperationId = Guid.CreateVersion7().ToString();
        try
        {
            CurrentSubject = PermissionAuthorization.SubjectFrom((await Authentication.GetAuthenticationStateAsync()).User);
            await ReadAsync();
            if (RefreshRequired && WriteOutcome == WorkspaceWriteOutcome.Committed) Success = "Изменения сохранены";
            RefreshRequired = false;
        }
        catch (AccessDeniedException exception)
        {
            CurrentSubject = new(Guid.Empty, false);
            Forbidden = true;
            Error = FailureMessage(exception, "Read", nameof(ReloadAsync), "Доступ к данным изменился.");
        }
        catch (Exception exception)
        {
            Error = FailureMessage(exception, "Read", nameof(ReloadAsync),
                "Не удалось загрузить данные. Повторите обновление после восстановления соединения.");
        }
        finally { Loading = false; }
    }

    // Keep the existing Task contract for callers that only use Success/Error. New multi-step
    // handlers use the returned confirmation below, never infer a commit from Error == null.
    protected Task ExecuteAsync(Func<Task> action, [CallerMemberName] string operation = "")
        => ExecuteConfirmedAsync(action, operation);

    protected async Task<bool> ExecuteConfirmedAsync(Func<Task> action, [CallerMemberName] string operation = "")
    {
        if (Busy || Loading) return false;
        Busy = true; Error = null; Success = null; Forbidden = false;
        WriteOutcome = WorkspaceWriteOutcome.NotSent;
        OperationId = Guid.CreateVersion7().ToString();
        try
        {
            try
            {
                CurrentSubject = PermissionAuthorization.SubjectFrom((await Authentication.GetAuthenticationStateAsync()).User);
            }
            catch (Exception exception)
            {
                CurrentSubject = new(Guid.Empty, false);
                Error = FailureMessage(exception, "Authenticate", operation,
                    "Не удалось проверить сеанс. Команда не отправлена. Войдите заново и повторите действие.");
                return false;
            }

            try { await action(); }
            catch (AccessDeniedException exception)
            {
                WriteOutcome = WorkspaceWriteOutcome.Rejected;
                Forbidden = true;
                Error = FailureMessage(exception, "Command", operation, "Нет права выполнить это действие.");
                return false;
            }
            catch (DbUpdateConcurrencyException exception)
            {
                WriteOutcome = WorkspaceWriteOutcome.Rejected;
                Error = FailureMessage(exception, "Command", operation,
                    "Данные уже изменены. Обновите данные и проверьте актуальное состояние перед повтором решения.");
                return false;
            }
            catch (ArgumentException exception)
            {
                WriteOutcome = WorkspaceWriteOutcome.Rejected;
                Error = FailureMessage(exception, "Validation", operation, exception.Message);
                return false;
            }
            catch (FileStorageException exception)
            {
                // File metadata may have been committed before the provider failed.
                WriteOutcome = WorkspaceWriteOutcome.Unknown;
                Error = FailureMessage(exception, "Storage", operation,
                    "Загрузка не подтверждена. Обновите карточку и проверьте состояние вложения перед повтором.");
                return false;
            }
            catch (Exception exception)
            {
                // A failed acknowledgement is not proof of a rolled-back database write.
                WriteOutcome = WorkspaceWriteOutcome.Unknown;
                Error = FailureMessage(exception, "Command", operation,
                    "Результат сохранения не подтверждён. Сначала проверьте данные; не создавайте запись заново.");
                return false;
            }

            WriteOutcome = WorkspaceWriteOutcome.Committed;
            Success = "Изменения сохранены";
            try
            {
                await ReadAsync();
                RefreshRequired = false;
            }
            catch (Exception exception)
            {
                RefreshRequired = true;
                if (exception is AccessDeniedException) Forbidden = true;
                // Keep Error null: existing dialogs close on Error == null or Success != null.
                // The command is confirmed even when this read fails. Retrying means GET/read,
                // not invoking action again. The detailed UI can use RefreshRequired.
                Success = FailureMessage(exception, "RefreshAfterCommit", operation,
                    "Изменения сохранены, но экран не обновлён. Обновите данные, не повторяйте сохранение.");
            }
            return true;
        }
        finally { Busy = false; }
    }

    protected string ReadFailure(Exception exception, string message, [CallerMemberName] string operation = "")
    {
        string id = Guid.CreateVersion7().ToString();
        WriteDiagnostic(id, "Read", operation, exception);
        return $"{message} Номер обращения: {id}.";
    }

    private string FailureMessage(Exception exception, string phase, string operation, string message)
    {
        WriteDiagnostic(OperationId, phase, operation, exception);
        return $"{message} Номер обращения: {OperationId}.";
    }

    private void WriteDiagnostic(string id, string phase, string operation, Exception exception)
    {
        // Never pass exception/InnerException, messages, user forms, file bytes, URLs or
        // credentials to ILogger. Exception type + phase + operation identify the boundary.
        try { LogFailure(Logger, id, phase, GetType().Name, operation, exception.GetType().Name, null); }
        catch { /* A logging-provider failure must not change the business operation's outcome. */ }
    }
}
