# GATE-SPIKE-001-02 — Fixture harness

**Статус:** Отложен владельцем; не является активным заданием

**Разрешение реализации:** отсутствует; требуется отдельное утверждение владельца

Вместо этого scope утверждён [первый live-прототип](GATE-SPIKE-001-02_Live_прототип.md).

**Родитель:** TP-SPIKE-001, раздел 5

**Ветка:** `spike/001-avito-viability`

**Режим:** offline, без обращения к Avito

**Предусловие:** Gate 01 принят по коммиту `8607e8c60f23ea40177ab8742fadc51f867663e3`

## 1. Цель и границы

Создать воспроизводимую инфраструктуру чтения локальных HTML fixtures:
валидацию входа и metadata, безопасный допуск очищенных файлов, стабильный
порядок обработки и машинный отчёт. Harness проверяет пригодность fixture,
а не классифицирует страницу и не извлекает объявления.

Все параметры ниже — предложения для согласования вместе с Gate.
Документ не разрешает создавать код или импортировать файлы.

## 2. Required reading

Использовать точный список из ACTIVE_TASK.md. После документов изучить только
затрагиваемые контракты, сериализацию, CLI и тесты Gate 01. Не читать будущие
Gate и планы ERP. Предусловия live-исследования SPIKE не требуют браузера
или двух Windows-машин для этого offline Gate.

## 3. Входит после отдельного утверждения

- synthetic fixtures и инструкция подготовки sanitized fixtures;
- отдельные модели metadata и отчёта harness;
- проверка пути, размера, расширения, UTF-8, metadata и SHA-256;
- допуск только synthetic или вручную reviewed sanitized HTML;
- deterministic runner, стабильные коды ошибок и ненулевой exit code при отказе;
- CLI validate/run, обновление help и инструкции;
- синтетические положительные и отрицательные тесты;
- отчёт Gate и остановка.

## 4. Каталоги и происхождение

| Область | Назначение | Git / runner |
|---|---|---|
| `fixtures/synthetic/` | Искусственные HTML и metadata без реальных данных | Отслеживаются; runner допускает после валидации |
| `fixtures/sanitized/` | Вручную очищенные и reviewed HTML с metadata | Git только после review точных байтов; runner требует запись review |
| `local-input/` | Частные исходные HTML, переданные владельцем | Уже игнорируется Git; runner не читает и не копирует |
| `artifacts/fixture-harness/` | Локальные JSON-отчёты | Уже игнорируется Git; без HTML, личных данных и абсолютных путей |

Новый `fixtures/private/` не создавать. Исходный HTML не хранить в отслеживаемых
каталогах даже временно. Private HTML не импортировать автоматически.
Sanitized каталог может содержать только инструкцию до появления reviewed файлов.
Для завершения Gate достаточно synthetic fixtures; реальные примеры не выдумывать.

## 5. Metadata v1

Один HTML и один JSON metadata с одинаковым basename в одном каталоге.
Предлагаемые поля:

- `schemaVersion`: обязательное `1.0`;
- `fixtureId`: уникальный локальный ID `[a-z0-9][a-z0-9_-]{0,63}`;
- `kind`: `Synthetic` или `Sanitized`, совпадает с каталогом;
- `sourceCode`: `SYNTHETIC` или `AVITO` соответственно;
- `htmlFile`: только basename `.html` или `.htm`, без путей;
- `encoding`: только `utf-8`, BOM допустим;
- `sha256`: 64 hex-символа, hash точных байтов HTML, включая BOM;
- `expectedClassification`: enum Gate 01, ручное ожидание для будущего Gate 03;
- `review`: null для Synthetic; для Sanitized обязательны `status=Approved`,
  непустой opaque `reviewId` (GUID), `reviewedAtUtc` с UTC offset 0 и
  `approvedSha256`, совпадающий с `sha256` и фактическим hash.

Не включать исходный путь, имя пользователя, email, cookies, headers, токены,
URL с session/query-параметрами, account IDs или свободные личные комментарии.
Fixture ID не является external ID объявления. Synthetic external IDs запрещены.
Ожидание классификации — контрольная аннотация; runner не подтверждает её
правильность. Не создавать успешный ObservationResult для валидации файла.
Неизвестные поля, версии, enum и неполная metadata отклоняются.

## 6. Допуск и sanitization review

1. Владелец определяет допустимость сохранения и ответственного за review.
2. Исходный HTML остаётся в игнорируемой private области.
3. Ответственный вручную очищает HTML вне Git: session state, cookies, tokens,
   личные сообщения, контакты и личные идентификаторы должны отсутствовать.
4. Ответственный просматривает точные очищенные байты и metadata, включая scripts,
   inline JSON, hidden attributes, URL и комментарии. HTML не исполняется.
5. После review создаётся Approved-запись с hash этих байтов и opaque ID;
   только очищенная reviewed пара допускается в `fixtures/sanitized/`.
6. Изменение HTML делает прежний hash/review недействительным; нужен новый review.

Approved-запись и поиск подозрительных маркеров не доказывают очистку произвольного
HTML. Автоматический sanitizer и гарантия удаления всех секретов не входят
в Gate. При сомнении файл не добавлять в Git и не выводить его текст.
Отсутствие реальных HTML не блокирует synthetic harness.

## 7. Валидация входа

Предлагаемые лимиты: HTML — от 1 байта до 5 MiB включительно;
metadata — от 1 байта до 64 KiB включительно; до 100 пар на запуск.
Размер ограничивать до чтения и во время чтения, чтобы рост файла не обходил лимит.

Принимать обычные файлы разрешённых каталогов, без рекурсивного обхода.
Нормализовать путь и проверить принадлежность корню на Windows. Запретить
traversal, абсолютные пути в metadata, alternate data streams и reparse points
(symlinks/junctions) в пути от корня репозитория до входного файла.
Игнорируемый private каталог не может быть root runner.

Разрешены `.html`/`.htm` и парный `.json`; расширение не доказывает MIME.
Проверять строгий UTF-8, запрещать NUL/binary input. Отклонять очевидный не-HTML
по минимальному признаку HTML-разметки; точный критерий документировать и покрыть
synthetic тестами. Не заявлять валидность DOM: HTML parser не используется.
Не читать ресурсы, не запускать scripts и не разрешать внешние ссылки.
Missing/orphan pairs, duplicate IDs (включая различия регистра), malformed metadata
и hash mismatch дают структурированный отказ. HTML читать один раз в ограниченный
buffer; hash и проверки должны работать с теми же байтами.

I/O и отмена обрабатываются явно. Не выводить HTML, metadata, исходные CLI-аргументы
или exception message с путями в stdout/stderr и JSON-отчёт.

## 8. CLI, runner и deterministic отчёт

Предлагаемые команды после утверждения:

```powershell
dotnet run --project src/LandErp.ParserSpike -c Release --no-build -- fixtures validate --root fixtures/synthetic
dotnet run --project src/LandErp.ParserSpike -c Release --no-build -- fixtures run --root fixtures/synthetic --output artifacts/fixture-harness/synthetic.json
```

`--root` допускает только `fixtures/synthetic` или `fixtures/sanitized` относительно
репозитория. `validate` выполняет все проверки без записи. `run` выполняет те же
проверки и записывает JSON summary. Нет команды импорта private HTML.

Exit codes: 0 — все fixtures валидны; 2 — неверные аргументы/неизвестная команда;
3 — validation failure, включая пустой набор; 4 — I/O или ошибка записи;
5 — отмена. Пустой набор не считается успешным экспериментом.

JSON harness отделён от результата парсинга: schema version, статус валидации,
число обработанных/отклонённых пар, упорядоченные элементы с валидным fixture ID,
безопасной относительной ссылкой, kind, hash и кодами ошибок. Для небезопасного
имени не выводить исходное имя; использовать порядковый номер.
Expected classification выводится только как аннотация, не observed state.
Не включать текущее время, длительность, случайные GUID и абсолютные пути.
Перечислять пары по ordinal-порядку имён, ошибки — в фиксированном порядке.
Неизменные байты HTML и metadata дают byte-identical JSON при повторе.

Output ограничен `artifacts/fixture-harness/`, без traversal/reparse points.
Не перезаписывать существующие файлы: записывать только новый файл.
Повторять с другим новым output-путём, который не включается в JSON.
При ошибке записи вернуть 4, не объявлять отчёт сохранённым.

Минимальные стабильные коды: `FIXTURE_EMPTY_SET`, `FIXTURE_LIMIT_EXCEEDED`,
`FIXTURE_INVALID_PATH`, `FIXTURE_INVALID_TYPE`, `FIXTURE_INVALID_ENCODING`,
`FIXTURE_INVALID_SIZE`, `FIXTURE_METADATA_INVALID`, `FIXTURE_PAIR_MISSING`,
`FIXTURE_DUPLICATE_ID`, `FIXTURE_HASH_MISMATCH`, `FIXTURE_REVIEW_REQUIRED`,
`FIXTURE_IO_ERROR`, `FIXTURE_OUTPUT_ERROR`, `FIXTURE_CANCELLED`.

## 9. Обязательные тесты и проверки

Тесты: валидный synthetic набор; границы размеров и количества; UTF-8/BOM,
NUL и binary input; пустой набор; missing/orphan pairs; malformed и unsupported
metadata; duplicate IDs; traversal/reparse paths; hash mismatch; Sanitized без
Approved-review или с несовпадающим approved hash; детерминизм при разном порядке
перечисления; безопасные I/O ошибки и отмена; запрет перезаписи output;
exit codes; отсутствие секретных полей и HTML в JSON.
Негативные данные — искусственные, во временных каталогах, без private файлов.
Сохранить все тесты инвариантов Gate 01.

После реализации фактически выполнить:

1. `dotnet --info` — стабильный закреплённый .NET 10;
2. `dotnet restore LandErp.slnx --locked-mode`;
3. `dotnet build LandErp.slnx -c Release --no-restore`;
4. `dotnet test LandErp.slnx -c Release --no-build`;
5. `dotnet format LandErp.slnx --no-restore --verify-no-changes`;
6. help, unknown command и CLI validate/run synthetic набора с проверкой exit codes;
7. два run одного набора в новые output-файлы и сравнение точных байтов;
8. CLI отказ на synthetic invalid/empty наборе с проверкой ненулевого exit code;
9. поиск секретов/profile paths в tracked/new files, проверку Git-ignore для
   private/artifacts; ручной review всех реальных sanitized файлов, если есть;
10. просмотр полного diff, включая новые файлы, и `git diff --check`.

Недоступные проверки фиксировать как ограничения, не заменять предположениями.
Live-эксперименты и две Windows-машины не требуются этим Gate.

## 10. Изменяемые области после утверждения

- `src/LandErp.ParserSpike/**` — CLI, harness и отдельные модели;
- `tests/LandErp.ParserSpike.Tests/**` — тесты harness;
- `fixtures/synthetic/**`, `fixtures/sanitized/**` — synthetic пары и инструкции,
  реальные sanitized пары только после review;
- `README.md` — команды и правила подготовки;
- `docs/03-active/reports/GATE-SPIKE-001-02_REPORT.md` — отчёт.

Использовать BCL и принятый тестовый стек. Новые пакеты, сборки, изменение версий
и принятых контрактов Gate 01 не предусмотрены. При выявленной необходимости
остановить зависимую работу и согласовать отдельно. Пустые Browser/Avito классы
не создавать.

## 11. Не входит

Классификатор, DOM-селекторы, search/detail extraction, автоматическая очистка
произвольного HTML, live-навигация, Playwright/браузер/profile, screenshot/trace,
сервер/БД/API/UI, дополнительные источники и следующие Gate.

## 12. Definition of Done и шаблон отчёта

Harness воспроизводимо валидирует synthetic fixtures, не допускает непроверенные
sanitized/private файлы, выдаёт стабильные ошибки и не сохраняет чувствительные
данные. Все обязательные проверки реально выполнены, создан отчёт. Классификация
страниц и бизнес-поля не реализованы.

Отчёт `reports/GATE-SPIKE-001-02_REPORT.md`: изменённые файлы; решения и отклонения;
происхождение fixtures и review references без личных данных; точные команды,
exit codes и результаты тестов; сравнение повторных JSON; Git-ignore и поиск
секретов; ограничения и недоступные проверки; рекомендуемый review владельцем.
После отчёта остановиться, ACTIVE_TASK на Gate 03 не переключать.
Commit/push реализации требуют отдельного запроса владельца.

## 13. Решения для согласования владельцем

1. Только offline validation harness, без классификации/extractor;
   завершение на synthetic fixtures без реальных HTML допустимо.
2. Лимиты 5 MiB HTML, 64 KiB metadata, 100 пар; UTF-8 и парные HTML/JSON.
3. Metadata v1, SHA-256, ручной Approved-review точных байтов для Sanitized;
   ответственный за review и допустимость сохранения реальных материалов.
4. CLI validate/run, отдельный deterministic JSON и exit codes 0/2/3/4/5;
   private input не импортируется, output не перезаписывается.

До отдельного утверждения реализация всех пунктов запрещена.
