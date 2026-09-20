using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260920165000_IncomingDuplicateCandidates")]
public partial class IncomingDuplicateCandidates : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "duplicate_candidates",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false,
                    comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                candidate_listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                score = table.Column<int>(type: "integer", nullable: false),
                reasons_json = table.Column<string>(type: "jsonb", nullable: false),
                status = table.Column<string>(type: "text", nullable: false),
                reviewed_by_employee_id = table.Column<Guid>(type: "uuid", nullable: true),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false,
                    comment: "UTC момент записи факта в LandErp; не заменяет неизвестную дату действия факта в реальном мире."),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                version = table.Column<long>(type: "bigint", nullable: false,
                    comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_duplicate_candidates", x => x.id);
                table.ForeignKey("fk_duplicate_candidates_candidate_listing_id", x => x.candidate_listing_id,
                    principalSchema: "catalog", principalTable: "listings", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_duplicate_candidates_listing_id", x => x.listing_id,
                    principalSchema: "catalog", principalTable: "listings", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_duplicate_candidates_organization_id", x => x.organization_id,
                    principalSchema: "organization", principalTable: "organizations", principalColumn: "id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_duplicate_candidates_reviewed_by_employee_id", x => x.reviewed_by_employee_id,
                    principalSchema: "organization", principalTable: "employees", principalColumn: "id", onDelete: ReferentialAction.Restrict);
            },
            comment: "Сохранённые кандидаты на совпадение двух входящих предложений. Решение менеджера не удаляется и не пересоздаётся повторным matching.");

        migrationBuilder.CreateIndex("ix_duplicate_candidates_candidate_listing_id", "catalog", "duplicate_candidates", "candidate_listing_id");
        migrationBuilder.CreateIndex("ix_duplicate_candidates_listing_id", "catalog", "duplicate_candidates", "listing_id");
        migrationBuilder.CreateIndex("ix_duplicate_candidates_reviewed_by_employee_id", "catalog", "duplicate_candidates", "reviewed_by_employee_id");
        migrationBuilder.CreateIndex("ix_duplicate_candidates_organization_id_listing_id_candidate_listing_id",
            "catalog", "duplicate_candidates", new[] { "organization_id", "listing_id", "candidate_listing_id" }, unique: true);
        migrationBuilder.CreateIndex("ix_duplicate_candidates_organization_id_status_recorded_at",
            "catalog", "duplicate_candidates", new[] { "organization_id", "status", "recorded_at" });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "duplicate_candidates", schema: "catalog");
}
