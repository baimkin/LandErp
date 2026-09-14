using LandErp.Application.Modules.Catalog.Domain;
using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Procurement.Domain;
using LandErp.Application.Modules.Workflow.Domain;
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
            new WorkflowStage { Id = "returned", Name = "Возвращён менеджеру" }, new WorkflowStage { Id = "approved", Name = "Дальнейшая работа одобрена" });
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
        builder.Entity<PropertyCase>().HasIndex(item => item.ListingId).IsUnique();
        builder.Entity<PropertyCase>().HasIndex(item => item.BusinessNumber).IsUnique();
        builder.Entity<PropertyCase>().HasOne<Listing>().WithMany().HasForeignKey(item => item.ListingId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<WorkflowStage>().WithMany().HasForeignKey(item => item.StageId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<Employee>().WithMany().HasForeignKey(item => item.ManagerEmployeeId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<Assignment>().WithMany().HasForeignKey(item => item.AssignmentId).OnDelete(DeleteBehavior.Restrict);
        builder.Entity<PropertyCase>().HasOne<WorkTask>().WithMany().HasForeignKey(item => item.WorkTaskId).OnDelete(DeleteBehavior.Restrict);
    }
}
