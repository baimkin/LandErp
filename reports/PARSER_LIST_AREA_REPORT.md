# PARSER-LIST-AREA — 05.11.2026

Первый этап последовательной проверки: Крекшино Avito. Владелец разрешил
исправление, offline tests, рабочую установку и подтвердил закрытие Parser.
Ветка codex/inspection-file-improvements сохранена; накопленное не удалялось.

## Причина и изменение

Список уже подтверждал source-declared drawId через видимые map links, однако
QueueRunner создавал SearchAreaGuard по самому наличию drawId до определения
режима. Список не получает map markers/polygon и мог завершиться Partial с нулём.

Теперь guard активирует только фактически наблюдаемая карта. Для обычного
списка с drawId повторно проверяются исходный drawId, все видимые source map
links, фильтры (включая metro/opaque f), номер страницы и финальный read.
Неподтверждённые условия останавливают сбор до добавления нового снимка;
ранее сохранённые данные остаются. Список не требует map geometry/координат.
Программного открытия карты и дополнительных marketplace API запросов нет.

Карта по прежнему требует текущую геометрию, выделение и координаты внутри
полигона. Карта, сменившаяся списком в текущем run, получает LAYOUT_CHANGED;
source-declared список не может обойти уже начатый map guard.

## Файлы

- QueueRunner.cs: выбор guard по наблюдаемой карте; защита map→list.
- AvitoSearchIdentity.cs: все source links должны согласовываться.
- DomSourcePage.cs: возвращаются все видимые map-button/minimap-banner links;
  противоречащие ссылки не разрешаются выбором первой.
- AvitoListAreaTests.cs, test csproj, Fixtures/avito-krekshino-list-state.json:
  только public URL из сохранённого Крекшино, без raw HTML/auth/личных данных.
- README, ACTIVE_TASK, GATE-PARSER-LIST-AREA и этот отчёт.

## Проверки

Release tests build и staging build с `--no-restore -m:1 -p:UseSharedCompilation=false`
— Passed, 0 warnings/errors. Три own DLL staging совпали SHA-256 с проверенными.

Адресные AvitoListAreaTests — **10/10 Passed**. Выполняется настоящий
DomSourcePage над интерфейсным fixture (без браузера), source links из
сохранённого HTML Крекшино. Проверены список без map navigation/geometry/coords,
две страницы Local/Server, пропавшее подтверждение в начале/на второй странице/
в финальном read, changed draw/metro/filter/foreign/conflicting source links,
сохранность предыдущих данных и запрет понижения map→list.

`artifacts/parser-list-area/Test-Acceptance.ps1` — **180/180 Passed**, включая
прежние map/selection/geometry/list-filter/contract/outbox/WPF проверки.
Evidence: `artifacts/parser-list-area/tests/list-area-acceptance.trx`.
`git diff --check` адресных файлов Passed (только LF/CRLF warnings).
Browser/живой Avito/Server не запускались; пакеты и контракты не менялись.

## Ограничение и следующий шаг

Рабочая установка выполнена в `artifacts/LandErp-Parser`: проверены 155 SHA-256,
обновлены 4 файла, все 4 сохранены в backup. 125 защищённых файлов без изменений.
Evidence: `artifacts/parser-list-area/local-update/verification.json`.
Backup: `artifacts/parser-list-area/local-update/backup-58ea865458134a28a3f00a3841d90b8b`.
Диагностический ярлык сохранён. После установки Parser остаётся закрытым.

Для списка используется подтверждение области самим источником через его
публичные ссылки и неизменность условий. Это не независимое геометрическое
подтверждение каждой карточки; pixel/polygon proof для списка не заявляется.
Source, отражающий устаревший drawId в своих ссылках, остаётся пределом этого
доказательства. Требуется контроль состава реальной выдачи Крекшино владельцем.
Автоматический запуск после установки и переход к координатам/пагинации других
локаций не выполнялись. Следующий шаг — тот же ручной запуск Крекшино Avito.
