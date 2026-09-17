using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PropertyCaseDocumentChecklist : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "document_requirement_id",
                schema: "procurement",
                table: "case_attachments",
                type: "uuid",
                nullable: true,
                comment: "Опциональная связь файла с конкретным пунктом чек-листа документов PropertyCase.");

            migrationBuilder.CreateTable(
                name: "case_document_requirements",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Самостоятельный рабочий объект закупки, к которому относится источник."),
                    code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    expected_source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Опциональный UTC срок выполнения рабочей задачи или уточнений при возврате."),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    updated_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_document_requirements", x => x.id);
                    table.ForeignKey(
                        name: "fk_case_document_requirements_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_document_requirements_updated_by_employee_id",
                        column: x => x.updated_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Чек-лист обязательных документов конкретного PropertyCase: ожидаемый источник, статус запроса/получения/проверки, срок и комментарий.");

            migrationBuilder.Sql("""
                INSERT INTO procurement.case_document_requirements
                    (id, organization_id, property_case_id, code, title, description, expected_source, status,
                     due_at, note, updated_by_employee_id, updated_at, version)
                SELECT gen_random_uuid(), value.organization_id, value.id, template.code, template.title,
                       template.description, template.expected_source, 'Missing', NULL, '',
                       value.manager_employee_id, value.recorded_at, 1
                FROM procurement.property_cases AS value
                CROSS JOIN (VALUES
                    ('egrn', 'Выписка ЕГРН', 'Актуальные сведения о правах, правообладателях и ограничениях.', 'Росреестр'),
                    ('owner_identity', 'Документы собственника', 'Документы для идентификации собственника или его представителя.', 'Продавец'),
                    ('title_basis', 'Документ-основание права', 'Основание возникновения права для глубокой юридической проверки.', 'Продавец'),
                    ('access_scheme', 'Схема подъезда / сервитут', 'Правовое и фактическое основание доступа к участку.', 'Продавец'),
                    ('cadastral_plan', 'Кадастровый план', 'Границы, конфигурация и кадастровые сведения об участке.', 'Росреестр')
                ) AS template(code, title, description, expected_source);
                """);

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_document_requirement_id",
                schema: "procurement",
                table: "case_attachments",
                column: "document_requirement_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_document_requirements_property_case_id_code",
                schema: "procurement",
                table: "case_document_requirements",
                columns: new[] { "property_case_id", "code" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_case_document_requirements_property_case_id_status",
                schema: "procurement",
                table: "case_document_requirements",
                columns: new[] { "property_case_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_case_document_requirements_updated_by_employee_id",
                schema: "procurement",
                table: "case_document_requirements",
                column: "updated_by_employee_id");

            migrationBuilder.AddForeignKey(
                name: "fk_case_attachments_document_requirement_id",
                schema: "procurement",
                table: "case_attachments",
                column: "document_requirement_id",
                principalSchema: "procurement",
                principalTable: "case_document_requirements",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_case_attachments_document_requirement_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropTable(
                name: "case_document_requirements",
                schema: "procurement");

            migrationBuilder.DropIndex(
                name: "ix_case_attachments_document_requirement_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropColumn(
                name: "document_requirement_id",
                schema: "procurement",
                table: "case_attachments");
        }
    }
}
