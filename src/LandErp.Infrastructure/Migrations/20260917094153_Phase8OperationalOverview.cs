using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase8OperationalOverview : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "search_group_market_settings",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    search_group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_days = table.Column<int>(type: "integer", nullable: false, comment: "Период наблюдений группы в днях для расчёта рыночной медианы и средней цены за сотку."),
                    allowed_property_types = table.Column<string[]>(type: "text[]", nullable: false, comment: "Разрешённые source-derived типы участков для статистики группы; не юридическая категория или подтверждённый ВРИ."),
                    min_price_per_sotka = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true, comment: "Опциональная включительная нижняя граница цены за сотку для статистики конкретной группы."),
                    max_price_per_sotka = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true, comment: "Опциональная включительная верхняя граница цены за сотку для статистики конкретной группы."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_group_market_settings", x => x.id);
                    table.ForeignKey(
                        name: "fk_search_group_market_settings_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_search_group_market_settings_search_group_id",
                        column: x => x.search_group_id,
                        principalSchema: "collection",
                        principalTable: "search_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Индивидуальные параметры серверного расчёта рыночной цены для одной группы поиска; не являются общей конфигурацией Dashboard.");

            migrationBuilder.CreateIndex(
                name: "ix_search_group_market_settings_organization_id_search_group_id",
                schema: "collection",
                table: "search_group_market_settings",
                columns: ["organization_id", "search_group_id"],
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_search_group_market_settings_search_group_id",
                schema: "collection",
                table: "search_group_market_settings",
                column: "search_group_id",
                unique: true);

            migrationBuilder.Sql("""
                INSERT INTO collection.search_group_market_settings
                    (id, organization_id, search_group_id, period_days, allowed_property_types, version)
                SELECT gen_random_uuid(), organization_id, id, 30,
                    ARRAY['Izhs','Snt','Dnp','Lph','Gardening','Kfh','Industrial','Other']::text[], 1
                FROM collection.search_groups;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "search_group_market_settings",
                schema: "collection");
        }
    }
}
