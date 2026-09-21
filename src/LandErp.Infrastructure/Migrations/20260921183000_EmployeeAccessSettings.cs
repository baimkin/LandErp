using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260921183000_EmployeeAccessSettings")]
public partial class EmployeeAccessSettings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "employee_access_settings",
            schema: "identity",
            columns: table => new
            {
                employee_id = table.Column<Guid>(type: "uuid", nullable: false,
                    comment: "Сотрудник, для которого явно сохранены настройки Access V1; одновременно PK и FK на organization.employees."),
                incoming_access = table.Column<string>(type: "text", nullable: false,
                    comment: "Уровень доступа к входящим предложениям: None, Read или Process."),
                procurement_access = table.Column<string>(type: "text", nullable: false,
                    comment: "Уровень возможности в закупке: None, Read, Manager или Head. Области просмотра и работы задаются отдельно."),
                procurement_read_scope = table.Column<string>(type: "text", nullable: false,
                    comment: "Максимальная область просмотра закупки; не даёт права изменять объект сама по себе."),
                procurement_work_scope = table.Column<string>(type: "text", nullable: false,
                    comment: "Максимальная область рабочих действий закупки; не может быть шире ProcurementReadScope."),
                collection_access = table.Column<string>(type: "text", nullable: false,
                    comment: "Уровень доступа к поискам и Parser: None, Read или Manage."),
                can_assign_inspections = table.Column<bool>(type: "boolean", nullable: false,
                    comment: "Разрешено назначать полевые осмотры; само по себе не расширяет область просмотра закупки."),
                can_perform_inspections = table.Column<bool>(type: "boolean", nullable: false,
                    comment: "Разрешено выполнять назначенные полевые осмотры без общего доступа к закупке."),
                can_confirm_purchase = table.Column<bool>(type: "boolean", nullable: false,
                    comment: "Разрешено фиксировать факт покупки как отдельная возможность, независимо от должности."),
                can_manage_templates = table.Column<bool>(type: "boolean", nullable: false,
                    comment: "Разрешено изменять шаблоны проверок и осмотров как отдельная возможность."),
                can_read_audit = table.Column<bool>(type: "boolean", nullable: false,
                    comment: "Разрешено читать аудит организации как отдельная возможность."),
                version = table.Column<long>(type: "bigint", nullable: false,
                    comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_employee_access_settings", x => x.employee_id);
                table.ForeignKey(
                    name: "fk_employee_access_settings_employee_id",
                    column: x => x.employee_id,
                    principalSchema: "organization",
                    principalTable: "employees",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            },
            comment: "Явные настройки возможностей сотрудника Access V1. Отсутствие строки означает временную совместимость со старой permission-моделью до переключения AP-02.");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "employee_access_settings", schema: "identity");
    }
}
