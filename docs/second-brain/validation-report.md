# Validation report

**Snapshot `reqviora-docs-r1` · Documentation draft · Product approval pending**

| Finding | Status | Evidence or closure condition |
| --- | --- | --- |
| VAL-01 · Package inventory | Passed | 19 canonical references plus manifest, JSON parse and SHA-256 checks. |
| VAL-02 · Traceability | Passed | All 46 BRD requirement IDs, 12 rule IDs, 13 authoring views, nine open decision IDs and Markdown file links checked. |
| VAL-03 · Scope approval | Open | Resolve OD-01–07 and record authorized product sign-off for the exact baseline. |
| VAL-04 · Persistence baseline | Open | Resolve OD-08 after reviewing PostgreSQL in the repository and SQL Server in the candidate BRD. |
| VAL-05 · Current implementation evidence | Inspected | Backend repository shows platform/auth and owner-scoped project APIs. Frontend repository shows scaffold and placeholders. |
| VAL-06 · Product execution | Pending | Run the Coffee Shop milestone and release workflows with signed-off prerequisites, fixtures and acceptance records. |

## Candidate product checks

| Area | Test evidence to collect |
| --- | --- |
| Access | Unauthenticated call, other project ID, expired/revoked grant, forbidden approval and MCP writes |
| Data integrity | Stale draft write, concurrent approval, stable old revision/export after later edits, interrupted-job retry |
| Requirements | Unreadable source, conflict/clarification, explicit human confirmation, changed or unmapped action |
| Blueprint and preview | Unsupported components, broken routes, unreachable screens, required field recovery and mock-only effects |
| Migration | Repeatable seven-to-eight mapping, prior review retained, clear rollback/recovery |
| Export | Before/after revision comparison, exact inventory, JSON/links, archive integrity and hash verification |
| Quality | Keyboard/assistive technology, declared browsers/viewports, Tamil/English flows and agreed performance workloads |

A product validation finding should carry stable rule ID, severity, affected object, version, evidence, recovery link and resolution or acknowledgment. Approval must reference the saved version and validation run. See [acceptance rules](business-rules-and-acceptance.md).
