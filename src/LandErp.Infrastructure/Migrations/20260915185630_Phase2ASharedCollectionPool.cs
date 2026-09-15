using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase2ASharedCollectionPool : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_search_configurations_agent_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropForeignKey(
                name: "fk_search_configurations_department_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropForeignKey(
                name: "fk_search_configurations_team_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropIndex(
                name: "ix_search_configurations_agent_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropIndex(
                name: "ix_search_configurations_department_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropIndex(
                name: "ix_search_configurations_team_id",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.AlterTable(
                name: "search_configurations",
                schema: "collection",
                comment: "Поисковые ссылки организации без назначения конкретного Collector или области закупки. Browser settings и cookies остаются локально.",
                oldComment: "Разрешённые поиски организации с конкретным Collector и областью закупки. Browser settings и cookies остаются локально.");

            migrationBuilder.AlterTable(
                name: "jobs",
                schema: "collection",
                comment: "Работа общего пула: Pending без исполнителя; Agent назначается при claim. Lease fencing и terminal executor сохраняют фактическую историю.",
                oldComment: "Одна работа сбора: Pending ожидает; Leased закреплена до срока; Completed/LimitReached завершена; AwaitingManualAction требует ручного действия; Failed/Interrupted не считаются пустым успехом.");

            migrationBuilder.AlterColumn<Guid>(
                name: "team_id",
                schema: "collection",
                table: "search_configurations",
                type: "uuid",
                nullable: true,
                comment: "Устаревшее поле прежней маршрутизации; новый runtime его не читает и не заполняет.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Рабочая команда для Team scope. Отсутствие команды не расширяет область доступа.");

            migrationBuilder.AlterColumn<Guid>(
                name: "department_id",
                schema: "collection",
                table: "search_configurations",
                type: "uuid",
                nullable: true,
                comment: "Устаревшее поле прежней маршрутизации; новый runtime его не читает и не заполняет.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Подразделение закупки, ограничивающее Department visibility объекта.");

            migrationBuilder.AlterColumn<Guid>(
                name: "agent_id",
                schema: "collection",
                table: "search_configurations",
                type: "uuid",
                nullable: true,
                comment: "Устаревшее поле прежней маршрутизации; новый runtime его не читает и не заполняет.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение.");

            migrationBuilder.AlterColumn<Guid>(
                name: "agent_id",
                schema: "collection",
                table: "jobs",
                type: "uuid",
                nullable: true,
                comment: "Фактический исполнитель работы; отсутствует у Pending и устанавливается атомарно при claim.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "search_configurations",
                schema: "collection",
                comment: "Разрешённые поиски организации с конкретным Collector и областью закупки. Browser settings и cookies остаются локально.",
                oldComment: "Поисковые ссылки организации без назначения конкретного Collector или области закупки. Browser settings и cookies остаются локально.");

            migrationBuilder.AlterTable(
                name: "jobs",
                schema: "collection",
                comment: "Одна работа сбора: Pending ожидает; Leased закреплена до срока; Completed/LimitReached завершена; AwaitingManualAction требует ручного действия; Failed/Interrupted не считаются пустым успехом.",
                oldComment: "Работа общего пула: Pending без исполнителя; Agent назначается при claim. Lease fencing и terminal executor сохраняют фактическую историю.");

            migrationBuilder.AlterColumn<Guid>(
                name: "team_id",
                schema: "collection",
                table: "search_configurations",
                type: "uuid",
                nullable: true,
                comment: "Рабочая команда для Team scope. Отсутствие команды не расширяет область доступа.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Устаревшее поле прежней маршрутизации; новый runtime его не читает и не заполняет.");

            migrationBuilder.AlterColumn<Guid>(
                name: "department_id",
                schema: "collection",
                table: "search_configurations",
                type: "uuid",
                nullable: true,
                comment: "Подразделение закупки, ограничивающее Department visibility объекта.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Устаревшее поле прежней маршрутизации; новый runtime его не читает и не заполняет.");

            migrationBuilder.AlterColumn<Guid>(
                name: "agent_id",
                schema: "collection",
                table: "search_configurations",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Устаревшее поле прежней маршрутизации; новый runtime его не читает и не заполняет.");

            migrationBuilder.AlterColumn<Guid>(
                name: "agent_id",
                schema: "collection",
                table: "jobs",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                comment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true,
                oldComment: "Фактический исполнитель работы; отсутствует у Pending и устанавливается атомарно при claim.");

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_agent_id",
                schema: "collection",
                table: "search_configurations",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_department_id",
                schema: "collection",
                table: "search_configurations",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_team_id",
                schema: "collection",
                table: "search_configurations",
                column: "team_id");

            migrationBuilder.AddForeignKey(
                name: "fk_search_configurations_agent_id",
                schema: "collection",
                table: "search_configurations",
                column: "agent_id",
                principalSchema: "collection",
                principalTable: "agents",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_search_configurations_department_id",
                schema: "collection",
                table: "search_configurations",
                column: "department_id",
                principalSchema: "organization",
                principalTable: "org_units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_search_configurations_team_id",
                schema: "collection",
                table: "search_configurations",
                column: "team_id",
                principalSchema: "organization",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
