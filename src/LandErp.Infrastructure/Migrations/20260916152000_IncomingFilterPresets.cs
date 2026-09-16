using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace LandErp.Infrastructure.Migrations;

/// <summary>Forward migration for organization/search-group scoped Incoming filter presets.</summary>
[DbContext(typeof(LandErpDbContext))]
[Migration("20260916152000_IncomingFilterPresets")]
public sealed class IncomingFilterPresets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "incoming_filter_presets",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Идентификатор сохранённого фильтра Incoming."),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец фильтра."),
                search_group_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Группа поиска, в рамках которой доступен фильтр."),
                name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false, comment: "Отображаемое название сохранённого фильтра."),
                criteria_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Версионированное состояние фильтра Incoming без свободного текстового поиска."),
                sort_order = table.Column<int>(type: "integer", nullable: false, comment: "Порядок отображения фильтра внутри группы поиска."),
                active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true, comment: "Признак доступности фильтра; удаление выполняется деактивацией."),
                version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 1L, comment: "Версия для optimistic concurrency при переименовании и удалении.")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_incoming_filter_presets", x => x.id);
                table.ForeignKey(
                    name: "fk_incoming_filter_presets_organizations_organization_id",
                    column: x => x.organization_id,
                    principalSchema: "organization",
                    principalTable: "organizations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_incoming_filter_presets_search_groups_search_group_id",
                    column: x => x.search_group_id,
                    principalSchema: "collection",
                    principalTable: "search_groups",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            },
            comment: "Именованные server-side пресеты фильтров Incoming, привязанные к организации и группе поиска.");

        migrationBuilder.CreateIndex(
            name: "ix_incoming_filter_presets_organization_group_sort",
            schema: "catalog",
            table: "incoming_filter_presets",
            columns: new[] { "organization_id", "search_group_id", "sort_order" });

        migrationBuilder.CreateIndex(
            name: "ix_incoming_filter_presets_organization_group_name_active",
            schema: "catalog",
            table: "incoming_filter_presets",
            columns: new[] { "organization_id", "search_group_id", "name" },
            unique: true,
            filter: "active");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "incoming_filter_presets", schema: "catalog");
    }
}
