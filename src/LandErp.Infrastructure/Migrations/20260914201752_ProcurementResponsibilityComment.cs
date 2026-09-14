using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementResponsibilityComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "manager_employee_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: false,
                comment: "Менеджер, ответственный за первичный анализ объекта; получатель возврата руководителя по умолчанию.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Руководитель сотрудника, которому можно передать рабочую ответственность.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "manager_employee_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: false,
                comment: "Руководитель сотрудника, которому можно передать рабочую ответственность.",
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldComment: "Менеджер, ответственный за первичный анализ объекта; получатель возврата руководителя по умолчанию.");
        }
    }
}
