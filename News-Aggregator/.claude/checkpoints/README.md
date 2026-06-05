# Checkpoints

Point-in-time snapshots of completed work units (one per MVP prompt). Each records the files
changed, the verification result, and the unblocked next step — so any agent can resume cold.

| Checkpoint | Prompt | Result | File |
|---|---|---|---|
| Concurrent enrichment workflow | P2 | 150 passed, 0 failed | `p2-concurrent-enrichment.md` |
| Sequential editorial workflow | P3 | 164 passed, 0 failed | `p3-sequential-editorial.md` |

Earlier work (Core business logic, the three source adapters, P1 enrichment contract) predates
this checkpoints log — see `../analysis/session.md` and `../analysis/implementation-summary.md`.
