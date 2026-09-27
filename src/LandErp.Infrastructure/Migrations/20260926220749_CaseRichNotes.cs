#pragma warning disable CA1861 // Generated migration index columns are a fixed schema definition.
﻿using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CaseRichNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_rich_notes",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Объект закупки, которому принадлежит текст секции; описание внешнего источника не изменяется."),
                    section = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false, comment: "Независимая секция: рабочие заметки, базовые или глубокие проверки; не результат структурированной проверки."),
                    document_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Очищенный сервером JSON документа C-01; изображения только по проверенным ID CaseAttachment, без HTML и base64."),
                    updated_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_rich_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_case_rich_notes_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_rich_notes_updated_by_employee_id",
                        column: x => x.updated_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Три независимых свободных документа PropertyCase. Текущее значение и версия; прежние значения в неизменяемом аудите.");

            migrationBuilder.CreateIndex(
                name: "ix_case_rich_notes_property_case_id_section",
                schema: "procurement",
                table: "case_rich_notes",
                columns: new[] { "property_case_id", "section" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_rich_notes_updated_by_employee_id",
                schema: "procurement",
                table: "case_rich_notes",
                column: "updated_by_employee_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_rich_notes",
                schema: "procurement");
        }
    }
}
