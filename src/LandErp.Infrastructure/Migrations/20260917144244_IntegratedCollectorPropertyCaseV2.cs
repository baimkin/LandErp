using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IntegratedCollectorPropertyCaseV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                INSERT INTO identity.permissions (id, description)
                VALUES ('collection.read', 'Чтение поисков, очереди, истории и состояния локальных Parser')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO identity.role_permissions (role_id, permission_id)
                SELECT DISTINCT role_id, 'collection.read'
                FROM identity.role_permissions
                WHERE permission_id IN ('agents.manage', 'searches.manage')
                ON CONFLICT (role_id, permission_id) DO NOTHING;

                INSERT INTO identity.permissions (id, description)
                VALUES ('procurement_purchase.confirm', 'Подтверждение и исправление факта покупки объекта')
                ON CONFLICT (id) DO NOTHING;

                INSERT INTO identity.role_permissions (role_id, permission_id)
                SELECT id, 'procurement_purchase.confirm'
                FROM identity.roles
                WHERE normalized_name IN ('OWNER', 'PROCUREMENTHEAD')
                ON CONFLICT (role_id, permission_id) DO NOTHING;
                """);

            migrationBuilder.CreateTable(
                name: "scheduler_status",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    last_started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC момент начала последнего цикла фонового scheduler."),
                    last_succeeded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC момент последнего полностью успешного цикла scheduler."),
                    last_failed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC момент последнего зарегистрированного сбоя scheduler."),
                    last_queued_count = table.Column<int>(type: "integer", nullable: false, comment: "Число новых работ, поставленных последним успешным циклом scheduler."),
                    last_failure_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, comment: "Безопасный машинный код последнего сбоя scheduler без текста исключения и секретов.")
                },
                constraints: table => table.PrimaryKey("pk_scheduler_status", x => x.id),
                comment: "Текущее техническое состояние server scheduler: последний старт, успешный цикл, ошибка и число поставленных работ.");

            migrationBuilder.AddColumn<DateTimeOffset>(name: "activation_expires_at", schema: "collection", table: "agents",
                type: "timestamp with time zone", nullable: true, comment: "UTC срок действия одноразового кода подключения Parser.");
            migrationBuilder.AddColumn<string>(name: "activation_hash", schema: "collection", table: "agents",
                type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "",
                comment: "SHA-256 verifier одноразового кода подключения; открытый код не хранится.");
            migrationBuilder.AddColumn<DateTimeOffset>(name: "activation_used_at", schema: "collection", table: "agents",
                type: "timestamp with time zone", nullable: true, comment: "UTC момент успешного обмена одноразового кода на постоянную machine credential.");
            migrationBuilder.AddColumn<string>(name: "attention_code", schema: "collection", table: "agents",
                type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "",
                comment: "Текущий очищаемый machine code ручного внимания: CAPTCHA, вход или rate limit.");
            migrationBuilder.AddColumn<DateTimeOffset>(name: "last_activity_at", schema: "collection", table: "agents",
                type: "timestamp with time zone", nullable: true, comment: "UTC момент последнего полезного действия Parser внутри текущей работы.");
            migrationBuilder.AddColumn<int>(name: "progress_current_page", schema: "collection", table: "agents",
                type: "integer", nullable: true, comment: "Текущая страница источника, сообщённая Parser, если применимо.");
            migrationBuilder.AddColumn<int>(name: "progress_max_pages", schema: "collection", table: "agents",
                type: "integer", nullable: true, comment: "Серверный предел страниц для текущей работы, сообщённый Parser.");
            migrationBuilder.AddColumn<int>(name: "progress_processed", schema: "collection", table: "agents",
                type: "integer", nullable: false, defaultValue: 0, comment: "Последнее число обработанных элементов, сообщённое heartbeat.");
            migrationBuilder.AddColumn<int>(name: "progress_total", schema: "collection", table: "agents",
                type: "integer", nullable: true, comment: "Ожидаемое общее число элементов, если источник смог его определить.");
            migrationBuilder.AddColumn<string>(name: "runtime_state", schema: "collection", table: "agents",
                type: "text", nullable: false, defaultValue: "Idle", comment: "Последнее заявленное Parser состояние выполнения без browser-specific деталей.");

            migrationBuilder.AddColumn<Guid>(name: "document_requirement_id", schema: "procurement", table: "case_attachments",
                type: "uuid", nullable: true, comment: "Опциональная связь файла с конкретным пунктом чек-листа документов PropertyCase.");

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
                    table.ForeignKey(name: "fk_case_document_requirements_property_case_id", column: x => x.property_case_id,
                        principalSchema: "procurement", principalTable: "property_cases", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(name: "fk_case_document_requirements_updated_by_employee_id", column: x => x.updated_by_employee_id,
                        principalSchema: "organization", principalTable: "employees", principalColumn: "id", onDelete: ReferentialAction.Restrict);
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

            migrationBuilder.CreateIndex(name: "ix_case_attachments_document_requirement_id", schema: "procurement",
                table: "case_attachments", column: "document_requirement_id");
            migrationBuilder.CreateIndex(name: "ix_case_document_requirements_property_case_id_code", schema: "procurement",
                table: "case_document_requirements", columns: new[] { "property_case_id", "code" }, unique: true);
            migrationBuilder.CreateIndex(name: "ix_case_document_requirements_property_case_id_status", schema: "procurement",
                table: "case_document_requirements", columns: new[] { "property_case_id", "status" });
            migrationBuilder.CreateIndex(name: "ix_case_document_requirements_updated_by_employee_id", schema: "procurement",
                table: "case_document_requirements", column: "updated_by_employee_id");
            migrationBuilder.AddForeignKey(name: "fk_case_attachments_document_requirement_id", schema: "procurement",
                table: "case_attachments", column: "document_requirement_id", principalSchema: "procurement",
                principalTable: "case_document_requirements", principalColumn: "id", onDelete: ReferentialAction.Restrict);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(name: "fk_case_attachments_document_requirement_id", schema: "procurement", table: "case_attachments");
            migrationBuilder.DropTable(name: "case_document_requirements", schema: "procurement");
            migrationBuilder.DropIndex(name: "ix_case_attachments_document_requirement_id", schema: "procurement", table: "case_attachments");
            migrationBuilder.DropColumn(name: "document_requirement_id", schema: "procurement", table: "case_attachments");

            foreach (string column in new[]
            {
                "activation_expires_at", "activation_hash", "activation_used_at", "attention_code", "last_activity_at",
                "progress_current_page", "progress_max_pages", "progress_processed", "progress_total", "runtime_state"
            })
            {
                migrationBuilder.DropColumn(name: column, schema: "collection", table: "agents");
            }

            migrationBuilder.DropTable(name: "scheduler_status", schema: "collection");
            migrationBuilder.Sql("""
                DELETE FROM identity.role_permissions
                WHERE permission_id IN ('collection.read', 'procurement_purchase.confirm');
                DELETE FROM identity.permissions
                WHERE id IN ('collection.read', 'procurement_purchase.confirm');
                """);

        }
    }
}
