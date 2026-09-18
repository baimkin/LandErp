using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CollectionResultRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "consecutive_failures",
                schema: "collection",
                table: "search_configurations",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Число последовательных неуспешных запусков; успешный или ограниченный лимитом запуск сбрасывает счётчик.");

            migrationBuilder.AddColumn<string>(
                name: "coverage_json",
                schema: "collection",
                table: "jobs",
                type: "jsonb",
                nullable: true,
                comment: "Факты о полноте выдачи, переданные Parser; заявленное источником количество является подсказкой.");

            migrationBuilder.AddColumn<string>(
                name: "reason_code",
                schema: "collection",
                table: "jobs",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "",
                comment: "Машинный код причины финального результата Parser.");

            migrationBuilder.AddColumn<bool>(
                name: "requires_operator_attention",
                schema: "collection",
                table: "jobs",
                type: "boolean",
                nullable: false,
                defaultValue: false,
                comment: "Требуется действие оператора; ожидаемый автоматический повтор сам по себе внимания не требует.");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "retry_at",
                schema: "collection",
                table: "jobs",
                type: "timestamp with time zone",
                nullable: true,
                comment: "Срок дополнительной серверной попытки, независимый от обычного расписания поиска.");

            migrationBuilder.AddColumn<int>(
                name: "retry_attempt",
                schema: "collection",
                table: "jobs",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                comment: "Номер дополнительной попытки в ограниченной цепочке восстановления.");

            migrationBuilder.AddColumn<Guid>(
                name: "retry_of_job_id",
                schema: "collection",
                table: "jobs",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "warnings_json",
                schema: "collection",
                table: "jobs",
                type: "jsonb",
                nullable: false,
                defaultValueSql: "'[]'::jsonb",
                comment: "Ограниченный список машинных предупреждений финального результата.");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_retry_at",
                schema: "collection",
                table: "jobs",
                column: "retry_at",
                filter: "retry_at IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_jobs_retry_of_job_id",
                schema: "collection",
                table: "jobs",
                column: "retry_of_job_id",
                unique: true,
                filter: "retry_of_job_id IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "fk_jobs_retry_of_job_id",
                schema: "collection",
                table: "jobs",
                column: "retry_of_job_id",
                principalSchema: "collection",
                principalTable: "jobs",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_jobs_retry_of_job_id",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_retry_at",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropIndex(
                name: "ix_jobs_retry_of_job_id",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "consecutive_failures",
                schema: "collection",
                table: "search_configurations");

            migrationBuilder.DropColumn(
                name: "coverage_json",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "reason_code",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "requires_operator_attention",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "retry_at",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "retry_attempt",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "retry_of_job_id",
                schema: "collection",
                table: "jobs");

            migrationBuilder.DropColumn(
                name: "warnings_json",
                schema: "collection",
                table: "jobs");
        }
    }
}
