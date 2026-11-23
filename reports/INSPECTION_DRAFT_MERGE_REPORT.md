# INSPECTION-DRAFT-MERGE — отчёт

04.11.2026. Прямой запрос: продолжать/завершать осмотр между вкладками без потери
ответов; выбирать только разные правки одного поля. Дополнение: пользователь
работал в одной вкладке, сначала загрузил видео, затем заполнил пункты.
Gate: docs/03-active/GATE-INSPECTION-DRAFT-MERGE.md.

## Решения и границы

- InspectionDraftMerge хранит исходный снимок и объединяет его со свежим сервером
  и несохранёнными ветками этого пользователя/объекта. Ответ+статус связаны;
  комментарий, общий вывод и решение независимы. Одинаковые изменения, переносы
  строк/trim и равные числовые значения с разной записью не создают конфликт.
- Истинные пересечения выводятся как выбор конкретных значений; удаление всего
  черновика больше не является способом продолжить. Сводка показывает число
  полей, требующих выбора. Перед записью всегда fresh read + merge; при гонке
  запись повторяется с новым чтением до трёх попыток, затем можно повторить вручную.
- Права, назначение/ID и завершённый статус проверяются актуально; завершённые
  ответы не перезаписываются. Идентичный уже завершённый результат не создаёт
  повторного завершения. Поздние несохранённые ответы остаются в локальном JSON
  черновике и доступны через «Скачать черновик».
- JS хранит отдельную ветку каждого открытого документа и revision. Подтверждение
  удаляет только точные прочитанные revision, не более новые вводы из других вкладок.
  Объединённые revision учитываются при последующем восстановлении. Ключ новых
  черновиков содержит UserId и CaseId; старый ключ читается для совместимости.
- DOM сразу сохраняет ответы и поля окна завершения, включая при задержке circuit.
  Последующий полный .NET render не пишет снимок поверх каждого нажатия клавиши.
  Своя сохранённая DOM-ветка имеет приоритет над более медленным render; при сбое
  localStorage сохраняется in-memory ввод и показывается предупреждение.
- Все сценарии используют прежний SaveInspection контракт и optimistic item/inspection
  versions. Сервер дополнительно блокирует строку осмотра на время транзакции, чтобы
  ответ и завершение не проходили параллельно после проверки прежнего статуса.
  Это не блокировка на время заполнения формы. Новых migrations/пакетов/ролей нет.

## Найденный single-tab дефект

Прежний browser draft считал снимки совместимыми только по inspection.Version.
При answer-only save могут измениться SiteInspectionItem.Version, а родительская
версия не изменится. Поздний DOM input мог продолжить старый снимок пунктов.
Теперь сравнивается весь вектор item ID/version. Отдельный JS тест воспроизводит
ситуацию неизменной родительской версии и новой версии пункта.
Это доказанный дефект кода, но причина конкретного production обращения
01a1a628-92f3-7007-9261-b8f3c00d6262 не установлена без его состояния/логов.

## Совместимость старых черновиков

В прежнем формате нет исходных значений. Если версии совпадают, свежий сервер
можно использовать как исходный снимок. Если версии различаются, невозможно
доказать, какая сторона меняла спорное поле: отличающиеся значения сохраняются
и предлагаются для сверки с пояснением про старый формат. Одинаковые значения
и заведомо неизменённые поля выбора не требуют. Старые записи не удаляются до
подтверждения сохранения; очищение не является условием восстановления.

## Файлы

ACTIVE_TASK, Gate, screen04, этот отчёт;
src/LandErp.Server/Components/Procurement/InspectionDraftMerge.cs;
src/LandErp.Server/Components/Pages/SiteInspectionPage.razor/.css/.js;
src/LandErp.Infrastructure/Modules/Procurement/ProcurementWorkspace.cs;
tests/LandErp.Foundation.Tests/InspectionDraftMergeTests.cs,
InspectionDraftMergeFlowTests.cs; scripts/Test-PropertyInspectionDraft.cjs.

## Проверки

Release build — 0 warnings/errors. Итоговый прогон после последних изменений
и точные результаты фиксируются ниже. JS: 8/8 Passed, включая item version,
изоляцию вкладок, late acknowledgement, modal conclusion, legacy и storage failure.
Pure merge: первоначальные 8/8 Passed; добавлена числовая эквивалентность.
PostgreSQL: stale answers → merge → simultaneous completion Passed.
Первый Edge/Blazor прогон с двумя вкладками/выбором/завершением Passed (53 с),
desktop/mobile screenshots просмотрены. Расширен точной single-tab последовательностью
до завершения и поздним экспортом после завершения в другой вкладке.

Первые два browser запуска остановились до UI: неверный default путь test dotnet,
затем права Windows crypto. LANDERP_DOTNET задан на установленный SDK; тестовый
HTTPS сервер запущен с разрешением. Тестовые PFX и БД — только синтетические.
Один расширенный browser прогон остановился при создании второго тестового case:
fixture отозвала incoming access у своего менеджера; исправлено только в fixture.

Расширенный старый suite: 13 Passed / 3 Failed. Два нерелевантных старых теста
не исправлялись: DocumentChecklistTracksRequestAttachmentVerificationAndHistory
ожидает 5 требований вместо текущих 4; FullProcurementCycleInspectionOfflineDraftAndAcquiredWorkInBrowser
использует прежний неподготовленный сценарий назначения/старый UI и не находит
«Начать осмотр». Третий — описанный дефект новой fixture. Финальный scope suite
исключает два прежних теста и включает актуальную браузерную приёмку.

## Ограничения и следующий шаг

Рабочая БД/production/Яндекс Диск/Safari/iPhone не проверялись. Browser uploads
используют маленькие синтетические mp4 и Local backend; воспроизведены реальные
формы/черновики/сохранение, а не скорость Яндекса и не видеокодек.
Несохранённый черновик другого устройства недоступен через localStorage;
серверно сохранённые изменения объединяются независимо от устройства.
Нельзя молча объединять терминальное/переназначенное состояние или удалять
неподтверждённые черновики. Параллельные Parser/старые изменения сохранены.
Commit/push/deploy не выполнялись. Следующий шаг после проверок — публикация
по отдельному запросу владельца. Следующий Gate не начат.

## Итог адресного прогона

- Последняя Release build: Passed, 0 warnings/errors.
- merge-verified.trx: 14/14 Passed, 2 мин 11 с. 9 pure merge, 1 PostgreSQL
  stale/atomic completion, 1 реальный Edge/Blazor сценарий, 3 существующих server
  inspection/access/snapshot regression tests. Нет пропущенных проверок.
- Node scripts/Test-PropertyInspectionDraft.cjs: 8/8 Passed.
- node --check SiteInspectionPage.razor.js и scoped git diff --check: Passed.
- Расширенный сценарий в одной вкладке действительно доведён до завершения:
  два synthetic mp4 → ответы 20/20 → вывод → завершён; затем новый объект для
  двух вкладок, disjoint edits → same-field choice → completion → late draft download.
- Найденная тестом ошибка late export исправлена: read-only завершённый осмотр
  проверяется до запрета записи CanPerform; никакой записи в завершённый объект нет.
- Финальные desktop/390px completion screenshots просмотрены:
  artifacts/inspection-draft-merge/browser/cfe0c4ecf41242879de9b7c6dc1b78af/.
  Дополнительный финальный browser прогон — 1/1 Passed, 1 мин 9 с:
  merge-browser-mobile-final.trx. Проверены геометрия 390px без переполнения и выбор
  значения на mobile; conflict-mobile.png просмотрен. Финальные снимки:
  artifacts/inspection-draft-merge/browser/d509b9d39ff848c4bf8b75d4413a8724/.

Команда scope suite: dotnet test tests/LandErp.Foundation.Tests -c Release --no-build
--filter 'FullyQualifiedName~InspectionDraftMerge|FullyQualifiedName~InspectionAcquisitionTests.Phase4Corrections|FullyQualifiedName~InspectionAcquisitionTests.InspectionSnapshotReconnect|FullyQualifiedName~InspectionAcquisitionTests.AssignedInspector'.
Для browser host LANDERP_DOTNET указывает установленный dotnet; тесты используют
синтетические сертификаты/БД и не получают production конфигурацию.
