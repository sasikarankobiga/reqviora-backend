# Audience and roles

| Role or access class | Candidate access |
| --- | --- |
| Project Owner | Manage project settings and membership, confirm scope, export and approve where expressly authorized |
| Editor / Business Analyst | Work on requirements, clarifications, features, screens and traceability within the project grant |
| UI/UX Designer | Configure tokens, components, layouts, actions, editor history and preview |
| Reviewer / Approver | Inspect evidence, request changes and approve the exact revision only with approval authority |
| Developer / Viewer / Client | Read permitted drafts, revisions, previews or handoff references |
| Platform Administrator | Operate platform policy, quotas, abuse handling and audit within assigned permissions |
| External AI Client | Read authorized project/revision context through a separate revocable MCP grant |

Project membership and permissions apply to every project-scoped action, including source downloads, job results, shared previews and exports. Treat draft editing, membership administration and approval as independently authorized duties. UI guards guide navigation. The server enforces access. Re-check permissions after revocation. See OD-03 for the final matrix and sharing rules.

Current backend evidence covers authenticated owner scoping for existing project endpoints. Project-wide collaborator permissions remain candidate scope.

Source: BRD §§3, 4, 7.2 and Appendix D.
