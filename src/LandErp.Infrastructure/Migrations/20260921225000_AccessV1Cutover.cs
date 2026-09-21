using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260921225000_AccessV1Cutover")]
public partial class AccessV1Cutover : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO identity.employee_access_settings (
                employee_id,
                incoming_access,
                procurement_access,
                procurement_read_scope,
                procurement_work_scope,
                collection_access,
                can_assign_inspections,
                can_perform_inspections,
                can_confirm_purchase,
                can_manage_templates,
                can_read_audit,
                version)
            SELECT
                employee.id,
                CASE
                    WHEN EXISTS (
                        SELECT 1 FROM identity.role_permissions grant_row
                        WHERE grant_row.role_id = assignment.role_id
                          AND grant_row.permission_id = 'manager_decisions.create') THEN 'Process'
                    WHEN EXISTS (
                        SELECT 1 FROM identity.role_permissions grant_row
                        WHERE grant_row.role_id = assignment.role_id
                          AND grant_row.permission_id = 'manager_queue.read') THEN 'Read'
                    ELSE 'None'
                END,
                CASE
                    WHEN EXISTS (
                        SELECT 1 FROM identity.role_permissions grant_row
                        WHERE grant_row.role_id = assignment.role_id
                          AND grant_row.permission_id = 'procurement_approvals.decide') THEN 'Head'
                    WHEN EXISTS (
                        SELECT 1 FROM identity.role_permissions grant_row
                        WHERE grant_row.role_id = assignment.role_id
                          AND grant_row.permission_id = 'manager_decisions.create') THEN 'Manager'
                    WHEN EXISTS (
                        SELECT 1 FROM identity.role_permissions grant_row
                        WHERE grant_row.role_id = assignment.role_id
                          AND grant_row.permission_id = 'manager_queue.read') THEN 'Read'
                    ELSE 'None'
                END,
                assignment.scope,
                assignment.scope,
                CASE
                    WHEN EXISTS (
                        SELECT 1 FROM identity.role_permissions grant_row
                        WHERE grant_row.role_id = assignment.role_id
                          AND grant_row.permission_id IN ('searches.manage', 'agents.manage')) THEN 'Manage'
                    WHEN EXISTS (
                        SELECT 1 FROM identity.role_permissions grant_row
                        WHERE grant_row.role_id = assignment.role_id
                          AND grant_row.permission_id = 'collection.read') THEN 'Read'
                    ELSE 'None'
                END,
                EXISTS (
                    SELECT 1 FROM identity.role_permissions grant_row
                    WHERE grant_row.role_id = assignment.role_id
                      AND grant_row.permission_id = 'inspections.request'),
                EXISTS (
                    SELECT 1 FROM identity.role_permissions grant_row
                    WHERE grant_row.role_id = assignment.role_id
                      AND grant_row.permission_id = 'inspections.perform'),
                EXISTS (
                    SELECT 1 FROM identity.role_permissions grant_row
                    WHERE grant_row.role_id = assignment.role_id
                      AND grant_row.permission_id = 'procurement_purchase.confirm'),
                EXISTS (
                    SELECT 1 FROM identity.role_permissions grant_row
                    WHERE grant_row.role_id = assignment.role_id
                      AND grant_row.permission_id IN ('manager_decisions.create', 'procurement_approvals.decide')),
                EXISTS (
                    SELECT 1 FROM identity.role_permissions grant_row
                    WHERE grant_row.role_id = assignment.role_id
                      AND grant_row.permission_id = 'audit.read'),
                1
            FROM organization.employees employee
            JOIN organization.employee_assignments assignment ON assignment.employee_id = employee.id
            JOIN identity.roles role_row ON role_row.id = assignment.role_id
            WHERE role_row.name <> 'Owner'
              AND NOT EXISTS (
                  SELECT 1
                  FROM identity.employee_access_settings existing
                  WHERE existing.employee_id = employee.id);
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Irreversible data cutover: deleting generated rows could also delete settings
        // that were legitimately edited after AP-04. Rollback requires a pre-cutover backup.
    }
}
