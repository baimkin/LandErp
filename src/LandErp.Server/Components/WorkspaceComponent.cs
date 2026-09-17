using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Foundation.Files;
using LandErp.Server.Security;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Server.Components;

public abstract class WorkspaceComponent : ComponentBase
{
    [Inject] protected AuthenticationStateProvider Authentication { get; set; } = default!;
    protected Subject CurrentSubject { get; private set; } = new(Guid.Empty, false);
    protected bool Loading { get; private set; } = true;
    protected bool Busy { get; private set; }
    protected string? Error { get; private set; }
    protected bool Forbidden { get; private set; }
    protected string? Success { get; set; }

    protected abstract Task ReadAsync();

    protected override async Task OnInitializedAsync() => await ReloadAsync();

    protected async Task ReloadAsync()
    {
        Loading = true; Error = null; Forbidden = false;
        CurrentSubject = PermissionAuthorization.SubjectFrom((await Authentication.GetAuthenticationStateAsync()).User);
        try { await ReadAsync(); }
        catch (AccessDeniedException) { Forbidden = true; }
        catch (Exception) { Error = "Не удалось загрузить данные. Повторите после восстановления соединения."; }
        finally { Loading = false; }
    }

    protected async Task ExecuteAsync(Func<Task> action)
    {
        if (Busy) return;
        Busy = true; Error = null; Success = null;
        CurrentSubject = PermissionAuthorization.SubjectFrom((await Authentication.GetAuthenticationStateAsync()).User);
        try { await action(); await ReadAsync(); Success = "Изменения сохранены"; }
        catch (AccessDeniedException) { Forbidden = true; }
        catch (DbUpdateConcurrencyException) { Error = "Данные уже изменены. Обновите страницу и повторите решение."; }
        catch (ArgumentException exception) { Error = exception.Message; }
        catch (FileStorageException exception) { Error = exception.Message + " Обновите карточку, чтобы увидеть состояние вложения."; }
        catch (Exception) { Error = "Сохранение не выполнено. Проверьте соединение и повторите."; }
        finally { Busy = false; }
    }
}
