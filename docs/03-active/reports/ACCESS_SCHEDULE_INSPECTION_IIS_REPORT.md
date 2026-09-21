# Access, scheduling, inspection workflow and IIS certificate package

Date: 21 September 2026  
Base commit: `f554c5a3297fadc28812133780f14ccfe30e288f`  
Working branch: `codex/access-schedules-inspections-iis`

## Scope

This package closes four production issues together because they share the same
operator/admin boundary:

1. editable fixed-time search schedules;
2. built-in role rights and existing-production synchronization;
3. a field-inspector workflow based on the existing SiteInspection checklist;
4. Certbot PEM renewal synchronization to an IIS HTTPS binding.

It deliberately does not redesign Collection scheduling, Procurement workflow or
the inspection checklist engine.

## Scheduling

The browser time inputs now use real Blazor two-way binding instead of a
display-only `value` plus a separate change handler. Changed values such as
`08:30`, not only the untouched `09:00` default, therefore reach the form
model before preview/save.

Both UI and server schedule rules validate the same contract:

- 1–12 fixed times;
- exact `HH:mm`;
- canonical sorted values;
- duplicate times are rejected rather than silently de-duplicated.

Manual and interval schedules retain the existing scheduler semantics.

## Built-in access catalog

Built-in role grants have one canonical synchronizer. It is called by Local
setup, fresh Owner bootstrap and the explicit production database setup command.

Relevant grants:

| Role | Relevant effective rights |
| --- | --- |
| Owner | all built-in administration, procurement and inspection rights |
| Administrator | administration + Collection + Procurement read; no manager/head decision or purchase confirmation |
| ProcurementManager | Procurement read/manager decisions + inspection assignment |
| ProcurementHead | Procurement read/head decisions/purchase + Collection/search/Parser management + inspection assignment |
| Inspector | assigned-inspection list/read + perform only |
| Viewer | existing read-only organization/procurement visibility |

Collection searches, schedules and Parser agents are organization resources.
Their services still require the corresponding permission and organization ID,
but no longer require `AccessScope.Organization`. A ProcurementHead can
therefore keep Department/Team procurement visibility without gaining unrelated
PropertyCases.

New Owner/Administrator assignments require `AccessScope.Organization`.
Existing assignments are not silently rewritten by setup; an intentionally
mis-scoped existing Administrator must be corrected through normal organization
administration so the change remains explicit/auditable.

## Inspection workflow

The existing `SiteInspection` remains the single inspection aggregate. It now
also stores:

- inspector;
- requester;
- requested time;
- due time;
- field instructions;
- nullable actual start time.

Assignment snapshots the existing checklist. The assigned Inspector can see only
their assignments and the dedicated inspection workspace. Assignment itself is
the object-level access grant: it does not make the employee a Procurement
manager and does not grant Incoming/Procurement queue access.

The mobile-first `/inspections` page shows active/completed assignments,
requester, due/overdue state, progress and a direct link to the checklist.
The existing checklist retains local-device draft protection, conflict handling
and media upload. Procurement managers/heads can assign or reassign an unfinished
inspection; Administrator can read inspection results through Procurement but
cannot assign, fill or confirm purchase.

Server authorization is repeated at the use-case boundary for inspection read,
start/save, assignment and inspection attachment access.

## Production role update

Before deploying this package to an existing production installation, run the
normal elevated database setup:

    ./scripts/Initialize-ProductionDatabase.ps1

It remains idempotent. In addition to migrations/runtime database grants it now
synchronizes the canonical built-in permission catalog. No permission migration
runs from Server/Worker startup.

## IIS / Certbot

New scripts:

- `scripts/Install-IisCertificateRenewal.ps1`
- `scripts/Sync-IisCertificate.ps1`

Installation is parameterized by IIS site, hostname and Certbot lineage; none are
hard-coded in source. Synchronization updates exactly one matching HTTPS binding,
verifies the served TLS certificate and never deletes unrelated certificates.
The Certbot deploy-hook is the immediate path and the daily SYSTEM Scheduled Task
is the recovery path.

See `PRODUCTION_RUNBOOK.md` section 7.1 for installation/operation.

## Tests added/extended

The package contains regressions for:

- edited fixed times in the real Collectors browser flow plus form/server validation;
- Collection management with non-Organization procurement scope;
- canonical role synchronization on fresh and already-populated role catalogs;
- Administrator Organization-scope invariant;
- assigned Inspector access without Procurement queue/purchase rights;
- Administrator Procurement read with server-side denial of business decisions;
- targeted IIS binding update / deploy hook / daily fallback source contract;
- EF migration/model consistency through the existing PostgreSQL test suite.

Actual repository CI status is checked against the final published commit; it is
not inferred from the presence of these tests.
