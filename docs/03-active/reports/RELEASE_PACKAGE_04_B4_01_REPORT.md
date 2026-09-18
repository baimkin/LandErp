# Release Package 04 — B4-01 report

**Finding:** LR-10 — Parser heartbeat / LastUsefulActionAt null bug  
**Branch:** `codex/release-package-04`  
**Base:** `74bc3dd7f39b1d5dffd9911e8072973df56478bc`

## Что изменено

Исправлена одна semantics bug в `CollectorGateway.ApplyProgress`.

Ранее nullable `LastUsefulActionAt` напрямую присваивался `CollectorAgent.LastActivityAt`. Поэтому heartbeat без нового useful action и terminal cleanup могли обнулить уже известную отметку.

Теперь Server:
- принимает heartbeat до первого useful action;
- сохраняет null, пока useful action действительно ещё не было;
- после первого timestamp не стирает его последующими null heartbeat;
- не двигает last useful time назад;
- не стирает его при завершении job.

Transient progress counters после final по-прежнему очищаются.

## Что не пришлось менять

Shared queue/lease architecture уже реализует требуемые Package 04 свойства:
- compatible shared claim;
- one-active-work;
- heartbeat lease extension;
- expired reclaim;
- stale fencing;
- actual executor history;
- concurrent claim protection;
- local recovery при network/lease;
- partial/CAPTCHA/stop behavior.

Поэтому B4-01 не добавляет migration, новую lease таблицу или новый protocol version.

## Tests as code

Добавлен targeted PostgreSQL regression в `CollectionPoolTests`:
- early heartbeat with null useful action renews lease;
- later explicit useful action persists;
- later null does not erase it;
- completion preserves it while clearing transient progress.

Existing lease/reclaim/Parser transport tests остаются regression matrix B4-01.

## Проверки

Restore/build/PostgreSQL/ParserSpike по решению владельца: **Not run**.

## Dependency

Package 03 B3-04 validation остаётся owner-run и не считается принятой этим commit.
