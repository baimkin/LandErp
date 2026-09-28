using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UxRepairTaskResultsAndPhotoEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                schema: "workflow",
                table: "work_tasks",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "completed_by_employee_id",
                schema: "workflow",
                table: "work_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "result_document_json",
                schema: "workflow",
                table: "work_tasks",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "source_negotiation_id",
                schema: "workflow",
                table: "work_tasks",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "content_sha256",
                schema: "catalog",
                table: "photo_fingerprints",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "photo_evidence_json",
                schema: "catalog",
                table: "duplicate_candidates",
                type: "jsonb",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<Guid>(
                name: "negotiation_id",
                schema: "foundation",
                table: "business_timeline",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "task_id",
                schema: "foundation",
                table: "business_timeline",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_work_tasks_completed_by_employee_id",
                schema: "workflow",
                table: "work_tasks",
                column: "completed_by_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_work_tasks_source_negotiation_id",
                schema: "workflow",
                table: "work_tasks",
                column: "source_negotiation_id");

            migrationBuilder.AddForeignKey(
                name: "fk_work_tasks_completed_by_employee_id",
                schema: "workflow",
                table: "work_tasks",
                column: "completed_by_employee_id",
                principalSchema: "organization",
                principalTable: "employees",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_work_tasks_source_negotiation_id",
                schema: "workflow",
                table: "work_tasks",
                column: "source_negotiation_id",
                principalSchema: "procurement",
                principalTable: "negotiations",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_work_tasks_completed_by_employee_id",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropForeignKey(
                name: "fk_work_tasks_source_negotiation_id",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "ix_work_tasks_completed_by_employee_id",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropIndex(
                name: "ix_work_tasks_source_negotiation_id",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "completed_at",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "completed_by_employee_id",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "result_document_json",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "source_negotiation_id",
                schema: "workflow",
                table: "work_tasks");

            migrationBuilder.DropColumn(
                name: "content_sha256",
                schema: "catalog",
                table: "photo_fingerprints");

            migrationBuilder.DropColumn(
                name: "photo_evidence_json",
                schema: "catalog",
                table: "duplicate_candidates");

            migrationBuilder.DropColumn(
                name: "negotiation_id",
                schema: "foundation",
                table: "business_timeline");

            migrationBuilder.DropColumn(
                name: "task_id",
                schema: "foundation",
                table: "business_timeline");
        }
    }
}
