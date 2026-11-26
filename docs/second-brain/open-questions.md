# Open questions

| ID | Decision needed | Suggested responsible roles | Exit evidence |
| --- | --- | --- | --- |
| OD-01 | Seven or eight stages? Confirm Stage 8, legacy mapping and recovery. | Product, UX, engineering | Signed stage contract and migration fixtures |
| OD-02 | Which targets, components, variants, layout and editor operations are supported? | Product, UX, engineering | Versioned registry and profile support matrix |
| OD-03 | Who may share, approve and delegate MCP reads? What is grant expiry/revocation behavior? | Product, security | Permissions matrix, grant lifecycle and denial tests |
| OD-04 | Which Blueprint/manifest versions, export fields and supplements are approved? How does the earlier prototype map? | Product, engineering | Approved schemas, inventory reconciliation and package fixtures |
| OD-05 | Which AI/STT providers, languages, caps, quotas and fallback policy apply? | Product, security, engineering | Provider decision, consent copy and Tamil/English quality and cost pilot |
| OD-06 | Which hosting, region, storage, retention, deletion and recovery policies apply? | Platform, privacy | Runbooks, owners and approved recovery/retention objectives |
| OD-07 | Which performance, availability, accessibility and browser thresholds gate release? | QA, UX, platform | Target matrix, workloads and verification plan |
| OD-08 | Adopt PostgreSQL as used by the backend or migrate to the BRD's suggested SQL Server? | Engineering, product | ADR, deployment/backup implications and migration decision |
| OD-09 | Which candidate helper ideas belong in V1 and how are they governed? | Product, UX, security | Scope disposition, new FR IDs if accepted and acceptance evidence |

OD-01–07 follow BRD Appendix D. OD-08 tracks the repository discrepancy and OD-09 tracks candidate enhancements from Appendix D.1.

## Candidate AI helpers under OD-09

- **Data model suggestions** map confirmed requirements to conceptual entities and relationships for review.
- **API contract suggestions** propose resources and request/response shapes linked to feature flows.
- **Data privacy advice** surfaces consent, retention, permission and sensitive-data questions.
- **Localization coverage** identifies missing translations or locale-sensitive formatting.
- **Design alternative comparison** checks two supported alternatives against confirmed goals and constraints.

Appendix D.1 records these as proposed specification and review aids. Inclusion, acceptance criteria and any new requirement IDs depend on change control.
