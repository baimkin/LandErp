using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CollectorMachineProtocol : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activation_expires_at",
                schema: "collection",
                table: "agents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC срок действия одноразового кода подключения Parser.");

            migrationBuilder.AddColumn<string>(
                name: "activation_hash",
                schema: "collection",
                table: "agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "SHA-256 verifier одноразового кода подключения; открытый код не хранится.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "activation_used_at",
                schema: "collection",
                table: "agents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC момент успешного обмена одноразового кода на постоянную machine credential.");

            migrationBuilder.AddColumn<string>(
                name: "attention_code",
                schema: "collection",
                table: "agents",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "Текущий очищаемый machine code ручного внимания: CAPTCHA, вход или rate limit.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_activity_at",
                schema: "collection",
                table: "agents",
                type: "timestamp with time zone",
                nullable: true,
                comment: "UTC момент последнего полезного действия Parser внутри текущей работы.");

            migrationBuilder.AddColumn<int>(
                name: "progress_current_page",
                schema: "collection",
                table: "agents",
                type: "integer",
                nullable: true,
                comment: "Текущая страница источника, сообщённая Parser, если применимо.");

            migrationBuilder.AddColumn<int>(
                name: "progress_max_pages",
                schema: "collection",
                table: "agents",
                type: "integer",
                nullable: true,
                comment: "Серверный предел страниц для текущей работы, сообщённый Parser.");

            migrationBuilder.AddColumn<int>(
                name: "progress_processed",
                schema: "collection",
                table: "agents",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Последнее число обработанных элементов, сообщённое heartbeat.");

            migrationBuilder.AddColumn<int>(
                name: "progress_total",
                schema: "collection",
                table: "agents",
                type: "integer",
                nullable: true,
                comment: "Ожидаемое общее число элементов, если источник смог его определить.");

            migrationBuilder.AddColumn<string>(
                name: "runtime_state",
                schema: "collection",
                table: "agents",
                type: "text",
                nullable: false,
                defaultValue: "Idle",
                comment: "Последнее заявленное Parser состояние выполнения без browser-specific деталей.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "activation_expires_at",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "activation_hash",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "activation_used_at",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "attention_code",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "last_activity_at",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "progress_current_page",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "progress_max_pages",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "progress_processed",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "progress_total",
                schema: "collection",
                table: "agents");

            migrationBuilder.DropColumn(
                name: "runtime_state",
                schema: "collection",
                table: "agents");
        }
    }
}
