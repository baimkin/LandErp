using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260921143000_CatalogObjectGroups")]
public partial class CatalogObjectGroups : Migration
{
    private static readonly string[] OrganizationUpdatedColumns = ["organization_id", "updated_at"];
    private static readonly string[] OrganizationGroupColumns = ["organization_id", "object_group_id"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "object_groups",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false,
                    comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false,
                    comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false,
                    comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_object_groups", x => x.id);
                table.ForeignKey(
                    name: "fk_object_groups_organization_id",
                    column: x => x.organization_id,
                    principalSchema: "organization",
                    principalTable: "organizations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            },
            comment: "Подтверждённые группы объявлений одного физического объекта. Группа не имеет главного объявления и не заменяет PropertyCase.");

        migrationBuilder.CreateIndex(
            name: "ix_object_groups_organization_id_updated_at",
            schema: "catalog",
            table: "object_groups",
            columns: OrganizationUpdatedColumns);

        migrationBuilder.AddColumn<Guid>(
            name: "object_group_id",
            schema: "catalog",
            table: "listings",
            type: "uuid",
            nullable: true,
            comment: "Опциональная связь объявления с подтверждённой группой одного физического объекта; null означает самостоятельное объявление.");

        migrationBuilder.CreateIndex(
            name: "ix_listings_object_group_id",
            schema: "catalog",
            table: "listings",
            column: "object_group_id");

        migrationBuilder.CreateIndex(
            name: "ix_listings_organization_id_object_group_id",
            schema: "catalog",
            table: "listings",
            columns: OrganizationGroupColumns,
            filter: "object_group_id IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "fk_listings_object_group_id",
            schema: "catalog",
            table: "listings",
            column: "object_group_id",
            principalSchema: "catalog",
            principalTable: "object_groups",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "fk_listings_object_group_id",
            schema: "catalog",
            table: "listings");

        migrationBuilder.DropIndex(
            name: "ix_listings_object_group_id",
            schema: "catalog",
            table: "listings");

        migrationBuilder.DropIndex(
            name: "ix_listings_organization_id_object_group_id",
            schema: "catalog",
            table: "listings");

        migrationBuilder.DropColumn(
            name: "object_group_id",
            schema: "catalog",
            table: "listings");

        migrationBuilder.DropTable(
            name: "object_groups",
            schema: "catalog");
    }
}
