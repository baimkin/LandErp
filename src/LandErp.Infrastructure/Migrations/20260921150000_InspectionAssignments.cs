using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260921150000_InspectionAssignments")]
public partial class InspectionAssignments : Migration
{
    private static readonly string[] InspectorDueColumns = ["inspector_employee_id", "due_at"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            COMMENT ON TABLE procurement.site_inspections IS
            'Полевой осмотр конкретного PropertyCase: назначение исполнителя, срок, snapshot чек-листа, черновик и факт завершения.';
            """);

        migrationBuilder.Sql("""
            COMMENT ON TABLE procurement.case_attachments IS
            'Связи вложений с PropertyCase, переговорами, проверками, осмотром или его пунктом; материалы осмотра доступны также назначенному осмотрщику.';
            """);

        migrationBuilder.DropIndex(
            name: "ix_site_inspections_inspector_employee_id",
            schema: "procurement",
            table: "site_inspections");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "started_at",
            schema: "procurement",
            table: "site_inspections",
            type: "timestamp with time zone",
            nullable: true,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "due_at",
            schema: "procurement",
            table: "site_inspections",
            type: "timestamp with time zone",
            nullable: true,
            comment: "Опциональный UTC срок полевого осмотра.");

        migrationBuilder.AddColumn<string>(
            name: "instructions",
            schema: "procurement",
            table: "site_inspections",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: false,
            defaultValue: "",
            comment: "Краткая цель и указания для полевого осмотра без предоставления осмотрщику прав на решение по закупке.");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "requested_at",
            schema: "procurement",
            table: "site_inspections",
            type: "timestamp with time zone",
            nullable: true,
            comment: "UTC момент последнего назначения или переназначения осмотра.");

        migrationBuilder.AddColumn<Guid>(
            name: "requested_by_employee_id",
            schema: "procurement",
            table: "site_inspections",
            type: "uuid",
            nullable: true,
            comment: "Сотрудник закупки, назначивший полевой осмотр; не становится исполнителем осмотра автоматически.");

        migrationBuilder.CreateIndex(
            name: "ix_site_inspections_inspector_employee_id_due_at",
            schema: "procurement",
            table: "site_inspections",
            columns: InspectorDueColumns);

        migrationBuilder.CreateIndex(
            name: "ix_site_inspections_requested_by_employee_id",
            schema: "procurement",
            table: "site_inspections",
            column: "requested_by_employee_id");

        migrationBuilder.AddForeignKey(
            name: "fk_site_inspections_requested_by_employee_id",
            schema: "procurement",
            table: "site_inspections",
            column: "requested_by_employee_id",
            principalSchema: "organization",
            principalTable: "employees",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            COMMENT ON TABLE procurement.site_inspections IS
            'Полевой осмотр конкретного PropertyCase: черновик, общий вывод, решение и факт завершения.';
            """);

        migrationBuilder.Sql("""
            COMMENT ON TABLE procurement.case_attachments IS
            'Связи вложений с PropertyCase, переговорами, проверками, осмотром или его пунктом; доступ всегда наследуется от owning PropertyCase.';
            """);

        migrationBuilder.DropForeignKey(
            name: "fk_site_inspections_requested_by_employee_id",
            schema: "procurement",
            table: "site_inspections");

        migrationBuilder.DropIndex(
            name: "ix_site_inspections_inspector_employee_id_due_at",
            schema: "procurement",
            table: "site_inspections");

        migrationBuilder.DropIndex(
            name: "ix_site_inspections_requested_by_employee_id",
            schema: "procurement",
            table: "site_inspections");

        migrationBuilder.Sql("""
            UPDATE procurement.site_inspections
            SET started_at = COALESCE(started_at, requested_at, CURRENT_TIMESTAMP)
            WHERE started_at IS NULL;
            """);

        migrationBuilder.DropColumn(name: "due_at", schema: "procurement", table: "site_inspections");
        migrationBuilder.DropColumn(name: "instructions", schema: "procurement", table: "site_inspections");
        migrationBuilder.DropColumn(name: "requested_at", schema: "procurement", table: "site_inspections");
        migrationBuilder.DropColumn(name: "requested_by_employee_id", schema: "procurement", table: "site_inspections");

        migrationBuilder.AlterColumn<DateTimeOffset>(
            name: "started_at",
            schema: "procurement",
            table: "site_inspections",
            type: "timestamp with time zone",
            nullable: false,
            oldClrType: typeof(DateTimeOffset),
            oldType: "timestamp with time zone",
            oldNullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_site_inspections_inspector_employee_id",
            schema: "procurement",
            table: "site_inspections",
            column: "inspector_employee_id");
    }
}
