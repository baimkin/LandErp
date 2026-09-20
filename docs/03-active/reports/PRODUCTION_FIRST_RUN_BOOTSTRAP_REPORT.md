# Production first-run bootstrap

**Scope:** complete the two missing first-production operations after B4-03.

## Implemented

- `Initialize-ProductionDatabase.ps1` provisions separate migrator/runtime roles
  and the application database on loopback PostgreSQL 18, applies migrations,
  installs the explicit runtime grants and verifies runtime DDL denial.
- Existing roles/database are never replaced: unsafe attributes, foreign
  ownership and mismatched endpoints fail closed. Partial successful setup is
  resumable and no destructive cleanup is attempted.
- `Initialize-ProductionOwner.ps1` creates exactly one first Owner in an empty
  production identity/organization database. Exact retry is idempotent; any
  different pre-existing state requires manual review.
- Secrets are read through hidden PowerShell prompts, passed to the child process
  through temporary environment variables and removed in `finally`. They are not
  command arguments or output.
- Server and Worker startup remain unable to create roles, migrate schema or
  bootstrap Owner.

## Required checks

- Release build: passed, 0 warnings / 0 errors;
- PowerShell syntax for the two new scripts: passed;
- one targeted real-PostgreSQL scenario covering migrations, grants, DDL denial,
  first Owner, exact retry and refusal of a different Owner: passed, 1/1.

Browser tests are not part of this change.
