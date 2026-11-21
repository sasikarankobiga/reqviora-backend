# Navigation and journeys

## Candidate authoring routes

| Path | View |
| --- | --- |
| `/login`, `/register`, `/forgot-password` | UI-01 |
| `/projects` | UI-02 |
| `/projects/:projectId/overview` | UI-03 |
| `/projects/:projectId/sources` | UI-04 |
| `/projects/:projectId/review-input` | UI-05 |
| `/projects/:projectId/builder/:step` | UI-06 |
| `/projects/:projectId/features-screens` | UI-07 |
| `/projects/:projectId/navigation` | UI-08 |
| `/projects/:projectId/design/:designId` | UI-09 |
| `/projects/:projectId/preview` | UI-10 |
| `/projects/:projectId/review-export` | UI-11 |
| `/projects/:projectId/second-brain` | UI-12 |
| `/admin` | UI-13 |

Route entry checks identity, context and permission. After a reload or deep link, restore the authorized project/step or show a recoverable error. Keep the selected project and revision visible on preview, review and export.

## Main journeys

- **Manual** — sign in → create project → choose target profiles → builder → feature/screen coverage → navigation → tokens/components/states → design → mock preview → review and export.
- **Assisted** — add inputs → run source analysis → review evidence and clarifications → confirm requirements → continue through builder.
- **Approval** — save draft → validate exact version → resolve blockers → authorized explicit approval → read-only revision → export or browse.
- **Recovery** — failure or quota limit offers a retry or manual path. A stale write offers reload/compare while preserving recoverable edits.

## Customer design navigation

Model entry screen, source/destination, trigger action, optional guard and Back/Cancel/error paths. A dragged graph node changes its position. Editing an edge changes navigation. Validate selected targets, inaccessible roles and reachability from each designated entry flow. In the School Library example, Book Details links to a simulated pending-request action and librarian review is a separate authorized design path.

Source: BRD §§4, 8.2 and Appendix A. GUIDE §18.
