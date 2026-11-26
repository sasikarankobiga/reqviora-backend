# Interactions and states

Describe each action with trigger, screen/component, allowed actor, preconditions, input validation, success outcome, error/recovery path and trace links. Model states with explicit UI feedback and focus behavior.

| Flow | Expected transitions and recovery |
| --- | --- |
| Source upload / STT | Awaiting input or consent → queued → processing → ready, unreadable or failed. Report progress, allow correction and apply retry limits. |
| Claim review | Suggested → confirmed by explicit user decision, rejected or unresolved/deferred. Preserve evidence, modifications and decision history. |
| Draft save | Editing → saving → saved. On conflict show latest version and preserve unsaved changes for recovery. |
| UI generation | Pending → schema validation → local/AI design ready or failure. Label source and offer supported fallback. |
| Edit / undo | Apply one supported change, record history, recalculate impacts and re-render preview. Confirm potentially destructive regeneration. |
| Mock interaction | Validate fixture input → loading/success/empty/error/denied states as applicable. Reset demo state between runs. |
| Approval | Validate saved version → blockers or ready → explicit authorized approval of that version → read-only snapshot. |
| Export | Requested → queued/running → ready, failed or access revoked. Bind retry to the same snapshot and recheck download authorization. |

Design mode edits the model. Test mode uses simulated state. A required control carries accessible field feedback, a recovery affordance and a deterministic outcome.

Source: BRD Modules 3–5, 8–13 and §§6–8.
