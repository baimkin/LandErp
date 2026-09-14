using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF-generated one-shot migration arrays are not hot-path allocations.
#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CollectorCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "collection");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.CreateTable(
                name: "agents",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    credential_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "SHA-256 verifier высокоэнтропийного credential Collector. Открытый token показывается только при создании."),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version_text = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Версия установленного приложения Collector; не concurrency token."),
                    capabilities = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Источники, которые поддерживает установленная версия Collector; неподдерживаемая работа не выдаётся."),
                    registered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_heartbeat_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Последний принятый heartbeat UTC; online означает enabled и связь не старше трёх минут."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_agents", x => x.id);
                    table.ForeignKey(
                        name: "fk_agents_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Зарегистрированные локальные Collector. Сервер хранит только verifier credential; отзыв немедленно запрещает новые обращения.");

            migrationBuilder.CreateTable(
                name: "listings",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Подразделение закупки, ограничивающее Department visibility объекта."),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Рабочая команда для Team scope. Отсутствие команды не расширяет область доступа."),
                    source = table.Column<string>(type: "text", nullable: false),
                    external_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    title = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    price = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true, comment: "Последняя известная публичная цена предложения, decimal; не подтверждённый факт сделки. Валюта отдельно."),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, comment: "ISO 4217 валюта денежного значения; Stage 1 принимает RUB."),
                    area_square_meters = table.Column<decimal>(type: "numeric(19,4)", precision: 19, scale: 4, nullable: true, comment: "Последняя известная площадь в м², decimal. Отсутствие не равно нулю."),
                    location = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    description = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    seller_name = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    photos_json = table.Column<string>(type: "jsonb", nullable: false),
                    first_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Самый ранний известный UTC момент наблюдения этого объявления."),
                    last_observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "Самый новый принятый UTC момент наблюдения для обновления current state."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент регистрации изменения бизнес-данных; используется для очереди новых/изменившихся объектов."),
                    queue_reason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Объяснение появления в очереди: новое объявление или реально изменённые поля."),
                    data_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Версия существенных данных объявления. Не увеличивается от неизменного повторного наблюдения."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listings", x => x.id);
                    table.ForeignKey(
                        name: "fk_listings_department_id",
                        column: x => x.department_id,
                        principalSchema: "organization",
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listings_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listings_team_id",
                        column: x => x.team_id,
                        principalSchema: "organization",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Текущее известное состояние объявления, не идентичность земельного участка. Отсутствие поля не стирает ранее полученное значение.");

            migrationBuilder.CreateTable(
                name: "search_configurations",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение."),
                    department_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Подразделение закупки, ограничивающее Department visibility объекта."),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Рабочая команда для Team scope. Отсутствие команды не расширяет область доступа."),
                    label = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    url = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    max_pages = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_search_configurations", x => x.id);
                    table.ForeignKey(
                        name: "fk_search_configurations_agent_id",
                        column: x => x.agent_id,
                        principalSchema: "collection",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_search_configurations_department_id",
                        column: x => x.department_id,
                        principalSchema: "organization",
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_search_configurations_team_id",
                        column: x => x.team_id,
                        principalSchema: "organization",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Разрешённые поиски организации с конкретным Collector и областью закупки. Browser settings и cookies остаются локально.");

            migrationBuilder.CreateTable(
                name: "jobs",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение."),
                    search_id = table.Column<Guid>(type: "uuid", nullable: false),
                    state = table.Column<string>(type: "text", nullable: false),
                    lease_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Случайный fencing token конкретной выдачи работы; прежний token после перевыдачи отклоняется."),
                    lease_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC срок действия reservation; heartbeat продлевает только действующий lease."),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    result_code = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    accepted_count = table.Column<int>(type: "integer", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_jobs_agent_id",
                        column: x => x.agent_id,
                        principalSchema: "collection",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_jobs_search_id",
                        column: x => x.search_id,
                        principalSchema: "collection",
                        principalTable: "search_configurations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Одна работа сбора: Pending ожидает; Leased закреплена до срока; Completed/LimitReached завершена; AwaitingManualAction требует ручного действия; Failed/Interrupted не считаются пустым успехом.");

            migrationBuilder.CreateTable(
                name: "deliveries",
                schema: "collection",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение."),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "SHA-256 принятого contract payload для проверки неизменности повторной доставки."),
                    receipt_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Прежний результат при idempotent retry, включая фактические accepted/duplicate counters."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_deliveries_job_id",
                        column: x => x.job_id,
                        principalSchema: "collection",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемые квитанции доставки Collector. Повтор ResultId с тем же payload возвращает прежний ответ; другой payload запрещён.");

            migrationBuilder.CreateTable(
                name: "observations",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    agent_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Локальное приложение Collector, которому разрешена работа или которое доставило наблюдение."),
                    job_id = table.Column<Guid>(type: "uuid", nullable: false),
                    observation_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Стабильный ID локального наблюдения Collector; не ExternalId объявления."),
                    content_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "SHA-256 typed observation для deduplication; не идентификатор объекта недвижимости."),
                    payload_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Typed public source observation: Raw/Parsed/Presence, provenance, adapter version. Не raw browser response."),
                    changes_json = table.Column<string>(type: "jsonb", nullable: false, comment: "Поля, изменившие известное состояние; отсутствие поля не обозначает очистку."),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент наблюдения источника; поздняя доставка не делает старые значения новыми."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observations", x => x.id);
                    table.ForeignKey(
                        name: "fk_observations_agent_id",
                        column: x => x.agent_id,
                        principalSchema: "collection",
                        principalTable: "agents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_job_id",
                        column: x => x.job_id,
                        principalSchema: "collection",
                        principalTable: "jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "catalog",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемые наблюдения публичных объявлений: Source/ExternalId, Raw/Parsed/Presence, provenance и версия адаптера. Browser state и raw HTML здесь не хранятся.");

            migrationBuilder.CreateIndex(
                name: "ix_agents_organization_id",
                schema: "collection",
                table: "agents",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_deliveries_job_id",
                schema: "collection",
                table: "deliveries",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_agent_id",
                schema: "collection",
                table: "jobs",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_search_id_state",
                schema: "collection",
                table: "jobs",
                columns: new[] { "search_id", "state" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_department_id",
                schema: "catalog",
                table: "listings",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_listings_organization_id_changed_at",
                schema: "catalog",
                table: "listings",
                columns: new[] { "organization_id", "changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_listings_organization_id_source_external_id",
                schema: "catalog",
                table: "listings",
                columns: new[] { "organization_id", "source", "external_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listings_team_id",
                schema: "catalog",
                table: "listings",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_agent_id_observation_key",
                schema: "catalog",
                table: "observations",
                columns: new[] { "agent_id", "observation_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_observations_job_id",
                schema: "catalog",
                table: "observations",
                column: "job_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_listing_id_observed_at_content_hash",
                schema: "catalog",
                table: "observations",
                columns: new[] { "listing_id", "observed_at", "content_hash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_agent_id",
                schema: "collection",
                table: "search_configurations",
                column: "agent_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_department_id",
                schema: "collection",
                table: "search_configurations",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_search_configurations_team_id",
                schema: "collection",
                table: "search_configurations",
                column: "team_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deliveries",
                schema: "collection");

            migrationBuilder.DropTable(
                name: "observations",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "jobs",
                schema: "collection");

            migrationBuilder.DropTable(
                name: "listings",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "search_configurations",
                schema: "collection");

            migrationBuilder.DropTable(
                name: "agents",
                schema: "collection");
        }
    }
}
