# Goals and success criteria

| Goal | Pilot evidence | Suggested measurement |
| --- | --- | --- |
| Resume work safely | An authorized owner returns to the same saved draft and a stale client receives conflict feedback | Median and p95 save latency, conflict recovery success |
| Turn inputs into reviewed requirements | User can correct a transcript and trace a decision to the original evidence, or continue manually | Time from input to reviewed blueprint, percentage of accepted claims with resolvable anchors |
| Cover in-scope features | Each selected feature has screen, action and acceptance coverage or a surfaced gap | Fully covered selected features divided by selected features |
| Deliver an editable design | User generates a supported starter design, edits it and previews the saved change | Generation and preview p95, successful representative journeys |
| Make approval auditable | Authorized actor approves the exact saved version after deterministic checks | Approvals with actor, version, timestamp and validation evidence |
| Export a coherent handoff | Package inventory, hashes, internal links and revision identity agree | Verified exports divided by attempted exports, categorized failures |
| Preserve project isolation | Unauthorized reads, writes and MCP requests are denied | Unauthorized successes in negative cases, attempted access and audit coverage |

Set target thresholds, pilot workloads and browser matrix through OD-07. Record baseline, threshold, sample size and outcome with each pilot result.

Source: BRD §§2, 10–11.
