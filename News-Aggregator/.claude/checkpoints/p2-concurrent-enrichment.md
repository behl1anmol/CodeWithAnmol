# Checkpoint — P2: Concurrent enrichment workflow

**Prompt:** P2 (`../prompts/mvp-completion-prompts.md`) · **Prereq:** P1 ✅ · **Result:** 150 passed, 0 failed.
**Commits:** `122e25a` (workflow) · `37d71f9` (agent-cache leak fix). Merged via PR #9.

## What it does
`ConcurrentEnrichmentWorkflow.EnrichAsync` is the fan-out/fan-in enrichment of docs §3.3: per
article, run **Summarizer ∥ Categorizer ∥ Ranker**, merge their replies via the pure P1
`EnrichedItemAssembler`, stream progress, and bound cross-article parallelism.

## Files
| File | Change |
|---|---|
| `Infrastructure/Workflows/ConcurrentEnrichmentWorkflow.cs` | implemented `EnrichAsync` (replaced `NotImplementedException`) |
| `Infrastructure/Agents/AgentFrameworkAgentFactory.cs` | cache one `AIAgent` per role (`ConcurrentDictionary<AgentRole, Lazy<AIAgent>>`) — fix `37d71f9` |
| `Tests/Fakes/FakeAgentFactory.cs` | new — `IAgentFactory` backing each role with a canned reply; optional `clientFactory` hook; caches one agent per role like the real factory |
| `Tests/Workflows/ConcurrentEnrichmentWorkflowTests.cs` | new — 7 tests |

## Verified Agent-Framework behaviour (Microsoft.Agents.AI.Workflows 1.9.0)
- Workflow input must be a `List<ChatMessage>`.
- `Workflow wf = AgentWorkflowBuilder.BuildConcurrent([summarizer, categorizer, ranker])`, run via
  `InProcessExecution.RunStreamingAsync(wf, input, ct)`.
- A `TurnToken(emitEvents: true)` must be sent (`run.TrySendMessageAsync`) or the run hangs `Idle`;
  with it, the run surfaces `AgentResponseUpdateEvent`s for live progress.
- `AgentResponseUpdateEvent.ExecutorId == AIAgent.Id`, so updates route back to roles **by id**.
  The aggregator's message order is **not** guaranteed → parse after the run, not in the aggregator.
- `WatchStreamAsync` ends **without throwing** on cancellation → call
  `cancellationToken.ThrowIfCancellationRequested()` after the loop (fail-fast, matching Core).

## Key decisions
- **Cross-article concurrency** bounded by `SemaphoreSlim(Math.Max(1, MaxDegreeOfParallelism))` +
  `Task.WhenAll` over index-ordered tasks → input order preserved (`BuildConcurrent` only
  parallelizes the 3 agents over **one** article — docs §3.3 "Batching note").
- **Parsing stays in Core** — the aggregator does no parsing; merge is `EnrichedItemAssembler.Assemble`,
  which is total (junk LLM output still yields a valid `EnrichedItem`, never throws).
- Agent-cache fix: building an `IChatClient` per agent per article retained provider/HTTP resources
  on every refresh → cache one agent per role for the factory lifetime (agents are stateless/reusable).

## Next
Unblocked: **P3** (sequential editorial), **P4** (UI), **P6** (smoke test). P3 is the sibling of P2.
