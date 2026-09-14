# TP-LOCAL-001 — исправление выбора ссылок

Дата: 2026-09-14. Ветка: spike/001-avito-viability.
Основание: замечание владельца о запуске неотмеченных ссылок при испытании карты.
Статус: исправлено локально; проверка владельцем остаётся следующим шагом.

## Причина и изменение

SelectClick читал CheckBox.IsChecked внутри Task.Run. WPF запрещает чтение свойства
контрола из чужого потока: экранная галочка менялась, запись в SQLite завершалась
ошибкой, очередь могла использовать прежние отметки. Это не действие Force/freshness.
Состояние галочки теперь читается на UI-потоке; сохранения выполняются последовательно.
Запуск ждёт сохранения текущих отметок, повторный старт во время подготовки блокируется.
При ошибке сохранения запуск не продолжается с прежним выбором; следующая смена отметки
может повторить запись. Массовое изменение отметок также проходит через эту очередь.
Выбор после запуска относится к следующей очереди: текущая уже имеет свой снимок задач.

## Файлы

- src/LandErp.ParserSpike.Desktop/WorkspaceWindow.xaml.cs
- tests/LandErp.ParserSpike.Tests/WorkspaceUiTests.cs
- README.md: путь новой сборки
- docs/03-active/ACTIVE_TASK.md: отчёт/сборка
- этот отчёт

## Проверки

Locked restore с ignore-failed-sources/NuGetAudit=false и Release build,
ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-map-selection: PASS,
0 предупреждений, 0 ошибок; новые зависимости не добавлены.

```powershell
dotnet test tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-build --no-restore -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-map-selection --filter "FullyQualifiedName~WorkspaceUiTests|FullyQualifiedName~LocalCollectionTests|FullyQualifiedName~QueueRunnerTests" --logger "trx;LogFileName=selection.trx" --results-directory artifacts/local-001-map-selection/test-results
```

22 passed, 0 failed, 0 skipped. UI-тест на временной базе снимает галочку Cian,
немедленно нажимает Start и проверяет одну задачу Avito и сохранённую отметку Cian=false.
Остальные адресные тесты проверяют окно, выбор/резервирование/свежесть/хранение и очередь.
Последний полный прогон до этой точечной UI-правки: 83 passed, см. отчёт карты.
Тестовые окна используют временные базы; пользовательское приложение не закрывалось.
После проверки изменено только отступное оформление блока StartClick и документы.

## Испытание

Нажать «Остановить» в прежней версии, закрыть её. Запустить
artifacts/local-001-map-selection/bin/LandErp.ParserSpike.Desktop/release/LandErp.ParserSpike.Desktop.exe.
Снять все отметки, отметить только нужную карту и нажать «Обработать отмеченные».
База/сохранённые поиски/профили остаются прежними. Исторические задачи продолжают
показываться в общем журнале; текущий запуск определяется его batch, старые строки
не означают, что все они повторно обрабатываются.
Live не запускался исполнителем. Commit/push, другие Gate и production не выполнялись.
