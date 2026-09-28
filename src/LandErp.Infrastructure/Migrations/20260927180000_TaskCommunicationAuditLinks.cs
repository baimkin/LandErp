using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
namespace LandErp.Infrastructure.Migrations;
[DbContext(typeof(LandErpDbContext))]
[Migration("20260927180000_TaskCommunicationAuditLinks")]
public sealed class TaskCommunicationAuditLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Only explicit IDs from the atomic creation audit establish legacy relationships.
        // No inference from text, dates or task completion state.
        migrationBuilder.Sql("""
            UPDATE workflow.work_tasks t SET source_negotiation_id = n.id
            FROM foundation.audit_events a
            JOIN procurement.negotiations n ON n.id::text = a.changes->>'NegotiationId'
                AND n.organization_id = a.organization_id AND n.property_case_id = a.object_id
            WHERE a.action = 'CaseCommunicationRecorded' AND a.object_type = 'PropertyCase'
                AND t.id::text = a.changes#>>'{Task,Id}'
                AND t.organization_id = a.organization_id AND t.object_id = a.object_id
                AND t.object_type = 'PropertyCase' AND t.source_negotiation_id IS NULL;
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Preserve known business relationships on rollback. The earlier schema migration owns the column.
    }
}
