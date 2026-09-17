using LandErp.Application.Foundation.Files;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Server.Components.Procurement;

// Opt-in only: the other ERP screens keep their existing feedback contract.
public abstract class CaseWorkspaceComponent : WorkspaceComponent
{
    private string? operationError;
    protected new string? Error => operationError ?? base.Error;
    protected new string? Success { get; set; }
    protected new bool Busy { get; private set; }
    protected bool NeedsRefresh { get; private set; }
    protected string? RefreshWarning => NeedsRefresh ? "Изменения сохранены, но данные карточки не обновились. Обновите карточку перед следующим действием." : null;
    protected void ClearFeedback() { operationError = null; Success = null; }
    protected new async Task ReloadAsync()
    {
        operationError = null;
        await base.ReloadAsync();
        if (base.Error == null && !Forbidden) NeedsRefresh = false;
    }
    protected new async Task ExecuteAsync(Func<Task> action) => await ExecuteAsync(action, "Изменения сохранены");
    protected async Task ExecuteAsync(Func<Task> action, string message)
    {
        if (Busy) return;
        ClearFeedback();
        if (NeedsRefresh) { operationError = RefreshWarning; return; }
        Busy = true;
        try
        {
            NeedsRefresh = !await CaseOperation.SaveAndRefreshAsync(action, ReadAsync);
            Success = message;
        }
        catch (AccessDeniedException) { operationError = "Недостаточно прав для этого действия. Введённые данные сохранены в форме."; }
        catch (DbUpdateConcurrencyException) { operationError = "Объект изменён другим сотрудником. Закройте форму и обновите карточку перед повторным сохранением."; }
        catch (ArgumentException ex) { operationError = ex.Message; }
        catch (FileStorageException ex) { operationError = ex.Message + " Проверьте состояние файла в документах перед повторной загрузкой."; }
        catch (IOException) { operationError = "Не удалось прочитать файл. Проверьте доступность файла и ограничение 8 МБ."; }
        catch (Exception) { operationError = "Не удалось подтвердить сохранение. Проверьте историю объекта перед повторной попыткой."; }
        finally { Busy = false; }
    }
}
