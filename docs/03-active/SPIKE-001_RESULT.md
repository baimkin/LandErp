# SPIKE-001 — итоговый отчёт

**Дата:** 2026-09-14.\
**Статус:** Закрыт как исследовательская фаза по запросу ERP-00.\
**Решение дальнейшего пути:** Conditional Go для независимого технического основания ERP;
общий Go и production-приёмка Collector не объявлены.\
**Владелец решения:** владелец продукта — прямой запрос на переход ERP-00 от 2026-09-14.\
**Collector checkpoint:** `3994644d5a00413bb53513b78c5492c7e0b70692`, ветка `spike/001-avito-viability`.\
**Версии:** SDK 10.0.100 / C#14 / .NET10 / Playwright1.62.0 по отчётам;
точный Chrome/Edge build финального checkpoint не зафиксирован.\
**Проверенные Windows-компьютеры:** один подтверждённый средой отчётов Windows x64;
второй компьютер — NotVerified.

## Выполненные эксперименты и evidence

| Эксперимент | Факт | Evidence |
|---|---|---|
| Gate 01: контракты/CLI | Принят владельцем по `8607e8c60f23ea40177ab8742fadc51f867663e3` | [Отчёт Gate 01 в checkpoint](https://github.com/baimkin/LandErp/blob/3994644d5a00413bb53513b78c5492c7e0b70692/docs/03-active/reports/GATE-SPIKE-001-01_REPORT.md) |
| Collector Avito/Cian | Windows UI, local SQLite/history/export, selected links, settings snapshots, queue/claims, tabs, recovery/pause/manual resume | [Отчёт TP-LOCAL-001 в checkpoint](https://github.com/baimkin/LandErp/blob/3994644d5a00413bb53513b78c5492c7e0b70692/docs/03-active/reports/TP-LOCAL-001_REPORT.md) |
| Поля Cian | Присланный snapshot: 28 карточек, конфликт площади; synthetic negative tests | Тот же отчёт; это не полная live-приёмка Cian |
| Карта Avito | JSON cards, координаты/precision/контур, история, сохранность SQLite schema2; 83 offline tests при реализации карты | [Отчёт карты в checkpoint](https://github.com/baimkin/LandErp/blob/3994644d5a00413bb53513b78c5492c7e0b70692/docs/03-active/reports/TP-LOCAL-001_MAP_REPORT.md) |
| Последний полный контроль | 85 passed / 0 failed / 0 skipped; Release 0 warnings/errors | [Последний фактический отчёт](reports/TP-LOCAL-001_MAP_DIAGNOSTIC_REPORT.md), финальный раздел |
| Live-сверка карты владельцем | 59 строк = 43 unique IDs + 16 повторов; 43 видимые карточки, DOM совпал со сбором, 0 geo/ID-исключений | Тот же отчёт; это один подтверждённый сеанс |
| Synthetic volume | 10 000 observations; write ~1,8s, count+page100 ~0,15s на этой машине | TP-LOCAL-001 report; не SLA и не live массовый сбор |

Последний фактический отчёт импортирован в docs-ветку из указанного checkpoint
без изменения содержания. Исторические фразы «Go не объявлен», старые пути
сборок и промежуточные числа tests отражают момент отчёта и не переписаны.
Новый итог не выдаёт их за новые проверки.

## Гипотезы

| Гипотеза исходного Spike | Статус | Обоснование |
|---|---|---|
| H-01 headed browser | Частично | Chrome/Edge sessions подтверждены локальными тестами и ограниченным live-сеансом; не вся первоначальная серия профилей |
| H-02 классификация | Частично | Offline positive/negative checks; вся live-выборка не измерена |
| H-03 основной контейнер | Частично | Synthetic/snapshot проверки; сверка DOM карты 43 ID |
| H-04 внешние ID/URL | Частично | Негативные checks и live-карта; 99% по широкой выборке не рассчитаны |
| H-05 полнота полей | Частично | Поля выдачи/conflicts проверены offline; detail и field-level live проценты отсутствуют |
| H-06 влияние persistent profile на входы | Частично | Persistent profiles работают; сравнение частоты повторных входов не измерено |
| H-07 unknown не пустой успех | Частично | Negative regression checks пройдены; не заявляется доказательство для любой будущей разметки |
| H-08 два компьютера | Не проверена | Второй Windows-компьютер NotVerified |
| H-09 частота/регулярность | Не проверена | Длительная консервативная live-серия и частота блокировок не измерены |
| H-10 безопасная диагностика | Частично | Sanitised diagnostics/secret negative tests; полный разбор всех эксплуатационных сбоев не выполнен |

## Метрики и ограничения решения

Пороги §16 первоначальной программы не снижались ради закрытия.
99% ID/URL, 98% title/price, 90% area/location и полная field-level live
выборка не рассчитаны; 100% всех исходно запланированных fixtures не доказаны
одним числом 85 тестов. Переносимость и эксплуатационная частота вмешательства
не измерены. Поэтому это закрытие исследовательской фазы с принятыми ограничениями,
а не утверждение выполнения всех исходных Go-критериев.

Автономный Collector практически реализован; ERP foundation не зависит от
доступности Avito/Cian и может развиваться отдельно по ADR-007.
Gate 02/TP-LOCAL-001 не объявляются полностью принятыми; прежние Gate03–08
не выполнены и не активируются. Server/API отсутствуют; detail parser,
Cian map parser и часть operational scenarios не реализованы.

## Ручное вмешательство и ограничения источников

CAPTCHA/login/429 ставят источник на pause; продолжение — явная кнопка
и повторная классификация заблокированной вкладки. Частота таких остановок
в регулярной работе NotVerified. Автоматического обхода защиты/retries/masking нет.

В карте totalCount может отражать строки с повторами; 59 не равно 59 объявлений.
Действующий критерий unique IDs == totalCount остаётся консервативным и может
оставлять совпавший с видимым DOM сбор частичным. В этом переходе criterion не меняется.
Открыты localPriority и завершение Avito на установленном лимите.
Precision — код источника; точность координат в метрах не доказана.

## Что разрешено сохранить и что требует review

[Таблица reuse/adapt/experimental](reports/ERP-00_REPORT.md) фиксирует конкретные
части Collector. Reuse означает сохранение проверенной логики/инвариантов/regressions
в Collector, а не автоматическое копирование production assembly.
Wire contracts, server adapter/auth/lease/outbox, упаковка/lifetime —
отдельные адаптации; selectors/loading/map completeness остаются experimental.

Не переносятся browser profiles, секреты, пользовательская SQLite,
debug/raw source artifacts и source-specific Playwright в Server.
Collector не получает PostgreSQL credentials и не знает ERP workflow.
Новый production parser не создаётся заново в ERP-01.

## Обязательные условия следующего решения

ERP-00 снимает общий Spike-блокер ERP, но сохраняет запрет неутверждённой реализации.
Следующий [ERP-01](GATE-ERP-01_Техническое_основание_и_БД.md) подготовлен:
foundation/БД/technical checks. До migration — финальный review conventions/DDL;
до production apply — отдельное разрешение. Collector production acceptance —
отдельная проверка live-качества, recovery/переносимости и открытых defects.

## Изменения документации и подпись решения

AGENTS, README, START_HERE, ACTIVE_TASK, ERP-00, вводные FP/бизнес-карты,
старые TP и отчёт обновлены по прямому запросу владельца 2026-09-14.
Решение о переходе основано на этом запросе, не на выдуманной подписи
приёмки Collector. Код, runtime data и Git history не изменялись;
merge/rebase веток не выполнялись.
