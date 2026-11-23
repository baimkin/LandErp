# Пакет A — ускорение Incoming

23.11.2026. Scope: [GATE-INCOMING-PACKAGE-A](../docs/03-active/GATE-INCOMING-PACKAGE-A.md).
Ветка `codex/inspection-file-improvements`, исходный HEAD
`c6a79c725d57064732a6676e66cfa7d862fe6bff`. Общий dirty checkout сохранён.
Пакет A реализован локально и проверен на disposable PostgreSQL и в headless
браузере. Пакет B не активирован; production и Git publication не выполнялись.

## Что реализовано

- Основное чтение `ReadPageAsync`: rows + exact Total, last-page clamp в сервисе,
  одна короткая READ ONLY REPEATABLE READ транзакция для criteria/count/rows/metadata.
  UI больше не повторяет полный read из-за исчезнувшей последней страницы.
- `ReadInformationAsync` отдельно возвращает Summary, Preset/Group counts,
  fingerprint scope и captured AsOf/FreshAt. Прежний `ReadAsync` сохранён для
  совместимости и возвращает полный результат на том же оптимизированном SQL.
- Запросные MATERIALIZED CTE сворачивают историю в пары listing/search до JOIN
  изменяемой metadata, затем вычисляют saved matches один раз. SQL criteria
  переводит EF из прежнего IncomingCatalogQuery; отдельного SQL rules engine нет.
  Альтернативы переиспользуют saved matches, добавляя только text/slice.
- PresetCounts — GROUP BY PresetId; GroupCounts — COUNT(DISTINCT ListingId),
  включая ungrouped bucket. Эти числа не считаются десятками conditional
  агрегатов по всему каталогу. Summary/AllPresetsCount сохраняют прежний контекст.
- Resolver пресетов использует один запрос списка и один batch metadata,
  сохраняя V1 compatibility/fail-closed и отсутствие read-side writes.
  Штатное чтение metadata использует ту же connection/snapshot, что и страница.
- Последний search страницы выбирается SQL top-1, flags свёрнуты PostgreSQL;
  вся history страницы больше не материализуется в приложение. При одинаковом
  ObservedAt добавлен Observation.Id DESC tie-break для ранее неопределённой подписи.
- UI показывает таблицу до informational read; загрузка/ошибка чисел и рынка
  независимы. До результата — «—», затем время снимка по Москве. При page/size/sort
  один снимок до 60 с; повтор первого cache miss того же scope объединяется с
  уже выполняющимся расчётом. Смена scope/refresh/mutation отменяет старый расчёт.
- Поздние ответы проверяют scope и mutation/request revision. Информация меняет
  только Summary/FilterCounts, никогда Items/Total. После подтверждённого просмотра,
  отказа/передачи остаются прежние локальные обновления и bounded refill;
  informational snapshot помечается устаревшим без общего reload.
- «Обновить» читает свежую страницу и числа. Dispose отменяет page/info/detail и
  debounce. Время информационного снимка берётся на начале отбора, поэтому расчёт,
  пересёкший полночь, не получает ложную свежесть нового календарного дня.

Нет persistent memberships, новых migrations/indexes/grants, queue/jobs,
packages/extensions, общего cache, повышения product timeout или новых handlers
в Collector/бизнес-командах. Пакет B не начинался. Bulk IDs/versions/stamps,
авторизация, AutoInclude и медиана используют прежние свежие authoritative пути.

## Изменённые файлы текущей задачи

Пути относительно C:\.Projects\LandErp; изменения поверх ранее накопленного scope.

- `src/LandErp.Application/Modules/Catalog/Public/IncomingCatalogReadContracts.cs`.
- `src/LandErp.Infrastructure/Modules/Catalog/IncomingCatalogReadService.cs`,
  `IncomingCatalogQuery.cs`, `IncomingCatalogSelection.cs`, `IncomingFilterPresetService.cs`;
  новый `IncomingCatalogReadSql.cs` в том же каталоге.
- `src/LandErp.Server/Components/Pages/IncomingCatalogV2.razor`,
  `.Dismiss.cs`, `.Updates.cs`; новый `.Information.cs`.
- Новые `tests/LandErp.Foundation.Tests/IncomingPackageATests.cs` и
  `IncomingInformationUiTests.cs`; адресные правки `IncomingLoadTimeoutTests.cs`,
  `IncomingQuickDismissTests.cs`, `IncomingFastOpenTransferTests.cs` и двух browser
  counterparts. Старые browser artifacts не перезаписываются: outputs перенесены
  в ignored `artifacts/incoming-package-a/browser/`.
- `docs/14-ui-kit/screens/01-incoming-listings.md`, свой Gate/секция ACTIVE_TASK,
  отметка активации A в плане, этот отчёт. Исходники Parser/media/logging и
  остальные задачи не изменялись.

## Проверки и воспроизводимость

Сборки изолированы от работающего preview через `--artifacts-path` под
`artifacts/incoming-package-a/`. SDK 10.0.401. Locked restore использовал кеш:
сетевая проверка NuGet vulnerabilities недоступна (NU1900), для локальных restore
передан `-p:NuGetAudit=false`. Это параметр запуска, не изменение project/package
policy. Версии пакетов/lock-файлы не менялись; новый security audit не выполнен.

Основная форма команд (конкретные TRX и итоги ниже):

```powershell
dotnet restore tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj --locked-mode --artifacts-path artifacts/incoming-package-a/ui-final-build --ignore-failed-sources -p:NuGetAudit=false
dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-restore --artifacts-path artifacts/incoming-package-a/final-build --disable-build-servers -p:UseSharedCompilation=false --filter 'FullyQualifiedName~IncomingPackageATests|FullyQualifiedName~InformationLoadsAfterRows|FullyQualifiedName~IncomingWorkingSelectionTests|FullyQualifiedName~IncomingFilterPresetTests|FullyQualifiedName~IncomingArchiveSortingTests'
dotnet test tests/LandErp.Foundation.Tests/LandErp.Foundation.Tests.csproj -c Release --no-build --no-restore --artifacts-path artifacts/incoming-package-a/ui-final-build --filter 'FullyQualifiedName~IncomingLoadTimeoutTests'
```

Browser harness получает `LANDERP_DOTNET` (именно это имя) и
`LANDERP_TEST_BUILD_ROOT` с абсолютным путём к изолированной сборке. Запускается
только одноразовый Test Server на случайном localhost HTTPS-порту и headless Edge;
производственный/пользовательский Server/preview не перезапускается.
Новые БД/роли создаются и удаляются только владельцем disposable fixture с точными
именами `landerp_test_*`; действующие local/production БД не используются.

Differential тест сравнивает новую выборку с неизменённым authoritative
IncomingCatalogSelection (`optimizeRead=false`): ID, Total, все slices, alternative
counts и distinct group unions. Дополнительно проверены criteria/null/area=0,
source-declared и text land types, комментарии/AND text, multi-search/multi-zone,
selected/draft/All/Archive/Participants, московская полночь и Age без writes,
foreign organization, scope fingerprints и clamp.

UI проверка отдельно контролирует initial page-only read, TTL, ошибку info/market,
single-flight на первом miss при навигации, поздний ответ другого scope и mutation.
Существующие fast-dismiss/transfer/monitoring/bulk тесты дополняют matrix.

## Метод измерения скорости

Два synthetic набора: 25k listings / 500k observations / 50 zonal filters и
100k / 2m / 100 filters. Пять повторов каждого пути: page, selected-page,
AllListings page, information и compatibility full read. Штатный preset service,
авторизация и metadata входят в service latency. Время seed не входит.
Для seed используется set-based INSERT в своей disposable БД; его локальный
timeout 60 с не меняет product/read timeout 10 с.

Первый read и повторы разделены в JSON. PostgreSQL buffers после seed уже могли
быть прогреты; физически холодный storage/буфер не эмулировался. Это локальная
машина, а не production SLA. Во время части запусков были другие disposable
проверки/сборки; отдельные прогоны 4 readers + настоящий Collector ingress
фиксируются в concurrent.json. Не выводить сравнение SLA из одного best run.

Probe считает EF reader commands и их длительности; у штатного списка пресетов
ещё два Npgsql metadata commands, SET READ ONLY — отдельная control command,
BEGIN/COMMIT — transaction protocol. EF-счётчик не равен числу TCP packets.
Число команд не растёт как P или page rows; metadata результата ограничена
окном, а не глубиной observations. Query plans — synthetic EXPLAIN ANALYZE,
BUFFERS, FORMAT JSON; production SQL/параметры/raw logs не собирались.

Промежуточная версия прошла baseline, но упёрлась в 10 с при 100k/2m во вторичном
count. Это устранено GROUP BY/COUNT DISTINCT по matches, reuse альтернатив и
свёрткой истории до metadata JOIN. Неудачный прогон сохранён в performance.trx.
Отдельные ошибки оснастки: короткое имя фильтра «B», ожидание /50 при 100 группах,
сброс тестового текста «Пагинация» в более широкий каталог, неверное имя переменной
пути dotnet в browser harness; они исправлены в тестах/параметрах запуска.
Windows также блокировала импорт ключа временного PFX при запуске тестового
сервера в ограниченном режиме. Повторные тесты с необходимым доступом к этому
ключу прошли; настройки/сертификаты production не использовались. У PFX каждого
запуска своё имя. Временная безопасная диагностика startup (только тип исключения
и имена методов, без сообщений/SQL/credentials) удалена, PostgresTests.cs побайтно
восстановлен из исходной копии текущей задачи.

## Фактические результаты

| Проверка / artifact в artifacts/incoming-package-a/tests | Результат |
|---|---|
| final-correctness.trx | 10/10 Passed: полный differential matrix и прежние selection/preset/archive/sort тесты; затем менялся только guard in-flight UI, SQL остался тем же |
| final-ui-browser.trx | 16 UI/component сценариев Passed; две попытки browser не стартовали из-за неверного пути dotnet и позже отдельно повторены успешно |
| ui-startup-diagnosis.trx | Финальный расширенный UI guard/TTL/single-flight/scope/mutation тест Passed; отдельный browser startup ещё блокировался PFX и повторен ниже |
| browser-verified.trx | 2/2 Passed: реальные DOM/scroll/next/refill и закрытие loading card/передача всей группы/выбранный менеджер |
| final-information-ui.trx | 1/1 Passed после удаления временной диагностики: TTL/single-flight/late scope/late mutation и сохранение Items/Total при secondary errors |
| performance-verified.trx | 2/2 Passed: оба объёма, 5×5 чтений каждого набора, 4 concurrent readers + реальный Collector ingress |
| regression.trx, промежуточный прогон | 31 сценарий monitoring/команд/fast updates Passed; один fail оснастки на имени «B», исправлен и покрыт final-correctness |

Сборки завершились без warnings/errors. Последняя контрольная пересборка и
повтор guard после удаления временной диагностики завершились успешно.
Счётчики таблицы относятся к указанным прогонам,
складывать их как число разных тестов нельзя. Полный Foundation suite не запускался.

Времена **в секундах**, на каждом пути n=5. p95 — эмпирический nearest-rank,
при таком малом n совпадает с максимумом и не является оценкой production tail.

| Набор / путь | Первый | Медиана | p95/максимум |
|---|---:|---:|---:|
| 25k/500k/50 — страница + exact Total | 1.976 | 1.678 | 1.976 |
| выбран один фильтр | 0.822 | 0.818 | 0.833 |
| AllListings page | 0.290 | 0.295 | 0.416 |
| информация отдельно | 1.326 | 1.236 | 1.403 |
| compatibility full read | 1.741 | 1.741 | 1.822 |
| 100k/2m/100 — страница + exact Total | 8.990 | 8.363 | 9.733 |
| выбран один фильтр | 2.285 | 2.283 | 2.418 |
| AllListings page | 0.469 | 0.391 | 0.487 |
| информация отдельно | 5.304 | 5.270 | 5.438 |
| compatibility full read | 9.118 | 9.118 | 9.164 |

Прежний одиночный synthetic full service read на 25k/500k/50 был 11.031 с,
count 8.890 с. Новый полный путь 1.712–1.822 с; main UI path 1.482–1.976 с уже
включает exact Total. Старые 1.009 с window-only не используются как эквивалентный
контракт. В предыдущем comparative read metadata была FixedPresets; новый замер
включает штатный resolver. Различаются и прогрев/нагрузка машины: это контрольное
подтверждение ускорения, не production SLA.

Четыре concurrent main reads: 1.851–1.867 с на 25k и 8.179–8.374 с на 100k.
Реальный Collector ingress завершился успешно в обоих случаях; combined runs
2.646 и 8.374 с. Ни одной failed read command/DB_TIMEOUT в проверенном финальном
нагрузочном прогоне. Product ingress/его write path не изменён.

Максимальная отдельная команда main baseline — 0.717 с, информационная — 1.033 с.
На увеличенном наборе main до 5.240 с, информация до 5.050 с: запас относительно
лимита 10 с есть, но цель 3 с для страницы **не достигнута на увеличенном наборе**
и туда не переносится с baseline. Измеренный рост требует учитывать объёмы при
дальнейшей эксплуатации, а не включать B без решения владельца.

Main — 12 EF reader commands плюс 2 batch preset metadata commands и control
statement; information-only — 5 EF readers плюс те же 2 metadata commands/control.
Число сохраняется при росте 50→100 фильтров; queries на каждый preset/listing нет.
В основном COUNT плане на baseline 218 узлов, 2 SubPlan/InitPlan и один узел
observations с actual 500000 rows/1 loop; у information 361/127 и также один
observations scan. Прежний общий count имел 5614/705/700 соответственно, без
ANALYZE. Это сравнение изменившейся структуры SQL, не «700 сетевых запросов».
Main count и rows всё ещё выполняются отдельно и каждый имеет свой scoped CTE.

На 100k планы показывают spill во временные blocks (page count 71379 reads,
information 78563); история и промежуточные matches остаются пределом A при росте.
История не удалялась и work_mem/серверные настройки не увеличивались.
Evidence: measurements-final/*/{package-a.json,concurrent.json,*-plan.json},
verified-summary.json и verified-plan-summary.json в ignored artifacts текущей задачи.

## Ограничения и границы результата

- Конкретный production incident SQL по-прежнему не установлен. Ускорен локально
  доказанный тяжёлый путь; production deployment не выполнялся.
- История читается один раз на отдельный SQL; main Count и rows — два SQL на
  общем снимке. Стоимость всё ещё растёт с историей и числом фильтров. Это не
  постоянный precompute и не обещание одинаковых 3 с на любом объёме.
- Информационный snapshot может быть старым до нового чтения/«Обновить»; время
  показано. Rows/Total всегда свежие на собственном снимке, bulk не использует TTL.
- Известная граница bulk concurrency из аудита (group/time/view изменения после
  selection до row locks без изменения Listing.Version) не перепроектировалась.
  Новое чтение не заявляется её исправлением; проверки прав/stamps сохранены.
- Физически холодный PostgreSQL и production нагрузка/latency остаются не проверены.
  Увеличение timeout и автоматический тяжёлый fallback не применяются.

## Сохранность и завершение

`git diff --check` по файлам пакета прошёл; trailing whitespace нет; все 7 локальных
Markdown links плана/Gate/отчёта разрешаются. SHA-256 остальных исходно изменённых
tracked файлов и `.git/index` совпали с baseline, новых tracked-изменений вне
перечня пакета нет. Восстановление ACTIVE_TASK без добавленной секции A даёт
исходный побайтный hash: остальные секции сохранены. Ветка и HEAD совпали с началом.
Контрольные копии/измерения находятся в ignored artifacts текущей задачи.

Пакет A завершён в локальном checkout. Рекомендуемый следующий шаг — ручная
приёмка владельцем поведения Incoming на своей сборке и отдельное решение о
публикации/развёртывании. Production этим запросом не разрешён и не изменён.
Пакет B не начинать: данные о росте/временных blocks сохранены для будущего
решения, но не активируют новую схему автоматически. Commit/push не выполнялись.
