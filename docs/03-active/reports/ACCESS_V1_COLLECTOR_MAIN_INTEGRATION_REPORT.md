# Access V1 + Collector enrichment integration

**Base `main`:** `54fa184e98f41dba3f040cde41aec181153ea054`

**Inputs:** Access V1 R5 `375c75d43a975db9a47d8c2959744667e5b51ef5`; Collector enrichment `d864f69bd9df6e7fb47d6b084b2eeeb534151da5`

**Integration branch:** `codex/access-v1-collector-integration`

The two branches were merged without text conflicts. The combined Release build initially exposed one obsolete test constructor; `IncomingMonitoringTests` now uses the current Access V1 constructor. Targeted PostgreSQL tests then found that the new `catalog.listing_contacts` table was absent from runtime grants. `ProductionDatabaseInitializer.ApplyRuntimeAccessAsync` now grants its runtime role `SELECT,INSERT,UPDATE`; no delete or broader catalog permission was added. The original Collector migration SQL was not changed.

Checks: locked restore passed; Release build passed with 0 warnings and 0 errors; Parser contract/transport tests 16/16 passed. The focused Foundation selection initially passed 23/25; the two failures were the contact write/read and HTTPS Collector delivery, both caused by the missing table grant. After the grant fix, those two tests passed 2/2. The selection also covered Access V1 AP-02–AP-05, cutover, clean PostgreSQL migrations and `HasPendingModelChanges() == false`. No full suite or browser tests were run for this integration.

The existing localhost Server and Worker were stopped before deployment. The combined LocalSetup applied pending schema/grants to the existing `landerp_local` database without recreating it. The combined Server and Worker were started with the existing private settings on `https://localhost:7240`; `/health/live`, `/health/ready`, and `/account/login` returned HTTP 200. Manual browser acceptance remains with the owner.

The repository `main` is to be fast-forwarded to this integration commit; no rebase, force-push or history rewrite is required.
