namespace LandErp.Application.Foundation;

/// <summary>Readiness checks connectivity and required schema, never changes it.</summary>
public interface IDatabaseStatus
{
    Task<bool> IsReadyAsync(CancellationToken cancellationToken);
}
