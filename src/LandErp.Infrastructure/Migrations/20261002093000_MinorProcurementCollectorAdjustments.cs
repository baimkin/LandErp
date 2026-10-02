using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20261002093000_MinorProcurementCollectorAdjustments")]
public partial class MinorProcurementCollectorAdjustments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "url", schema: "collection", table: "search_configurations",
            type: "character varying(12000)", maxLength: 12000, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(2000)", oldMaxLength: 2000);
        migrationBuilder.AddColumn<decimal>(
            name: "target_purchase_price_per_sotka", schema: "collection", table: "search_group_market_settings",
            type: "numeric(19,4)", nullable: true,
            comment: "Ручная нужная цена покупки за сотку в RUB; ориентир закупки, не цена объявления и не цена сделки.");
        migrationBuilder.Sql("""
            UPDATE procurement.case_document_requirements AS r
            SET code='gpzu', title='ГПЗУ', description='Градостроительный план земельного участка.',
                expected_source='Органы местного самоуправления', version=version+1
            WHERE r.code='cadastral_plan'
              AND NOT EXISTS (SELECT 1 FROM procurement.case_document_requirements AS e
                              WHERE e.property_case_id=r.property_case_id AND e.code='gpzu');
            DELETE FROM procurement.case_document_requirements AS r
            WHERE r.code IN ('access_scheme','cadastral_plan')
              AND NOT EXISTS (SELECT 1 FROM procurement.case_attachments AS a WHERE a.document_requirement_id=r.id);
            """);
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            UPDATE procurement.case_document_requirements AS r
            SET code='cadastral_plan', title='Кадастровый план',
                description='Границы, конфигурация и кадастровые сведения об участке.',
                expected_source='Росреестр', version=version+1
            WHERE r.code='gpzu'
              AND NOT EXISTS (SELECT 1 FROM procurement.case_document_requirements AS e
                              WHERE e.property_case_id=r.property_case_id AND e.code='cadastral_plan');
            """);
        migrationBuilder.DropColumn(name: "target_purchase_price_per_sotka", schema: "collection", table: "search_group_market_settings");
        migrationBuilder.AlterColumn<string>(
            name: "url", schema: "collection", table: "search_configurations",
            type: "character varying(2000)", maxLength: 2000, nullable: false,
            oldClrType: typeof(string), oldType: "character varying(12000)", oldMaxLength: 12000);
    }
}
