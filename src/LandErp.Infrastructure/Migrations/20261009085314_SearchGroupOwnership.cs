using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SearchGroupOwnership : Migration
    {
        private static readonly string[] SearchGroupOwnerColumns = ["organization_id", "owner_employee_id"];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "owner_employee_id",
                schema: "collection",
                table: "search_groups",
                type: "uuid",
                nullable: true,
                comment: "Сотрудник-владелец зоны; NULL означает старую или созданную Parser административную зону.");

            migrationBuilder.AddColumn<bool>(
                name: "can_manage_search_groups",
                schema: "identity",
                table: "employee_access_settings",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Разрешено создавать зоны поиска и управлять только собственными зонами; право не даёт управления Parser.");

            migrationBuilder.CreateIndex(
                name: "ix_search_groups_organization_id_owner_employee_id",
                schema: "collection",
                table: "search_groups",
                columns: SearchGroupOwnerColumns);

            migrationBuilder.CreateIndex(
                name: "ix_search_groups_owner_employee_id",
                schema: "collection",
                table: "search_groups",
                column: "owner_employee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_search_groups_owner_employee_id",
                schema: "collection",
                table: "search_groups",
                column: "owner_employee_id",
                principalSchema: "organization",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_search_groups_owner_employee_id",
                schema: "collection",
                table: "search_groups");

            migrationBuilder.DropIndex(
                name: "ix_search_groups_organization_id_owner_employee_id",
                schema: "collection",
                table: "search_groups");

            migrationBuilder.DropIndex(
                name: "ix_search_groups_owner_employee_id",
                schema: "collection",
                table: "search_groups");

            migrationBuilder.DropColumn(
                name: "owner_employee_id",
                schema: "collection",
                table: "search_groups");

            migrationBuilder.DropColumn(
                name: "can_manage_search_groups",
                schema: "identity",
                table: "employee_access_settings");
        }
    }
}
