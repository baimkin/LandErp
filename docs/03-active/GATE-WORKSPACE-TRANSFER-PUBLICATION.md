# WORKSPACE-TRANSFER-PUBLICATION

16.11.2026 владелец прямо разрешил commit/push в main двух завершённых доработок:
PARSING-WORKSPACE и INCOMING-MANAGER-TRANSFER, и запросил скрипт обновления сервера.
Прежние запреты публикации этих Gate сняты этим запросом. Production-развёртывание
выполняет владелец; агент только готовит проверенный скрипт.

Required reading: README → START_HERE → ACTIVE_TASK → этот Gate → уже прочитанные
GATE-PARSING-WORKSPACE / GATE-INCOMING-MANAGER-TRANSFER и отчёты →
PRODUCTION_RUNBOOK §§5–6, Publish-Production / Deploy-ProductionRelease →
только scope этих двух задач. Исходный v2 включён как утверждённый reference.

Remote base main проверен: edce4b916b9aacc96c5c636cae8731ce567a15bc.
Снимок строится от него, без иных локальных Parser/inspection-draft/media изменений.
В snapshot ACTIVE_TASK — только этот запрос, два завершённых этапа и история
удалённого main. Локальные ветка, индекс, накопленное и работающий preview сохраняются.

Файлы: UI/Collection contracts/read-model, три новых класса/компонента, три теста,
screens01/07, reference v2, три исходных Gate/отчёта, этот Gate/отчёт, ACTIVE_TASK.
Не добавлять migrations, настройки, tokens/credentials/local-data/artifacts.
Проверки: diff/scope/blob SHA, locked restore/Release точного main-based snapshot,
адресные tests и real browser/PostgreSQL, синтаксис pinned update script.
Публикация: один commit, единственный parent текущий main, atomic expected-head
fast-forward update main без force. Перед обновлением ref повторно сверить main.

Отчёт reports/WORKSPACE_TRANSFER_PUBLICATION_REPORT.md и ignored evidence
artifacts/workspace-transfer-publication/. После публикации подтвердить remote SHA
и все blobs. Скрипт сохраняет versioned releases C:\LandErp\artifacts\production\releases,
использует штатный deploy, не меняет private config/БД и проверяет reviewed commit.

Подготовка подтверждена: 24 scoped файла, незапланированных отличий base 0;
locked restore и Release 0 warnings/errors; адресные 35/35 Passed на точном
main-based snapshot, включая обе функции в браузере и сохранённую галерею.
Синтаксис update template проверен. SHA и факт обновления main фиксируются
во внешнем verification.json и итоговом сообщении после atomic ref update.
