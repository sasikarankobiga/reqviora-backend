# Components and layout

Use a versioned component registry defining allowed types, typed props, variants, child constraints, actions and states. The Blueprint compiler validates registry references before rendering. The visual editor changes supported properties and layout through the same contract.

| Candidate building block | Example responsibility |
| --- | --- |
| Container / layout section | Group children with order, spacing and profile-aware arrangement |
| Text / label | Provide readable content and accessible names |
| Button | Trigger a defined navigation, validation or mock action |
| Text input | Manage a typed value, required/error state and label |
| Card / list | Render structured content or mock results with an empty state |
| Dialog | Confirm an action or explain a blocking issue and restore focus on close |

These examples inform OD-02. The repository contains a candidate v1 Blueprint schema and School Library sample under `contracts/`. Use the checked-in schema with its matching examples during development and verify approvals before treating it as a production interface.

Rendering policy: validate types and prop ranges, limit nesting and per-screen resources, reject arbitrary code, surface unsupported components, separate local starter output from AI output, and keep editor regeneration versioned with a preview of affected changes. Persist history and validate restored designs against the active contract.

Source: BRD Modules 8–11 and Appendix A. GUIDE §§6, 19–20.
