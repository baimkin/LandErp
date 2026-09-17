namespace LandErp.Server.Components.Procurement;

/// <summary>Keep write acknowledgement separate from refreshing the screen: a refresh failure must never invite a duplicate write.</summary>
public static class CaseOperation
{
    public static async Task<bool> SaveAndRefreshAsync(Func<Task> save, Func<Task> refresh)
    {
        await save();
        try { await refresh(); return true; }
        catch (Exception) { return false; }
    }
}
