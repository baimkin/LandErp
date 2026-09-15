using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF-generated one-shot migration arrays are not hot-path allocations.

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Phase1CatalogProcurementBoundary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_property_cases_listing_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropIndex(
                name: "ix_listings_organization_id_source_external_id",
                schema: "catalog",
                table: "listings");

            migrationBuilder.AlterTable(
                name: "property_cases",
                schema: "procurement",
                comment: "Самостоятельные рабочие объекты закупки. Внешние предложения связаны отдельно и не владеют жизненным циклом кейса.",
                oldComment: "Рабочие кейсы закупки по объявлениям. Хранят первичный анализ, стадию, менеджера и ссылки на общие task/assignment; не полный Due Diligence.");

            migrationBuilder.AlterTable(
                name: "listings",
                schema: "catalog",
                comment: "Универсальные входящие предложения Catalog из автоматических и ручных источников; не идентичность земельного участка.",
                oldComment: "Текущее известное состояние объявления, не идентичность земельного участка. Отсутствие поля не стирает ранее полученное значение.");

            migrationBuilder.AlterColumn<Guid>(
                name: "listing_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "cadastral_number",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Кадастровый номер, если известен; не является обязательной или единственной идентичностью объекта.");

            migrationBuilder.AddColumn<string>(
                name: "currency",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "RUB",
                comment: "ISO 4217 валюта денежного значения; Stage 1 принимает RUB.");

            migrationBuilder.AddColumn<Guid>(
                name: "department_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: true,
                comment: "Подразделение закупки, ограничивающее Department visibility объекта.");

            migrationBuilder.AddColumn<string>(
                name: "facts_provenance",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "Migration from legacy Listing snapshot",
                comment: "Происхождение первоначальных рабочих фактов кейса; migration/system не означает подтверждение человеком.");

            migrationBuilder.AddColumn<Guid>(
                name: "team_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: true,
                comment: "Рабочая команда для Team scope. Отсутствие команды не расширяет область доступа.");

            migrationBuilder.AddColumn<decimal>(
                name: "working_area_square_meters",
                schema: "procurement",
                table: "property_cases",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Рабочая площадь PropertyCase в м²; исходные значения каждого источника сохраняются отдельно.");

            migrationBuilder.AddColumn<string>(
                name: "working_location",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: true,
                comment: "Рабочее местоположение PropertyCase, независимое от текущей доступности внешнего объявления.");

            migrationBuilder.AddColumn<decimal>(
                name: "working_price",
                schema: "procurement",
                table: "property_cases",
                type: "numeric(19,4)",
                precision: 19,
                scale: 4,
                nullable: true,
                comment: "Рабочая цена PropertyCase; не перезаписывается автоматически при изменении публичной цены источника.");

            migrationBuilder.AddColumn<string>(
                name: "working_title",
                schema: "procurement",
                table: "property_cases",
                type: "character varying(20000)",
                maxLength: 20000,
                nullable: false,
                defaultValue: "Объект без названия",
                comment: "Рабочее название PropertyCase, сохраняемое независимо от последующих изменений внешних источников.");

            migrationBuilder.AlterColumn<string>(
                name: "url",
                schema: "catalog",
                table: "listings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                comment: "Опциональная HTTPS-ссылка на источник; ручное предложение может существовать без URL.",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000);

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_observed_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Самый новый принятый UTC момент наблюдения для обновления current state.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "Самый новый принятый UTC момент наблюдения для обновления current state.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "first_observed_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Самый ранний известный UTC момент наблюдения этого объявления.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldComment: "Самый ранний известный UTC момент наблюдения этого объявления.");

            migrationBuilder.AlterColumn<string>(
                name: "external_id",
                schema: "catalog",
                table: "listings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true,
                comment: "Опциональный внешний ID в конкретном источнике; отсутствие не заменяется пустой строкой или synthetic ID.",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512);

            migrationBuilder.AddColumn<string>(
                name: "cadastral_number",
                schema: "catalog",
                table: "listings",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true,
                comment: "Кадастровый номер, если известен; не является обязательной или единственной идентичностью объекта.");

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_employee_id",
                schema: "catalog",
                table: "listings",
                type: "uuid",
                nullable: true,
                comment: "Сотрудник, вручную добавивший входящее предложение; отсутствует у автоматического Collector ingress.");

            migrationBuilder.AddColumn<string>(
                name: "disposition",
                schema: "catalog",
                table: "listings",
                type: "text",
                nullable: false,
                defaultValue: "Incoming",
                comment: "Текущее решение первичного отбора: входящее, мониторинг, в работе, отклонено, снято или продано.");

            migrationBuilder.AddColumn<string>(
                name: "ingestion_kind",
                schema: "catalog",
                table: "listings",
                type: "text",
                nullable: false,
                defaultValue: "Collector",
                comment: "Способ поступления: Collector, сотрудник, миграция или интеграция; ручной путь не создаёт фиктивные jobs/observations.");

            migrationBuilder.AddColumn<string>(
                name: "ingress_comment",
                schema: "catalog",
                table: "listings",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                comment: "Комментарий о происхождении или контексте ручного входящего предложения.");

            migrationBuilder.AddColumn<string>(
                name: "provenance",
                schema: "catalog",
                table: "listings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "Collector V1 (legacy)",
                comment: "Человекочитаемое происхождение входящего элемента или подтверждения связи без технических секретов.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "received_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                comment: "UTC момент поступления элемента в Catalog; для автоматического источника отличается от времени наблюдения при задержке доставки.");

            migrationBuilder.CreateTable(
                name: "property_case_source_links",
                schema: "procurement",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    property_case_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Самостоятельный рабочий объект закупки, к которому относится источник."),
                    catalog_item_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Входящий элемент Catalog, связанный с PropertyCase; подтверждённая связь уникальна для элемента."),
                    confirmed = table.Column<bool>(type: "boolean", nullable: false, comment: "Подтверждённая связь владеет принадлежностью источника одному PropertyCase; возможные совпадения не подтверждены."),
                    relation_type = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Тип связи источника с кейсом; не изменяет жизненный цикл самого Catalog item."),
                    actor_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provenance = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false, comment: "Человекочитаемое происхождение входящего элемента или подтверждения связи без технических секретов."),
                    reviewed_data_revision = table.Column<long>(type: "bigint", nullable: false, comment: "Версия публичных данных, рассмотренная при последнем решении. Новые данные возвращают объект в изменившуюся очередь."),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_property_case_source_links", x => x.id);
                    table.ForeignKey(
                        name: "fk_property_case_source_links_actor_employee_id",
                        column: x => x.actor_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_property_case_source_links_catalog_item_id",
                        column: x => x.catalog_item_id,
                        principalSchema: "catalog",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_property_case_source_links_property_case_id",
                        column: x => x.property_case_id,
                        principalSchema: "procurement",
                        principalTable: "property_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Подтверждённые и возможные связи входящих элементов Catalog с PropertyCase. Один входящий элемент может иметь не более одной подтверждённой связи.");

            migrationBuilder.Sql("""
                UPDATE catalog.listings
                SET ingestion_kind = 'Collector',
                    provenance = 'Collector V1 (legacy)',
                    disposition = CASE WHEN EXISTS (
                        SELECT 1 FROM procurement.property_cases pc WHERE pc.listing_id = catalog.listings.id
                    ) THEN 'InWork' ELSE 'Incoming' END,
                    received_at = recorded_at;

                UPDATE procurement.property_cases pc
                SET working_title = COALESCE(l.title, 'Объект без названия'),
                    working_price = l.price,
                    currency = l.currency,
                    working_area_square_meters = l.area_square_meters,
                    working_location = l.location,
                    department_id = (SELECT ea.org_unit_id FROM organization.employee_assignments ea WHERE ea.employee_id = pc.manager_employee_id LIMIT 1),
                    team_id = (SELECT ea.team_id FROM organization.employee_assignments ea WHERE ea.employee_id = pc.manager_employee_id LIMIT 1),
                    facts_provenance = 'Migration from legacy Listing snapshot'
                FROM catalog.listings l
                WHERE pc.listing_id = l.id;

                INSERT INTO procurement.property_case_source_links
                    (id, organization_id, property_case_id, catalog_item_id, confirmed, relation_type,
                     actor_employee_id, provenance, reviewed_data_revision, recorded_at)
                SELECT pc.listing_id, pc.organization_id, pc.id, pc.listing_id, true, 'Source',
                       NULL, 'Migration from legacy PropertyCase.ListingId', pc.reviewed_data_revision, pc.recorded_at
                FROM procurement.property_cases pc
                WHERE pc.listing_id IS NOT NULL;

                UPDATE procurement.property_cases SET listing_id = NULL WHERE listing_id IS NOT NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_department_id",
                schema: "procurement",
                table: "property_cases",
                column: "department_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_listing_id",
                schema: "procurement",
                table: "property_cases",
                column: "listing_id",
                unique: true,
                filter: "listing_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_team_id",
                schema: "procurement",
                table: "property_cases",
                column: "team_id");

            migrationBuilder.CreateIndex(
                name: "ix_listings_created_by_employee_id",
                schema: "catalog",
                table: "listings",
                column: "created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_listings_organization_id_source_external_id",
                schema: "catalog",
                table: "listings",
                columns: new[] { "organization_id", "source", "external_id" },
                unique: true,
                filter: "external_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_property_case_source_links_actor_employee_id",
                schema: "procurement",
                table: "property_case_source_links",
                column: "actor_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_property_case_source_links_catalog_item_id",
                schema: "procurement",
                table: "property_case_source_links",
                column: "catalog_item_id",
                unique: true,
                filter: "confirmed");

            migrationBuilder.CreateIndex(
                name: "ix_property_case_source_links_property_case_id_catalog_item_id",
                schema: "procurement",
                table: "property_case_source_links",
                columns: new[] { "property_case_id", "catalog_item_id" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_listings_created_by_employee_id",
                schema: "catalog",
                table: "listings",
                column: "created_by_employee_id",
                principalSchema: "organization",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_property_cases_department_id",
                schema: "procurement",
                table: "property_cases",
                column: "department_id",
                principalSchema: "organization",
                principalTable: "org_units",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_property_cases_team_id",
                schema: "procurement",
                table: "property_cases",
                column: "team_id",
                principalSchema: "organization",
                principalTable: "teams",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                UPDATE procurement.property_cases AS property_case
                SET listing_id = source_link.catalog_item_id
                FROM procurement.property_case_source_links AS source_link
                WHERE source_link.property_case_id = property_case.id
                  AND source_link.confirmed = TRUE
                  AND property_case.listing_id IS NULL;
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_listings_created_by_employee_id",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropForeignKey(
                name: "fk_property_cases_department_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropForeignKey(
                name: "fk_property_cases_team_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropTable(
                name: "property_case_source_links",
                schema: "procurement");

            migrationBuilder.DropIndex(
                name: "ix_property_cases_department_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropIndex(
                name: "ix_property_cases_listing_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropIndex(
                name: "ix_property_cases_team_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropIndex(
                name: "ix_listings_created_by_employee_id",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropIndex(
                name: "ix_listings_organization_id_source_external_id",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "cadastral_number",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "currency",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "department_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "facts_provenance",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "team_id",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "working_area_square_meters",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "working_location",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "working_price",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "working_title",
                schema: "procurement",
                table: "property_cases");

            migrationBuilder.DropColumn(
                name: "cadastral_number",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "created_by_employee_id",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "disposition",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "ingestion_kind",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "ingress_comment",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "provenance",
                schema: "catalog",
                table: "listings");

            migrationBuilder.DropColumn(
                name: "received_at",
                schema: "catalog",
                table: "listings");

            migrationBuilder.AlterTable(
                name: "property_cases",
                schema: "procurement",
                comment: "Рабочие кейсы закупки по объявлениям. Хранят первичный анализ, стадию, менеджера и ссылки на общие task/assignment; не полный Due Diligence.",
                oldComment: "Самостоятельные рабочие объекты закупки. Внешние предложения связаны отдельно и не владеют жизненным циклом кейса.");

            migrationBuilder.AlterTable(
                name: "listings",
                schema: "catalog",
                comment: "Текущее известное состояние объявления, не идентичность земельного участка. Отсутствие поля не стирает ранее полученное значение.",
                oldComment: "Универсальные входящие предложения Catalog из автоматических и ручных источников; не идентичность земельного участка.");

            migrationBuilder.AlterColumn<Guid>(
                name: "listing_id",
                schema: "procurement",
                table: "property_cases",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "url",
                schema: "catalog",
                table: "listings",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true,
                oldComment: "Опциональная HTTPS-ссылка на источник; ручное предложение может существовать без URL.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "last_observed_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                comment: "Самый новый принятый UTC момент наблюдения для обновления current state.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Самый новый принятый UTC момент наблюдения для обновления current state.");

            migrationBuilder.AlterColumn<DateTimeOffset>(
                name: "first_observed_at",
                schema: "catalog",
                table: "listings",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)),
                comment: "Самый ранний известный UTC момент наблюдения этого объявления.",
                oldClrType: typeof(DateTimeOffset),
                oldType: "timestamp with time zone",
                oldNullable: true,
                oldComment: "Самый ранний известный UTC момент наблюдения этого объявления.");

            migrationBuilder.AlterColumn<string>(
                name: "external_id",
                schema: "catalog",
                table: "listings",
                type: "character varying(512)",
                maxLength: 512,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(512)",
                oldMaxLength: 512,
                oldNullable: true,
                oldComment: "Опциональный внешний ID в конкретном источнике; отсутствие не заменяется пустой строкой или synthetic ID.");

            migrationBuilder.CreateIndex(
                name: "ix_property_cases_listing_id",
                schema: "procurement",
                table: "property_cases",
                column: "listing_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listings_organization_id_source_external_id",
                schema: "catalog",
                table: "listings",
                columns: new[] { "organization_id", "source", "external_id" },
                unique: true);
        }
    }
}
