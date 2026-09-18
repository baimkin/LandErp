# LandErp Production Runbook — first small-team deployment

**Scope:** B4-03 preparation.
**Target:** Windows host, PostgreSQL 18, one Server + one Worker, HTTPS reverse proxy.
**Not included:** HA, Kubernetes, Prometheus/Grafana, automatic schema migration on startup.

## 1. Production layout

Recommended paths:

- releases: `C:\Program Files\LandErp\releases\<version>`;
- private runtime state: `C:\ProgramData\LandErp`;
- config/secrets: `C:\ProgramData\LandErp\config\production.json`;
- persistent ASP.NET Data Protection keys: `C:\ProgramData\LandErp\data-protection`;
- process logs: `C:\ProgramData\LandErp\logs`;
- backups: `C:\ProgramData\LandErp\backups`.

Server and Worker are managed by two native Windows Scheduled Tasks: `LandErp Server` and `LandErp Worker`.
They run under LocalService, start at boot and restart after failure. The task action contains no DB/storage secret.

## 2. Required software

- Windows PowerShell 5.1+;
- .NET 10 / ASP.NET Core Runtime compatible with the published release;
- PostgreSQL 18 server and PostgreSQL 18 client tools;
- an HTTPS reverse proxy on the same host or an explicitly trusted proxy IP.

## 3. Prepare private production config

1. Copy `deploy/windows/production.config.example.json` to a private location outside Git.
2. Fill the runtime PostgreSQL connection, Yandex Disk token, a stable 32-hex `ConnectionId` (for example `[guid]::NewGuid().ToString('N')`), public `AllowedHosts`, Data Protection path and proxy IPs. Never rotate `ConnectionId` while existing storage keys use it.
3. Never commit the filled file.
4. Run elevated PowerShell:

    ./scripts/Initialize-ProductionConfig.ps1 -SourcePath C:\Private\landerp-production.json

The initializer rejects placeholders, copies the config to ProgramData, creates the persistent key directory and restricts ACLs.

## 4. Database migrations

Server and Worker never change schema on startup.
Before a release containing migrations, configure `LANDERP_MIGRATOR_CONNECTION` only in the elevated migration session and run:

    ./scripts/Invoke-Migrations.ps1 -Action Script
    ./scripts/Invoke-Migrations.ps1 -Action Apply

Do not put migrator credentials into `production.json`; runtime uses the limited application role.

## 5. Publish a release

    ./scripts/Publish-Production.ps1 -Version 2026.09.18.1

Output is created under `artifacts\production\releases\<version>`.
The script performs locked restore and Release publish for Server and Worker. It does not run tests.

## 6. First install / controlled update

Run elevated:

    ./scripts/Deploy-ProductionRelease.ps1 -ReleasePath .\artifacts\production\releases\2026.09.18.1

The deploy script:

1. copies immutable versioned binaries under Program Files;
2. preserves previous release metadata;
3. registers or updates managed Server/Worker tasks;
4. configures boot start and restart-on-failure;
5. starts Worker and Server;
6. checks local `/health/live` and `/health/ready`;
7. restores previous release metadata automatically when a new deployment fails health checks.

It never applies migrations.

## 7. Reverse proxy / HTTPS

Production Server backend listens on loopback by default: `http://127.0.0.1:5080`.
Do not expose this port publicly.

The public reverse proxy must:

1. terminate TLS with a valid certificate;
2. redirect public HTTP to HTTPS;
3. proxy to `127.0.0.1:5080`;
4. send `X-Forwarded-For` and `X-Forwarded-Proto`;
5. be loopback or be listed in `ReverseProxy:KnownProxies`.

Server consumes forwarded headers before its HTTPS/account check. Authentication cookies remain Secure and Production emits HSTS.

Optional minimal Caddy example:

    erp.example.com {
        reverse_proxy 127.0.0.1:5080
    }

Equivalent IIS/nginx configuration is acceptable when it preserves the same TLS/header boundary.

## 8. Persistent Data Protection

Production startup fails if `Security:DataProtectionKeysPath` is absent or unavailable.
This prevents silent use of ephemeral cookie keys.

B4-04 must sign in through public HTTPS, restart Server and confirm expected authenticated session behavior survives the normal restart.

## 9. Logs

Each process launch writes separate stdout/stderr files to `C:\ProgramData\LandErp\logs`.
Use those together with the `/collectors` operational-health block.

## 10. Rollback

    ./scripts/Rollback-ProductionRelease.ps1

Rollback swaps current/previous immutable release metadata, restarts Worker/Server and checks `/health/live`.
Do not roll back binaries across an incompatible irreversible schema migration without an explicit migration rollback plan.

## 11. Backup

Use a dedicated PostgreSQL backup operator and pgpass. Do not pass DB passwords as command arguments.
Configure the operator session with `PGHOST`, optional `PGPORT`, `PGDATABASE`, `PGUSER` and standard pgpass or `PGPASSFILE`.

    ./scripts/Backup-Production.ps1

The backup contains:

- custom-format PostgreSQL dump;
- private production config;
- persistent Data Protection keys;
- active release metadata;
- SHA-256 manifest.

The backup directory contains secrets and must remain ACL-restricted.

Yandex Disk object bytes are provider-managed and are not duplicated by this script. B4-04 restore validation must prove that a restored DB record can still read at least one known stored attachment. Independent media replication is a separate future policy.

## 12. Restore drill

Restore uses an empty database name and refuses to overwrite an existing database.
Configure restore operator `PGHOST`, optional `PGPORT`, `PGUSER` plus pgpass, then run:

    ./scripts/Restore-Production.ps1 -BackupPath C:\ProgramData\LandErp\backups\<timestamp> -TargetDatabase landerp_restore_drill -ApplicationFilesDestination C:\Temp\LandErp-Restore-Drill

The script verifies dump SHA-256, creates a clean DB, runs pg_restore, checks migration history and critical row counts, and can restore config/Data Protection files into an isolated directory.
Required PostgreSQL role names/grants must exist on the validation instance; missing-role errors fail the drill.

To finish G-02 in B4-04, create a separate drill config pointing to `landerp_restore_drill`, start Server against it, verify `/health/ready`, sign in, inspect critical business rows and read a known attachment.

## 13. Reboot / crash acceptance for B4-04

Owner-run checks:

1. reboot the target Windows host;
2. confirm both Scheduled Tasks are Running;
3. public HTTPS `/health/live` and `/health/ready` succeed;
4. kill Server and verify Task Scheduler restarts it;
5. kill Worker and verify scheduler health recovers;
6. verify `/account/login` only through HTTPS;
7. execute backup plus clean restore drill;
8. execute one controlled deploy and one rollback.
