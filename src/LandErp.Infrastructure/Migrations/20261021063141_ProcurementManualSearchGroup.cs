using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementManualSearchGroup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "manual_search_group_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: true,
                comment: "Явная группа поиска объекта закупки. NULL: группы определяются по подтверждённым источникам. Архивирование группы не снимает назначение. Не влияет на состав медианы.");

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_manual_search_group_id",
                schema: "procurement",
                table: "property_cases",
                column: "manual_search_group_id");

            migrationBuilder.AddForeignKey(
                name: "fk_property_cases_manual_search_group_id",
                schema: "procurement",
                table: "property_cases",
                column: "manual_search_group_id",
                principalSchema: "collection",
                principalTable: "search_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_property_cases_manual_search_group_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropIndex(
                name: "ix_property_cases_manual_search_group_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "manual_search_group_id",
                schema: "procurement",
                table: "property_cases");
        }
    }
}
