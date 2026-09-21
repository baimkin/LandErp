using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260921193000_CollectorCatalogEnrichment")]
public partial class CollectorCatalogEnrichment : Migration
{
    private static readonly string[] ListingContactIdentityColumns = ["listing_id", "type", "normalized_value"];
    private static readonly string[] OrganizationContactColumns = ["organization_id", "type", "normalized_value"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "source_published_at",
            schema: "catalog",
            table: "listings",
            type: "timestamp with time zone",
            nullable: true,
            comment: "UTC момент публикации/добавления на площадке, если источник отдал надёжное значение; не заменяет FirstObservedAt.");

        migrationBuilder.AddColumn<decimal>(
            name: "latitude",
            schema: "catalog",
            table: "listings",
            type: "numeric(9,6)",
            precision: 9,
            scale: 6,
            nullable: true,
            comment: "Широта WGS84 (EPSG:4326), если источник отдал координаты и они нормализованы адаптером.");

        migrationBuilder.AddColumn<decimal>(
            name: "longitude",
            schema: "catalog",
            table: "listings",
            type: "numeric(9,6)",
            precision: 9,
            scale: 6,
            nullable: true,
            comment: "Долгота WGS84 (EPSG:4326), если источник отдал координаты и они нормализованы адаптером.");

        migrationBuilder.AddColumn<string[]>(
            name: "declared_land_types",
            schema: "catalog",
            table: "listings",
            type: "text[]",
            nullable: true,
            comment: "Типы участка, структурированно заявленные площадкой; не юридически подтверждённый ВРИ или категория земли.");

        migrationBuilder.Sql("UPDATE catalog.listings SET declared_land_types = ARRAY[]::text[] WHERE declared_land_types IS NULL;");

        migrationBuilder.AlterColumn<string[]>(
            name: "declared_land_types",
            schema: "catalog",
            table: "listings",
            type: "text[]",
            nullable: false,
            comment: "Типы участка, структурированно заявленные площадкой; не юридически подтверждённый ВРИ или категория земли.",
            oldClrType: typeof(string[]),
            oldType: "text[]",
            oldNullable: true,
            oldComment: "Типы участка, структурированно заявленные площадкой; не юридически подтверждённый ВРИ или категория земли.");

        migrationBuilder.CreateTable(
            name: "listing_contacts",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false,
                    comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "text", nullable: false,
                    comment: "Тип публичного контакта: телефон, email, Telegram, WhatsApp, сайт или другое."),
                value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false,
                    comment: "Публичное значение контакта, наблюдаемое в объявлении; не credential и не секрет."),
                normalized_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false,
                    comment: "Нормализованное значение контакта для дедупликации внутри объявления; исходное отображение хранится отдельно."),
                display_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false,
                    comment: "Человекочитаемое отображение публичного контакта в том виде, в котором его удобно показать пользователю."),
                source = table.Column<string>(type: "text", nullable: false,
                    comment: "Источник объявления, в котором наблюдался контакт."),
                is_primary = table.Column<bool>(type: "boolean", nullable: false,
                    comment: "Признак основного контакта в последнем наблюдении, где этот контакт присутствовал."),
                first_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false,
                    comment: "Самый ранний известный UTC момент наблюдения этого объявления."),
                last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false,
                    comment: "Самый новый принятый UTC момент наблюдения для обновления current state.")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_listing_contacts", x => x.id);
                table.ForeignKey(
                    name: "fk_listing_contacts_listing_id",
                    column: x => x.listing_id,
                    principalSchema: "catalog",
                    principalTable: "listings",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "fk_listing_contacts_organization_id",
                    column: x => x.organization_id,
                    principalSchema: "organization",
                    principalTable: "organizations",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            },
            comment: "Наблюдаемые публичные контакты конкретного объявления. Не являются общей CRM-карточкой продавца и не удаляются только из-за отсутствия в следующем снимке.");

        migrationBuilder.CreateIndex(
            name: "ix_listing_contacts_listing_id_type_normalized_value",
            schema: "catalog",
            table: "listing_contacts",
            columns: ListingContactIdentityColumns,
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_listing_contacts_organization_id_type_normalized_value",
            schema: "catalog",
            table: "listing_contacts",
            columns: OrganizationContactColumns);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "listing_contacts", schema: "catalog");
        migrationBuilder.DropColumn(name: "source_published_at", schema: "catalog", table: "listings");
        migrationBuilder.DropColumn(name: "latitude", schema: "catalog", table: "listings");
        migrationBuilder.DropColumn(name: "longitude", schema: "catalog", table: "listings");
        migrationBuilder.DropColumn(name: "declared_land_types", schema: "catalog", table: "listings");
    }
}
