using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase3IncomingCatalog : Migration
    {
        private static readonly string[] EventIndexColumns = ["catalog_item_id", "recorded_at"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "disposition",
                schema: "catalog",
                table: "listings",
                type: "text",
                nullable: false,
                comment: "Текущее решение первичного отбора: входящее, мониторинг, в работе, отклонено, дубль, фейк, снято или продано.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Текущее решение первичного отбора: входящее, мониторинг, в работе, отклонено, снято или продано.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "attention_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC момент последнего сигнала внимания к входящему элементу.");

            migrationBuilder.AddColumn<bool>(
                name: "attention_required",
                schema: "catalog",
                table: "listings",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Явный признак, что входящий элемент требует повторного внимания из-за новых данных или выполненного условия мониторинга.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_evaluated_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC момент последней оценки текущих source values против условий мониторинга.");

            migrationBuilder.AddColumn<decimal>(
                name: "last_evaluated_price",
                schema: "catalog",
                table: "listings",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Последняя общая цена источника, использованная при оценке условий мониторинга.");

            migrationBuilder.AddColumn<decimal>(
                name: "last_evaluated_price_per_sotka",
                schema: "catalog",
                table: "listings",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Последняя вычисленная цена за сотку, использованная при оценке условий мониторинга.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "monitoring_started_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC момент установки текущих условий мониторинга цены.");

            migrationBuilder.AddColumn<decimal>(
                name: "target_price_per_sotka",
                schema: "catalog",
                table: "listings",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Независимый порог цены за сотку для мониторинга; заполненные пороги применяются по правилу ИЛИ.");

            migrationBuilder.AddColumn<decimal>(
                name: "target_total_price",
                schema: "catalog",
                table: "listings",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Независимый порог общей цены для мониторинга; null означает, что условие не задано.");

            migrationBuilder.CreateTable(
                name: "events",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Входящий элемент Catalog, к которому относится неизменяемое событие внимания."),
                    kind = table.Column<string>(type: "text", nullable: false, comment: "Стабильный тип события: изменение источника, мониторинг, классификация или возобновление кейса."),
                    message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false, comment: "Человекочитаемое объяснение события без секретов и raw payload источника."),
                    observed_price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true, comment: "Общая цена источника в момент события, если была известна."),
                    observed_price_per_sotka = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true, comment: "Вычисленная цена за сотку в момент события, если цена и площадь были известны."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_events_catalog_item_id",
                        column: x => x.catalog_item_id,
                        principalSchema: "catalog",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_events_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемая бизнес-история внимания к входящему предложению: изменения источника, мониторинг цены, классификация и возобновление кейса.");

            migrationBuilder.CreateIndex(
                name: "ix_events_catalog_item_id_recorded_at",
                schema: "catalog",
                table: "events",
                columns: EventIndexColumns);

            migrationBuilder.CreateIndex(
                name: "ix_events_organization_id",
                schema: "catalog",
                table: "events",
                column: "organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "events",
                schema: "catalog");

            migrationBuilder.DropColumn(
                name: "attention_at",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "attention_required",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "last_evaluated_at",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "last_evaluated_price",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "last_evaluated_price_per_sotka",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "monitoring_started_at",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "target_price_per_sotka",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "target_total_price",
                schema: "catalog",
                table: "listings");

            migrationBuilder.AlterColumn<string>(
                name: "disposition",
                schema: "catalog",
                table: "listings",
                type: "text",
                nullable: false,
                comment: "Текущее решение первичного отбора: входящее, мониторинг, в работе, отклонено, снято или продано.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Текущее решение первичного отбора: входящее, мониторинг, в работе, отклонено, дубль, фейк, снято или продано.");
        }
    }
}
