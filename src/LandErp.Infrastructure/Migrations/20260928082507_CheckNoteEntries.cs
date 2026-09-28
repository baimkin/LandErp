#pragma warning disable CA1861 // Fixed migration column list.
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CheckNoteEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_case_rich_notes_property_case_id_section",
                schema: "procurement",
                table: "case_rich_notes");

            migrationBuilder.AlterTable(
                name: "case_rich_notes",
                schema: "procurement",
                comment: "Рабочий документ и отдельные заметки проверок PropertyCase. Текущие версии и удалённые записи; прежние значения в неизменяемом аудите.",
                oldComment: "Три независимых свободных документа PropertyCase. Текущее значение и версия; прежние значения в неизменяемом аудите.");

            migrationBuilder.AddColumn<bool>(
                name: "deleted",
                schema: "procurement",
                table: "case_rich_notes",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Заметка удалена из списка; текст и ссылки на файлы сохраняются для истории.");

            migrationBuilder.CreateIndex(
                name: "ix_case_rich_notes_property_case_id_section",
                schema: "procurement",
                table: "case_rich_notes",
                columns: new[] { "property_case_id", "section" },
                unique: true,
                filter: "section = 'Working' AND NOT deleted");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_case_rich_notes_property_case_id_section",
                schema: "procurement",
                table: "case_rich_notes");

            migrationBuilder.DropColumn(
                name: "deleted",
                schema: "procurement",
                table: "case_rich_notes");

            migrationBuilder.AlterTable(
                name: "case_rich_notes",
                schema: "procurement",
                comment: "Три независимых свободных документа PropertyCase. Текущее значение и версия; прежние значения в неизменяемом аудите.",
                oldComment: "Рабочий документ и отдельные заметки проверок PropertyCase. Текущие версии и удалённые записи; прежние значения в неизменяемом аудите.");

            migrationBuilder.CreateIndex(
                name: "ix_case_rich_notes_property_case_id_section",
                schema: "procurement",
                table: "case_rich_notes",
                columns: new[] { "property_case_id", "section" },
                unique: true);
        }
    }
}
