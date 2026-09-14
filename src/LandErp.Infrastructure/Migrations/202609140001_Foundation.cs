using LandErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace LandErp.Infrastructure.Migrations;

[DbContext(typeof(LandErpDbContext))]
[Migration("202609140001_Foundation")]
public sealed class Foundation : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(LandErpDbContext.FoundationSchema);
        migrationBuilder.Sql("""
            COMMENT ON SCHEMA foundation IS 'Техническое основание LandErp. Схемой управляет только migration path.';
            COMMENT ON TABLE foundation.migration_history IS 'Применённые версии схемы LandErp. Не является журналом бизнес-событий; применённые migrations не переписываются.';
            COMMENT ON COLUMN foundation.migration_history."MigrationId" IS 'Идентификатор применённого изменения схемы. Порядок задаёт единый migration stream.';
            COMMENT ON COLUMN foundation.migration_history."ProductVersion" IS 'Версия EF Core, которой создано изменение схемы, а не версия бизнес-данных.';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The EF history container must survive migration-to-zero so EF can record rollback.
        migrationBuilder.Sql("COMMENT ON SCHEMA foundation IS NULL;");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder.HasAnnotation("ProductVersion", "10.0.12");
        modelBuilder.HasAnnotation("Relational:MaxIdentifierLength", 63);
        modelBuilder.UseIdentityByDefaultColumns();
    }
}
