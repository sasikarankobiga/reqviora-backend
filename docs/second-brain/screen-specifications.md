# Screen specifications

These are candidate REQVIORA authoring views. Customer design screens live inside an individual Blueprint. See [navigation](navigation-and-journeys.md) for paths and [interactions](interactions-and-states.md) for cross-cutting behavior.

| ID | View | Main actions and state |
| --- | --- | --- |
| UI-01 | Account | Register, log in and recover account. Show field errors and generic failure feedback. |
| UI-02 | Projects dashboard | Create, search, resume, archive and restore. Show empty list, loading and denied states. |
| UI-03 | Project overview | Edit project metadata/profiles, manage membership if authorized and resume saved draft. Show conflict and archival status. |
| UI-04 | Source hub | Upload, type, record, cancel, correct transcript and remove where permitted. Show consent, processing and unreadable indicators. |
| UI-05 | AI review | Compare candidate and evidence, confirm/edit/reject/defer, answer clarifications. Show quota, stale result and missing evidence. |
| UI-06 | Guided builder | Save incomplete steps, resume and migrate older drafts. Show progress, conflicts and missing sections. |
| UI-07 | Features and screens | Set priorities, select/dismiss suggestions, add custom screens and inspect coverage. Show unmapped and affected records. |
| UI-08 | Navigation graph | Lay out graph, configure edges and check reachability. Provide keyboard-accessible route table and broken-edge feedback. |
| UI-09 | Design workspace | Generate, inspect, edit, undo and regenerate with safeguards. Show tokens, states, fallback and impact findings. |
| UI-10 | Mock preview / final design | Select profile, reset state, click through and inspect unsupported behavior. Display simulation and revision labels. |
| UI-11 | Review and export | Validate, inspect blockers/advisories, request changes, approve exact draft, compare versions and export. |
| UI-12 | Second Brain explorer | Browse permitted references for one pinned snapshot, view manifest and download artifacts. |
| UI-13 | Platform operations | Inspect audit/jobs/usage and administer allowed policies. Surface empty, failed and denied states. |

User/project dashboards surface recent work, status and resume actions. The design/review view surfaces coverage and validation. Platform operations show job health, cost and access signals. Confirm per-view permissions before implementation.

Source: BRD §§4–5, 8 and Appendix A.

## Guided builder outputs

| Stage | Saved output |
| --- | --- |
| 1 · Identity & Audience | Project goals, users, design profiles, constraints and scope |
| 2 · Features & Screens | Reviewed feature priorities, selected/default/custom screens and mapping |
| 3 · Navigation | Routes, action triggers, conditions and journey entry points |
| 4 · Theme & Brand | Color, type, layout and responsive tokens |
| 5 · Components & Layout | Supported instances, variants, sections and layout rules |
| 6 · Interactions & States | Required fields, actions, validation and recovery states |
| 7 · AI UI Design Workspace | Supported generated screens, local fallback, visual edit history and mock preview |
| 8 · Review & Export | Coverage and findings, review, authorized approval, immutable revision and handoff |

Each stage can save partial work. For legacy drafts, preview migration and preserve old Stage 7 review material when moving it to candidate Stage 8. Approval depends on the exact persisted draft and its validation.
