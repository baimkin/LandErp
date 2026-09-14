# TP-LOCAL-001 — диагностика неполноты карты

Дата: 2026-09-14. Ветка: spike/001-avito-viability.
Основание: прямое разрешение владельца на точечную диагностику после live-сбора 48/59.
Статус: реализовано локально; live проверяет владелец. Приёмка/Go не объявлены.

## Изменения

- Models.cs: типизированные MapBatchDiagnostic/MapDiagnostic, необязательная диагностика PageObservation.
- AvitoMapCapture.cs: число строк/новых публичных ID/повторов по каждой порции,
  рекомендации/явные внешние результаты, ошибки ID/URL и разбора карточки;
  отсутствие координат/внешние точки по координатам исходной порции и текущему
  подтверждённому контуру. Общие уникальные JSON-ID и фактически собранные объекты
  считаются отдельно. Накопленные DOM-ID сравниваются с JSON и сохранённым результатом.
- DomSourcePage.cs: измерение scrollTop/clientHeight/scrollHeight правого контейнера
  без изменения прокрутки; безопасный код ошибки при неизвестной геометрии.
- QueueRunner.cs: автоматические события «Диагностика карты» / «Снимок» при изменении
  состояния и «Итог» при финальном контроле. Запись в существующий collection.jsonl,
  с JobId/BatchId/вкладкой и безопасным адресом. Отдельную JSON-запись включать не нужно.
- AvitoMapTests.cs: новая проверка повторов/исключений/DOM-only ID; проверка геометрии
  после колеса и фактического сохранения итоговой диагностики в журнале.
- README.md/ACTIVE_TASK.md: новая сборка/порядок теста; этот отчёт.

Пути к исходникам: src/LandErp.ParserSpike/LocalCollection/; тест —
tests/LandErp.ParserSpike.Tests/AvitoMapTests.cs. Новых пакетов, изменений SQLite,
исправлений пагинации/localPriority или новых механизмов сбора этим шагом нет.

## Интерпретация

Batches[].Received — строки ответа. NewIds/Repeated — новые/повторные цифровые ID
среди всех прочитанных строк, до бизнес-фильтра; Ids — только публичные цифровые ID.
InvalidIdOrUrl/InvalidCards и Recommendations — причины раннего исключения.
MissingCoordinates/OutsidePolygon — причины географического исключения разобранных карточек.
При неизвестной зоне внешний статус не вычисляется; ZoneConfirmed=false явно записывается.
Категории не следует складывать с NewIds/Repeated как взаимоисключающие.
JsonUnique — разные цифровые ID в ответах, Collected — итог после проверки/фильтра.
DomWithoutJson — увиденные карточки без соответствующего JSON-ID;
DomNotCollected — увиденные карточки, отсутствующие в текущем собранном результате.
Loading и геометрия показывают, был ли список внизу и шла ли загрузка.
Сырые ответы, описания, продавцы, cookies/headers/аккаунтные поля в этих событиях отсутствуют.

Диагностика видит только полученные ответы и распознанные DOM-карточки во время чтения;
она не доказывает наличие никогда не отданного сайтом объявления. Парсер/фильтры и
условия завершения сохранены. Повторные/изменённые данные сохраняются по прежним правилам.

## Проверки

```powershell
dotnet restore LandErp.slnx --locked-mode --ignore-failed-sources -p:NuGetAudit=false -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-map-diagnostic
dotnet build LandErp.slnx -c Release --no-restore -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-map-diagnostic
dotnet test tests/LandErp.ParserSpike.Tests/LandErp.ParserSpike.Tests.csproj -c Release --no-build --no-restore -p:ArtifactsPath=C:/.Projects/LandErp/artifacts/local-001-map-diagnostic --filter "FullyQualifiedName~AvitoMapTests|FullyQualifiedName~WorkspaceUiTests|FullyQualifiedName~QueueRunnerTests" --logger "trx;LogFileName=map-diagnostic.trx" --results-directory artifacts/local-001-map-diagnostic/test-results
```

Locked restore PASS. Release build PASS: 0 предупреждений, 0 ошибок.
Адресный прогон: 25 passed, 0 failed, 0 skipped, 15 секунд.
После уточнения счётчиков исходных координат порций повторены build и AvitoMapTests:
13 passed, 0 failed, 0 skipped. TRX: map-diagnostic-final.trx в той же папке.
Проверены счётчики, исключения, DOM-only ID, отсутствие тестового секретного поля,
журнал, контур/координаты, неполнота/предел/свежесть, пауза/отмена, выбор ссылки и UI.
Тесты Chrome — локальные подставные страницы, временные базы/контексты.
Полный проект/весь docs повторно не читался; полный тестовый набор не повторялся
без необходимости. Последний полный прогон карты: 83 passed, см. отчёт карты.
Первоначальная сборка выявила CA1861 в тестовых массивах: исправлено без отключения анализатора.
git diff --check PASS (только уведомления LF/CRLF прежних файлов); ветка соответствует.
Пользовательские окна/профили/база не закрывались и не изменялись исполнителем.

## Испытание

Закрыть старое приложение; запустить
artifacts/local-001-map-diagnostic/bin/LandErp.ParserSpike.Desktop/release/LandErp.ParserSpike.Desktop.exe.
Снять остальные отметки, отметить одну карту с нужной областью, предел 10 порций,
«Начать заново», «Обработать отмеченные». Диагностика пишется автоматически.
После окончания сообщить «готово»: исполнитель сам прочитает local-data/diagnostics/collection.jsonl
и сверит последний BatchId с SQLite. Копировать журнал/страницу пользователю не нужно.
Если выполнение приостановится, сообщить это, не подменять зону другой.
Следующий шаг — анализ результата этого теста, без запуска других Gate/production.
Commit/push не выполнялись, требуют отдельного запроса.

## Контроль перед фиксацией текущего состояния

По отдельному запросу владельца 2026-09-14 текущие код/документы подготовлены
к commit/push в spike/001-avito-viability. Перед фиксацией выполнены locked restore,
Release build и полный TestCategory!=Live прогон с
ArtifactsPath=C:/.Projects/LandErp/artifacts/checkpoint-local-001:
**85 passed, 0 failed, 0 skipped**, 31 секунда, сборка без предупреждений/ошибок.
TRX: artifacts/checkpoint-local-001/test-results/checkpoint.trx. Сетевой NuGetAudit
отключён для офлайн-сборки, зависимости закреплены lock-файлами.
Исправлено только устаревшее указание версии Playwright в README: фактически 1.62.0.
Владелец подтвердил 43 видимые карточки карты; журнал показал 59 строк с 16 повторами,
43 разных ID, 0 географических/ID-исключений и совпадение DOM со сбором.
Счётчик карты остаётся ориентиром; изменение статуса неполноты пока не реализовано.
Оставшиеся проблемы: localPriority и завершение Avito на настроенном лимите.
Локальные данные/профили/сборки и журналы в состав Git не входят.
Фиксация состояния не означает приёмку всех Gate, Go или разрешение серверной части.
