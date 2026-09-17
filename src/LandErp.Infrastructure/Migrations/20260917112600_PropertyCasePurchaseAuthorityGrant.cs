using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(LandErpDbContext))]
[Migration("20260917112600_PropertyCasePurchaseAuthorityGrant")]
public sealed class PropertyCasePurchaseAuthorityGrant : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            INSERT INTO identity.permissions (id, description)
            VALUES ('procurement_purchase.confirm', 'Подтверждение и исправление факта покупки объекта')
            ON CONFLICT (id) DO NOTHING;

            INSERT INTO identity.role_permissions (role_id, permission_id)
            SELECT id, 'procurement_purchase.confirm'
            FROM identity.roles
            WHERE normalized_name IN ('OWNER', 'PROCUREMENTHEAD')
            ON CONFLICT (role_id, permission_id) DO NOTHING;
            """);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DELETE FROM identity.role_permissions WHERE permission_id = 'procurement_purchase.confirm';
            DELETE FROM identity.permissions WHERE id = 'procurement_purchase.confirm';
            """);
    }
}
