# M-03 — Тест спроса и показатели рынка на экранах

Разрешён владельцем 2026-09-26 поверх локальных M-01/M-02, ветка
`codex/m-01-median-participation`. Без commit/push/merge и рабочих БД.

## Required reading

- GATE-M-02.md и reports/M_02_REPORT.md — сохраняемые формула и участники.
- STAGE-1_DATA_CONVENTIONS.md и ERP-01_DB_COMMENT_CONVENTION.md — перед schema.
- ../04-foundation/FP-004_Сквозное_ERP-ядро_и_общие_бизнес-механизмы.md — права, аудит, общие бизнес-механизмы (ранее прочитанные применимые разделы).

## Scope и решения

- Один nullable положительный ручной ориентир RUB/сот. на организацию/группу,
  в существующей SearchGroupMarketSettings, numeric(19,4), ввод RUB округляется
  до 2 знаков ToEven; null означает отсутствие. Не измерение спроса и не сделка.
- Запись только CanHeadProcurement, текущие правила SystemOwner и явных прав
  Administrator сохраняются. Организация, активность группы, Version и аудит обязательны.
- Общие read-контракт и компонент: медиана, средняя, тест спроса, группа и участники.
- Incoming: группа текущего фильтра; без группы подсказка. Procurement: фильтр
  группы через подтверждённые источники и наблюдения, после ProcurementVisibility.
- Карточка: только группы подтверждённых источников, стабильный порядок и переключение.
- Overview: новая колонка между средней и выборкой в обоих представлениях.
- Inline сохранить/отмена; ссылки участников только Incoming.Read; обновление при чтении.
- Никаких изменений формулы/участников M-02, источников цен, ingestion/Parser,
  бизнес-процессов или широкого UI. Прежние срезы processed/pending сохраняются.

## Файлы и проверки

Settings entity/mapping/snapshot и forward migration; GroupMarketContracts/Service;
OverviewService/Contracts; ProcurementQueueV2ReadService/Contracts; общий компонент
цен и редактора; IncomingCatalogV2, ProcurementQueueV2, CaseWorkspace, Home и их CSS;
MarketDemandTests; ACTIVE_TASK и этот Gate/отчёт.

Release-сборка и целевые PostgreSQL-тесты: права/org/version/валидация/очистка,
группа фильтра Incoming, видимость закупки и отсутствие дублей, 0/1/N групп карточки,
одинаковые значения контрактов, комментарий/модель миграции. Браузер и Server не запускать.
Визуальная приёмка владельцем отдельно. Следующий Gate не активируется.
