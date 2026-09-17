using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CollectorServerManagement : Migration
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
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheduler_status", x => x.id);
                },
                comment: "Текущее техническое состояние server scheduler: последний старт, успешный цикл, ошибка и число поставленных работ.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "scheduler_status",
                schema: "collection");

            migrationBuilder.Sql("""
                DELETE FROM identity.role_permissions WHERE permission_id = 'collection.read';
                DELETE FROM identity.permissions WHERE id = 'collection.read';
                """);
        }
    }
}
