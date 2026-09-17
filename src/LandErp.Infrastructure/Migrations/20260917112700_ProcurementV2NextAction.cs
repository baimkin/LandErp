using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementV2NextAction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "workflow",
                table: "work_tasks",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "",
                comment: "Человекочитаемое описание или цель следующего действия без технического payload.");

            migrationBuilder.AddColumn<string>(
                name: "type",
                schema: "workflow",
                table: "work_tasks",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "General",
                comment: "Стабильный прикладной тип следующего действия; пользовательское название и цель хранятся отдельно.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "description",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "type",
                schema: "workflow",
                table: "work_tasks");
        }
    }
}
