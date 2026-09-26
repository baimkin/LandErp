using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260926140000_GroupDemandTestPrice")]
public partial class GroupDemandTestPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<decimal>(name: "demand_test_price_per_sotka", schema: "collection",
            table: "search_group_market_settings", type: "numeric(19,4)", nullable: true,
            comment: "Ручная цена теста спроса за сотку в RUB; не измеренный спрос и не цена сделки.");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "demand_test_price_per_sotka", schema: "collection", table: "search_group_market_settings");
}
