using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260920203000_CatalogPriceChangeDelta")]
public partial class CatalogPriceChangeDelta : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<decimal>(
            name: "previous_observed_price",
            schema: "catalog",
            table: "events",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: true,
            comment: "Предыдущее известное значение публичной цены непосредственно перед событием изменения источника.");

        migrationBuilder.AddColumn<decimal>(
            name: "previous_observed_price_per_sotka",
            schema: "catalog",
            table: "events",
            type: "numeric(19,4)",
            precision: 19,
            scale: 4,
            nullable: true,
            comment: "Предыдущая вычисленная цена за сотку непосредственно перед событием изменения источника.");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "previous_observed_price", schema: "catalog", table: "events");
        migrationBuilder.DropColumn(name: "previous_observed_price_per_sotka", schema: "catalog", table: "events");
    }
}
