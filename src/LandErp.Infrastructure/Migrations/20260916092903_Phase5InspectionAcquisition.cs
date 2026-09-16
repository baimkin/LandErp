using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase5InspectionAcquisition : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_case_attachments_owner",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropColumn(
                name: "amount",
                schema: "procurement",
                table: "negotiations");

            migrationBuilder.DropColumn(
                name: "price_type",
                schema: "procurement",
                table: "negotiations");

            migrationBuilder.AlterTable(
                name: "stages",
                schema: "workflow",
                comment: "Стабильные стадии первого процесса закупки, включая terminal acquired. Approved означает дальнейшую работу, не покупку.",
                oldComment: "Стабильные стадии первого процесса закупки: new/analysis/clarify/monitor/rejected/pending_head/returned/approved. Approved означает дальнейшую работу, не покупку.");

            migrationBuilder.AlterTable(
                name: "negotiations",
                schema: "procurement",
                comment: "Неизменяемый CRM-журнал событий коммуникации PropertyCase. Цена продавца, предложение покупателя и согласованная цена опциональны и не смешиваются с публичной ценой источника.",
                oldComment: "Неизменяемая специализированная история переговоров PropertyCase. Публичная цена, цена продавца, предложение покупателя и согласованная цена не смешиваются.");

            migrationBuilder.AlterTable(
                name: "case_attachments",
                schema: "procurement",
                comment: "Связи вложений с PropertyCase, переговорами, проверками, осмотром или его пунктом; доступ всегда наследуется от owning PropertyCase.",
                oldComment: "Связи вложений с PropertyCase, переговорами или проверками; доступ всегда наследуется от owning PropertyCase.");

            migrationBuilder.AlterColumn<string>(
                name: "stage_id",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "Текущая стадия закупки. Только acquired обозначает подтверждённую завершённую покупку.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "Текущая стадия первичной закупки. Approved разрешает следующий анализ, не обозначает завершённую покупку.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "acquired_at",
                schema: "procurement",
                table: "property_cases",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "acquired_by_employee_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "acquisition_comment",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "acquisition_date",
                schema: "procurement",
                table: "property_cases",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "acquisition_price",
                schema: "procurement",
                table: "property_cases",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "agreed_price",
                schema: "procurement",
                table: "negotiations",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Опциональная согласованная цена переговоров; сама по себе не означает приобретение.");

            migrationBuilder.AddColumn<decimal>(
                name: "buyer_offer",
                schema: "procurement",
                table: "negotiations",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Опциональное предложение покупателя в том же реальном событии коммуникации.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "next_step_due_at",
                schema: "procurement",
                table: "negotiations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "seller_price",
                schema: "procurement",
                table: "negotiations",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Опциональная названная продавцом цена в конкретном событии коммуникации; публичная цена источника хранится отдельно.");

            migrationBuilder.AddColumn<string>(
                name: "description_snapshot",
                schema: "procurement",
                table: "case_checks",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "template_item_id",
                schema: "procurement",
                table: "case_checks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "template_item_version",
                schema: "procurement",
                table: "case_checks",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "owner_type",
                schema: "procurement",
                table: "case_attachments",
                type: "text",
                nullable: false,
                comment: "Owning business object вложения: PropertyCase, событие переговоров, проверка, осмотр или пункт осмотра.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Owning business object вложения: PropertyCase, переговоры или проверка.");

            migrationBuilder.AddColumn<string>(
                name: "description",
                schema: "procurement",
                table: "case_attachments",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "inspection_id",
                schema: "procurement",
                table: "case_attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "inspection_item_id",
                schema: "procurement",
                table: "case_attachments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "case_check_template_items",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    level = table.Column<string>(type: "text", nullable: false, comment: "Уровень проверки: быстрая первичная либо глубокая/юридическая."),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, comment: "Разрешена ли работа сотрудника. При выключении существующая cookie не обходит серверную проверку."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_case_check_template_items", x => x.id);
                },
                comment: "Редактируемый справочник типовых проверок организации. CaseCheck хранит snapshot и не меняется вслед за справочником.");

            migrationBuilder.CreateTable(
                name: "inspection_template_items",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    answer_type = table.Column<string>(type: "text", nullable: false),
                    options_json = table.Column<string>(type: "jsonb", nullable: false),
                    unit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normal_answer = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    allow_attachments = table.Column<bool>(type: "boolean", nullable: false),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false, comment: "Разрешена ли работа сотрудника. При выключении существующая cookie не обходит серверную проверку."),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inspection_template_items", x => x.id);
                },
                comment: "Версионируемый настраиваемый чек-лист полевого осмотра организации без универсального rules engine.");

            migrationBuilder.CreateTable(
                name: "site_inspections",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Самостоятельный рабочий объект закупки, к которому относится источник."),
                    status = table.Column<string>(type: "text", nullable: false),
                    overall_conclusion = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    preliminary_decision = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    inspector_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_inspections", x => x.id);
                    table.ForeignKey(
                        name: "fk_site_inspections_inspector_employee_id",
                        column: x => x.inspector_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_site_inspections_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Полевой осмотр конкретного PropertyCase: черновик, общий вывод, решение и факт завершения.");

            migrationBuilder.CreateTable(
                name: "site_inspection_items",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    inspection_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_item_version = table.Column<long>(type: "bigint", nullable: false),
                    title_snapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    sort_order_snapshot = table.Column<int>(type: "integer", nullable: false),
                    answer_type_snapshot = table.Column<string>(type: "text", nullable: false),
                    options_json_snapshot = table.Column<string>(type: "jsonb", nullable: false),
                    unit_snapshot = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    normal_answer_snapshot = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    allow_attachments_snapshot = table.Column<bool>(type: "boolean", nullable: false),
                    required_snapshot = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    answer = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    note = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_site_inspection_items", x => x.id);
                    table.ForeignKey(
                        name: "fk_site_inspection_items_inspection_id",
                        column: x => x.inspection_id,
                        principalSchema: "procurement",
                        principalTable: "site_inspections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_site_inspection_items_template_item_id",
                        column: x => x.template_item_id,
                        principalSchema: "procurement",
                        principalTable: "inspection_template_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Snapshot пунктов шаблона и ответы конкретного осмотра; последующее изменение шаблона не переписывает историю.");

            migrationBuilder.InsertData(
                schema: "workflow",
                table: "stages",
                columns: new[] { "id", "name" },
                values: new object[] { "acquired", "Куплено" });

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_acquired_by_employee_id",
                schema: "procurement",
                table: "property_cases",
                column: "acquired_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_checks_template_item_id",
                schema: "procurement",
                table: "case_checks",
                column: "template_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_inspection_id",
                schema: "procurement",
                table: "case_attachments",
                column: "inspection_id");

            migrationBuilder.CreateIndex(
                name: "ix_case_attachments_inspection_item_id",
                schema: "procurement",
                table: "case_attachments",
                column: "inspection_item_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_case_attachments_owner",
                schema: "procurement",
                table: "case_attachments",
                sql: "(owner_type = 'Case' AND negotiation_id IS NULL AND check_id IS NULL AND inspection_id IS NULL AND inspection_item_id IS NULL) OR (owner_type = 'Negotiation' AND negotiation_id IS NOT NULL AND check_id IS NULL AND inspection_id IS NULL AND inspection_item_id IS NULL) OR (owner_type = 'Check' AND negotiation_id IS NULL AND check_id IS NOT NULL AND inspection_id IS NULL AND inspection_item_id IS NULL) OR (owner_type = 'Inspection' AND negotiation_id IS NULL AND check_id IS NULL AND inspection_id IS NOT NULL AND inspection_item_id IS NULL) OR (owner_type = 'InspectionItem' AND negotiation_id IS NULL AND check_id IS NULL AND inspection_id IS NULL AND inspection_item_id IS NOT NULL)");

            migrationBuilder.CreateIndex(
                name: "ix_case_check_template_items_organization_id_sort_order",
                schema: "procurement",
                table: "case_check_template_items",
                columns: new[] { "organization_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_case_check_template_items_organization_id_title",
                schema: "procurement",
                table: "case_check_template_items",
                columns: new[] { "organization_id", "title" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inspection_template_items_organization_id_key",
                schema: "procurement",
                table: "inspection_template_items",
                columns: new[] { "organization_id", "key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inspection_template_items_organization_id_sort_order",
                schema: "procurement",
                table: "inspection_template_items",
                columns: new[] { "organization_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_site_inspection_items_inspection_id_template_item_id",
                schema: "procurement",
                table: "site_inspection_items",
                columns: new[] { "inspection_id", "template_item_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_site_inspection_items_template_item_id",
                schema: "procurement",
                table: "site_inspection_items",
                column: "template_item_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_inspections_inspector_employee_id",
                schema: "procurement",
                table: "site_inspections",
                column: "inspector_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_site_inspections_property_case_id",
                schema: "procurement",
                table: "site_inspections",
                column: "property_case_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_case_attachments_inspection_id",
                schema: "procurement",
                table: "case_attachments",
                column: "inspection_id",
                principalSchema: "procurement",
                principalTable: "site_inspections",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_case_attachments_inspection_item_id",
                schema: "procurement",
                table: "case_attachments",
                column: "inspection_item_id",
                principalSchema: "procurement",
                principalTable: "site_inspection_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_case_checks_template_item_id",
                schema: "procurement",
                table: "case_checks",
                column: "template_item_id",
                principalSchema: "procurement",
                principalTable: "case_check_template_items",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_property_cases_acquired_by_employee_id",
                schema: "procurement",
                table: "property_cases",
                column: "acquired_by_employee_id",
                principalSchema: "organization",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_case_attachments_inspection_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_case_attachments_inspection_item_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropForeignKey(
                name: "fk_case_checks_template_item_id",
                schema: "procurement",
                table: "case_checks");

            migrationBuilder.DropForeignKey(
                name: "fk_property_cases_acquired_by_employee_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropTable(
                name: "case_check_template_items",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "site_inspection_items",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "site_inspections",
                schema: "procurement");

            migrationBuilder.DropTable(
                name: "inspection_template_items",
                schema: "procurement");

            migrationBuilder.DropIndex(
                name: "ix_property_cases_acquired_by_employee_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropIndex(
                name: "ix_case_checks_template_item_id",
                schema: "procurement",
                table: "case_checks");

            migrationBuilder.DropIndex(
                name: "ix_case_attachments_inspection_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropIndex(
                name: "ix_case_attachments_inspection_item_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_case_attachments_owner",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DeleteData(
                schema: "workflow",
                table: "stages",
                keyColumn: "id",
                keyValue: "acquired");

            migrationBuilder.DropColumn(
                name: "acquired_at",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "acquired_by_employee_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "acquisition_comment",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "acquisition_date",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "acquisition_price",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "agreed_price",
                schema: "procurement",
                table: "negotiations");

            migrationBuilder.DropColumn(
                name: "buyer_offer",
                schema: "procurement",
                table: "negotiations");

            migrationBuilder.DropColumn(
                name: "next_step_due_at",
                schema: "procurement",
                table: "negotiations");

            migrationBuilder.DropColumn(
                name: "seller_price",
                schema: "procurement",
                table: "negotiations");

            migrationBuilder.DropColumn(
                name: "description_snapshot",
                schema: "procurement",
                table: "case_checks");

            migrationBuilder.DropColumn(
                name: "template_item_id",
                schema: "procurement",
                table: "case_checks");

            migrationBuilder.DropColumn(
                name: "template_item_version",
                schema: "procurement",
                table: "case_checks");

            migrationBuilder.DropColumn(
                name: "description",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropColumn(
                name: "inspection_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.DropColumn(
                name: "inspection_item_id",
                schema: "procurement",
                table: "case_attachments");

            migrationBuilder.AlterTable(
                name: "stages",
                schema: "workflow",
                comment: "Стабильные стадии первого процесса закупки: new/analysis/clarify/monitor/rejected/pending_head/returned/approved. Approved означает дальнейшую работу, не покупку.",
                oldComment: "Стабильные стадии первого процесса закупки, включая terminal acquired. Approved означает дальнейшую работу, не покупку.");

            migrationBuilder.AlterTable(
                name: "negotiations",
                schema: "procurement",
                comment: "Неизменяемая специализированная история переговоров PropertyCase. Публичная цена, цена продавца, предложение покупателя и согласованная цена не смешиваются.",
                oldComment: "Неизменяемый CRM-журнал событий коммуникации PropertyCase. Цена продавца, предложение покупателя и согласованная цена опциональны и не смешиваются с публичной ценой источника.");

            migrationBuilder.AlterTable(
                name: "case_attachments",
                schema: "procurement",
                comment: "Связи вложений с PropertyCase, переговорами или проверками; доступ всегда наследуется от owning PropertyCase.",
                oldComment: "Связи вложений с PropertyCase, переговорами, проверками, осмотром или его пунктом; доступ всегда наследуется от owning PropertyCase.");

            migrationBuilder.AlterColumn<string>(
                name: "stage_id",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                comment: "Текущая стадия первичной закупки. Approved разрешает следующий анализ, не обозначает завершённую покупку.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldComment: "Текущая стадия закупки. Только acquired обозначает подтверждённую завершённую покупку.");

            migrationBuilder.AddColumn<decimal>(
                name: "amount",
                schema: "procurement",
                table: "negotiations",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: false,
                defaultValue: 0m,
                comment: "Денежное значение переговоров decimal; валюта хранится отдельно.");

            migrationBuilder.AddColumn<string>(
                name: "price_type",
                schema: "procurement",
                table: "negotiations",
                type: "text",
                nullable: false,
                defaultValue: "",
                comment: "Тип цены переговоров: стартовая, предложение продавца, предложение покупателя или согласованная; типы не перезаписывают друг друга.");

            migrationBuilder.AlterColumn<string>(
                name: "owner_type",
                schema: "procurement",
                table: "case_attachments",
                type: "text",
                nullable: false,
                comment: "Owning business object вложения: PropertyCase, переговоры или проверка.",
                oldClrType: typeof(string),
                oldType: "text",
                oldComment: "Owning business object вложения: PropertyCase, событие переговоров, проверка, осмотр или пункт осмотра.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_case_attachments_owner",
                schema: "procurement",
                table: "case_attachments",
                sql: "(owner_type = 'Case' AND negotiation_id IS NULL AND check_id IS NULL) OR (owner_type = 'Negotiation' AND negotiation_id IS NOT NULL AND check_id IS NULL) OR (owner_type = 'Check' AND negotiation_id IS NULL AND check_id IS NOT NULL)");
        }
    }
}
