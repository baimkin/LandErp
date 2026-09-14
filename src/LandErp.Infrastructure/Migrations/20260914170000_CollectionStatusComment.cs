using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("20260914170000_CollectionStatusComment")]
public sealed class CollectionStatusComment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        COMMENT ON SCHEMA collection IS 'Управление разрешённой работой самостоятельных локальных Collector, без browser state.';
        COMMENT ON SCHEMA catalog IS 'Публичные объявления и неизменяемые наблюдения; не реестр идентичности земельных участков.';
        COMMENT ON COLUMN collection.jobs.state IS 'Состояние работы: Pending ожидает выдачи; Leased действует до LeaseExpiresAt; Completed/LimitReached завершены; ручная проверка/ошибка/прерывание не являются успешной пустой выдачей.';
        """);
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql("""
        COMMENT ON COLUMN collection.jobs.state IS NULL;
        COMMENT ON SCHEMA collection IS NULL;
        COMMENT ON SCHEMA catalog IS NULL;
        """);
}
