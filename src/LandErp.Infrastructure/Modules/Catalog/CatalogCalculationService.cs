using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

public sealed class CatalogCalculationService(IDbContextFactory<LandErpDbContext> factory,
    IEmployeeAccessService employeeAccess, IIncomingFilterPresetService presets, TimeProvider time)
    : ICatalogCalculationService
{
    private async Task<Guid> RequireAsync(Subject subject, CancellationToken cancellationToken)
    {
        var access = await employeeAccess.ResolveAsync(subject, cancellationToken);
        if (!access.CanProcessIncoming) throw new AccessDeniedException();
        return access.OrganizationContext.OrganizationId;
    }

    public async Task<CatalogCalculationResult> SetAsync(Subject subject, CatalogCalculationTarget target,
        bool include, string correlationId, CancellationToken cancellationToken)
    {
        Guid organizationId = await RequireAsync(subject, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        Listing item = await db.Listings.SingleOrDefaultAsync(item => item.Id == target.Id
            && item.OrganizationId == organizationId, cancellationToken) ?? throw new AccessDeniedException();
        if (item.Version != target.ExpectedVersion) throw Conflict();
        var result = Summarize([item], include);
        if (result.Changed > 0) Change(db, item, include, subject.UserId, "Ручное действие", correlationId, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<CatalogCalculationPreview> PreviewAsync(Subject subject, CatalogCalculationSelection selection,
        bool include, CancellationToken cancellationToken)
    {
        Guid organizationId = await RequireAsync(subject, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        Listing[] items = await SelectAsync(db, subject, organizationId, selection, cancellationToken);
        var result = Summarize(items, include);
        return new(Stamp(items, include), result.Total, result.Changed, result.AlreadySet, result.Ineligible, result.Reasons);
    }

    public async Task<CatalogCalculationResult> ApplyAsync(Subject subject, CatalogCalculationSelection selection,
        bool include, string expectedStamp, string correlationId, CancellationToken cancellationToken)
    {
        Guid organizationId = await RequireAsync(subject, cancellationToken);
        await using var db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Listing[] selected = await SelectAsync(db, subject, organizationId, selection, cancellationToken);
        Guid[] ids = selected.Select(item => item.Id).ToArray();
        // Lock in stable order, then compare the actual versions with the preview. Never overwrite
        // a concurrent user decision, even when the cohort has the same size as before.
        Listing[] items = ids.Length == 0 ? [] : (await db.Listings.FromSqlInterpolated(
            $"SELECT * FROM catalog.listings WHERE organization_id={organizationId} AND id=ANY({ids}) ORDER BY id FOR UPDATE")
            .ToArrayAsync(cancellationToken));
        if (Stamp(items, include) != expectedStamp) throw Conflict();
        var result = Summarize(items, include);
        foreach (Listing item in items)
            if (item.IncludeInCalculation != include && (!include || CatalogCalculationEligibility.Reason(item) == null))
                Change(db, item, include, subject.UserId, "Массовое действие", correlationId, time.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private async Task<Listing[]> SelectAsync(LandErpDbContext db, Subject subject, Guid organizationId,
        CatalogCalculationSelection selection, CancellationToken cancellationToken)
    {
        var saved = selection.Filter.WorkingScope == null || selection.Filter.WorkingScope.Mode is IncomingCatalogMode.Archive or IncomingCatalogMode.Participants
            ? Array.Empty<IncomingFilterPresetView>() : await presets.ReadAsync(subject, cancellationToken);
        var prepared = await IncomingCatalogSelection.PrepareAsync(db, organizationId, time.GetUtcNow(),
            selection.Filter, saved, cancellationToken);
        var query = db.Listings.AsNoTracking().Where(item => item.OrganizationId == organizationId).Where(prepared.Current);
        if (selection.Selected is { } targets)
        {
            Guid[] ids = targets.Select(item => item.Id).Distinct().ToArray();
            if (ids.Length != targets.Count) throw new ArgumentException("Объявление выбрано несколько раз.");
            var items = await query.Where(item => ids.Contains(item.Id)).ToArrayAsync(cancellationToken);
            if (items.Length != targets.Count) throw Conflict();
            var versions = targets.ToDictionary(item => item.Id, item => item.ExpectedVersion);
            if (items.Any(item => item.Version != versions[item.Id])) throw Conflict();
            return items;
        }
        return await query.ToArrayAsync(cancellationToken);
    }

    private static CatalogCalculationResult Summarize(Listing[] items, bool include)
    {
        int changed = 0, already = 0;
        Dictionary<string, int> reasons = [];
        foreach (Listing item in items)
        {
            // The saved choice survives temporary data loss. A repeated include is already set,
            // while an explicit exclude remains available regardless of data eligibility.
            if (item.IncludeInCalculation == include) { already++; continue; }
            string? reason = include ? CatalogCalculationEligibility.Reason(item) : null;
            if (reason != null) reasons[reason] = reasons.GetValueOrDefault(reason) + 1;
            else changed++;
        }
        return new(items.Length, changed, already, reasons.Values.Sum(), reasons);
    }

    private static string Stamp(Listing[] items, bool include) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes((include ? "include:" : "exclude:") + string.Join(';', items.OrderBy(item => item.Id)
            .Select(item => item.Id.ToString("N") + ":" + item.Version.ToString(CultureInfo.InvariantCulture))))));

    private static DbUpdateConcurrencyException Conflict() => new(
        "Объявления или состав отбора изменились. Обновите список и проверьте количество заново.");

    internal static void Change(LandErpDbContext db, Listing item, bool include, Guid? actorId,
        string reason, string correlationId, DateTimeOffset now)
    {
        if (item.IncludeInCalculation == include) return;
        bool previous = item.IncludeInCalculation;
        item.IncludeInCalculation = include;
        db.CatalogEvents.Add(new()
        {
            Id = Guid.CreateVersion7(), OrganizationId = item.OrganizationId, CatalogItemId = item.Id,
            Kind = CatalogEventKind.CalculationParticipationChanged,
            Message = $"{(include ? "Включено в расчёт" : "Исключено из расчёта")}: {reason}", RecordedAt = now
        });
        db.AuditEvents.Add(new()
        {
            Id = Guid.CreateVersion7(), OrganizationId = item.OrganizationId, ActorId = actorId ?? Guid.Empty,
            Action = "CatalogCalculationParticipationChanged", ObjectType = "CatalogItem", ObjectId = item.Id,
            Changes = JsonSerializer.Serialize(new { Previous = previous, Current = include, Reason = reason }),
            RecordedAt = now, CorrelationId = correlationId.Length <= 64 ? correlationId : Guid.CreateVersion7().ToString()
        });
    }
}
