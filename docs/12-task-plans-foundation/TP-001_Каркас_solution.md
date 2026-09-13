# TP-001 — Каркас solution LandErp

**Статус:** На согласовании  
**Зависимости:** ADR-001, ADR-002, FP-001  
**Результат:** пустой LandErp собирается одной командой с правильными границами проектов.

## Входит

- `LandErp.slnx`;
- проекты Server, Application, Infrastructure, Worker, Agent, AgentContracts;
- пять test-проектов по FP-001;
- project references только в разрешённых направлениях;
- каталоги `docs`, `.roo/rules`, `scripts` без лишних заготовок классов;
- перенос утверждённых документов и Starter Pack в репозиторий.

## Не входит

Пакеты EF/Playwright, БД, Identity, UI-компоненты и бизнес-сущности.

## Целевые ссылки

| Проект | Ссылается на |
|---|---|
| Server | Application, Infrastructure |
| Worker | Application, Infrastructure |
| Infrastructure | Application |
| Agent | AgentContracts |
| Application | BCL; AgentContracts только при доказанной необходимости |
| AgentContracts | только BCL |

## Эталон настройки проекта

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
</Project>
```

Точные команды создания Zoo Code сначала показывает владельцу. Запрещено
генерировать placeholder-сервисы и доменные сущности ради заполнения каталогов.

## Проверки

- solution содержит только заявленные проекты;
- `dotnet restore` и `dotnet build` проходят;
- Agent не имеет ссылок на серверные проекты;
- Application не ссылается на Infrastructure/Server/Worker;
- diff не содержит секретов, `bin` и `obj`.

## Definition of Done

- каркас открывается в VS Code и собирается;
- правила Zoo Code подхватываются из `.roo/rules`;
- `.rooignore` проверен;
- структура соответствует ADR-002;
- команды и записи были отдельно одобрены владельцем.

