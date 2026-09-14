# TP-002 — Сборка, SDK и зависимости

**Статус:** Пересмотрен ERP-00: reuse/adapt для ERP-01; старый scope не активен\

> Ревизия 2026-09-14: Reuse проверенных SDK/CPM/locks/MSTest из Collector checkpoint после выбора базы; новые EF/Npgsql версии отдельно проверяются.
> [ERP-00 report](../03-active/reports/ERP-00_REPORT.md) — итоговая ревизия.
> Примеры ниже — исторический материал, не approved implementation. Текущий
> scope задаёт ACTIVE_TASK; ни этот TP, ни старые зависимости не разрешают код.
**Зависимости:** TP-001, ADR-001, ADR-006  
**Результат:** SDK, анализаторы и версии пакетов управляются централизованно и воспроизводимо.

## Входит

- `global.json` с существующей проверенной patch-версией .NET 10 SDK;
- `Directory.Build.props`;
- `Directory.Packages.props` после фактического выбора первых пакетов;
- editorconfig и единые nullable/analyzer правила;
- один тестовый стек;
- документированный порядок обновления.

## Первая allowlist зависимостей

- EF Core + Npgsql добавляются только в TP-003;
- ASP.NET Core Identity — framework capability в TP-005;
- Microsoft.Data.Sqlite и Playwright — при задачах Agent;
- xUnit и Testcontainers.PostgreSql — кандидаты для согласованного test stack;
- остальные пакеты требуют явного обоснования по ADR-006.

## Эталон общих настроек

```xml
<Project>
  <PropertyGroup>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <AnalysisLevel>latest-recommended</AnalysisLevel>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
  </PropertyGroup>
</Project>
```

## Правило версий

Zoo Code не угадывает patch-версии. Перед записью он выполняет разрешённую
проверку доступных стабильных версий, показывает матрицу совместимости и только
после одобрения закрепляет точные значения. Wildcard запрещены.

## Проверки

- сборка падает на warning;
- все проекты используют net10.0/C# 14;
- версии пакетов не размножены по csproj;
- restore воспроизводим на чистом package cache;
- лицензии первых внешних пакетов зафиксированы.

## Definition of Done

Есть зелёные restore/build/format checks, список фактических зависимостей и
отсутствуют пакеты «на будущее».

