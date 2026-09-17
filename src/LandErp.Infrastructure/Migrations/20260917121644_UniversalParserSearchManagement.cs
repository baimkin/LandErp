using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UniversalParserSearchManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "can_manage_searches",
                schema: "collection",
                table: "agents",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Разрешено ли этому Parser создавать группы и поиски своей организации через ограниченный machine API.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "can_manage_searches",
                schema: "collection",
                table: "agents");
        }
    }
}
