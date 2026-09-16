using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

/// <summary>Makes Incoming saved-filter grouping optional; groups are organization metadata, not filter semantics.</summary>
[DbContext(typeof(LandErpDbContext))]
[Migration("20260916174500_IncomingFilterPresetsOptionalGroup")]
public sealed class IncomingFilterPresetsOptionalGroup : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<Guid>(
            name: "search_group_id",
            schema: "catalog",
            table: "incoming_filter_presets",
            type: "uuid",
            nullable: true,
            comment: "Необязательная группа для визуальной организации сохранённого фильтра.",
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldComment: "Группа поиска, в рамках которой доступен фильтр.");

        migrationBuilder.CreateIndex(
            name: "ix_incoming_filter_presets_organization_name_ungrouped_active",
            schema: "catalog",
            table: "incoming_filter_presets",
            columns: new[] { "organization_id", "name" },
            unique: true,
            filter: "active AND search_group_id IS NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_incoming_filter_presets_organization_name_ungrouped_active",
            schema: "catalog",
            table: "incoming_filter_presets");

        migrationBuilder.Sql("DELETE FROM catalog.incoming_filter_presets WHERE search_group_id IS NULL;");

        migrationBuilder.AlterColumn<Guid>(
            name: "search_group_id",
            schema: "catalog",
            table: "incoming_filter_presets",
            type: "uuid",
            nullable: false,
            comment: "Группа поиска, в рамках которой доступен фильтр.",
            oldClrType: typeof(Guid),
            oldType: "uuid",
            oldNullable: true,
            oldComment: "Необязательная группа для визуальной организации сохранённого фильтра.");
    }
}
