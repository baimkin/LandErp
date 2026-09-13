# LandErp — .NET и зависимости

- Использовать C# 14, .NET 10 LTS, ASP.NET Core 10, Blazor Web App, EF Core 10,
  совместимый Npgsql и PostgreSQL 18.
- Nullable включён, warnings-as-errors включены, analyzers централизованы.
- Версии SDK и пакетов закрепляются централизованно; wildcard запрещены.
- Новая зависимость требует задачи, владельца, лицензии, оценки поддержки и
  объяснения, почему платформы недостаточно.
- Не добавлять MediatR, AutoMapper, FluentValidation, Redis, broker, Elasticsearch,
  PostGIS/NTS, UI/component, chart/map library «на будущее».
- Предпочитать BCL, ASP.NET Core и официальные provider.
- TypeScript/npm не подключать до утверждённого сценария.
- Публичные контракты и нетривиальные публичные методы документировать XML.
- Писать явный читаемый код; не использовать магию ради сокращения строк.

