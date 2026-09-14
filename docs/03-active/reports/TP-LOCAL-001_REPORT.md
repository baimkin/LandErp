# TP-LOCAL-001 — отчёт реализации

Статус: реализован локально, ожидает ручного испытания и приёмки владельцем.
Разрешение реализации: прямой запрос 2026-09-13.
Ветка: `spike/001-avito-viability`. Gate 03–08 не начаты.

| Часть | Состояние | Проверки |
| --- | --- | --- |
| A Модели и обновление SQLite | Verified offline | Legacy backup/import, rollback, повторное открытие, future schema |
| B Ссылки, настройки, очередь | Verified offline | 20 ссылок / 10 выбранных, concurrent claims, token/revision, settings snapshot |
| C Сессии и вкладки | Verified offline | Реальные 3+3 leases Chrome, отдельные профили; global limit 1, pause/stop |
| D Avito | Verified offline | Полное описание, seller/statistics, wheel, numeric pagination, protection |
| E Cian | Verified offline | Синтетическая DOM и присланный образец: 28 карточек, конфликт площади |
| F Свежесть, продолжение, пауза | Verified offline | 4→5→7, skip/force/TTL, source CAPTCHA pause и ручной resume |
| G Интерфейс | Verified offline | Добавление двух источников, settings save, resize, отсутствие autorun |
| H Общие проверки и объём | Verified offline | Release, regression, 10 000 записей, audit; итоговые команды ниже |

Live: NotVerified. Успешные синтетические проверки не доказывают доступность сайтов.

## 1. Исходная точка

HEAD до реализации: `f72c31a9b0984964f55bd1b90a6f3eb63304a36e`.
В рабочем дереве уже находились непубликовавшиеся код Gate 02 и документация
подготовки TP. Они сохранены; принятые контракты Gate 01 и их тесты не переписаны.
Gate 02 не объявлен принятым. SPIKE-001/Go не закрыты.
Commit/push не выполнялись: текущий запрос разрешает реализацию и проверки.

## 2. Файлы этого этапа

Все пути ниже относительно корня репозитория.
Сокращения LocalCollection и Desktop относятся к перечисленным рядом project roots.

- `src/LandErp.ParserSpike/LocalCollection/Models.cs`: общий versioned envelope,
  Presence/Raw/Parsed, задания, settings, независимые интерфейсы.
- `LocalCollection/SearchUrls.cs`: whitelist HTTPS/source/filter, canonical search
  и безопасные DOM checkpoints, проверка соответствия поиску при пагинации.
- `LocalCollection/LocalStore.cs`: schema v1, backup legacy базы, импорт истории
  и старых запусков, очередь, claims, committed prefix, merge, SQL paging/export.
- `LocalCollection/BrowserSessions.cs`: source contexts/profiles и leases вкладок.
- `LocalCollection/DomSourcePage.cs`: DOM snapshot, типизация Avito/Cian, wheel,
  click pagination, page classification/HTTP status и описание без 500-char cap.
- `LocalCollection/QueueRunner.cs`: worker pool, source budget/pause, свежесть/
  продолжение, безопасные checkpoints и остановка.
- `src/LandErp.ParserSpike.Desktop/WorkspaceController.cs`: local lifetime,
  instance ownership, открытие поиска без сбора.
- `Desktop/WorkspaceWindow.xaml`, `WorkspaceWindow.xaml.cs`: ссылки, очередь,
  journal, source notifications, filters/history/export.
- `Desktop/SettingsEditor.cs`: читаемый редактор настроек с валидацией.
- `Desktop/App.xaml`: запуск нового рабочего окна.
- `tests/LandErp.ParserSpike.Tests/LocalCollectionTests.cs`, `LocalBrowserTests.cs`,
  `QueueRunnerTests.cs`, `WorkspaceUiTests.cs`: новые проверки.
- `README.md`, `docs/START_HERE.md`, `docs/03-active/ACTIVE_TASK.md`,
  `TP-LOCAL-001_Локальный_сбор_Avito_Cian.md`, этот отчёт: актуальный маршрут,
  разрешение, карта модулей, инструкция и результаты.

Новые NuGet packages/версии не добавлялись. CPM/locks, изменённые до этого этапа,
сохранены. Прежние AvitoBrowser/SearchParser/SearchCollector/MainWindow/ListingStore
сохранены как совместимый прототип и регрессионный контур. Новое окно запускает
общий engine; в его runner нет зависимостей от Avito SearchListing/IsAvitoUrl.

## 3. Реализованные решения

### Очередь и прогресс

Поиск хранит label, canonical URL, source, selected/enabled, revision и archive.
Параметры фильтров и повторяющиеся значения сохраняются; tracking исключается.
Нераспознанный параметр отклоняется с пояснением, исходная потенциально
чувствительная ссылка не сохраняется. Detail URL отклоняется.

Запуск копирует отмеченные включённые links и settings в batch. Ссылка резервируется
атомарно Pending→Running с owner/token; unique active-link index исключает двойную
работу. Редактирование активной ссылки запрещено, изменение selection влияет
на следующий batch. Archive сохраняет историю.

Observations append-only/idempotent по pass/page/source/id/fingerprint; реальные ID.
Частичные снимки сохраняются по мере прокрутки. Завершённая страница и её next URL
фиксируются в одной транзакции с результатами. Ошибка/защита/неизвестная пагинация/
ограничение прокрутки не предоставляют завершённое покрытие. Смена номера/фильтров
вручную прекращает задачу, не приписывая данные другой странице.

Freshness 24h / resume 1h / max pages 10 изменяются в настройках. Skip проверяет
revision, compatibility, возраст pass и непрерывный committed prefix. End выдачи
покрывает более высокий новый limit; предел 4 не покрывает запрос 10. После 4
продолжение идёт с 5, известный конец 7 завершает pass. Resume age считается от
начала pass; stale/force начинают новый pass с 1. Незавершённая страница повторяется.

### Браузер и защиты

Avito/Cian имеют отдельные постоянные Chrome/Edge contexts и profiles, по 1–3
worker tabs; default 1+1, общий предел 6. При общем пределе 1 оба источника
обрабатываются последовательно. Leases не позволяют двум workers использовать
одну страницу. Общий бюджет переходов на источник не умножается на число вкладок.
Inactive вкладки после batch остаются открыты для просмотра и переиспользуются.
Смена браузера/таймаутов пересоздаёт свободный source context при следующем запуске.

Wheel идёт серией 6 малых событий (60px/60ms default), позиция мыши внутри окна,
DOM/высота/loader/стабильность проверяются с ограничением циклов/ожиданий.
Пагинация — следующий номер или next/Дальше в распознанном pagination block;
End/UnknownInvalid различаются. Фильтры и номер проверяются, циклы запрещены.
Cian DOM может убрать redundant location[0]=region; допускается только это
проверенное преобразование при сохранении region. Остальные изменения фильтров
дают ошибку, адреса следующей страницы не строятся самостоятельно.

CAPTCHA/login/429 блокируют новые действия всех вкладок источника. Другая
площадка продолжает при наличии общего слота. Оповещение указывает source,
worker и поиск. Resume только вручную и после повторной классификации именно
вкладок, обнаруживших защиту. Обычная ошибка — Continue либо PauseSource.
Ошибка базы останавливает batch. Нет retries/masking/proxy/CAPTCHA bypass.

Закрытие/stop отменяют ожидания и освобождают claims, сохраняя committed pages.
Заброшенная navigation при отмене закрывает свою вкладку перед переиспользованием.
Операции имеют finite timeouts; launch наблюдается до завершения своего timeout,
чтобы не оставлять поздно запущенный бесхозный browser/profile.
Instance file guard удерживается до закрытия; crash recovery не запускает сбор.
Browser profile дополнительно защищён от второго управляющего экземпляра.

### Поля и база

Из DOM основной выдачи: source/id/url/title/price/area assertions/assignment/
address/transport/full description/raw date/seller name/profile/type/statistics/
completed advertisements/badges/available photo URLs/explicit unit price.
Derived unit price отделена от исходного поля. Статистика «завершённых» не
объявляется общим количеством объявлений; тип продавца берётся из явного label.
Отсутствующие поля сохраняют Presence. Изображения не скачиваются, телефон/
контакты не раскрываются и кнопки сообщений не нажимаются.

Противоречащие площади сохраняются с origin/raw/unit conversion и AREA_CONFLICT,
canonical площадь остаётся null. Нет усреднения/догадок. Description читается
через textContent с абзацами; защитный предел 100000 символов помечается явно.
Advertising/recommendations/other regions исключаются; unsupported DOM не
трактуется как пустой успешный результат.

SQLite user_version=1, резервная копия через BackupDatabase учитывает WAL.
Миграция транзакционная, повторная не дублирует, future version отклоняется.
Старые таблицы остаются; legacy Avito история импортируется с provenance Legacy,
неизвестным presence текста и без придуманного page coverage. Старые runs видны
с reason Legacy; свежесть из них не выводится.

Ключ latest: source+external ID, history сохраняет исходное наблюдение.
Missing не очищает известное; out-of-order observation не переписывает более
новое latest. Сортировка/фильтрация/offset/limit выполняются в SQL; Cyrillic поиск
использует ordinal ignore-case функцию внутри SQLite. Run export потоковый,
с schema/version/result IDs и явной семантикой raw observations.

## 4. Проверки

Фактически запускались:

```powershell
dotnet restore LandErp.slnx --locked-mode --artifacts-path artifacts/local-001
dotnet build LandErp.slnx -c Release --no-restore --artifacts-path artifacts/local-001
dotnet test LandErp.slnx -c Release --no-restore --artifacts-path artifacts/local-001 --filter 'TestCategory!=Live' --logger 'trx;LogFileName=local-verified.trx' --blame-hang-timeout 60s
dotnet format LandErp.slnx --no-restore --verify-no-changes
dotnet list LandErp.slnx package --vulnerable --include-transitive --no-restore
git diff --check
```

Последний подтверждённый полный запуск: 63 Passed, 0 Failed, 0 Skipped.
Release: 0 warnings, 0 errors. Format verify и git whitespace checks: Passed.
CLI непосредственно из новой сборки: help exit 0; unknown command exit 2.
Дополнительно проверены одинаковые ID двух источников, связи двух поисков
с одной записью и Cyrillic SQL search/source/link/job filters.
Включены принятые regression contracts/CLI, прежний desktop/browser и новые
storage/claims/TTL/migration/runner/source DOM/UI проверки.
Проверены stop в Open/Read/Wheel/Next/Follow, rollback failed page commit,
recovery и stale owner rejection, user pause, source CAPTCHA и manual resume,
global limit 1 и pool 3+3, реальные Chrome contexts с отдельными profiles.

Присланный Cian файл обработан локально, scripts disabled, запросы ресурсов
заблокированы. Получено 28 cards; контрольные price 21900000, area 2628 м²,
seller Павел Романов, transport 19 км от МКАД, AREA_CONFLICT второй карточки.
Файл не скопирован в Git/fixtures/logs; содержимое страницы в отчёт не включено.

Synthetic объём: 10000 observations, transactional write около 1,8 секунды,
count+page100 около 0,15 секунды в одном запуске. Это измерение этой машины,
не SLA и не live запросы. Export 10000, повтор одного item не дублирует историю.
UI тесты: startup без браузера/queue, добавление Avito+Cian, settings save и
расширение grid при resize. Снимок проверен визуально:
`artifacts/local-001/workspace.png`.

В sandbox первый browser run завершился ошибкой driver process; после запуска
разрешённых тестовых окон вне ограничения среды полный набор прошёл.
Во время разработки исправлены UTF-8 synthetic fixture, refresh race списка,
global limit starvation и проверки source/page после ручной навигации.
Audit: известных уязвимостей в трёх проектах не найдено. Новых зависимостей нет;
license/support policy существующих Playwright/SQLite остаётся из Gate 02.
Profiles/DB/backups/downloaded HTML не входят в git tracked files.

## 5. Ограничения и NotVerified

- Live Avito/Cian и объём реальных ссылок в этом этапе не запускались:
  следующий шаг предназначен для испытания владельцем. Приёмка не объявлена.
- Второй компьютер/длительная нагрузка/реальное изменение DOM — NotVerified.
- Snapshot fixture является офлайн образцом; успешность не доказывает отсутствие
  защиты или полноту live pagination. При изменении DOM нужен разбор нового примера.
- Неизвестные фильтры URL требуют явного добавления безопасного параметра;
  очереди detail URLs не поддерживаются.
- Legacy runs не дают coverage; старую запись для fresh skip нужно собрать заново.
- Field warnings видны в деталях/исходной истории, invalid карточки — в причине
  частичного журнала; отдельного графика производительности/агрегатора ошибок нет.
- Inactive browser tabs остаются видимыми, global limit ограничивает активные workers.
- UI пока локальный инструмент испытания: подробные Raw/Presence/Parsed поля
  доступны как читаемый JSON; production views и серверный обмен не созданы.

## 6. Запуск и следующий шаг

Executable: `artifacts/local-001/bin/LandErp.ParserSpike.Desktop/release/LandErp.ParserSpike.Desktop.exe`.
База прежняя: `local-data/spike-002.sqlite`; backup рядом при первом обновлении.
Инструкция пошагового испытания находится в README.

Владельцу: закрыть прежний прототип, открыть новую сборку, добавить поисковые
ссылки Avito/Cian, сначала проверить 1+1 tab / 2–3 страницы и контрольные поля.
Затем добавить много ссылок, выбрать subset, настроить limit, запустить очередь,
проверить историю/повторы/resume/паузы и сверить main-only с браузером.
После отчёта исполнитель останавливается. Следующие Gate/production/server/
detail/телефоны/сообщения не начаты.

## 7. Замечания владельца при испытании

Зафиксированы по запросу владельца; исправление сейчас не выполнялось.

1. **Avito и Cian: слишком узкие ограничения сохранения ссылок.** Нельзя сохранить поисковую
   SEO-ссылку, например:
   `https://zvenigorod.cian.ru/kupit-zemelniy-uchastok-moskovskaya-oblast-odincovskiy-gorodskoy-okrug/`.
   Ограничение только на `/cat.php` препятствует обычному сценарию добавления поиска.
   Уточнение владельца: относится к обоим источникам, включая SEO-адреса,
   обычную выдачу и поиск с карты. Сохранение публичной ссылки поддерживаемого
   источника отделить от готовности парсера обработать конкретный тип страницы.
   Неподдерживаемый тип помечать явно; он не является пустым успешным сбором.
   Detail URL можно сохранить, но не запускать по нему обход страниц выдачи
   и не объявлять detail-парсинг реализованным.

   Пример карты Cian от владельца:
   `https://www.cian.ru/cat.php?bbox=55.45100680616616%2C37.033441283811456%2C55.66272863937307%2C37.91234753381145&center=55.55701093301586%2C37.472894408811456&deal_type=sale&engine_version=2&land_status[0]=2&maxmcad=30&object_type[0]=3&offer_type=suburban&region=4593&zoom=12`.
   Не отклонять сохранение из-за обычных дополнительных фильтров; сохранять
   географию и смысл поиска, включая bbox/center/zoom, maxmcad, land_status
   и повторяющиеся параметры. Исходную пользовательскую ссылку и нормализованный
   ключ поиска различать; не удалять влияющие на выдачу параметры молча.
   Сохранить проверки домена/публичного адреса и исключение секретов из хранения;
   неизвестный параметр не равнозначен опасному. Правила отделения tracking,
   чувствительных параметров и фильтров уточнить при реализации.
   Пагинацию и совместимость checkpoint проверять с учётом вида поиска
   и преобразования адреса сайтом.
2. **Интерфейс: отказ сохранения ссылки слишком неявный.** Владелец не сразу
   замечает, что URL не подходит. При доработке показывать заметную ошибку
   непосредственно рядом с полем URL: что не принято, почему и что сделать;
   сохранять введённую ссылку для исправления и явно подтверждать успешное сохранение.

Статус обоих замечаний: исправлены локально 2026-09-14, ожидают повторного испытания
владельцем. Результаты: [отчёт доработок](TP-LOCAL-001_FIXES_REPORT.md).
Это не приёмка TP и не начало следующего этапа.
