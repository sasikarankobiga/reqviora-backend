# Business rules and acceptance

These checks express candidate acceptance intent. Execution evidence belongs in the [validation report](validation-report.md).

| Rule | Enforcement | Acceptance scenario |
| --- | --- | --- |
| BRULE-01 | Claim review and decision record | An AI result remains suggested until an authorized user explicitly accepts or edits and confirms it. Rejection or deferral stays visible. |
| BRULE-02 | Traceability validator | Remove the only action covering a required feature. Validation identifies the feature and affected screen and blocks approval for the critical omission. |
| BRULE-03 | Navigation validator | Enable a route to a deselected screen. The graph flags the edge and approval is blocked. |
| BRULE-04 | Screen state validator | Omit an error/recovery state from a required action. Validation identifies the missing state and applies the agreed severity. |
| BRULE-05 | Blueprint schema, registry and renderer | Supply an unsupported component or action. Rendering reports a supported fallback or prevents the behavior and surfaces a finding. |
| BRULE-06 | Revision transaction | Approve a valid saved draft as an authorized reviewer. Subsequent edits create or update a separate draft. Approved content retains its original identity and data. |
| BRULE-07 | Revision-bound export | Edit a later draft while packaging an earlier revision. Every exported file still reflects the selected revision and its lifecycle label. |
| BRULE-08 | API authorization and MCP grants | Try another project's data, a revoked revision grant and an MCP write. All are denied and security-sensitive attempts are audited. |
| BRULE-09 | Preview and sharing | Execute mock checkout or booking. Preview displays a simulation label and updates only the mock state. |
| BRULE-10 | Editor impact analysis | Remove a component tied to a requirement. The edit preserves the trace or reports affected requirements and actions before approval. |
| BRULE-11 | Builder migration | Migrate a seven-stage draft with populated review data. Preview the remap, recover on failure and retain the old review data in the new review stage. |
| BRULE-12 | Source processing and claim review | Upload an unreadable scan or conflicting sources. Mark uncertainty, keep available anchors and allow correction or clarification. |

Source: BRD §6.
