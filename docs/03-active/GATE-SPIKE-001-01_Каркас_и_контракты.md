# GATE-SPIKE-001-01 — Каркас и контракты

**Статус:** Принят владельцем
**Ветка:** `spike/001-avito-viability`  
**Режим:** offline, без обращения к Avito  
**Следующий Gate:** не разрешён автоматически

Принят владельцем 13 сентября 2026 года по коммиту
`8607e8c60f23ea40177ab8742fadc51f867663e3`.
Фактические результаты: [отчёт Gate 01](reports/GATE-SPIKE-001-01_REPORT.md).
Задание ниже сохранено как критерии принятого этапа.

## 1. Цель

Создать минимальный, собираемый и тестируемый фундамент исследовательского
прототипа. Gate должен зафиксировать язык контрактов и безопасных исходов, но не
реализовывать браузер и реальные селекторы.

## 2. Входит

- `global.json` с .NET 10 и согласованной roll-forward policy;
- `Directory.Build.props`;
- `Directory.Packages.props` с фиксированными версиями;
- `LandErp.slnx`;
- console-проект `src/LandErp.ParserSpike`;
- тестовый проект `tests/LandErp.ParserSpike.Tests`;
- включённые nullable и warnings as errors для собственного кода;
- базовые namespaces/folders без пустого scaffolding;
- контракты страницы, результата и значения;
- JSON serialization round-trip;
- CLI с `--help` и безопасным отказом при неизвестной команде;
- unit-тесты инвариантов контрактов;
- краткая инструкция сборки и запуска;
- отчёт Gate.

## 3. Контракты, которые нужно определить

### Page classification

- `SearchResults`;
- `ListingDetails`;
- `AuthenticationRequired`;
- `Captcha`;
- `RateLimited`;
- `ListingUnavailable`;
- `SourceError`;
- `Unknown`.

### Presence

- `NotInspected`;
- `Absent`;
- `Empty`;
- `Present`;
- `ParseFailed`.

### Общая оболочка результата

- версия схемы;
- source code;
- adapter version;
- requested/final URL как необязательные значения;
- observed time в UTC;
- page classification;
- outcome success/attention/failure;
- warnings и errors как коды;
- correlation ID;
- диагностические ссылки только как безопасные opaque reference.

Gate не обязан финализировать все поля выдачи и detail. Он должен обеспечить
расширяемую, сериализуемую и проверяемую основу.

## 4. Обязательные инварианты

- `Captcha`, `AuthenticationRequired`, `RateLimited`, `SourceError` и `Unknown`
  не могут иметь outcome `Success`;
- `Present` требует raw или typed value согласно контракту;
- `ParseFailed` сохраняет raw и не выдаёт придуманное typed value;
- observed time сериализуется однозначно в UTC;
- schema version обязательна;
- error/warning представлены стабильными кодами, а не только текстом;
- JSON не содержит cookies, headers, password и browser profile path;
- повторный round-trip не меняет значимые поля.

## 5. Не входит

- реальные или сохранённые страницы Avito;
- AngleSharp, HtmlAgilityPack или иной HTML parser;
- запуск Chromium;
- установка браузеров Playwright;
- DOM locators;
- браузерный профиль;
- screenshot/trace;
- search/detail extractor;
- серверный API, БД и UI;
- архитектура всей ERP.

## 6. Разрешённые зависимости

Для executable допускается только BCL. Версия `Microsoft.Playwright` может быть
зафиксирована централизованно для следующего Gate, но package reference и
скачивание браузера в Gate 01 не требуются. Тестовый framework выбирается из
поддерживаемого минимального набора и фиксируется с кратким обоснованием.

Любая дополнительная зависимость требует остановки и решения владельца.

## 7. Проверки

Обязательно фактически выполнить:

1. `dotnet --info`;
2. restore решения;
3. build решения в Release;
4. test решения в Release;
5. запуск CLI с `--help`;
6. запуск CLI с неизвестной командой и проверка ненулевого exit code;
7. поиск случайно добавленных секретов и browser profile paths в отслеживаемых
   файлах;
8. просмотр итогового diff.

Если среда не содержит .NET 10, не подменять проверку другим SDK: зафиксировать
блокер.

## 8. Ожидаемые тесты

- JSON round-trip общей оболочки;
- запрет success для attention/failure classifications;
- состояния presence и их инварианты;
- UTC timestamp;
- стабильность кодов warning/error;
- безопасный CLI exit code;
- отсутствие секретных полей в сериализованном результате.

Не писать тесты декоративной структуры каталогов.

## 9. Изменяемые области

Разрешено создавать или изменять:

- корневые build-файлы;
- `src/LandErp.ParserSpike/**`;
- `tests/LandErp.ParserSpike.Tests/**`;
- `README.md` только для актуализации команд запуска;
- `docs/03-active/reports/GATE-SPIKE-001-01_REPORT.md`.

Остальные планы и ADR не изменять. Если найдено противоречие, описать его в
отчёте.

## 10. Definition of Done

- solution восстанавливается и собирается в Release;
- все тесты проходят;
- CLI показывает help и безопасно отклоняет неизвестную команду;
- контракты покрывают обязательные классификации и presence;
- инварианты защитных состояний исполняются кодом и тестами;
- нет live-навигации и реальных селекторов;
- нет необоснованных зависимостей;
- создан отчёт Gate;
- агент остановился и не начал Gate 02.
