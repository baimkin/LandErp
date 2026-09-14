using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable
#pragma warning disable CA1861 // EF-generated one-shot migration arrays are not hot-path allocations.

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class IdentityOrganization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "foundation");

            migrationBuilder.EnsureSchema(
                name: "organization");

            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "audit_events",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Автор действия — доверенная серверная идентичность; не берётся из клиентского payload."),
                    action = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Стабильный код выполненного значимого действия, история сохраняется append-only."),
                    object_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия."),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Внутренний UUID бизнес-объекта, к которому относится событие."),
                    changes = table.Column<string>(type: "jsonb", nullable: false, comment: "Безопасные значения значимых изменений до/после в JSON; passwords, cookies, tokens и ключи исключены."),
                    correlation_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Ограниченный безопасный идентификатор связи действия с HTTP-запросом или локальной командой."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_events", x => x.id);
                },
                comment: "Неизменяемый журнал значимых действий: кто, когда, что и в какой области изменил. Runtime не исправляет и не удаляет события.");

            migrationBuilder.CreateTable(
                name: "organizations",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    business_time_zone = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Явный IANA timezone бизнес-сроков организации. Instants в БД сохраняются UTC.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_organizations", x => x.id);
                },
                comment: "Организации, являющиеся границами доступа LandErp. Не являются юридическими лицами сделки.");

            migrationBuilder.CreateTable(
                name: "permissions",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    description = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_permissions", x => x.id);
                },
                comment: "Каталог стабильных системных разрешений на действия. Проверяется сервером вместе с scope.");

            migrationBuilder.CreateTable(
                name: "roles",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    concurrency_stamp = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Технический Identity concurrency token для защиты от потерянных обновлений.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_roles", x => x.id);
                },
                comment: "Именованные наборы разрешений. Роль не заменяет проверку области данных.");

            migrationBuilder.CreateTable(
                name: "users",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false, comment: "Адрес активирован закрытым приглашением или локальным Owner bootstrap; публичной регистрации нет."),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Identity password verifier; открытый пароль не хранится и не логируется."),
                    security_stamp = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Версия безопасности Identity для отзыва сессий при изменении учётной записи."),
                    concurrency_stamp = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Технический Identity concurrency token для защиты от потерянных обновлений."),
                    phone_number = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false, comment: "Включена MFA. Для Owner/Administrator этого поля недостаточно: текущий вход также должен пройти MFA."),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC момент завершения блокировки входа после неуспешных попыток."),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false, comment: "Число неуспешных попыток, используемое Identity lockout policy.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                },
                comment: "Учётные записи сотрудников. Пароли хранятся только как Identity verifier; доступ к бизнес-данным задаётся назначениями и permissions.");

            migrationBuilder.CreateTable(
                name: "org_units",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_org_units", x => x.id);
                    table.ForeignKey(
                        name: "fk_org_units_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Подразделения организации, определяющие рабочую ответственность и Department visibility.");

            migrationBuilder.CreateTable(
                name: "positions",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_positions", x => x.id);
                    table.ForeignKey(
                        name: "fk_positions_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Настраиваемые должности сотрудников; название должности само по себе не предоставляет permissions.");

            migrationBuilder.CreateTable(
                name: "role_claims",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Набор permissions для текущего назначения; итоговый доступ ограничен scope."),
                    claim_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    claim_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_role_claims_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Технические утверждения ролей Identity; бизнес permissions хранятся отдельно.");

            migrationBuilder.CreateTable(
                name: "role_permissions",
                schema: "identity",
                columns: table => new
                {
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Набор permissions для текущего назначения; итоговый доступ ограничен scope."),
                    permission_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Стабильный код серверной проверки операции, а не название кнопки интерфейса.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_role_permissions", x => new { x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "fk_role_permissions_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "identity",
                        principalTable: "permissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_role_permissions_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Состав разрешений каждой роли. Не даёт доступ к другим организациям.");

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Связь с технической учётной записью; не является внешним ID или бизнес-номером."),
                    display_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, comment: "Разрешена ли работа сотрудника. При выключении существующая cookie не обходит серверную проверку."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                    table.ForeignKey(
                        name: "fk_employees_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employees_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Профили сотрудников организации, связанные с учётной записью. Неактивный сотрудник не получает бизнес-доступ.");

            migrationBuilder.CreateTable(
                name: "user_claims",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Связь с технической учётной записью; не является внешним ID или бизнес-номером."),
                    claim_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    claim_value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_claims", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_claims_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Технические утверждения Identity. OrganizationId из клиентского payload не считается доказательством доступа.");

            migrationBuilder.CreateTable(
                name: "user_logins",
                schema: "identity",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    provider_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    provider_display_name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Связь с технической учётной записью; не является внешним ID или бизнес-номером.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_logins", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "fk_user_logins_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Технические связи с провайдерами входа Identity. В Stage 1 внешние провайдеры не включены.");

            migrationBuilder.CreateTable(
                name: "user_roles",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Связь с технической учётной записью; не является внешним ID или бизнес-номером."),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Набор permissions для текущего назначения; итоговый доступ ограничен scope.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_roles", x => new { x.user_id, x.role_id });
                    table.ForeignKey(
                        name: "fk_user_roles_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_user_roles_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Технические роли Identity для требований MFA и управления сессией. Бизнес-доступ проверяется по актуальному назначению сотрудника.");

            migrationBuilder.CreateTable(
                name: "user_tokens",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Связь с технической учётной записью; не является внешним ID или бизнес-номером."),
                    login_provider = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    value = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true, comment: "Техническое значение Identity token или claim. Секретные token values не публикуются и не входят в аудит.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_tokens", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "fk_user_tokens_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Технические секреты и verifier Identity, включая ключ MFA. Не выводятся в UI журналов, логи и аудит.");

            migrationBuilder.CreateTable(
                name: "teams",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Подразделение рабочей ответственности; используется для Department scope."),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teams", x => x.id);
                    table.ForeignKey(
                        name: "fk_teams_org_unit_id",
                        column: x => x.org_unit_id,
                        principalSchema: "organization",
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_teams_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Рабочие команды внутри подразделения; используются для Team visibility.");

            migrationBuilder.CreateTable(
                name: "employee_invitations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, к которому относится назначение или приглашение."),
                    token_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "SHA-256 одноразового высокоэнтропийного token; открытое значение не хранится."),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент окончания действия приглашения; после него активация запрещена."),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC момент однократной активации. Непустое значение запрещает повторное использование."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_invitations_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Одноразовые приглашения для активации сотрудников; открытый token не сохраняется, повторное использование запрещено.");

            migrationBuilder.CreateTable(
                name: "employee_assignments",
                schema: "organization",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, к которому относится назначение или приглашение."),
                    org_unit_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Подразделение рабочей ответственности; используется для Department scope."),
                    position_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Должность в организации; права определяются отдельно через роль и permission."),
                    team_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Рабочая команда для Team scope. Отсутствие команды не расширяет область доступа."),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "Руководитель сотрудника, которому можно передать рабочую ответственность."),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Набор permissions для текущего назначения; итоговый доступ ограничен scope."),
                    scope = table.Column<string>(type: "text", nullable: false, comment: "Own — собственные записи; AssignedObjects — назначенные объекты; Team — команда; Department — подразделение; Organization — одна организация."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_assignments_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_assignments_manager_employee_id",
                        column: x => x.manager_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_assignments_org_unit_id",
                        column: x => x.org_unit_id,
                        principalSchema: "organization",
                        principalTable: "org_units",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_assignments_position_id",
                        column: x => x.position_id,
                        principalSchema: "organization",
                        principalTable: "positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_assignments_role_id",
                        column: x => x.role_id,
                        principalSchema: "identity",
                        principalTable: "roles",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_employee_assignments_team_id",
                        column: x => x.team_id,
                        principalSchema: "organization",
                        principalTable: "teams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Актуальное назначение сотрудника: подразделение, должность, команда, руководитель и роль с областью доступа.");

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignments_employee_id",
                schema: "organization",
                table: "employee_assignments",
                column: "employee_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignments_manager_employee_id",
                schema: "organization",
                table: "employee_assignments",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignments_org_unit_id",
                schema: "organization",
                table: "employee_assignments",
                column: "org_unit_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignments_position_id",
                schema: "organization",
                table: "employee_assignments",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignments_role_id",
                schema: "organization",
                table: "employee_assignments",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_assignments_team_id",
                schema: "organization",
                table: "employee_assignments",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_employee_invitations_employee_id",
                schema: "identity",
                table: "employee_invitations",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_organization_id",
                schema: "organization",
                table: "employees",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                schema: "organization",
                table: "employees",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_org_units_organization_id_name",
                schema: "organization",
                table: "org_units",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_positions_organization_id_name",
                schema: "organization",
                table: "positions",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_role_claims_role_id",
                schema: "identity",
                table: "role_claims",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_role_permissions_permission_id",
                schema: "identity",
                table: "role_permissions",
                column: "permission_id");

            migrationBuilder.CreateIndex(
                name: "ix_roles_normalized_name",
                schema: "identity",
                table: "roles",
                column: "normalized_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_org_unit_id_name",
                schema: "organization",
                table: "teams",
                columns: new[] { "org_unit_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_teams_organization_id",
                schema: "organization",
                table: "teams",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_claims_user_id",
                schema: "identity",
                table: "user_claims",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_logins_user_id",
                schema: "identity",
                table: "user_logins",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_roles_role_id",
                schema: "identity",
                table: "user_roles",
                column: "role_id");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_email",
                schema: "identity",
                table: "users",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "ix_users_normalized_user_name",
                schema: "identity",
                table: "users",
                column: "normalized_user_name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "audit_events",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "employee_assignments",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "employee_invitations",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "role_permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_claims",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_logins",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_tokens",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "positions",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "teams",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "permissions",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "roles",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "org_units",
                schema: "organization");

            migrationBuilder.DropTable(
                name: "users",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "organizations",
                schema: "organization");
        }
    }
}
