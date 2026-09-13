# LandErp — архитектура

- Архитектура Server — модульный монолит; реализация — Vertical Slices.
- Исполняемые приложения: `LandErp.Server`, `LandErp.Worker`, `LandErp.Agent`.
- Контракты Agent изолированы в `LandErp.AgentContracts`.
- Модули: IdentityAccess, Administration, Collection, PropertyCatalog,
  ManagerWorkspace, DueDiligence, ProjectsFinance, InvestorPortal, Reporting.
- Внутри модуля направление: UI/Endpoint → Application → Domain; Infrastructure
  реализует интерфейсы Application.
- Не обращаться к `Internal` и таблицам другого модуля.
- Не вызывать handler другого use case напрямую.
- Не создавать `Common`, `Helpers`, generic repository или service locator.
- Межмодульная связь — публичный контракт, событие либо утверждённый read model.
- Микросервисы и внешний broker запрещены без нового ADR и измеримой причины.

