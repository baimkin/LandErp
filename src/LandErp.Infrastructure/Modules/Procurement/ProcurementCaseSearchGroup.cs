using LandErp.Application.Foundation;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Application.Modules.Procurement.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

public sealed partial class ProcurementWorkspace
{
    // Только принадлежность закупки к зоне. Catalog, источники и участие в медиане
    // эта команда не создаёт и не изменяет.
    public async Task SetSearchGroupAsync(Subject subject, SetCaseSearchGroup command,
        string correlationId, CancellationToken cancellationToken)
    {
        var access = await RequireProcurementAsync(subject, ProcurementAccessLevel.Manager, cancellationToken);
        var context = access.ProcurementWorkContext;
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await EmployeeWorkInvariant.LockOrganizationAsync(db, context.OrganizationId, cancellationToken);
        await EnsureActiveEmployeeAsync(db, context.EmployeeId, cancellationToken);
        Row row = await VisibleCases(db, context).SingleOrDefaultAsync(item => item.Case.Id == command.CaseId,
            cancellationToken) ?? throw new AccessDeniedException();
        if (row.Case.Version != command.ExpectedCaseVersion) throw new DbUpdateConcurrencyException();
        if (row.Case.ManualSearchGroupId == command.SearchGroupId) return;
        var group = await RequireActiveCaseSearchGroupAsync(db, context.OrganizationId, command.SearchGroupId, cancellationToken);
        Guid? previous = row.Case.ManualSearchGroupId;
        row.Case.ManualSearchGroupId = group?.Id;
        var now = time.GetUtcNow();
        db.BusinessTimeline.Add(new()
        {
            Id = DataConventions.NewId(), OrganizationId = context.OrganizationId,
            ObjectType = "PropertyCase", ObjectId = row.Case.Id, ActorEmployeeId = context.EmployeeId,
            Kind = "SearchGroupChanged", Title = "Изменена группа поиска",
            Body = group == null ? "Ручной выбор снят. Группы определяются по связанным источникам."
                : "Назначена группа поиска: " + group.Name, RecordedAt = now
        });
        OrganizationWorkspace.AddAudit(db, context, subject, "PropertyCaseSearchGroupChanged", "PropertyCase", row.Case.Id,
            new { PreviousSearchGroupId = previous, SearchGroupId = group?.Id, GroupName = group?.Name }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<SearchGroup?> RequireActiveCaseSearchGroupAsync(LandErpDbContext db,
        Guid organizationId, Guid? groupId, CancellationToken cancellationToken)
    {
        if (groupId == null) return null;
        // Архивирование не может пройти между проверкой активности и назначением.
        var group = await db.SearchGroups.FromSqlInterpolated(
            $"SELECT * FROM collection.search_groups WHERE id={groupId.Value} AND organization_id={organizationId} FOR SHARE")
            .SingleOrDefaultAsync(cancellationToken) ?? throw new AccessDeniedException();
        if (!group.Active) throw new ArgumentException("Группа поиска архивирована. Выберите действующую группу.");
        return group;
    }
}
