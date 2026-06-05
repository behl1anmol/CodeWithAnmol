# Microsoft Agent Framework — verified execution facts (1.9.0)

Everything here was **empirically verified** against the installed packages
(`Microsoft.Agents.AI` 1.9.0, `Microsoft.Agents.AI.Workflows` 1.9.0,
`Microsoft.Extensions.AI` 10.6.0) by running probes — not copied from `docs/03`, which is
directionally right but omits the parts that actually make a workflow run. Re-verify against
`src/Directory.Packages.props` if the pins have moved (see `verifying-apis.md`).

Assembly + XML doc locations (the fastest way to re-check a signature):
- `~/.nuget/packages/microsoft.agents.ai.workflows/1.9.0/lib/net10.0/Microsoft.Agents.AI.Workflows.xml`
- `~/.nuget/packages/microsoft.agents.ai/1.9.0/lib/net10.0/Microsoft.Agents.AI.xml`

## Table of contents
1. Building a workflow
2. Running a workflow — the non-obvious parts (TurnToken!)
3. Reading results & streaming progress
4. Routing agent output back to roles
5. Disposal & cancellation
6. Event type hierarchy (gotcha for `switch`)
7. Canonical per-article enrichment pattern (copy this)

---

## 1. Building a workflow

`AgentWorkflowBuilder` (namespace `Microsoft.Agents.AI.Workflows`). Verified overloads:

```csharp
// Concurrent — all agents get the SAME input and run in parallel; optional aggregator
// reduces each agent's output messages into one list. aggregator has a default (null),
// so the 1-arg form compiles and runs.
public static Workflow BuildConcurrent(
    IEnumerable<AIAgent> agents,
    Func<IList<List<ChatMessage>>, List<ChatMessage>>? aggregator = default);
public static Workflow BuildConcurrent(string name, IEnumerable<AIAgent> agents, Func<...>? aggregator = default);

// Sequential — each agent consumes the previous agent's conversation.
public static Workflow BuildSequential(IEnumerable<AIAgent> agents);
public static Workflow BuildSequential(string name, IEnumerable<AIAgent> agents);
```

- `BuildConcurrent` runs the supplied agents **truly in parallel** (probed: peak concurrent
  agent calls within one workflow = 3 for 3 agents). It parallelizes the *agent set over one
  input* — **not** across many articles. Batch across articles yourself with a
  `SemaphoreSlim` (see §7 and `docs/03 §3.3` "Batching note").

## 2. Running a workflow — the non-obvious parts

```csharp
// Generic input. Namespace Microsoft.Agents.AI.Workflows.
StreamingRun run = await InProcessExecution.RunStreamingAsync<TInput>(
    Workflow workflow, TInput input, string? runId = null, CancellationToken ct = default);
```

Two facts that are **not** in the docs and will silently break you:

1. **Input MUST be `List<ChatMessage>`.** Passing a `string` (even though
   `ChatClientAgent.RunAsync(string)` exists) produces **no events at all** — the workflow
   never starts. Use `[new ChatMessage(ChatRole.User, text)]`. A single `ChatMessage` also
   works; a bare `string` does not.

2. **You MUST send a `TurnToken` or the run hangs `Idle`.** Agents buffer the input and only
   "take their turn" when a `TurnToken` arrives. Without it: the start executor and the agent
   executors fire, then the run goes `Idle` — **no aggregation, no output, no progress events.**
   `emitEvents: true` is what makes the run surface `AgentResponseUpdateEvent`s (live progress).

   ```csharp
   await run.TrySendMessageAsync(new TurnToken(emitEvents: true));   // ctor: TurnToken(bool? emitEvents)
   ```

## 3. Reading results & streaming progress

```csharp
await foreach (WorkflowEvent evt in run.WatchStreamAsync(ct))
{
    switch (evt)
    {
        case AgentResponseUpdateEvent u:   // streamed token(s) from ONE agent
            // u.ExecutorId  -> which agent (see §4)
            // u.Update      -> AgentResponseUpdate; u.Update.Text is the delta; .AuthorName
            break;
        case WorkflowOutputEvent o:        // final aggregated output
            // o.Data is the aggregator's List<ChatMessage> (or the default last-message list)
            break;
    }
}
```

- `WorkflowEvent.Data` is the base payload property; `WorkflowOutputEvent` also has
  `.As<T>()` / `.Is<T>()` typed accessors.
- For the enrichment workflow we **ignore `WorkflowOutputEvent`** and assemble from the
  streamed updates instead (see §4 for why), keeping parsing in the Core assembler.

## 4. Routing agent output back to roles (the crux)

You usually need to know *which agent* produced a given message. Verified behaviour:

- **The aggregator's `IList<List<ChatMessage>>` is NOT in input-agent order.** Probed: agents
  passed `[Summarizer, Categorizer, Ranker]` arrived at the aggregator as
  `[Ranker, Categorizer, Summarizer]`. **Never route by index.**
- **`ChatMessage.AuthorName` == the agent's `Name`** — but only if you *set* a name. The repo's
  `AgentFrameworkAgentFactory` creates agents **without** a name, so `AuthorName` is `""`
  (empty) and the aggregator can't route by it either.
- **`AgentResponseUpdateEvent.ExecutorId` == `AIAgent.Id`** exactly (for unnamed agents). When
  an agent *is* named, the executor id is `"<Name>_<Id>"`. `AIAgent.Id` is a fresh GUID per
  constructed `ChatClientAgent`.

**Therefore the robust, in-scope routing** (no factory change needed): create the agents
yourself, build a `Dictionary<string, AgentRole>` keyed by each agent's `.Id`, accumulate
`AgentResponseUpdateEvent.Update.Text` per executor id, and call the Core assembler **after**
the run ("map after the run" — explicitly allowed by P2). This is why P2 does not route inside
the `BuildConcurrent` aggregator.

> Design note: naming the agents in the factory (so `AuthorName`/executor-id are human-readable
> in telemetry) is a reasonable *future* additive change, but it's outside P2's file scope and
> not required — routing by `.Id` is exact and self-contained.

## 5. Disposal & cancellation

- **`StreamingRun` is `IAsyncDisposable`** (it exposes `DisposeAsync`, no sync `Dispose`).
  Always `await using StreamingRun run = await InProcessExecution.RunStreamingAsync(...)`.
  Forgetting this is a resource leak (caught in review on P2).
- **`WatchStreamAsync(ct)` does NOT throw on cancellation** — "if cancellation is requested,
  the stream will end and no further events will be yielded, but this will not cancel the
  workflow execution." So to honour caller cancellation (fail-fast, matching Core), call
  `ct.ThrowIfCancellationRequested()` *after* the `await foreach`, and gate the whole thing on
  `await semaphore.WaitAsync(ct)` so a pre-cancelled token throws before any work starts.

## 6. Event type hierarchy (gotcha for `switch`)

Some events derive from others, so case order in a pattern `switch` matters (the compiler
errors `CS8120: switch case is unreachable` if you order them wrong). Verified:

- `AgentResponseUpdateEvent` **derives from** `ExecutorInvokedEvent`.
- `AgentResponseEvent` **derives from** `ExecutorCompletedEvent`.
- Both ultimately derive from `ExecutorEvent` (which has `.ExecutorId`) → `WorkflowEvent`.

Put the **more-derived** `Agent*Event` cases **before** the `Executor*Event` cases.

## 7. Canonical per-article enrichment pattern (copy this)

This is the verified shape used by `ConcurrentEnrichmentWorkflow` (Infrastructure). The
cross-article `SemaphoreSlim` bounding and order-preserving `Task.WhenAll` wrap around it.

```csharp
AIAgent summarizer = _agentFactory.CreateAgent(AgentRole.Summarizer);
AIAgent categorizer = _agentFactory.CreateAgent(AgentRole.Categorizer);
AIAgent ranker      = _agentFactory.CreateAgent(AgentRole.Ranker);

var roleByExecutorId = new Dictionary<string, AgentRole>(StringComparer.Ordinal)
{
    [summarizer.Id] = AgentRole.Summarizer,
    [categorizer.Id] = AgentRole.Categorizer,
    [ranker.Id]      = AgentRole.Ranker,
};
var replies = new Dictionary<AgentRole, StringBuilder> { /* one StringBuilder per role */ };

Workflow workflow = AgentWorkflowBuilder.BuildConcurrent([summarizer, categorizer, ranker]);
List<ChatMessage> input = [new ChatMessage(ChatRole.User, BuildPrompt(item))];

await using StreamingRun run = await InProcessExecution.RunStreamingAsync(
    workflow, input, cancellationToken: cancellationToken);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));   // REQUIRED — else hangs Idle

await foreach (WorkflowEvent evt in run.WatchStreamAsync(cancellationToken))
{
    if (evt is not AgentResponseUpdateEvent u ||
        !roleByExecutorId.TryGetValue(u.ExecutorId, out AgentRole role))
        continue;

    if (u.Update?.Text is { Length: > 0 } text) replies[role].Append(text);
    progress?.Report(new AgentProgress { Role = role, Stage = "enriching", /* counts */ });
}

cancellationToken.ThrowIfCancellationRequested();   // WatchStreamAsync won't throw on cancel

return EnrichedItemAssembler.Assemble(            // parsing stays in Core; assembler is total
    item,
    replies[AgentRole.Summarizer].ToString(),
    replies[AgentRole.Categorizer].ToString(),
    replies[AgentRole.Ranker].ToString());
```

**Testing it offline:** back the agents with `FakeAgentFactory(summary, categorizerJson,
rankerJson)`. Because the fake's agents are unnamed (like production), executor-id routing
works identically. To assert concurrency bounding, pass `FakeAgentFactory`'s `clientFactory`
hook a delaying `IChatClient` that records peak concurrent *articles* (key by the user prompt
— all three agents of one article share it), and assert `peak <= MaxDegreeOfParallelism`.
