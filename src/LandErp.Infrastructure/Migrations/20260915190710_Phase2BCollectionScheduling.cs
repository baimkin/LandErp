using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated migration metadata uses constant column arrays.

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2BCollectionScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fixed_times_json",
                schema: "collection",
                table: "search_configurations",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<int>(
                name: "interval_minutes",
                schema: "collection",
                table: "search_configurations",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_run_at",
                schema: "collection",
                table: "search_configurations",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Следующий расчётный запуск UTC; отсутствует у ручного или приостановленного поиска.");

            migrationBuilder.AddColumn<string>(
                name: "schedule_kind",
                schema: "collection",
                table: "search_configurations",
                type: "text",
                nullable: false,
                defaultValue: "Manual");

            migrationBuilder.AddColumn<Guid>(
                name: "search_group_id",
                schema: "collection",
                table: "search_configurations",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "changed_listings_count",
                schema: "collection",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "new_listings_count",
                schema: "collection",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "processed_count",
                schema: "collection",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "scheduled_for",
                schema: "collection",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Расчётный момент запуска UTC; обеспечивает идемпотентность планировщика.");

            migrationBuilder.CreateTable(
                name: "search_groups",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, comment: "Разрешена ли работа сотрудника. При выключении существующая cookie не обходит серверную проверку."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_groups", x => x.id);
                    table.ForeignKey(
                        name: "fk_search_groups_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Тонкая организационная группировка поисков без владения маршрутизацией, исполнителем или бизнес-процессом.");

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_organization_id_next_run_at",
                schema: "collection",
                table: "search_configurations",
                columns: new[] { "organization_id", "next_run_at" });

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_search_group_id",
                schema: "collection",
                table: "search_configurations",
                column: "search_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_search_id_scheduled_for",
                schema: "collection",
                table: "jobs",
                columns: new[] { "search_id", "scheduled_for" },
                unique: true,
                filter: "scheduled_for IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_search_groups_organization_id_name",
                schema: "collection",
                table: "search_groups",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_search_configurations_search_group_id",
                schema: "collection",
                table: "search_configurations",
                column: "search_group_id",
                principalSchema: "collection",
                principalTable: "search_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_search_configurations_search_group_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropTable(
                name: "search_groups",
                schema: "collection");

            migrationBuilder.DropIndex(
                name: "ix_search_configurations_organization_id_next_run_at",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropIndex(
                name: "ix_search_configurations_search_group_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropIndex(
                name: "ix_jobs_search_id_scheduled_for",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "fixed_times_json",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropColumn(
                name: "interval_minutes",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropColumn(
                name: "next_run_at",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropColumn(
                name: "schedule_kind",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropColumn(
                name: "search_group_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropColumn(
                name: "changed_listings_count",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "new_listings_count",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "processed_count",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "scheduled_for",
                schema: "collection",
                table: "jobs");
        }
    }
}
