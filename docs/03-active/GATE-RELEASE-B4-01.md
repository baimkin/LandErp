# GATE-RELEASE-B4-01 — Parser heartbeat / lease reliability

**Package:** Release Package 04 — Parser Reliability & Minimum Production Contour  
**Finding:** LR-10 — Parser heartbeat / LastUsefulActionAt null bug  
**Branch:** `codex/release-package-04`  
**Base:** `74bc3dd7f39b1d5dffd9911e8072973df56478bc`

## Цель

Parser должен оставаться корректно живым до первого useful action, heartbeat обязан продлевать действующий lease, а отсутствие нового `lastUsefulActionAt` не должно стирать последнее уже известное useful-action время.

B4-01 не переписывает shared queue/lease architecture: reclaim, fencing и one-job ownership уже реализованы и покрыты существующими PostgreSQL tests.

## Найденный дефект LR-10

V1 protocol определяет nullable progress fields как additive optional values: `null` означает «значение не передано».

До B4-01 `ApplyProgress` выполнял:

`agent.LastActivityAt = progress?.LastUsefulActionAt`

Из-за этого heartbeat с `LastUsefulActionAt=null` и terminal `ApplyProgress(agent, null)` могли стирать уже известную отметку полезной активности.

## Исправление

`LastActivityAt` теперь:
- не изменяется при `LastUsefulActionAt=null`;
- устанавливается только явным timestamp;
- двигается только вперёд;
- сохраняется после terminal progress cleanup.

Heartbeat до первого useful action остаётся валидным и продлевает lease. Остальные transient progress fields по завершении job по-прежнему очищаются.

## Lease / reclaim review

Текущая реализация уже сохраняет:
- один действующий lease на Agent;
- shared claim через `FOR UPDATE ... SKIP LOCKED`;
- heartbeat продлевает только действующий lease;
- expired lease может забрать другой compatible Agent;
- старый heartbeat/result fenced через lease id;
- terminal job сохраняет фактического исполнителя;
- concurrent claims не выдают одну job двум Agent;
- Parser сохраняет local/outbox data при lease/network recovery;
- CAPTCHA/Authentication/Partial/Interrupted paths остаются существующими.

Новая архитектура для этих пунктов не требуется.

## Tests as code

В `CollectionPoolTests` добавлен сценарий:
1. claim;
2. heartbeat через 2 минуты с progress и `LastUsefulActionAt=null`;
3. lease реально продлён;
4. useful timestamp появляется;
5. следующий heartbeat с null его не стирает;
6. final result очищает transient progress, но сохраняет last useful timestamp.

Существующие regressions остаются обязательными для будущей B4-04 validation:
- `RepeatedClaimReturnsTheSingleActiveLeaseAndLeavesOtherWorkPending`;
- `StaleAgentAcceptIsRejectedAfterExpiredLeaseIsReclaimed`;
- `SharedPoolClaimsByCapabilityFencesLeaseAndKeepsActualExecutor`;
- `ActivationProgressAttentionAndLeaseCodesAreExplicit`;
- partial/CAPTCHA/stop/outbox tests;
- ParserSpike lease/network transport tests.

## Вне scope

- B4-02 operational health screen;
- новый heartbeat table/history;
- новый lease algorithm;
- deployment/backup;
- migration/schema;
- enterprise observability.

## Проверки

По решению владельца:
- restore: **Not run**;
- Release build: **Not run**;
- PostgreSQL tests: **Not run**;
- ParserSpike tests: **Not run**.

Наличие test code не считается Passed.
