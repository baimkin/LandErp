# Release Package 04 — B4-03 report

**Scope:** production deployment + HTTPS/proxy/keys + backup/restore preparation
**Branch:** `codex/release-package-04`
**Base:** `4f0407cd48cc101d8c4de793b58ae7a64d3c9d90`

## Repository-side work

Production now has an explicit external config boundary, persistent Data Protection configuration and trusted forwarded-header processing.

Added Windows production scripts for config/ACL initialization, Release publish, managed Server/Worker startup, auto-start/restart, controlled versioned deployment, rollback, DB/config/key backup and clean restore drill.

`PRODUCTION_RUNBOOK.md` documents paths, secrets, migrations, HTTPS, update/rollback, logs, backup and owner-run acceptance.

## Deliberately not added

- Kubernetes/containers/HA;
- Prometheus/Grafana;
- third-party Windows service wrapper;
- automatic schema migrations on Server/Worker startup;
- public Kestrel certificate management;
- independent replication of Yandex object bytes.

## Validation

No build, tests, scheduled-task install, reboot, HTTPS, backup or restore was executed here. They remain **Not run** for owner validation / B4-04.
