# TP-003 — PostgreSQL и миграционный каркас

**Статус:** Пересмотрен ERP-00: adapt для ERP-01; старый scope не активен\

> Ревизия 2026-09-14: Reuse persistence/design-time/Local-Test checks; до первой migration финальный review conventions и schema-only DDL, без business tables.
> [ERP-00 report](../03-active/reports/ERP-00_REPORT.md) — итоговая ревизия.
> Примеры ниже — исторический материал, не approved implementation. Текущий
> scope задаёт ACTIVE_TASK; ни этот TP, ни старые зависимости не разрешают код.
**Зависимости:** TP-002, ADR-001, ADR-002, FP-001  
**Результат:** Server и Worker используют один проверяемый DbContext PostgreSQL 18 без автоматической production-миграции.

## Входит

- EF Core 10 и совместимый Npgsql provider;
- `LandErpDbContext` в Infrastructure;
- безопасная конфигурация connection string;
- служебная первая миграция без бизнес-таблиц;
- design-time создание контекста без production-секретов;
- integration test на реальном PostgreSQL 18;
- инструкция create/update/rollback для Local/Test.

## Эталон регистрации

```csharp
public static class PersistenceRegistration
{
    public static IServiceCollection AddLandErpPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("LandErp")
            ?? throw new InvalidOperationException(
                "Connection string 'LandErp' is required.");

        services.AddDbContext<LandErpDbContext>(options =>
            options.UseNpgsql(connectionString));

        return services;
    }
}
```

## Ограничения

- Agent не получает Npgsql и connection string;
- миграция не запускается из `Program.cs`;
- пароли не хранятся в `appsettings.json` репозитория;
- PostGIS/NTS не добавляются;
- schema/table naming и data conventions фиксируются до первой production-миграции, включая техническую foundation migration.

## Проверки

- подключение к чистой PostgreSQL 18;
- apply миграции, запуск smoke-test, rollback тестовой среды;
- Server и Worker разрешают DbContext одинаково;
- недостающая конфигурация завершает старт понятной ошибкой;
- SQL/EF logs не раскрывают connection string.

## Definition of Done

Миграционный каркас воспроизводим, integration test зелёный, production startup
не меняет схему автоматически.

