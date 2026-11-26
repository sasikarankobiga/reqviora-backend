# Feature-to-screen mapping

This matrix traces all 46 candidate requirements to modules and authoring views. A view may expose an outcome controlled by the backend. The checks are planned acceptance scenarios.

| Requirement | Capability | Modules | Views | Acceptance check |
| --- | --- | --- | --- | --- |
| FR-001 | Authenticate accounts and controlled recovery | M01 | UI-01, UI-03 | Register, sign in and complete a recovery request without exposing account secrets. |
| FR-002 | Create/search/archive/restore/resume projects | M02 | UI-02, UI-03, UI-06 | Create two projects, find one, archive and restore it, then resume its saved work. |
| FR-003 | Project-scoped roles and server authorization | M01 | UI-01, UI-03 | Verify owners and authorized members can access only resources allowed by their grants. |
| FR-004 | Choose separate target UI profiles | M02 | UI-02, UI-03, UI-06 | Select multiple design profiles and reopen the project with the same selections. |
| FR-005 | Manual builder without AI dependency | M02, M05 | UI-02, UI-03, UI-06 | Decline AI and complete the manually guided authoring workflow. |
| FR-006 | Version-safe draft saves and recovery | M02 | UI-02, UI-03, UI-06 | Save from two sessions and recover the stale client's edits after the conflict. |
| FR-007 | Upload multiple permitted project files | M03 | UI-04 | Upload multiple supported file types and receive a clear rejection for unsupported input. |
| FR-008 | Parse with source/segment anchors | M03 | UI-04 | Open a parsed claim and resolve its available page or text-segment anchor. |
| FR-009 | Consented Tamil/English transcription and edits | M03 | UI-04 | Record with consent in Tamil and English, correct the transcript and retain its provenance. |
| FR-010 | Freeform idea input | M03 | UI-04 | Save typed input as a distinct source that can support a claim. |
| FR-011 | Merge inputs without losing provenance | M03 | UI-04 | Analyze conflicting sources and retain both anchors in the review flow. |
| FR-012 | Extract goals, roles, features, journeys, screens | M04 | UI-05 | Queue analysis and review suggested goals, roles, features, journeys and screens. |
| FR-013 | Preserve suggested/confirmed/unresolved/excluded | M04 | UI-05 | Confirm, defer and reject separate claims and verify their persisted statuses. |
| FR-014 | Ask/answer clarification questions | M04 | UI-05 | Answer a clarification and inspect the impacted requirements. |
| FR-015 | AI gateway with quotas and rate limits | M04 | UI-05 | Hit the configured quota and verify metering, clear failure and manual continuation. |
| FR-016 | Feature priority, rules, acceptance intent | M06 | UI-07 | Persist feature priority, business rule references and acceptance intent. |
| FR-017 | Category default screens with rationale | M06 | UI-07 | Review category defaults with a reason for each suggestion. |
| FR-018 | Select/dismiss suggested or custom screens | M06 | UI-07 | Select, dismiss and add screens, then verify the saved selection. |
| FR-019 | Per-screen roles, purpose, actions, states | M06 | UI-07 | Inspect each selected screen's purpose, allowed roles, actions and states. |
| FR-020 | Trace goals → features → screens → actions | M06 | UI-07 | Follow a goal through its selected feature to its screen and action. |
| FR-021 | Edit navigation routes, graph and conditions | M07 | UI-08 | Edit route triggers, guards and destinations independently of node position. |
| FR-022 | Check route reachability/change impact | M07 | UI-08 | Remove a destination and surface affected paths and unreachable selections. |
| FR-023 | Design tokens and responsive rules | M08 | UI-06, UI-09 | Change a shared token and verify updates in saved profile previews. |
| FR-024 | Supported component instances and variants | M08 | UI-06, UI-09 | Render supported props and variants and report unsupported components. |
| FR-025 | Interaction, validation, permission and error states | M08 | UI-06, UI-09 | Exercise required, denied and error cases with a defined recovery action. |
| FR-026 | Compile schema-valid UI Configuration JSON | M09 | UI-09 | Compile a saved project to Blueprint JSON and validate against its matching schema. |
| FR-027 | Local starter UI fallback | M09 | UI-09 | Disable the AI provider and generate a labeled, supported local starter. |
| FR-028 | Safe authorized AI screen generation | M09 | UI-09 | Generate a schema-valid design through the authorized server job with provenance. |
| FR-029 | Render allowlisted components | M09 | UI-09 | Render registered types and surface unsupported entries without arbitrary execution. |
| FR-030 | Visual property/layout/action editing | M10 | UI-09 | Edit a supported property, layout and action, save and reopen. |
| FR-031 | Supported animation/motion editing | M10 | UI-09 | Change a supported transition and verify reduced-motion behavior. |
| FR-032 | Design edit history and regeneration safeguards | M10 | UI-09 | Undo or restore an edit and preview impact before regenerating. |
| FR-033 | Design/Test mode and viewport preview | M11 | UI-10 | Switch Design/Test and inspect the same journey in configured preview profiles. |
| FR-034 | Mock-only behavior in preview | M11 | UI-10 | Perform mock booking or checkout and verify only simulated state changes. |
| FR-035 | Flag editor-to-requirement impact | M10 | UI-09 | Remove a traced action and identify the impacted requirement before sign-off. |
| FR-036 | Deterministic blockers/advisories | M12 | UI-11 | Run validation twice against the same snapshot and compare rule IDs and severities. |
| FR-037 | Explicit approval of saved draft | M12 | UI-11 | Deny approval on stale or blocked drafts and accept an authorized valid version. |
| FR-038 | Compare/export immutable revisions | M12 | UI-11 | Compare revisions and verify a later draft edit leaves an older export stable. |
| FR-039 | Canonical 19-reference Second Brain + manifest | M13 | UI-11, UI-12 | Build the canonical 19-file inventory with a separate revision-bound manifest. |
| FR-040 | Markdown/JSON/ZIP versioned export | M13 | UI-11, UI-12 | Verify Markdown and JSON references and read a ZIP bound to one labeled snapshot. |
| FR-041 | Optional final design preview package | M11, M13 | UI-10, UI-11, UI-12 | When enabled by policy, retrieve the authorized design supplement for its revision. |
| FR-042 | Revocable project/revision read-only MCP | M14 | UI-13 | Read only granted context, revoke access and deny reads after revocation and all writes. |
| FR-043 | Audit access, approval, export and AI usage | M14 | UI-13 | Inspect attributed audit records for access, approval, export and AI usage. |
| FR-044 | Privacy/retention/provider disclosures | M03, M14 | UI-04, UI-13 | Check disclosures/consent and exercise approved retention and deletion workflows. |
| FR-045 | Accessible responsive web authoring | M01, M14 | UI-01, UI-03, UI-13 | Complete the core journey with keyboard, meaningful focus and supported sizes. |
| FR-046 | Safe 7-stage to 8-stage data migration | M05 | UI-06 | Preview and migrate a legacy draft while retaining its previous review data. |

See the [feature catalog](feature-catalog.md), [screen specifications](screen-specifications.md) and [business rules](business-rules-and-acceptance.md).
