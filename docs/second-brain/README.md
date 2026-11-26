# REQVIORA Project Second Brain

**Documentation draft · reqviora-docs-r1 · Approval pending**

This is the shared engineering handoff for REQVIORA, the product being built. The 19 reference files and separate [manifest](manifest.json) follow the inventory proposed in BRD v0.5 Appendix C. The manifest records this documentation snapshot's inventory and SHA-256 checksums. Its format and [project.json](project.json) describe this repository handoff. The runtime export and Blueprint contracts have their own approval and versioning.

| Start here | Reference |
| --- | --- |
| Understand the product | [Overview](project-overview.md), [goals](goals-and-success-criteria.md), [roles](audience-and-roles.md), [scope](scope-and-constraints.md) |
| Build or review a feature | [Feature catalog](feature-catalog.md), [business rules and acceptance](business-rules-and-acceptance.md), [coverage matrix](feature-screen-mapping.md) |
| Design and test the experience | [Screens](screen-specifications.md), [routes](navigation-and-journeys.md), [tokens](design-tokens.md), [components](components-and-layout.md), [interactions](interactions-and-states.md), [accessibility](accessibility-requirements.md) |
| Check readiness | [Decision register](decision-register.md), [open questions](open-questions.md), [validation report](validation-report.md), [implementation checklist](implementation-checklist.md) |
| Read structured context | [Project data](project.json), [manifest](manifest.json) |

## How to use this handoff

- Use the BRD references for candidate business scope and the pinned GitHub commits for implementation evidence. Check current code and contracts before implementing an integration.
- Keep requirement IDs stable. Update affected coverage rows, decisions, acceptance checks and machine-readable data together. Change the documentation revision and refresh the manifest after edits.
- Treat examples, candidate features and open questions according to their recorded status. Obtain human sign-off on an exact revision before recording approval.

## Verify a local copy

Run this from the Second Brain folder with Python 3. Verification uses the raw file bytes, so retain Git's LF line endings.

```python
import hashlib
import json
from pathlib import Path

manifest = json.loads(Path("manifest.json").read_text(encoding="utf-8"))
entries = manifest["files"]
expected = {entry["path"] for entry in entries}
actual = {path.name for path in Path(".").iterdir()}
assert len(entries) == len(expected) == manifest["referenceCount"] == 19
assert actual == expected | {"manifest.json"}
for entry in entries:
    data = Path(entry["path"]).read_bytes()
    assert len(data) == entry["sizeBytes"], entry["path"]
    assert hashlib.sha256(data).hexdigest() == entry["sha256"], entry["path"]
print("Verified all 19 reference files")
```
