# Implementation checklist

The checklist is a delivery sequence for proposed work. Inspect current code before closing a checklist item.

| Order | Backend and shared contracts | Frontend and acceptance evidence |
| --- | --- | --- |
| 1 | Resolve open scope/role/database decisions, version API, Blueprint and registry. | Confirm builder and target profiles, record accessibility/browser matrix. |
| 2 | Verify local dev and CI, auth/session rotation, owner scoping and migrations. | Wire API environment, auth lifecycle, route guards and accessible project views. |
| 3 | Implement versioned draft save and resume, conflict handling, lifecycle and migration preview. | Deliver manual builder and preserve entered data across steps and reloads. |
| 4 | Add source ingestion, anchored extraction, job status, human decisions and consent. | Provide upload progress, evidence comparison, transcript correction and manual continuation. |
| 5 | Model features/screens/routes and deterministic coverage and reachability. | Deliver selection, custom screens, coverage links, graph/table editor and recovery paths. |
| 6 | Compile and validate Blueprint, integrate allowlisted local and controlled AI generation. | Render registry components, edit supported properties and test visible impact and history. |
| 7 | Freeze authorized revisions transactionally and build snapshot-bound export with integrity checks. | Show grouped findings, exact draft version, approval and revision-bound browsing/downloads. |
| 8 | Enforce read-only MCP grants, consent/retention, quota, monitoring and operational audit. | Display allowed administration surfaces. Test grant revocation and cross-project denial. |
| 9 | Verify migrations, authorized/unauthorized API behavior, concurrent drafts and export integrity in CI. | Exercise Coffee Shop across supported browsers, including accessibility and failure paths. |

## First vertical slice

1. Create a Coffee Shop project, enter text and upload one supported source.
2. Review suggestions for menu, ordering and booking. Select Home, Menu, Cart and Booking as needed and map chosen requirements to screens and mock actions.
3. Connect paths, compile a valid Blueprint and render a dark-theme design.
4. Edit content, color and a card layout. Save, reload and replay navigation with mock confirmation and recovery.
5. Introduce a missing destination and required-field issue, confirm findings, correct them and export a revision-labeled draft. Verify package inventory and hashes.

## Repository references

Backend setup, migrations and Swagger instructions are in the pinned backend README. Swagger in development describes implemented route contracts. Frontend setup is in the pinned frontend README. Current backend implements owner-scoped project CRUD and authentication endpoints. Frontend evidence is a Flutter scaffold. Candidate BRD API paths should be reconciled before wiring them, including BRD's project metadata PATCH and the implemented PUT. [Backend README](https://github.com/sasikarankobiga/reqviora-backend/blob/61a8b3dc6c487a99201db0192819e6967ea6b127/README.md) · [Frontend README](https://github.com/sasikarankobiga/reqviora-frontend/blob/f70decfaef23937643885432b28e49dc4ad8f6ea/README.md)

Source: BRD §§7–11.

## API and persistence handoff

| Workstream | Current evidence or candidate contract | Implementation review |
| --- | --- | --- |
| Account and project | Current routes include auth register/login/refresh/logout, `GET /api/users/me`, `GET/POST /api/projects` and `GET/PUT /api/projects/{id}` | Compare browser integration to development OpenAPI and project request collection. Recovery, collaboration and archive/restore are candidate work. |
| Authoring | Candidate resource families include project drafts, sources, transcriptions, analysis jobs, candidate claims, clarifications and builder steps | Keep stable entity IDs. Use version preconditions for saves and preserve source segment references and decision history. |
| Design | Candidate resource families include features, screens, navigation, Blueprints, design generation and preview | Compile schema-valid content, validate cross-references and identify generation jobs and results with the input draft version. |
| Review and export | Candidate resources include validation runs, reviews, approvals, revisions, exports and manifests | Bind each export to an explicit snapshot. Store validation evidence and approval atomically. Recheck authorization when reading the job and downloading. |
| Operations | Candidate resources include MCP grant management, project audit and admin usage / job health | Enforce project/revision scope on the server. Manage grants through authenticated APIs and expose references through the separately approved MCP transport. |

Use EF Core migrations and enforce project ownership and relationships at the persistence boundary. Keep source metadata and locators in the database and uploaded bodies in authorized private storage. Maintain separate mutable drafts, immutable approved snapshots, job status, approval records and archive metadata. Reconcile engine-specific constraints and migrations through OD-08.

Use stable error codes and trace IDs. Proposed categories include malformed input, missing authentication, denied or unavailable resource, version conflict, semantic validation and quota exhaustion. Verify exact status codes and response shapes against the implemented contract before wiring the frontend.
