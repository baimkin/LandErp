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
        builder.Entity<CollectorAgent>().HasOne<LandErp.Application.Modules.Organization.Domain.Organization>().WithMany().HasForeignKey(item => item.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchConfiguration>().ToTable("search_configurations", "collection");
        builder.Entity<SearchConfiguration>().Property(item => item.Source).HasConversion<string>();
        builder.Entity<SearchConfiguration>().Property(item => item.Url).HasMaxLength(2000);
        builder.Entity<SearchConfiguration>().HasOne<CollectorAgent>().WithMany().HasForeignKey(item => item.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchConfiguration>().HasOne<OrgUnit>().WithMany().HasForeignKey(item => item.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<SearchConfiguration>().HasOne<Team>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServerCollectionJob>().ToTable("jobs", "collection");
        builder.Entity<ServerCollectionJob>().Property(item => item.State).HasConversion<string>();
        builder.Entity<ServerCollectionJob>().HasOne<CollectorAgent>().WithMany().HasForeignKey(item => item.AgentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServerCollectionJob>().HasOne<SearchConfiguration>().WithMany().HasForeignKey(item => item.SearchId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<ServerCollectionJob>().HasIndex(item => new { item.SearchId, item.State });
        builder.Entity<CollectionDelivery>().ToTable("deliveries", "collection");
        builder.Entity<CollectionDelivery>().Property(item => item.ReceiptJson).HasColumnType("jsonb");
        builder.Entity<CollectionDelivery>().HasOne<ServerCollectionJob>().WithMany().HasForeignKey(item => item.JobId).OnDelete(DeleteBehavior.Restrict);
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
    }
}
