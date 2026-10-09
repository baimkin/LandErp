using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable
#pragma warning disable CA1861 // EF generates short immutable column arrays for migration operations.

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ListingComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "comment_types",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_comment_types", x => x.id);
                    table.ForeignKey(
                        name: "fk_comment_types_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Расширяемый справочник видов комментариев организации; вид является категорией, а не готовой текстовой фразой.");

            migrationBuilder.CreateTable(
                name: "listing_comments",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, создавший актуальный комментарий."),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Сотрудник, последним изменивший актуальный комментарий."),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false, comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_comments", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_comments_comment_type_id",
                        column: x => x.comment_type_id,
                        principalSchema: "catalog",
                        principalTable: "comment_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_created_by_employee_id",
                        column: x => x.created_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "catalog",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_updated_by_employee_id",
                        column: x => x.updated_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Одно актуальное значение каждого вида комментария для объявления; прежние значения сохраняются отдельной историей.");

            migrationBuilder.CreateTable(
                name: "listing_comments_history",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                    listing_comment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                    comment_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    old_created_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    old_updated_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    old_updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    changed_by_employee_id = table.Column<Guid>(type: "uuid", nullable: false, comment: "Автор текущей операции UPDATE или DELETE, переданный транзакционным PostgreSQL-контекстом."),
                    changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, comment: "UTC время срабатывания PostgreSQL-триггера истории комментариев."),
                    operation = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, comment: "Тип операции, сформировавшей историю: Update или Delete.")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_listing_comments_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_listing_comments_history_changed_by_employee_id",
                        column: x => x.changed_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_history_comment_type_id",
                        column: x => x.comment_type_id,
                        principalSchema: "catalog",
                        principalTable: "comment_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_history_listing_id",
                        column: x => x.listing_id,
                        principalSchema: "catalog",
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_history_old_created_by_employee_id",
                        column: x => x.old_created_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_history_old_updated_by_employee_id",
                        column: x => x.old_updated_by_employee_id,
                        principalSchema: "organization",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_listing_comments_history_organization_id",
                        column: x => x.organization_id,
                        principalSchema: "organization",
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                },
                comment: "Неизменяемая история предыдущих значений и удалений комментариев, формируемая PostgreSQL-триггером с автором операции.");

            migrationBuilder.CreateIndex(
                name: "ix_comment_types_organization_id_is_active_sort_order",
                schema: "catalog",
                table: "comment_types",
                columns: new[] { "organization_id", "is_active", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_comment_types_organization_id_name",
                schema: "catalog",
                table: "comment_types",
                columns: new[] { "organization_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_comment_type_id",
                schema: "catalog",
                table: "listing_comments",
                column: "comment_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_created_by_employee_id",
                schema: "catalog",
                table: "listing_comments",
                column: "created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_listing_id",
                schema: "catalog",
                table: "listing_comments",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_organization_id_listing_id",
                schema: "catalog",
                table: "listing_comments",
                columns: new[] { "organization_id", "listing_id" });

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_organization_id_listing_id_comment_type_id",
                schema: "catalog",
                table: "listing_comments",
                columns: new[] { "organization_id", "listing_id", "comment_type_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_updated_by_employee_id",
                schema: "catalog",
                table: "listing_comments",
                column: "updated_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_history_changed_by_employee_id",
                schema: "catalog",
                table: "listing_comments_history",
                column: "changed_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_history_comment_type_id",
                schema: "catalog",
                table: "listing_comments_history",
                column: "comment_type_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_history_listing_comment_id",
                schema: "catalog",
                table: "listing_comments_history",
                column: "listing_comment_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_history_listing_id",
                schema: "catalog",
                table: "listing_comments_history",
                column: "listing_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_history_old_created_by_employee_id",
                schema: "catalog",
                table: "listing_comments_history",
                column: "old_created_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_history_old_updated_by_employee_id",
                schema: "catalog",
                table: "listing_comments_history",
                column: "old_updated_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_listing_comments_history_organization_id_listing_id_comment_type_id_changed_at",
                schema: "catalog",
                table: "listing_comments_history",
                columns: new[] { "organization_id", "listing_id", "comment_type_id", "changed_at" });

            migrationBuilder.Sql("""
                INSERT INTO catalog.comment_types
                    (id, organization_id, name, description, sort_order, is_active, version)
                SELECT gen_random_uuid(), organization.id, 'Общий комментарий',
                    'Основной рабочий комментарий к входящему объявлению.', 10, TRUE, 1
                FROM organization.organizations organization
                WHERE NOT EXISTS (
                    SELECT 1 FROM catalog.comment_types existing
                    WHERE existing.organization_id = organization.id
                        AND existing.name = 'Общий комментарий');

                CREATE INDEX ix_listing_comments_text_search
                    ON catalog.listing_comments
                    USING gin (to_tsvector('simple', text));

                CREATE FUNCTION catalog.capture_listing_comment_history()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $function$
                DECLARE
                    actor_text text;
                BEGIN
                    IF TG_OP = 'UPDATE' AND OLD.text IS NOT DISTINCT FROM NEW.text THEN
                        RETURN NEW;
                    END IF;

                    actor_text := current_setting('landerp.comment_actor_employee_id', true);
                    IF actor_text IS NULL OR actor_text = '' THEN
                        RAISE EXCEPTION 'Comment actor transaction context is required.'
                            USING ERRCODE = '22023';
                    END IF;

                    INSERT INTO catalog.listing_comments_history
                        (id, organization_id, listing_comment_id, listing_id, comment_type_id,
                         old_text, old_created_by_employee_id, old_created_at,
                         old_updated_by_employee_id, old_updated_at,
                         changed_by_employee_id, changed_at, operation)
                    VALUES
                        (gen_random_uuid(), OLD.organization_id, OLD.id, OLD.listing_id, OLD.comment_type_id,
                         OLD.text, OLD.created_by_employee_id, OLD.created_at,
                         OLD.updated_by_employee_id, OLD.updated_at,
                         actor_text::uuid, clock_timestamp(),
                         CASE WHEN TG_OP = 'DELETE' THEN 'Delete' ELSE 'Update' END);

                    IF TG_OP = 'DELETE' THEN
                        RETURN OLD;
                    END IF;
                    RETURN NEW;
                END;
                $function$;

                CREATE TRIGGER listing_comments_history_trigger
                    BEFORE UPDATE OR DELETE ON catalog.listing_comments
                    FOR EACH ROW EXECUTE FUNCTION catalog.capture_listing_comment_history();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS listing_comments_history_trigger ON catalog.listing_comments;
                DROP FUNCTION IF EXISTS catalog.capture_listing_comment_history();
                """);

            migrationBuilder.DropTable(
                name: "listing_comments",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "listing_comments_history",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "comment_types",
                schema: "catalog");
        }
    }
}
