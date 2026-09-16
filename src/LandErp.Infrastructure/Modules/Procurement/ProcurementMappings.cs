using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
using LandErp.Application.Foundation.Files;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

internal static class ProcurementMappings
{
    public static void Apply(ModelBuilder builder)
    {
        builder.HasSequence<long>("property_case_numbers", "procurement").StartsAt(1);
        builder.Entity<WorkflowStage>().ToTable("stages", "workflow");
        builder.Entity<WorkflowStage>().HasData(
            new WorkflowStage { Id = "new", Name = "Новое объявление" }, new WorkflowStage { Id = "analysis", Name = "Первичный анализ" },
            new WorkflowStage { Id = "clarify", Name = "Уточнить" }, new WorkflowStage { Id = "monitor", Name = "Наблюдать" },
            new WorkflowStage { Id = "rejected", Name = "Отклонён" }, new WorkflowStage { Id = "pending_head", Name = "У руководителя" },
            new WorkflowStage { Id = "returned", Name = "Возвращён менеджеру" }, new WorkflowStage { Id = "approved", Name = "Дальнейшая работа одобрена" },
            new WorkflowStage { Id = "negotiation", Name = "Переговоры и проверки" });
        builder.Entity<Assignment>().ToTable("assignments", "workflow");
        builder.Entity<Assignment>().HasIndex(item => new { item.ObjectType, item.ObjectId }).IsUnique();
        builder.Entity<Assignment>().HasOne<Employee>().WithMany().HasForeignKey(item => item.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkTask>().ToTable("work_tasks", "workflow");
        builder.Entity<WorkTask>().HasIndex(item => new { item.EmployeeId, item.Completed, item.DueAt });
        builder.Entity<WorkTask>().HasOne<Employee>().WithMany().HasForeignKey(item => item.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkflowTransition>().ToTable("transitions", "workflow");
        builder.Entity<WorkflowTransition>().HasOne<WorkflowStage>().WithMany().HasForeignKey(item => item.FromStageId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<WorkflowTransition>().HasOne<WorkflowStage>().WithMany().HasForeignKey(item => item.ToStageId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Approval>().ToTable("approvals", "workflow");
        builder.Entity<Approval>().Property(item => item.Reason).HasMaxLength(4000);
        builder.Entity<Approval>().HasOne<Employee>().WithMany().HasForeignKey(item => item.RequesterEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<Approval>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ApproverEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BusinessTimelineEntry>().ToTable("business_timeline", "foundation");
        builder.Entity<BusinessTimelineEntry>().Property(item => item.Body).HasMaxLength(10000);
        builder.Entity<BusinessTimelineEntry>().HasIndex(item => new { item.OrganizationId, item.ObjectType, item.ObjectId, item.RecordedAt });
        builder.Entity<BusinessTimelineEntry>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<BusinessTimelineEntry>().HasOne<Employee>().WithMany().HasForeignKey(item => item.TargetEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<InternalNotification>().ToTable("notifications", "foundation");
        builder.Entity<InternalNotification>().HasIndex(item => new { item.EmployeeId, item.ReadAt });
        builder.Entity<InternalNotification>().HasOne<Employee>().WithMany().HasForeignKey(item => item.EmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().ToTable("property_cases", "procurement");
        builder.Entity<PropertyCase>().HasIndex(item => item.ListingId).IsUnique().HasFilter("listing_id IS NOT NULL");
        builder.Entity<PropertyCase>().HasIndex(item => item.BusinessNumber).IsUnique();
        builder.Entity<PropertyCase>().HasOne<Listing>().WithMany().HasForeignKey(item => item.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<OrgUnit>().WithMany().HasForeignKey(item => item.DepartmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<Team>().WithMany().HasForeignKey(item => item.TeamId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<WorkflowStage>().WithMany().HasForeignKey(item => item.StageId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<Assignment>().WithMany().HasForeignKey(item => item.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<WorkTask>().WithMany().HasForeignKey(item => item.WorkTaskId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().Property(item => item.WorkingTitle).HasMaxLength(20000);
        builder.Entity<PropertyCase>().Property(item => item.WorkingLocation).HasMaxLength(20000);
        builder.Entity<PropertyCase>().Property(item => item.CadastralNumber).HasMaxLength(128);
        builder.Entity<PropertyCase>().Property(item => item.WorkingPrice).HasPrecision(19, 4);
        builder.Entity<PropertyCase>().Property(item => item.WorkingAreaSquareMeters).HasPrecision(19, 4);
        builder.Entity<PropertyCase>().Property(item => item.Currency).HasMaxLength(3);
        builder.Entity<PropertyCase>().Property(item => item.FactsProvenance).HasMaxLength(1000);
        builder.Entity<PropertyCaseSourceLink>().ToTable("property_case_source_links", "procurement");
        builder.Entity<PropertyCaseSourceLink>().HasIndex(item => item.CatalogItemId).IsUnique().HasFilter("confirmed");
        builder.Entity<PropertyCaseSourceLink>().HasIndex(item => new { item.PropertyCaseId, item.CatalogItemId }).IsUnique();
        builder.Entity<PropertyCaseSourceLink>().HasOne<PropertyCase>().WithMany().HasForeignKey(item => item.PropertyCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCaseSourceLink>().HasOne<Listing>().WithMany().HasForeignKey(item => item.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCaseSourceLink>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CaseNegotiation>().ToTable("negotiations", "procurement");
        builder.Entity<CaseNegotiation>().Property(item => item.PriceType).HasConversion<string>();
        builder.Entity<CaseNegotiation>().Property(item => item.Amount).HasPrecision(19, 4);
        builder.Entity<CaseNegotiation>().Property(item => item.Currency).HasMaxLength(3);
        builder.Entity<CaseNegotiation>().Property(item => item.Channel).HasMaxLength(128);
        builder.Entity<CaseNegotiation>().Property(item => item.Contact).HasMaxLength(512);
        builder.Entity<CaseNegotiation>().Property(item => item.Outcome).HasMaxLength(1000);
        builder.Entity<CaseNegotiation>().Property(item => item.Conditions).HasMaxLength(4000);
        builder.Entity<CaseNegotiation>().Property(item => item.Comment).HasMaxLength(4000);
        builder.Entity<CaseNegotiation>().Property(item => item.NextStep).HasMaxLength(1000);
        builder.Entity<CaseNegotiation>().HasIndex(item => new { item.PropertyCaseId, item.EffectiveAt });
        builder.Entity<CaseNegotiation>().HasOne<PropertyCase>().WithMany().HasForeignKey(item => item.PropertyCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CaseNegotiation>().HasOne<Employee>().WithMany().HasForeignKey(item => item.AuthorEmployeeId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CaseCheck>().ToTable("case_checks", "procurement");
        builder.Entity<CaseCheck>().Property(item => item.Level).HasConversion<string>();
        builder.Entity<CaseCheck>().Property(item => item.Status).HasConversion<string>();
        builder.Entity<CaseCheck>().Property(item => item.Title).HasMaxLength(512);
        builder.Entity<CaseCheck>().Property(item => item.Result).HasMaxLength(4000);
        builder.Entity<CaseCheck>().Property(item => item.Cost).HasPrecision(19, 4);
        builder.Entity<CaseCheck>().Property(item => item.Currency).HasMaxLength(3);
        builder.Entity<CaseCheck>().HasIndex(item => new { item.PropertyCaseId, item.Level, item.Status });
        builder.Entity<CaseCheck>().HasOne<PropertyCase>().WithMany().HasForeignKey(item => item.PropertyCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CaseCheck>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ResponsibleEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CaseCheck>().HasOne<Employee>().WithMany().HasForeignKey(item => item.AuthorEmployeeId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<StoredFile>().ToTable("stored_files", "foundation", table => table.HasCheckConstraint(
            "ck_stored_files_reference", "(storage_key IS NOT NULL AND external_url IS NULL) OR (storage_key IS NULL AND external_url IS NOT NULL) OR status IN ('PendingUpload', 'UploadFailed')"));
        builder.Entity<StoredFile>().Property(item => item.Status).HasConversion<string>();
        builder.Entity<StoredFile>().Property(item => item.OriginalName).HasMaxLength(512);
        builder.Entity<StoredFile>().Property(item => item.ContentType).HasMaxLength(256);
        builder.Entity<StoredFile>().Property(item => item.StorageKey).HasMaxLength(512);
        builder.Entity<StoredFile>().Property(item => item.ExternalUrl).HasMaxLength(2000);
        builder.Entity<StoredFile>().Property(item => item.Sha256).HasMaxLength(64);
        builder.Entity<StoredFile>().HasIndex(item => new { item.OrganizationId, item.Status, item.RecordedAt });
        builder.Entity<StoredFile>().HasOne<Employee>().WithMany().HasForeignKey(item => item.CreatedByEmployeeId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<CaseAttachment>().ToTable("case_attachments", "procurement", table => table.HasCheckConstraint(
            "ck_case_attachments_owner", "(owner_type = 'Case' AND negotiation_id IS NULL AND check_id IS NULL) OR (owner_type = 'Negotiation' AND negotiation_id IS NOT NULL AND check_id IS NULL) OR (owner_type = 'Check' AND negotiation_id IS NULL AND check_id IS NOT NULL)"));
        builder.Entity<CaseAttachment>().Property(item => item.OwnerType).HasConversion<string>();
        builder.Entity<CaseAttachment>().Property(item => item.Kind).HasConversion<string>();
        builder.Entity<CaseAttachment>().Property(item => item.Label).HasMaxLength(512);
        builder.Entity<CaseAttachment>().HasIndex(item => new { item.PropertyCaseId, item.RecordedAt });
        builder.Entity<CaseAttachment>().HasOne<PropertyCase>().WithMany().HasForeignKey(item => item.PropertyCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CaseAttachment>().HasOne<StoredFile>().WithMany().HasForeignKey(item => item.StoredFileId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CaseAttachment>().HasOne<CaseNegotiation>().WithMany().HasForeignKey(item => item.NegotiationId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CaseAttachment>().HasOne<CaseCheck>().WithMany().HasForeignKey(item => item.CheckId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<CaseAttachment>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);

        builder.Entity<PropertyCaseFactRevision>().ToTable("case_fact_revisions", "procurement");
        builder.Entity<PropertyCaseFactRevision>().Property(item => item.Field).HasConversion<string>();
        builder.Entity<PropertyCaseFactRevision>().Property(item => item.Value).HasMaxLength(20000);
        builder.Entity<PropertyCaseFactRevision>().HasIndex(item => new { item.PropertyCaseId, item.Field, item.RecordedAt });
        builder.Entity<PropertyCaseFactRevision>().HasOne<PropertyCase>().WithMany().HasForeignKey(item => item.PropertyCaseId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCaseFactRevision>().HasOne<Listing>().WithMany().HasForeignKey(item => item.CatalogItemId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCaseFactRevision>().HasOne<Employee>().WithMany().HasForeignKey(item => item.VerifiedByEmployeeId).OnDelete(DeleteBehavior.Restrict);
    }
}
