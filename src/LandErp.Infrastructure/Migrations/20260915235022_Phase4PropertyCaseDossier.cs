using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF generates short constant arrays for migration metadata.

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase4PropertyCaseDossier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "case_checks",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Самостоятельный рабочий объект закупки, к которому относится источник."),
                    level = table.Column<string>(type: "text", nullable: false, comment: "Уровень проверки: быстрая первичная либо глубокая/юридическая."),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    responsible_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Опциональный UTC срок выполнения рабочей задачи или уточнений при возврате."),
                    cost = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 валюта денежного значения; Stage 1 принимает RUB."),
                    result = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    blocker = table.Column<bool>(type: "boolean", nullable: false, comment: "Явный блокирующий риск; установка и снятие требуют права руководителя."),
                    author_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_checks", x => x.id);
                    table.ForeignKey(
                        name: "fk_case_checks_author_employee_id",
                        column: x => x.author_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_checks_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_checks_responsible_employee_id",
                        column: x => x.responsible_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Структурированные быстрые и глубокие проверки PropertyCase с ответственным, сроком, результатом и защищённым признаком блокера.");

            migrationBuilder.CreateTable(
                name: "case_fact_revisions",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Самостоятельный рабочий объект закупки, к которому относится источник."),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Входящий элемент Catalog, связанный с PropertyCase; подтверждённая связь уникальна для элемента."),
                    field = table.Column<string>(type: "text", nullable: false, comment: "Рабочее поле PropertyCase, которое пользователь явно подтвердил из конкретного источника."),
                    value = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false, comment: "Человекочитаемое значение рабочего факта в момент явного подтверждения сотрудником."),
                    verified_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_fact_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_case_fact_revisions_catalog_item_id",
                        column: x => x.catalog_item_id,
                        principalSchema: "catalog",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_fact_revisions_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_fact_revisions_verified_by_employee_id",
                        column: x => x.verified_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемые подтверждения явного переноса значения источника в рабочий факт PropertyCase; автоматическое перезаписывание запрещено.");

            migrationBuilder.CreateTable(
                name: "negotiations",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Самостоятельный рабочий объект закупки, к которому относится источник."),
                    price_type = table.Column<string>(type: "text", nullable: false, comment: "Тип цены переговоров: стартовая, предложение продавца, предложение покупателя или согласованная; типы не перезаписывают друг друга."),
                    amount = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: false, comment: "Денежное значение переговоров decimal; валюта хранится отдельно."),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 валюта денежного значения; Stage 1 принимает RUB."),
                    channel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false, comment: "Канал контакта: звонок, сообщение, встреча или другой понятный сотруднику способ."),
                    contact = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    outcome = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Результат конкретного контакта с продавцом; не является workflow-решением или фактом покупки."),
                    conditions = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    next_step = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false, comment: "Следующее согласованное действие после контакта без создания отдельного workflow engine."),
                    author_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Фактический UTC момент бизнес-действия (например контакта), если известен; RecordedAt отдельно фиксирует запись."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_negotiations", x => x.id);
                    table.ForeignKey(
                        name: "fk_negotiations_author_employee_id",
                        column: x => x.author_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_negotiations_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемая специализированная история переговоров PropertyCase. Публичная цена, цена продавца, предложение покупателя и согласованная цена не смешиваются.");

            migrationBuilder.CreateTable(
                name: "stored_files",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    owner_module = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    purpose = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Непрозрачный внутренний ключ backend-хранилища; не является публичной ссылкой и не выводится в staff UI."),
                    external_url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true, comment: "Проверенная HTTPS-ссылка для link-вложения; внутренний файл вместо неё использует закрытый StorageKey."),
                    original_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    content_type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false, comment: "Размер файла в байтах после принятой загрузки; лимит проверяется сервером."),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true, comment: "SHA-256 содержимого для контроля целостности без хранения бинарных данных в PostgreSQL."),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, инициировавший загрузку файла или добавление внешней ссылки."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stored_files", x => x.id);
                    table.CheckConstraint("ck_stored_files_reference", "(storage_key IS NOT NULL AND external_url IS NULL) OR (storage_key IS NULL AND external_url IS NOT NULL) OR status IN ('PendingUpload', 'UploadFailed')");
                    table.ForeignKey(
                        name: "fk_stored_files_created_by_employee_id",
                        column: x => x.created_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Провайдер-независимые метаданные файлов и внешних ссылок. Непрозрачный ключ хранилища не является пользовательским URL.");

            migrationBuilder.CreateTable(
                name: "case_attachments",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Самостоятельный рабочий объект закупки, к которому относится источник."),
                    stored_file_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Стабильная ссылка на provider-neutral метаданные; StorageKey не показывается сотруднику."),
                    owner_type = table.Column<string>(type: "text", nullable: false, comment: "Owning business object вложения: PropertyCase, переговоры или проверка."),
                    negotiation_id = table.Column<Guid>(type: "uuid", nullable: true),
                    check_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_attachments", x => x.id);
                    table.CheckConstraint("ck_case_attachments_owner", "(owner_type = 'Case' AND negotiation_id IS NULL AND check_id IS NULL) OR (owner_type = 'Negotiation' AND negotiation_id IS NOT NULL AND check_id IS NULL) OR (owner_type = 'Check' AND negotiation_id IS NULL AND check_id IS NOT NULL)");
                    table.ForeignKey(
                        name: "fk_case_attachments_actor_employee_id",
                        column: x => x.actor_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_attachments_check_id",
                        column: x => x.check_id,
                        principalSchema: "procurement",
                        principalTable: "case_checks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_attachments_negotiation_id",
                        column: x => x.negotiation_id,
                        principalSchema: "procurement",
                        principalTable: "negotiations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_attachments_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_case_attachments_stored_file_id",
                        column: x => x.stored_file_id,
                        principalSchema: "foundation",
                        principalTable: "stored_files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Связи вложений с PropertyCase, переговорами или проверками; доступ всегда наследуется от owning PropertyCase.");

            migrationBuilder.InsertData(
                schema: "workflow",
                table: "stages",
                columns: new[] { "id", "name" },
                values: new object[] { "negotiation", "Переговоры и проверки" });

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_actor_employee_id",
                schema: "procurement",
                table: "case_attachments",
                column: "actor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_check_id",
                schema: "procurement",
                table: "case_attachments",
                column: "check_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_negotiation_id",
                schema: "procurement",
                table: "case_attachments",
                column: "negotiation_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_property_case_id_recorded_at",
                schema: "procurement",
                table: "case_attachments",
                columns: new[] { "property_case_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_stored_file_id",
                schema: "procurement",
                table: "case_attachments",
                column: "stored_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_checks_author_employee_id",
                schema: "procurement",
                table: "case_checks",
                column: "author_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_checks_property_case_id_level_status",
                schema: "procurement",
                table: "case_checks",
                columns: new[] { "property_case_id", "level", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_case_checks_responsible_employee_id",
                schema: "procurement",
                table: "case_checks",
                column: "responsible_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_fact_revisions_catalog_item_id",
                schema: "procurement",
                table: "case_fact_revisions",
                column: "catalog_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_fact_revisions_property_case_id_field_recorded_at",
                schema: "procurement",
                table: "case_fact_revisions",
                columns: new[] { "property_case_id", "field", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_case_fact_revisions_verified_by_employee_id",
                schema: "procurement",
                table: "case_fact_revisions",
                column: "verified_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiations_author_employee_id",
                schema: "procurement",
                table: "negotiations",
                column: "author_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_negotiations_property_case_id_effective_at",
                schema: "procurement",
                table: "negotiations",
                columns: new[] { "property_case_id", "effective_at" });

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_created_by_employee_id",
                schema: "foundation",
                table: "stored_files",
                column: "created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_stored_files_organization_id_status_recorded_at",
                schema: "foundation",
                table: "stored_files",
                columns: new[] { "organization_id", "status", "recorded_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "case_attachments",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "case_fact_revisions",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "case_checks",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "negotiations",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "stored_files",
                schema: "foundation");

            migrationBuilder.DeleteData(
                schema: "workflow",
                table: "stages",
                keyColumn: "id",
                keyValue: "negotiation");
        }
    }
}
