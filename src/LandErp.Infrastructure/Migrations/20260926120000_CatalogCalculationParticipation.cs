using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260926120000_CatalogCalculationParticipation")]
public partial class CatalogCalculationParticipation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // No backfill from the old Overview formula: all existing rows start excluded.
        migrationBuilder.AddColumn<bool>(name: "include_in_calculation", schema: "catalog", table: "listings",
            type: "boolean", nullable: false, defaultValue: false,
            comment: "Общая для организации отметка участия в расчёте; существующие объявления не включаются автоматически.");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "include_in_calculation", schema: "catalog", table: "listings");
}
