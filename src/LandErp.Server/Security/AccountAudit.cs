using LandErp.Application.Foundation;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Server.Security;

internal static class AccountAudit
{
    public static async Task RecordAsync(LandErpDbContext db, Guid userId, string action)
    {
        var employee = await db.Employees.SingleAsync(item => item.UserId == userId);
        db.AuditEvents.Add(new() { Id = DataConventions.NewId(), OrganizationId = employee.OrganizationId,
            ActorId = userId, Action = action, ObjectId = employee.Id, ObjectType = "Employee",
            Changes = "{}", CorrelationId = "account", RecordedAt = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
    }
}
