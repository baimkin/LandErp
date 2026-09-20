using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260920193000_DuplicateImageFingerprints")]
public partial class DuplicateImageFingerprints : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "duplicate_settings",
            schema: "catalog",
            columns: table => new
            {
                organization_id = table.Column<Guid>(type: "uuid", nullable: false,
                    comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                candidate_threshold = table.Column<int>(type: "integer", nullable: false),
                description_similarity_percent = table.Column<int>(type: "integer", nullable: false),
                area_tolerance_percent = table.Column<int>(type: "integer", nullable: false),
                photo_hamming_distance = table.Column<int>(type: "integer", nullable: false),
                strong_photo_matches = table.Column<int>(type: "integer", nullable: false),
                common_photo_max_listings = table.Column<int>(type: "integer", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false,
                    comment: "Версия для optimistic concurrency. Каждое изменение увеличивает значение; stale commands отклоняются.")
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_duplicate_settings", x => x.organization_id);
                table.ForeignKey("fk_duplicate_settings_organization_id", x => x.organization_id,
                    principalSchema: "organization", principalTable: "organizations", principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            },
            comment: "Настраиваемые пороги определения дублей для организации; изменения применяются без перезапуска приложения.");

        migrationBuilder.CreateTable(
            name: "photo_fingerprints",
            schema: "catalog",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                organization_id = table.Column<Guid>(type: "uuid", nullable: false,
                    comment: "Организация-владелец записи; граница изоляции доступа, устанавливаемая сервером."),
                listing_id = table.Column<Guid>(type: "uuid", nullable: false),
                photo_index = table.Column<int>(type: "integer", nullable: false),
                url_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                perceptual_hash = table.Column<long>(type: "bigint", nullable: true),
                status = table.Column<string>(type: "text", nullable: false),
                failure_count = table.Column<int>(type: "integer", nullable: false),
                retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                source_data_revision = table.Column<long>(type: "bigint", nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_photo_fingerprints", x => x.id);
                table.ForeignKey("fk_photo_fingerprints_listing_id", x => x.listing_id,
                    principalSchema: "catalog", principalTable: "listings", principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey("fk_photo_fingerprints_organization_id", x => x.organization_id,
                    principalSchema: "organization", principalTable: "organizations", principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            },
            comment: "Компактные perceptual hashes фотографий входящих объявлений. Исходные изображения в PostgreSQL не сохраняются.");

        migrationBuilder.CreateIndex("ix_photo_fingerprints_listing_id_url_hash",
            "catalog", "photo_fingerprints", new[] { "listing_id", "url_hash" }, unique: true);
        migrationBuilder.CreateIndex("ix_photo_fingerprints_organization_id_status_retry_at",
            "catalog", "photo_fingerprints", new[] { "organization_id", "status", "retry_at" });
        migrationBuilder.CreateIndex("ix_photo_fingerprints_organization_id_perceptual_hash",
            "catalog", "photo_fingerprints", new[] { "organization_id", "perceptual_hash" },
            filter: "perceptual_hash IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "photo_fingerprints", schema: "catalog");
        migrationBuilder.DropTable(name: "duplicate_settings", schema: "catalog");
    }
}
