using System;
using Microsoft.EntityFrameworkCore.Migrations;

#pragma warning disable CA1861 // EF-generated one-shot migration arrays are not hot-path allocations.
#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ProcurementCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "workflow");

            migrationBuilder.EnsureSchema(
                name: "procurement");

            migrationBuilder.CreateSequence(
                name: "property_case_numbers",
                schema: "procurement");

            migrationBuilder.CreateTable(
                name: "approvals",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    object_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия."),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Внутренний UUID бизнес-объекта, к которому относится событие."),
                    requester_employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Менеджер, передавший объект на рассмотрение. Не может одобрить собственную передачу."),
                    approver_employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Руководитель, фактически сохранивший решение по передаче."),
                    outcome = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Решение руководителя: Return/Approve/Monitor/Reject. Не подразумевает покупку или финансовое обязательство."),
                    reason = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    considered_data_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Точная версия данных объявления, рассмотренная руководителем при решении."),
                    object_version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия объекта, к которой относится переход или решение; stale commands отклоняются."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_approvals", x => x.id);
                    table.ForeignKey(
                        name: "fk_approvals_approver_employee_id",
                        column: x => x.approver_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_approvals_requester_employee_id",
                        column: x => x.requester_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемые решения руководителя по конкретной передаче и версии данных. Одобрение не является юридическим или финансовым фактом сделки.");

            migrationBuilder.CreateTable(
                name: "assignments",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    object_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия."),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Внутренний UUID бизнес-объекта, к которому относится событие."),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, к которому относится назначение или приглашение."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_assignments_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Актуальная ответственность за бизнес-объект. Передача и возврат меняют исполнителя атомарно с workflow и историей.");

            migrationBuilder.CreateTable(
                name: "business_timeline",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    object_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия."),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Внутренний UUID бизнес-объекта, к которому относится событие."),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    body = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false, comment: "Рабочее содержание записи timeline: причина, что исправить/уточнить или результат ручного контакта."),
                    target_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    effective_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Фактический UTC момент бизнес-действия (например контакта), если известен; RecordedAt отдельно фиксирует запись."),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Опциональный UTC срок выполнения рабочей задачи или уточнений при возврате.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_timeline", x => x.id);
                    table.ForeignKey(
                        name: "fk_business_timeline_actor_employee_id",
                        column: x => x.actor_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_business_timeline_target_employee_id",
                        column: x => x.target_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемая бизнес-история объекта: решения, заметки, контакты, кому возвращён объект, что уточнить и срок. Отдельна от технического audit.");

            migrationBuilder.CreateTable(
                name: "notifications",
                schema: "foundation",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, к которому относится назначение или приглашение."),
                    object_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия."),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Внутренний UUID бизнес-объекта, к которому относится событие."),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "UTC момент просмотра внутреннего уведомления; отсутствие означает непрочитанное."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Внутренние уведомления исполнителю о передаче/возврате/решении. ReadAt отмечает просмотр; внешняя доставка не включена.");

            migrationBuilder.CreateTable(
                name: "stages",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    name = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stages", x => x.id);
                },
                comment: "Стабильные стадии первого процесса закупки: new/analysis/clarify/monitor/rejected/pending_head/returned/approved. Approved означает дальнейшую работу, не покупку.");

            migrationBuilder.CreateTable(
                name: "work_tasks",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    object_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия."),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Внутренний UUID бизнес-объекта, к которому относится событие."),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, к которому относится назначение или приглашение."),
                    completed = table.Column<bool>(type: "boolean", nullable: false, comment: "Завершена ли текущая рабочая задача; факты истории сохраняются независимо."),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true, comment: "Опциональный UTC срок выполнения рабочей задачи или уточнений при возврате."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_work_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_work_tasks_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Актуальная рабочая задача сотрудника по объекту, с опциональным сроком UTC; Completed не удаляет историю выполненной работы.");

            migrationBuilder.CreateTable(
                name: "transitions",
                schema: "workflow",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    object_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связанного бизнес-объекта, позволяющий восстановить происхождение действия."),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Внутренний UUID бизнес-объекта, к которому относится событие."),
                    from_stage_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Стадия объекта непосредственно перед сохранённым переходом."),
                    to_stage_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Стадия объекта после сохранённого перехода; не планируемая будущая стадия."),
                    action = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Стабильный код выполненного значимого действия, история сохраняется append-only."),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия объекта, к которой относится переход или решение; stale commands отклоняются."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transitions", x => x.id);
                    table.ForeignKey(
                        name: "fk_transitions_from_stage_id",
                        column: x => x.from_stage_id,
                        principalSchema: "workflow",
                        principalTable: "stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transitions_to_stage_id",
                        column: x => x.to_stage_id,
                        principalSchema: "workflow",
                        principalTable: "stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемые факты переходов workflow: кто выполнил действие, прежняя/новая стадия и версия объекта.");

            migrationBuilder.CreateTable(
                name: "property_cases",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_number = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Читаемый номер кейса PC-000001 из отдельной последовательности; не UUID и не ExternalId объявления."),
                    stage_id = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Текущая стадия первичной закупки. Approved разрешает следующий анализ, не обозначает завершённую покупку."),
                    manager_employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Руководитель сотрудника, которому можно передать рабочую ответственность."),
                    assignment_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Общая актуальная ответственность за кейс; изменение исполнителя сохраняется вместе с переходом."),
                    work_task_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Общая текущая задача по кейсу и её срок; будущий SLA/job engine не включён."),
                    pending_approval_id = table.Column<Guid>(type: "uuid", nullable: true, comment: "ID конкретной передачи руководителю. Один immutable Approval завершает эту передачу."),
                    reviewed_data_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Версия публичных данных, рассмотренная при последнем решении. Новые данные возвращают объект в изменившуюся очередь."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_cases", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_cases_assignment_id",
                        column: x => x.assignment_id,
                        principalSchema: "workflow",
                        principalTable: "assignments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_property_cases_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "catalog",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_property_cases_manager_employee_id",
                        column: x => x.manager_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_property_cases_stage_id",
                        column: x => x.stage_id,
                        principalSchema: "workflow",
                        principalTable: "stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_property_cases_work_task_id",
                        column: x => x.work_task_id,
                        principalSchema: "workflow",
                        principalTable: "work_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Рабочие кейсы закупки по объявлениям. Хранят первичный анализ, стадию, менеджера и ссылки на общие task/assignment; не полный Due Diligence.");

            migrationBuilder.InsertData(
                schema: "workflow",
                table: "stages",
                columns: new[] { "id", "name" },
                values: new object[,]
                {
                    { "analysis", "Первичный анализ" },
                    { "approved", "Дальнейшая работа одобрена" },
                    { "clarify", "Уточнить" },
                    { "monitor", "Наблюдать" },
                    { "new", "Новое объявление" },
                    { "pending_head", "У руководителя" },
                    { "rejected", "Отклонён" },
                    { "returned", "Возвращён менеджеру" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_approvals_approver_employee_id",
                schema: "workflow",
                table: "approvals",
                column: "approver_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_approvals_requester_employee_id",
                schema: "workflow",
                table: "approvals",
                column: "requester_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_employee_id",
                schema: "workflow",
                table: "assignments",
                column: "employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_assignments_object_type_object_id",
                schema: "workflow",
                table: "assignments",
                columns: new[] { "object_type", "object_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_business_timeline_actor_employee_id",
                schema: "foundation",
                table: "business_timeline",
                column: "actor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_business_timeline_organization_id_object_type_object_id_recorded_at",
                schema: "foundation",
                table: "business_timeline",
                columns: new[] { "organization_id", "object_type", "object_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_business_timeline_target_employee_id",
                schema: "foundation",
                table: "business_timeline",
                column: "target_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_employee_id_read_at",
                schema: "foundation",
                table: "notifications",
                columns: new[] { "employee_id", "read_at" });

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_assignment_id",
                schema: "procurement",
                table: "property_cases",
                column: "assignment_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_business_number",
                schema: "procurement",
                table: "property_cases",
                column: "business_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_listing_id",
                schema: "procurement",
                table: "property_cases",
                column: "listing_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_manager_employee_id",
                schema: "procurement",
                table: "property_cases",
                column: "manager_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_stage_id",
                schema: "procurement",
                table: "property_cases",
                column: "stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_work_task_id",
                schema: "procurement",
                table: "property_cases",
                column: "work_task_id");

            migrationBuilder.CreateIndex(
                name: "ix_transitions_from_stage_id",
                schema: "workflow",
                table: "transitions",
                column: "from_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_transitions_to_stage_id",
                schema: "workflow",
                table: "transitions",
                column: "to_stage_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_tasks_employee_id_completed_due_at",
                schema: "workflow",
                table: "work_tasks",
                columns: new[] { "employee_id", "completed", "due_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "approvals",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "business_timeline",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "notifications",
                schema: "foundation");

            migrationBuilder.DropTable(
                name: "property_cases",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "transitions",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "assignments",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "work_tasks",
                schema: "workflow");

            migrationBuilder.DropTable(
                name: "stages",
                schema: "workflow");

            migrationBuilder.DropSequence(
                name: "property_case_numbers",
                schema: "procurement");
        }
    }
}
