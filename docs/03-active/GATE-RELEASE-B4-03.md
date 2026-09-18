# GATE-RELEASE-B4-03 — Deployment, HTTPS & backup/restore preparation

**Package:** Release Package 04 — Parser Reliability & Minimum Production Contour
**Gates:** G-02 + G-03 + G-04 preparation
**Branch:** `codex/release-package-04`
**Base:** `4f0407cd48cc101d8c4de793b58ae7a64d3c9d90`

## Goal

Prepare the repository-side production contour for the first small-team deployment without introducing HA or an external DevOps platform.

## Implemented

- Production requires external config via `LANDERP_CONFIG_FILE`.
- Runtime DB credentials and storage token live outside Git.
- Server persists ASP.NET Data Protection keys to an explicit directory.
- Production consumes trusted forwarded headers before HTTPS/account enforcement and emits HSTS.
- Public TLS terminates at a reverse proxy; Kestrel backend remains loopback HTTP.
- Native Windows Task Scheduler supervises Server and Worker.
- Managed tasks start on boot and restart after failure.
- Releases are immutable/versioned and current/previous metadata supports rollback.
- Deployment performs local live/ready health checks.
- Backup/restore uses PostgreSQL client tools and pgpass, not password arguments.
- Backup includes DB, config, Data Protection keys and release metadata.
- Restore refuses an existing target DB and validates migration history/critical rows.

## Process supervisor choice

The canonical requirement is a managed service/process. Task Scheduler provides boot start and restart-on-failure for the current console executables without NSSM, a custom wrapper or another NuGet package.

## Security boundary

Filled production config is never checked into Git. Recommended path: `C:\ProgramData\LandErp\config\production.json`.
Initializer ACLs allow Administrators/SYSTEM and read-only LocalService; key/log directories grant runtime write only where required.
Forwarded headers are accepted only from ASP.NET Core trusted proxies plus explicitly configured addresses.

## Backup boundary

Application backup covers PostgreSQL, runtime config, Data Protection keys and release metadata.
Yandex object bytes remain provider-managed; B4-04 must prove attachment readability through restored DB metadata.

## Still owner-run

Not claimed Passed here: actual task installation, reboot auto-start, crash restart, public certificate, proxy headers, login/session restart, pg_dump, pg_restore, restored Server startup, stored-file read and real deploy/rollback.

## Checks

Per owner workflow, build/tests/deploy/restore were **Not run**.
