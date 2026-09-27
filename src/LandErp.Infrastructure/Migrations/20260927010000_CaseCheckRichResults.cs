using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260927010000_CaseCheckRichResults")]
public partial class CaseCheckRichResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(name: "result_document_json", schema: "procurement", table: "case_checks",
            type: "jsonb", nullable: true,
            comment: "Безопасный форматированный результат проверки по схеме C-01. Null означает прежний plain text в result; исходные записи массово не преобразуются.");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "result_document_json", schema: "procurement", table: "case_checks");
}
