using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Collection.Domain;
using LandErp.Application.Modules.Organization.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Collection;

internal static class CollectionMappings
{
    public static void Apply(ModelBuilder builder)
    {
        builder.Entity<CollectorAgent>().ToTable("agents", "collection");
        builder.Entity<CollectorAgent>().Property(item => item.RuntimeState).HasConversion<string>();
        builder.Entity<CollectorAgent>().Property(item => item.ActivationHash).HasMaxLength(64);
        builder.Entity<CollectorAgent>().Property(item => item.AttentionCode).HasMaxLength(64);
        builder.Entity<CollectorAgent>().HasOne<LandErp.Application.Modules.Organization.Domain.Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchConfiguration>().ToTable("search_configurations", "collection");
        builder.Entity<SearchConfiguration>().Property(item => item.Source).HasConversion<string>();
        builder.Entity<SearchConfiguration>().Property(item => item.ScheduleKind).HasConversion<string>();
        builder.Entity<SearchConfiguration>().Property(item => item.FixedTimesJson).HasColumnType("jsonb");
        builder.Entity<SearchConfiguration>().Property(item => item.ConsecutiveFailures)
            .HasComment("Число последовательных неуспешных запусков; успешный или ограниченный лимитом запуск сбрасывает счётчик.");
        builder.Entity<SearchConfiguration>().Property(item => item.Url).HasMaxLength(2000);
        // Compatibility-only columns remain physically present until final cleanup; runtime routing never reads them.
        builder.Entity<SearchConfiguration>().Property<Guid?>("AgentId");
        builder.Entity<SearchConfiguration>().Property<Guid?>("DepartmentId");
        builder.Entity<SearchConfiguration>().Property<Guid?>("TeamId");
        builder.Entity<SearchConfiguration>().HasOne<SearchGroup>().WithMany().HasForeignKey(item => item.SearchGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchConfiguration>().HasIndex(item => new { item.OrganizationId, item.NextRunAt });
        builder.Entity<SearchGroup>().ToTable("search_groups", "collection");
        builder.Entity<SearchGroup>().HasOne<LandErp.Application.Modules.Organization.Domain.Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchGroup>().HasIndex(item => new { item.OrganizationId, item.Name }).IsUnique();
        builder.Entity<SearchGroupMarketSettings>().ToTable("search_group_market_settings", "collection");
        builder.Entity<SearchGroupMarketSettings>().Property(item => item.AllowedPropertyTypes).HasColumnType("text[]");
        builder.Entity<SearchGroupMarketSettings>().Property(item => item.MinPricePerSotka).HasPrecision(19, 4);
        builder.Entity<SearchGroupMarketSettings>().Property(item => item.MaxPricePerSotka).HasPrecision(19, 4);
        builder.Entity<SearchGroupMarketSettings>().HasOne<SearchGroup>().WithOne().HasForeignKey<SearchGroupMarketSettings>(item => item.SearchGroupId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchGroupMarketSettings>().HasOne<LandErp.Application.Modules.Organization.Domain.Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchGroupMarketSettings>().HasIndex(item => new { item.OrganizationId, item.SearchGroupId }).IsUnique();
        builder.Entity<ServerCollectionJob>().ToTable("jobs", "collection");
        builder.Entity<ServerCollectionJob>().Property(item => item.State).HasConversion<string>();
        builder.Entity<ServerCollectionJob>().Property(item => item.ReasonCode).HasMaxLength(64);
        builder.Entity<ServerCollectionJob>().Property(item => item.ReasonCode).HasComment("Машинный код причины финального результата Parser.");
        builder.Entity<ServerCollectionJob>().Property(item => item.WarningsJson).HasColumnType("jsonb")
            .HasDefaultValueSql("'[]'::jsonb").HasComment("Ограниченный список машинных предупреждений финального результата.");
        builder.Entity<ServerCollectionJob>().Property(item => item.CoverageJson).HasColumnType("jsonb")
            .HasComment("Факты о полноте выдачи, переданные Parser; заявленное источником количество является подсказкой.");
        builder.Entity<ServerCollectionJob>().Property(item => item.RetryAt).HasComment("Срок дополнительной серверной попытки, независимый от обычного расписания поиска.");
        builder.Entity<ServerCollectionJob>().Property(item => item.RetryAttempt).HasComment("Номер дополнительной попытки в ограниченной цепочке восстановления.");
        builder.Entity<ServerCollectionJob>().Property(item => item.RequiresOperatorAttention).HasComment("Требуется действие оператора; ожидаемый автоматический повтор сам по себе внимания не требует.");
        builder.Entity<ServerCollectionJob>().HasOne<CollectorAgent>().WithMany().HasForeignKey(item => item.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServerCollectionJob>().HasOne<SearchConfiguration>().WithMany().HasForeignKey(item => item.SearchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServerCollectionJob>().HasOne<ServerCollectionJob>().WithMany().HasForeignKey(item => item.RetryOfJobId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServerCollectionJob>().HasIndex(item => new { item.SearchId, item.State });
        builder.Entity<ServerCollectionJob>().HasIndex(item => new { item.SearchId, item.ScheduledFor }).IsUnique().HasFilter("scheduled_for IS NOT NULL");
        builder.Entity<ServerCollectionJob>().HasIndex(item => item.RetryAt).HasFilter("retry_at IS NOT NULL");
        builder.Entity<ServerCollectionJob>().HasIndex(item => item.RetryOfJobId).IsUnique().HasFilter("retry_of_job_id IS NOT NULL");
        builder.Entity<CollectionDelivery>().ToTable("deliveries", "collection");
        builder.Entity<CollectionDelivery>().Property(item => item.ReceiptJson).HasColumnType("jsonb");
        builder.Entity<CollectionDelivery>().HasOne<ServerCollectionJob>().WithMany().HasForeignKey(item => item.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CollectionSchedulerStatus>().ToTable("scheduler_status", "collection");
        builder.Entity<CollectionSchedulerStatus>().Property(item => item.Id).ValueGeneratedNever();
        builder.Entity<CollectionSchedulerStatus>().Property(item => item.LastFailureCode).HasMaxLength(64);
        builder.Entity<Listing>().ToTable("listings", "catalog");
        builder.Entity<Listing>().Property(item => item.Source).HasConversion<string>();
        builder.Entity<Listing>().Property(item => item.IngestionKind).HasConversion<string>();
        builder.Entity<Listing>().Property(item => item.Disposition).HasConversion<string>();
        builder.Entity<Listing>().HasIndex(item => new { item.OrganizationId, item.Source, item.ExternalId }).IsUnique()
            .HasFilter("external_id IS NOT NULL");
        builder.Entity<Listing>().HasIndex(item => new { item.OrganizationId, item.ChangedAt });
        builder.Entity<Listing>().HasOne<LandErp.Application.Modules.Organization.Domain.Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Listing>().HasOne<OrgUnit>().WithMany().HasForeignKey(item => item.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Listing>().HasOne<Team>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Listing>().HasOne<Employee>().WithMany().HasForeignKey(item => item.CreatedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Listing>().Property(item => item.Url).HasMaxLength(2000);
        builder.Entity<Listing>().Property(item => item.Title).HasMaxLength(20000);
        builder.Entity<Listing>().Property(item => item.Location).HasMaxLength(20000);
        builder.Entity<Listing>().Property(item => item.Description).HasMaxLength(20000);
        builder.Entity<Listing>().Property(item => item.SellerName).HasMaxLength(20000);
        builder.Entity<Listing>().Property(item => item.Provenance).HasMaxLength(2000);
        builder.Entity<Listing>().Property(item => item.IngressComment).HasMaxLength(4000);
        builder.Entity<Listing>().Property(item => item.CadastralNumber).HasMaxLength(128);
        builder.Entity<Listing>().Property(item => item.Price).HasPrecision(19, 4);
        builder.Entity<Listing>().Property(item => item.AreaSquareMeters).HasPrecision(19, 4);
        builder.Entity<Listing>().Property(item => item.TargetTotalPrice).HasPrecision(19, 4);
        builder.Entity<Listing>().Property(item => item.TargetPricePerSotka).HasPrecision(19, 4);
        builder.Entity<Listing>().Property(item => item.LastEvaluatedPrice).HasPrecision(19, 4);
        builder.Entity<Listing>().Property(item => item.LastEvaluatedPricePerSotka).HasPrecision(19, 4);
        builder.Entity<Listing>().Property(item => item.Currency).HasMaxLength(3);
        builder.Entity<Listing>().Property(item => item.PhotosJson).HasColumnType("jsonb");
        builder.Entity<CatalogObservation>().ToTable("observations", "catalog");
        builder.Entity<CatalogObservation>().Property(item => item.PayloadJson).HasColumnType("jsonb");
        builder.Entity<CatalogObservation>().Property(item => item.ChangesJson).HasColumnType("jsonb");
        builder.Entity<CatalogObservation>().HasIndex(item => new { item.AgentId, item.ObservationKey }).IsUnique();
        builder.Entity<CatalogObservation>().HasIndex(item => new { item.ListingId, item.ObservedAt, item.ContentHash }).IsUnique();
        builder.Entity<CatalogObservation>().HasOne<Listing>().WithMany().HasForeignKey(item => item.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogObservation>().HasOne<ServerCollectionJob>().WithMany().HasForeignKey(item => item.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogObservation>().HasOne<CollectorAgent>().WithMany().HasForeignKey(item => item.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogDuplicateCandidate>().ToTable("duplicate_candidates", "catalog");
        builder.Entity<CatalogDuplicateCandidate>().Property(item => item.Status).HasConversion<string>();
        builder.Entity<CatalogDuplicateCandidate>().Property(item => item.ReasonsJson).HasColumnType("jsonb");
        builder.Entity<CatalogDuplicateCandidate>().HasIndex(item => item.ListingId);
        builder.Entity<CatalogDuplicateCandidate>().HasIndex(item => item.CandidateListingId);
        builder.Entity<CatalogDuplicateCandidate>().HasIndex(item => item.ReviewedByEmployeeId);
        builder.Entity<CatalogDuplicateCandidate>().HasIndex(item => new { item.OrganizationId, item.ListingId, item.CandidateListingId }).IsUnique();
        builder.Entity<CatalogDuplicateCandidate>().HasIndex(item => new { item.OrganizationId, item.Status, item.RecordedAt });
        builder.Entity<CatalogDuplicateCandidate>().HasOne<Listing>().WithMany().HasForeignKey(item => item.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogDuplicateCandidate>().HasOne<Listing>().WithMany().HasForeignKey(item => item.CandidateListingId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogDuplicateCandidate>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ReviewedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogDuplicateCandidate>().HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogPhotoFingerprint>().ToTable("photo_fingerprints", "catalog");
        builder.Entity<CatalogPhotoFingerprint>().Property(item => item.Status).HasConversion<string>();
        builder.Entity<CatalogPhotoFingerprint>().Property(item => item.UrlHash).HasMaxLength(64);
        builder.Entity<CatalogPhotoFingerprint>().HasIndex(item => new { item.ListingId, item.UrlHash }).IsUnique();
        builder.Entity<CatalogPhotoFingerprint>().HasIndex(item => new { item.OrganizationId, item.Status, item.RetryAt });
        builder.Entity<CatalogPhotoFingerprint>().HasIndex(item => new { item.OrganizationId, item.PerceptualHash })
            .HasFilter("perceptual_hash IS NOT NULL");
        builder.Entity<CatalogPhotoFingerprint>().HasOne<Listing>().WithMany().HasForeignKey(item => item.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogPhotoFingerprint>().HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CatalogDuplicateSettings>().ToTable("duplicate_settings", "catalog");
        builder.Entity<CatalogDuplicateSettings>().HasKey(item => item.OrganizationId);
        builder.Entity<CatalogDuplicateSettings>().HasOne<Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CatalogEvent>().ToTable("events", "catalog");
        builder.Entity<CatalogEvent>().Property(item => item.Kind).HasConversion<string>();
        builder.Entity<CatalogEvent>().Property(item => item.Message).HasMaxLength(4000);
        builder.Entity<CatalogEvent>().Property(item => item.ObservedPrice).HasPrecision(19, 4);
        builder.Entity<CatalogEvent>().Property(item => item.ObservedPricePerSotka).HasPrecision(19, 4);
        builder.Entity<CatalogEvent>().HasIndex(item => new { item.CatalogItemId, item.RecordedAt });
        builder.Entity<CatalogEvent>().HasOne<Listing>().WithMany().HasForeignKey(item => item.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CatalogEvent>().HasOne<LandErp.Application.Modules.Organization.Domain.Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}
