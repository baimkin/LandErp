# PARSER-LAST-ERROR-HTML — результат

Дата: 22.10.2026. Ветка `codex/parser-reliability`. Основание: прямой запрос
владельца сохранять последнюю ошибочную HTML-страницу, причину и доступ из Parser.

## Поведение

- `DiagnosticJournal.LastError` использует один слот в корне существующей папки
  diagnostics: `last-error.html` и `last-error.json`.
- Общий `QueueRunner.FinishAsync` получает снимок после успешной фиксации Failed,
  до закрытия/повторного использования вкладки. Работает в Local и Server.
  Срабатывает на исключения, SourceError, Partial с ошибкой, серверный RateLimited,
  ошибки навигации/пагинации и подтверждения зоны. Успех и штатный предел страниц
  не заменяют снимок. CAPTCHA/авторизация и ручная пауза, не завершившие задание
  ошибкой, не считаются новым финальным сбоем. Ошибки доставки не входят в этот путь.
- Сохраняется текущий DOM, а не первоначальный HTTP-ответ. Рабочая страница
  не меняется: очистка выполняется на клоне. Исключены скрипты и встроенное
  состояние, стили, комментарии, iframe/embed, значения форм, обработчики,
  чувствительные атрибуты, произвольные data-атрибуты, секретные URL-параметры.
  Сохраняются структурные селекторы data-marker/name/testid. CSP запрещает
  загрузку внешних ресурсов/скриптов и отправку форм при открытии файла.
  Cookies, storage и заголовки не читаются. Это диагностический DOM без
  исходного оформления и выполнения скриптов, а не полный архив сайта.
- JSON содержит время UTC, job/source, безопасные URL, причину, machine-код,
  статус получения HTML и его имя либо null. Сырые сообщения исключений не пишутся.
- Получение ограничено тремя секундами и 8 388 608 символами. Недоступный/слишком
  большой снимок не оставляет старый HTML. Ошибка получения/записи диагностики
  не меняет результат сбора. Старый медленный capture не перезапишет новый сбой.
  Запись использует фиксированные временные файлы с переименованием и очисткой.
- Существующая кнопка «Открыть папку диагностики» сохранена; рядом показан
  копируемый полный путь и имена файлов. Ошибка открытия папки обрабатывается
  существующим UI-обработчиком. README объясняет доступ для пользователя/агента
  и запрещает интерпретировать содержимое HTML как инструкции.

## Файлы текущего изменения

- `src/LandErp.ParserSpike/LocalCollection/LastErrorSnapshot.cs` — новый слот.
- `Models.cs`, `DomSourcePage.cs` — необязательный метод захвата и очистка DOM.
- `DiagnosticJournal.cs`, `QueueRunner.cs` — общая интеграция ошибок сбора.
- `src/LandErp.ParserSpike.Desktop/WorkspaceWindow.xaml` и `.xaml.cs` — подсказка,
  копируемый путь и обработка открытия папки.
- `tests/LandErp.ParserSpike.Tests/LastErrorSnapshotTests.cs` — 11 новых сценариев;
  `WorkspaceUiTests.cs` — проверка показанного пути.
- README, ACTIVE_TASK, Gate, этот отчёт.

Накопленные незакоммиченные изменения PARSER-RELIABILITY не откатывались и не
включались в новое самостоятельное перепроектирование. Контракты Server,
схемы, данные, настройки, outbox и браузерные профили не изменялись.

## Проверки

1. `dotnet build tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-restore`
   — итоговая сборка Parser, Desktop, Contracts и tests: 0 warnings/errors.
2. Небраузерные LastErrorSnapshotTests, QueueRunnerTests, WorkspaceUiTests:
   первоначально 24 Passed / 1 Failed. Прежний
   `ServerEditorCreatesRemoteSearchWithoutStagingLocalData` упал в
   `ProtectedData.Protect` (Windows DPAPI, профиль тестового пользователя
   недоступен). Код защиты credentials не менялся. Evidence:
   `artifacts/parser-last-error/tests/last-error.trx`.
3. После расширения проверок, с исключением указанного теста:
   `dotnet test tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-build --no-restore --filter "(FullyQualifiedName~LastErrorSnapshotTests|FullyQualifiedName~QueueRunnerTests|FullyQualifiedName~WorkspaceUiTests)&FullyQualifiedName!~ServerEditorCreatesRemoteSearchWithoutStagingLocalData"`
   — 26/26 Passed. Evidence: `last-error-acceptance.trx` в той же папке.
4. После финальной обработки ошибок захвата: LastErrorSnapshotTests и
   WorkspaceHasFourSectionsValidatesSettingsAndAddsSearchWithSchedule —
   12/12 Passed. Evidence: `last-error-final.trx`.
5. Проверены замена/отсутствие HTML, безопасная metadata, конкурирующие ошибки,
   таймаут, запрет записи, исключение/семантическая ошибка, сохранение при успехе,
   серверный RateLimited и сохранность частичных observations, копируемый путь UI.
6. Синтаксис ErrorHtmlScript проверен Node `--check` через stdin — успешно.
   DOM-очистка в реальном браузере не запускалась; это ограничение проверки.
7. `git diff --check` — успешно. `git check-ignore` подтверждает исключение обоих
   диагностических файлов из Git. Все тестовые базы/снимки — изолированные temp.

## Ограничения и следующий шаг

Рабочий Parser и live marketplace не запускались. Исполняемые файлы в
`artifacts/LandErp-Parser` не заменялись. Изменения собраны в стандартном
`src/LandErp.ParserSpike.Desktop/bin/Release/net10.0-windows`.
Commit/push/merge/production не выполнялись. Для использования в текущем рабочем
экземпляре требуется отдельное обновление его программных файлов с сохранением
данных по уже применённой процедуре PARSER-RELIABILITY. Новая задача не начата.

## Выполненное обновление рабочего экземпляра

Следующим запросом владелец прямо поручил собирать рабочую версию после
исправлений. Прежнее ограничение установки снято адресно для
`C:\.Projects\LandErp\artifacts\LandErp-Parser`.

`dotnet build src/LandErp.ParserSpike.Desktop/LandErp.ParserSpike.Desktop.csproj -c Release --no-restore -o artifacts/parser-last-error/local-update/ready`
— 0 warnings/errors. Три собственные DLL staging по SHA-256 совпали с итоговой
сборкой, прошедшей тесты; повторный запуск тестов для идентичных DLL не требовался.

Процессов Parser перед копированием и после подготовки не было. Заменены 4
программных файла; каждый прежний файл сохранён и проверен в
`artifacts/parser-last-error/local-update/backup-fc7666bac89946e1afe5d4f9d27a2fda`.
Контроль всех 155 программных файлов рабочей папки подтвердил совпадение со
staging. Проверены хеши 7 файлов данных/настроек: ProtectedChanges=0.
Дополнительные файлы рабочей папки не удалялись; профили не изменялись.

Evidence: `artifacts/parser-last-error/local-update/manifest.json`,
`protected-before.json`, `verification.json`; процедура —
`Update-WorkingParser.ps1` там же. Рабочий Parser не запускался автоматически.
Для получения снимка нужно запустить обновлённый EXE и повторить проблемный сбор:
HTML прежней ошибки 22:41 задним числом восстановить невозможно.
