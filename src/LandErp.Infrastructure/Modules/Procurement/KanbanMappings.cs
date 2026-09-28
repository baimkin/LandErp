using LandErp.Application.Modules.Organization.Domain;
using LandErp.Application.Modules.Procurement.Domain;
using Microsoft.EntityFrameworkCore;

namespace LandErp.Infrastructure.Modules.Procurement;

internal static class KanbanMappings
{
    public static void Apply(ModelBuilder b)
    {
        var p = b.Entity<KanbanPipeline>();
        p.ToTable("kanban_pipelines", "procurement", t => t.HasCheckConstraint("ck_kanban_pipeline_name", "length(btrim(name)) > 0"));
        p.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        p.HasOne<LandErp.Application.Modules.Organization.Domain.Organization>().WithMany().HasForeignKey(x => x.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        p.HasIndex(x => x.OrganizationId).IsUnique().HasFilter("is_default AND is_active");
        p.Property(x => x.Name).HasMaxLength(120);
        var s = b.Entity<KanbanStage>();
        s.ToTable("kanban_stages", "procurement", t => {
            t.HasCheckConstraint("ck_kanban_stage_initial", "NOT is_initial OR (is_active AND kind = 'Working')");
            t.HasCheckConstraint("ck_kanban_stage_name", "length(btrim(name)) > 0");
        });
        s.HasAlternateKey(x => new { x.OrganizationId, x.Id });
        s.HasAlternateKey(x => new { x.OrganizationId, x.PipelineId, x.Id });
        s.HasOne<KanbanPipeline>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PipelineId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        s.HasIndex(x => new { x.OrganizationId, x.PipelineId }).IsUnique().HasFilter("is_initial AND is_active");
        s.Property(x => x.Kind).HasConversion<string>().HasMaxLength(32);
        s.Property(x => x.Name).HasMaxLength(120);
        s.Property(x => x.Description).HasMaxLength(2000);
        b.Entity<PropertyCase>().HasAlternateKey(x => new { x.OrganizationId, x.Id });
        var m = b.Entity<KanbanMembership>();
        m.ToTable("kanban_memberships", "procurement");
        m.HasAlternateKey(x => new { x.OrganizationId, x.PropertyCaseId, x.Id });
        m.HasOne<PropertyCase>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyCaseId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.HasOne<KanbanStage>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PipelineId, x.StageId }).HasPrincipalKey(x => new { x.OrganizationId, x.PipelineId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        m.HasIndex(x => new { x.OrganizationId, x.PropertyCaseId, x.PipelineId }).IsUnique();
        m.HasIndex(x => new { x.OrganizationId, x.PipelineId, x.StageId, x.TransferredAt });
        var t = b.Entity<KanbanTunnel>();
        t.ToTable("kanban_tunnels", "procurement");
        t.HasOne<KanbanStage>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.SourceStageId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        t.HasOne<KanbanPipeline>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.TargetPipelineId }).HasPrincipalKey(x => new { x.OrganizationId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        t.HasIndex(x => new { x.OrganizationId, x.SourceStageId }).IsUnique().HasFilter("is_active");
        t.Property(x => x.Mode).HasConversion<string>().HasMaxLength(32);
        var h = b.Entity<KanbanTransition>();
        h.ToTable("kanban_transitions", "procurement");
        h.HasOne<KanbanMembership>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.PropertyCaseId, x.MembershipId }).HasPrincipalKey(x => new { x.OrganizationId, x.PropertyCaseId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        h.HasOne<KanbanStage>().WithMany().HasForeignKey(x => new { x.OrganizationId, x.ToPipelineId, x.ToStageId }).HasPrincipalKey(x => new { x.OrganizationId, x.PipelineId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        h.HasOne<Employee>().WithMany().HasForeignKey(x => x.ActorEmployeeId).OnDelete(DeleteBehavior.Restrict);
        h.HasOne<KanbanTunnel>().WithMany().HasForeignKey(x => x.TunnelId).OnDelete(DeleteBehavior.Restrict);
        h.HasIndex(x => new { x.OrganizationId, x.PropertyCaseId, x.RecordedAt });
        foreach (var type in new[] { typeof(KanbanPipeline), typeof(KanbanStage), typeof(KanbanMembership), typeof(KanbanTransition), typeof(KanbanTunnel) })
        {
            b.Entity(type).ToTable(tb => tb.HasComment("Канбан: пользовательские позиции и настройки, независимые от бизнес-состояния объекта."));
            foreach (var prop in b.Entity(type).Metadata.GetProperties())
                prop.SetComment("Канбан: " + prop.Name + ". Исторические факты не удаляются.");
        }
    }
}

