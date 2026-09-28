using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SourceChangesViewedRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "viewed_data_revision",
                schema: "procurement",
                table: "property_case_source_links",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                comment: "Версия источника, показанная команде в сравнении. 0 означает отсутствие отметки; не заменяет бизнес-рассмотрение.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "viewed_data_revision",
                schema: "procurement",
                table: "property_case_source_links");
        }
    }
}
