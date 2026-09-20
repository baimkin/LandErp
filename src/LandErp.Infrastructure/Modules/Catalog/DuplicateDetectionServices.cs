using LandErp.Application.Modules.Catalog.Contracts;
using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.IdentityAccess.Contracts;
using LandErp.Infrastructure.Modules.Organization;
using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Catalog;

internal readonly record struct DuplicateDetectionSettingsValues(
    int CandidateThreshold,
    int DescriptionSimilarityPercent,
    int AreaTolerancePercent,
    int PhotoHammingDistance,
    int StrongPhotoMatches,
    int CommonPhotoMaxListings)
{
    public static DuplicateDetectionSettingsValues Default => new(55, 55, 15, 8, 2, 20);
}

public sealed class DuplicateDetectionSettingsService(
    IDbContextFactory<LandErpDbContext> factory,
    IAccessControl access,
    TimeProvider time) : IDuplicateDetectionSettingsService
{
    public async Task<DuplicateDetectionSettingsView> ReadAsync(Subject subject, CancellationToken cancellationToken)
    {
        AccessContext context = await access.RequireAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        CatalogDuplicateSettings? row = await db.CatalogDuplicateSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == context.OrganizationId, cancellationToken);
        return row == null ? DefaultView() : View(row);
    }

    public async Task<DuplicateDetectionSettingsView> SaveAsync(Subject subject,
        UpdateDuplicateDetectionSettings command, string correlationId, CancellationToken cancellationToken)
    {
        Validate(command);
        AccessContext context = await access.RequireAsync(subject, Permissions.OrganizationManage, cancellationToken);
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        _ = (await db.Organizations.FromSqlInterpolated(
            $"SELECT * FROM organization.organizations WHERE id={context.OrganizationId} FOR UPDATE")
            .ToListAsync(cancellationToken)).SingleOrDefault() ?? throw new AccessDeniedException();
        CatalogDuplicateSettings? row = await db.CatalogDuplicateSettings
            .SingleOrDefaultAsync(item => item.OrganizationId == context.OrganizationId, cancellationToken);

        if (row == null)
        {
            if (command.ExpectedVersion != 0) throw new DbUpdateConcurrencyException();
            row = new()
            {
                OrganizationId = context.OrganizationId,
                CandidateThreshold = command.CandidateThreshold,
                DescriptionSimilarityPercent = command.DescriptionSimilarityPercent,
                AreaTolerancePercent = command.AreaTolerancePercent,
                PhotoHammingDistance = command.PhotoHammingDistance,
                StrongPhotoMatches = command.StrongPhotoMatches,
                CommonPhotoMaxListings = command.CommonPhotoMaxListings,
                UpdatedAt = time.GetUtcNow()
            };
            db.CatalogDuplicateSettings.Add(row);
        }
        else
        {
            if (row.Version != command.ExpectedVersion) throw new DbUpdateConcurrencyException();
            if (Same(row, command))
            {
                await transaction.CommitAsync(cancellationToken);
                return View(row);
            }
            row.CandidateThreshold = command.CandidateThreshold;
            row.DescriptionSimilarityPercent = command.DescriptionSimilarityPercent;
            row.AreaTolerancePercent = command.AreaTolerancePercent;
            row.PhotoHammingDistance = command.PhotoHammingDistance;
            row.StrongPhotoMatches = command.StrongPhotoMatches;
            row.CommonPhotoMaxListings = command.CommonPhotoMaxListings;
            row.UpdatedAt = time.GetUtcNow();
        }

        OrganizationWorkspace.AddAudit(db, context, subject, "CatalogDuplicateSettingsChanged", "Organization",
            context.OrganizationId, new
            {
                command.CandidateThreshold,
                command.DescriptionSimilarityPercent,
                command.AreaTolerancePercent,
                command.PhotoHammingDistance,
                command.StrongPhotoMatches,
                command.CommonPhotoMaxListings
            }, correlationId);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return View(row);
    }

    internal static async Task<DuplicateDetectionSettingsValues> ReadValuesAsync(
        LandErpDbContext db, Guid organizationId, CancellationToken cancellationToken)
    {
        CatalogDuplicateSettings? row = await db.CatalogDuplicateSettings.AsNoTracking()
            .SingleOrDefaultAsync(item => item.OrganizationId == organizationId, cancellationToken);
        return row == null
            ? DuplicateDetectionSettingsValues.Default
            : new(row.CandidateThreshold, row.DescriptionSimilarityPercent, row.AreaTolerancePercent,
                row.PhotoHammingDistance, row.StrongPhotoMatches, row.CommonPhotoMaxListings);
    }

    private static DuplicateDetectionSettingsView DefaultView()
    {
        DuplicateDetectionSettingsValues value = DuplicateDetectionSettingsValues.Default;
        return new(value.CandidateThreshold, value.DescriptionSimilarityPercent, value.AreaTolerancePercent,
            value.PhotoHammingDistance, value.StrongPhotoMatches, value.CommonPhotoMaxListings, 0);
    }

    private static DuplicateDetectionSettingsView View(CatalogDuplicateSettings row) => new(
        row.CandidateThreshold, row.DescriptionSimilarityPercent, row.AreaTolerancePercent,
        row.PhotoHammingDistance, row.StrongPhotoMatches, row.CommonPhotoMaxListings, row.Version);

    private static bool Same(CatalogDuplicateSettings row, UpdateDuplicateDetectionSettings command) =>
        row.CandidateThreshold == command.CandidateThreshold
        && row.DescriptionSimilarityPercent == command.DescriptionSimilarityPercent
        && row.AreaTolerancePercent == command.AreaTolerancePercent
        && row.PhotoHammingDistance == command.PhotoHammingDistance
        && row.StrongPhotoMatches == command.StrongPhotoMatches
        && row.CommonPhotoMaxListings == command.CommonPhotoMaxListings;

    private static void Validate(UpdateDuplicateDetectionSettings command)
    {
        if (command.CandidateThreshold is < 40 or > 100)
            throw new ArgumentException("Общий порог кандидата должен быть от 40 до 100.");
        if (command.DescriptionSimilarityPercent is < 40 or > 90)
            throw new ArgumentException("Порог похожести описания должен быть от 40% до 90%.");
        if (command.AreaTolerancePercent is < 1 or > 30)
            throw new ArgumentException("Допуск площади должен быть от 1% до 30%.");
        if (command.PhotoHammingDistance is < 0 or > 20)
            throw new ArgumentException("Чувствительность сравнения фото должна быть от 0 до 20.");
        if (command.StrongPhotoMatches is < 1 or > 5)
            throw new ArgumentException("Сильное совпадение фото должно требовать от 1 до 5 фотографий.");
        if (command.CommonPhotoMaxListings is < 3 or > 100)
            throw new ArgumentException("Порог типовой картинки должен быть от 3 до 100 объявлений.");
    }
}

public sealed class IncomingDuplicateMatchingMaintenance(
    IDbContextFactory<LandErpDbContext> factory,
    TimeProvider time) : IIncomingDuplicateMatchingMaintenance
{
    public async Task RefreshAsync(Guid catalogItemId, CancellationToken cancellationToken)
    {
        await using LandErpDbContext db = await factory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        Listing? listing = await db.Listings.SingleOrDefaultAsync(item => item.Id == catalogItemId, cancellationToken);
        if (listing == null) return;
        await IncomingDuplicateDetector.RefreshAsync(db, listing, time.GetUtcNow(), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
