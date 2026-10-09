using LandErp.Application.Foundation;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.IdentityAccess;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace LandErp.Infrastructure.Modules.Catalog;

public sealed class ListingCommentService(
    IDbContextFactory<LandErpDbContext> factory,
    IEmployeeAccessService employeeAccess,
    TimeProvider time) : IListingCommentService
{
    public async Task<ListingCommentEditorView> ReadAsync(Subject subject, Guid listingId,
        CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await RequireListingAsync(db, effective.OrganizationId, listingId, cancellationToken);
        bool canManageTypes = CanManageTypes(effective);

        ListingCommentTypeView[] types = await db.ListingCommentTypes.AsNoTracking()
            .Where(type => type.OrganizationId == effective.OrganizationId
                && (canManageTypes || type.IsActive
                    || db.ListingComments.Any(comment => comment.OrganizationId == effective.OrganizationId
                        && comment.ListingId == listingId && comment.CommentTypeId == type.Id)
                    || db.ListingCommentHistories.Any(history => history.OrganizationId == effective.OrganizationId
                        && history.ListingId == listingId && history.CommentTypeId == type.Id)))
            .OrderBy(type => type.SortOrder).ThenBy(type => type.Name).ThenBy(type => type.Id)
            .Select(type => new ListingCommentTypeView(type.Id, type.Name, type.Description,
                type.SortOrder, type.IsActive, type.Version))
            .ToArrayAsync(cancellationToken);

        ListingCommentView[] comments = await (
            from comment in db.ListingComments.AsNoTracking()
            join createdBy in db.Employees.AsNoTracking() on comment.CreatedByEmployeeId equals createdBy.Id
            join updatedBy in db.Employees.AsNoTracking() on comment.UpdatedByEmployeeId equals updatedBy.Id
            where comment.OrganizationId == effective.OrganizationId && comment.ListingId == listingId
            select new ListingCommentView(comment.Id, comment.ListingId, comment.CommentTypeId,
                comment.Text, comment.CreatedByEmployeeId, createdBy.DisplayName, comment.CreatedAt,
                comment.UpdatedByEmployeeId, updatedBy.DisplayName, comment.UpdatedAt, comment.Version))
            .ToArrayAsync(cancellationToken);

        ListingCommentHistoryView[] history = await (
            from entry in db.ListingCommentHistories.AsNoTracking()
            join oldAuthor in db.Employees.AsNoTracking() on entry.OldUpdatedByEmployeeId equals oldAuthor.Id
            join changedBy in db.Employees.AsNoTracking() on entry.ChangedByEmployeeId equals changedBy.Id
            where entry.OrganizationId == effective.OrganizationId && entry.ListingId == listingId
            orderby entry.ChangedAt descending, entry.Id descending
            select new ListingCommentHistoryView(entry.Id, entry.ListingCommentId, entry.ListingId,
                entry.CommentTypeId, entry.OldText, entry.OldUpdatedByEmployeeId, oldAuthor.DisplayName,
                entry.OldCreatedAt, entry.OldUpdatedAt, entry.ChangedByEmployeeId,
                changedBy.DisplayName, entry.ChangedAt, entry.Operation))
            .ToArrayAsync(cancellationToken);

        Dictionary<Guid, IReadOnlyList<ListingCommentHistoryView>> historyByType = history
            .GroupBy(entry => entry.CommentTypeId)
            .ToDictionary(group => group.Key, group => (IReadOnlyList<ListingCommentHistoryView>)group.ToArray());
        return new(types, comments, historyByType, effective.CanProcessIncoming, canManageTypes);
    }

    public async Task<ListingCommentEditorView> SaveAsync(Subject subject, SaveListingComment command,
        string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EffectiveEmployeeAccess effective = await RequireEditAsync(subject, cancellationToken);
        string text = NormalizeOptional(command.Text, 4000, "Комментарий слишком длинный.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await RequireListingAsync(db, effective.OrganizationId, command.ListingId, cancellationToken);
        ListingCommentType type = await db.ListingCommentTypes.SingleOrDefaultAsync(value =>
                value.Id == command.CommentTypeId && value.OrganizationId == effective.OrganizationId,
                cancellationToken)
            ?? throw new ArgumentException("Вид комментария не найден.");
        ListingComment? current = await db.ListingComments.SingleOrDefaultAsync(value =>
            value.OrganizationId == effective.OrganizationId && value.ListingId == command.ListingId
            && value.CommentTypeId == command.CommentTypeId, cancellationToken);

        if (current == null)
        {
            if (command.ExpectedVersion != null)
                throw Conflict();
            if (text.Length == 0) return await ReadAsync(subject, command.ListingId, cancellationToken);
            if (!type.IsActive) throw new ArgumentException("Неактивный вид нельзя добавить заново.");
        }
        else
        {
            if (command.ExpectedVersion != current.Version) throw Conflict();
            if (text.Length > 0 && string.Equals(text, current.Text, StringComparison.Ordinal))
                return await ReadAsync(subject, command.ListingId, cancellationToken);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await SetActorContextAsync(db, effective.EmployeeId, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        string action;
        object changes;
        if (current == null)
        {
            current = new()
            {
                Id = DataConventions.NewId(), OrganizationId = effective.OrganizationId,
                ListingId = command.ListingId, CommentTypeId = command.CommentTypeId, Text = text,
                CreatedByEmployeeId = effective.EmployeeId, CreatedAt = now,
                UpdatedByEmployeeId = effective.EmployeeId, UpdatedAt = now
            };
            db.ListingComments.Add(current);
            action = "ListingCommentCreated";
            changes = new { command.CommentTypeId, Text = text };
        }
        else if (text.Length == 0)
        {
            db.ListingComments.Remove(current);
            action = "ListingCommentDeleted";
            changes = new { command.CommentTypeId, PreviousText = current.Text };
        }
        else
        {
            string previous = current.Text;
            current.Text = text;
            current.UpdatedByEmployeeId = effective.EmployeeId;
            current.UpdatedAt = now;
            action = "ListingCommentUpdated";
            changes = new { command.CommentTypeId, PreviousText = previous, Text = text };
        }

        OrganizationWorkspace.AddAudit(db, effective.OrganizationContext, subject, action,
            "CatalogItem", command.ListingId, changes, correlationId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new DbUpdateConcurrencyException("Комментарий уже изменён другим сотрудником.", exception);
        }
        return await ReadAsync(subject, command.ListingId, cancellationToken);
    }

    public async Task<ListingCommentTypeView> SaveTypeAsync(Subject subject,
        SaveListingCommentType command, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EffectiveEmployeeAccess effective = await RequireTypeManagementAsync(subject, cancellationToken);
        string name = Required(command.Name, 120, "Укажите название вида комментария.");
        string? description = Optional(command.Description, 1000);
        if (command.SortOrder is < -100000 or > 100000)
            throw new ArgumentException("Порядок должен быть от -100000 до 100000.");
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ListingCommentType item;
        string action;
        if (command.Id is Guid id)
        {
            item = await db.ListingCommentTypes.SingleOrDefaultAsync(value =>
                    value.Id == id && value.OrganizationId == effective.OrganizationId, cancellationToken)
                ?? throw Conflict();
            if (command.ExpectedVersion != item.Version) throw Conflict();
            item.Name = name;
            item.Description = description;
            item.SortOrder = command.SortOrder;
            action = "ListingCommentTypeUpdated";
        }
        else
        {
            if (command.ExpectedVersion != null) throw Conflict();
            item = new()
            {
                Id = DataConventions.NewId(), OrganizationId = effective.OrganizationId,
                Name = name, Description = description, SortOrder = command.SortOrder, IsActive = true
            };
            db.ListingCommentTypes.Add(item);
            action = "ListingCommentTypeCreated";
        }
        OrganizationWorkspace.AddAudit(db, effective.OrganizationContext, subject, action,
            "ListingCommentType", item.Id, new { item.Name, item.Description, item.SortOrder, item.IsActive }, correlationId);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException
            { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            throw new ArgumentException("Вид с таким названием уже существует или был изменён.", exception);
        }
        return View(item);
    }

    public async Task<ListingCommentTypeView> SetTypeActiveAsync(Subject subject,
        SetListingCommentTypeActive command, string correlationId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        EffectiveEmployeeAccess effective = await RequireTypeManagementAsync(subject, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        ListingCommentType item = await db.ListingCommentTypes.SingleOrDefaultAsync(value =>
                value.Id == command.Id && value.OrganizationId == effective.OrganizationId, cancellationToken)
            ?? throw Conflict();
        if (item.Version != command.ExpectedVersion) throw Conflict();
        item.IsActive = command.IsActive;
        OrganizationWorkspace.AddAudit(db, effective.OrganizationContext, subject,
            command.IsActive ? "ListingCommentTypeRestored" : "ListingCommentTypeArchived",
            "ListingCommentType", item.Id, new { item.Name, item.IsActive }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return View(item);
    }

    private async Task<EffectiveEmployeeAccess> RequireReadAsync(Subject subject,
        CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (!effective.CanReadIncoming) throw new AccessDeniedException();
        return effective;
    }

    private async Task<EffectiveEmployeeAccess> RequireEditAsync(Subject subject,
        CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await RequireReadAsync(subject, cancellationToken);
        if (!effective.CanProcessIncoming) throw new AccessDeniedException();
        return effective;
    }

    private async Task<EffectiveEmployeeAccess> RequireTypeManagementAsync(Subject subject,
        CancellationToken cancellationToken)
    {
        EffectiveEmployeeAccess effective = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (!CanManageTypes(effective)) throw new AccessDeniedException();
        return effective;
    }

    private static bool CanManageTypes(EffectiveEmployeeAccess effective) =>
        effective.IsSystemOwner || effective.IsAdministrator || effective.CanHeadProcurement;

    private static async Task RequireListingAsync(LandErpDbContext db, Guid organizationId,
        Guid listingId, CancellationToken cancellationToken)
    {
        if (!await db.Listings.AsNoTracking().AnyAsync(item => item.Id == listingId
            && item.OrganizationId == organizationId, cancellationToken))
            throw new AccessDeniedException();
    }

    private static Task<int> SetActorContextAsync(LandErpDbContext db, Guid employeeId,
        CancellationToken cancellationToken) => db.Database.ExecuteSqlInterpolatedAsync(
        $"SELECT set_config('landerp.comment_actor_employee_id', {employeeId.ToString()}, true)", cancellationToken);

    private static string Required(string? value, int maxLength, string message)
    {
        string result = value?.Trim() ?? "";
        if (result.Length == 0 || result.Length > maxLength) throw new ArgumentException(message);
        return result;
    }

    private static string NormalizeOptional(string? value, int maxLength, string message)
    {
        string result = value?.Trim() ?? "";
        if (result.Length > maxLength) throw new ArgumentException(message);
        return result;
    }

    private static string? Optional(string? value, int maxLength)
    {
        string result = value?.Trim() ?? "";
        if (result.Length > maxLength) throw new ArgumentException("Описание слишком длинное.");
        return result.Length == 0 ? null : result;
    }

    private static ListingCommentTypeView View(ListingCommentType value) => new(value.Id, value.Name,
        value.Description, value.SortOrder, value.IsActive, value.Version);

    private static DbUpdateConcurrencyException Conflict() =>
        new("Запись уже изменена или недоступна. Обновите данные.");
}
