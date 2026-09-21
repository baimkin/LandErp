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
- .NET 10 SDK on the operator host for migrations/setup and ASP.NET Core Runtime compatible with the published release on the target host;
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
For the first installation, prepare three connection strings outside Git:

- provisioner: PostgreSQL administrator connected to the maintenance database, for example `postgres`;
- migrator: application database owner, targeting the new LandErp database;
- runtime: limited application role; this exact connection is also stored in `production.json`.

All three must target PostgreSQL 18 on loopback. Migrator and runtime role names
must differ. From elevated PowerShell run:

    ./scripts/Initialize-ProductionDatabase.ps1

The script requests missing provisioner/migrator connections through hidden
prompts, creates only absent roles/database, refuses unsafe existing role
attributes or foreign database ownership, applies migrations, replaces runtime
grants with the explicit least-privilege set and proves runtime DDL is denied.
It is resumable but never drops or overwrites an existing database.

Before a later release containing migrations, configure `LANDERP_MIGRATOR_CONNECTION` only in the elevated migration session and first review the generated SQL:

    ./scripts/Invoke-Migrations.ps1 -Action Script

Do not put migrator credentials into `production.json`; runtime uses the limited application role.

Then run `Initialize-ProductionDatabase.ps1`; the idempotent command applies
pending migrations, synchronizes the canonical built-in roles/permissions and
refreshes the explicit runtime grant set before the new binaries are deployed.
This is also the supported way to apply later built-in permission changes to an
existing production database; Server/Worker still never mutate schema or role
catalogs on startup.

## 4.1 Initial Owner

Create the first Owner only after database initialization and before public login:

    ./scripts/Initialize-ProductionOwner.ps1 -Login owner@example.com -OrganizationName "Company"

The script requests the migrator connection and initial password through hidden
prompts. It succeeds only on an empty identity/organization database. Repeating
the same login and organization is a no-op; different or partial existing identity
data causes a safe refusal for manual review. The first login requires password
change and MFA enrollment.

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

### 7.1 IIS + Certbot certificate renewal

Certbot renews its PEM lineage, but an IIS binding that points to an imported
Windows certificate does not follow that file automatically. For IIS deployments,
install the LandErp synchronization once from elevated PowerShell:

    ./scripts/Install-IisCertificateRenewal.ps1 `
      -SiteName "LandErp" `
      -HostName "erp.example.com" `
      -LineagePath "C:\path\to\certbot\live\erp.example.com"

The host must have `openssl.exe` available (or pass `-OpenSslPath`). The
installer validates that exactly one HTTPS binding matches the supplied IIS site
and hostname, stores only paths/names in
`C:\ProgramData\LandErp\config\https-renewal.json`, performs an initial
synchronization and registers the SYSTEM task `LandErp IIS Certificate` for a
daily 03:30 fallback check.

The installer also creates:

    C:\ProgramData\LandErp\bin\Certbot-DeployHook.cmd

Configure the existing Certbot renewal job to call that file as its
`--deploy-hook`. The deploy hook switches IIS immediately after a successful
renewal; the daily LandErp task is only a recovery path if that hook was skipped.
Do not create a second Certbot renewal schedule just for LandErp.

`Sync-IisCertificate.ps1` converts the current Certbot
`fullchain.pem` + `privkey.pem` to a temporary password-protected PFX without
placing the password on the command line, imports the new leaf certificate into
`LocalMachine\My`, updates only the exact configured IIS hostname binding and
performs a local TLS handshake using that hostname. It refuses certificates with
seven days or less remaining and does not delete old or unrelated certificates.
A failed verification leaves a visible Scheduled Task/Certbot hook failure for
operator investigation instead of reporting a false success.

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
