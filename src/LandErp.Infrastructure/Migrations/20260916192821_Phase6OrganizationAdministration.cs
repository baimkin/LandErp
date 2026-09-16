using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

public partial class Phase6OrganizationAdministration : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(name: "must_change_password", schema: "identity", table: "users",
            type: "boolean", nullable: false, defaultValue: false,
            comment: "Требует сменить выданный администратором временный пароль до обычной работы в ERP.");

        AddOrganizationColumns(migrationBuilder, "org_units", includeManager: true, includeVersion: false);
        AddOrganizationColumns(migrationBuilder, "positions", includeManager: false, includeVersion: false);
        AddOrganizationColumns(migrationBuilder, "teams", includeManager: true, includeVersion: true);

        migrationBuilder.CreateIndex(name: "ix_org_units_manager_employee_id", schema: "organization",
            table: "org_units", column: "manager_employee_id");
        migrationBuilder.CreateIndex(name: "ix_teams_manager_employee_id", schema: "organization",
            table: "teams", column: "manager_employee_id");
        migrationBuilder.AddForeignKey(name: "fk_org_units_manager_employee_id", schema: "organization",
            table: "org_units", column: "manager_employee_id", principalSchema: "organization",
            principalTable: "employees", principalColumn: "id", onDelete: ReferentialAction.Restrict);
        migrationBuilder.AddForeignKey(name: "fk_teams_manager_employee_id", schema: "organization",
            table: "teams", column: "manager_employee_id", principalSchema: "organization",
            principalTable: "employees", principalColumn: "id", onDelete: ReferentialAction.Restrict);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(name: "fk_org_units_manager_employee_id", schema: "organization", table: "org_units");
        migrationBuilder.DropForeignKey(name: "fk_teams_manager_employee_id", schema: "organization", table: "teams");
        migrationBuilder.DropIndex(name: "ix_org_units_manager_employee_id", schema: "organization", table: "org_units");
        migrationBuilder.DropIndex(name: "ix_teams_manager_employee_id", schema: "organization", table: "teams");
        migrationBuilder.DropColumn(name: "must_change_password", schema: "identity", table: "users");
        DropOrganizationColumns(migrationBuilder, "org_units", includeManager: true, includeVersion: false);
        DropOrganizationColumns(migrationBuilder, "positions", includeManager: false, includeVersion: false);
        DropOrganizationColumns(migrationBuilder, "teams", includeManager: true, includeVersion: true);
    }

    private static void AddOrganizationColumns(MigrationBuilder migrationBuilder, string table, bool includeManager, bool includeVersion)
    {
        migrationBuilder.AddColumn<bool>(name: "active", schema: "organization", table: table,
            type: "boolean", nullable: false, defaultValue: true,
            comment: "Активный элемент доступен для новых назначений; архивный сохраняется в истории и может быть восстановлен.");
        migrationBuilder.AddColumn<string>(name: "description", schema: "organization", table: table,
            type: "character varying(1000)", maxLength: 1000, nullable: false, defaultValue: "");
        if (includeManager)
            migrationBuilder.AddColumn<Guid>(name: "manager_employee_id", schema: "organization", table: table,
                type: "uuid", nullable: true,
                comment: "Назначенный руководитель подразделения или команды; права доступа определяются отдельно ролью и scope.");
        if (includeVersion)
            migrationBuilder.AddColumn<long>(name: "version", schema: "organization", table: table,
                type: "bigint", nullable: false, defaultValue: 1L,
                comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.");
    }

    private static void DropOrganizationColumns(MigrationBuilder migrationBuilder, string table, bool includeManager, bool includeVersion)
    {
        migrationBuilder.DropColumn(name: "active", schema: "organization", table: table);
        migrationBuilder.DropColumn(name: "description", schema: "organization", table: table);
        if (includeManager) migrationBuilder.DropColumn(name: "manager_employee_id", schema: "organization", table: table);
        if (includeVersion) migrationBuilder.DropColumn(name: "version", schema: "organization", table: table);
    }
}
