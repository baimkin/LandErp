namespace LandErp.Server.Components.Procurement;

// Card-specific presentation over the shared, diagnostic write boundary.
public abstract class CaseWorkspaceComponent : WorkspaceComponent
{
    protected bool NeedsRefresh => RefreshRequired;
    protected string? RefreshWarning => RefreshRequired ? Success : null;
    protected async Task ExecuteAsync(Func<Task> action) => await ExecuteAsync(action, "Изменения сохранены");
    protected new async Task ExecuteAsync(Func<Task> action, string message)
    {
        if (RefreshRequired) return;
        bool committed = await ExecuteConfirmedAsync(action);
        if (committed && !RefreshRequired) Success = message;
    }
}
