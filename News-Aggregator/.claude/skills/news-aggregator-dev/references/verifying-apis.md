# Verifying an SDK API before you code (the no-hallucination workflow)

The repo's overriding rule is "no hallucinated APIs." Method names, overloads, and especially
**runtime behaviour** must be confirmed against the **installed** package — not training data,
not the `docs/` chapters (which pin older versions and gloss over execution details). This is
the exact method that produced `agent-framework-1.9.md`. It costs a few minutes and prevents
the runtime bug/fix loops that are otherwise inevitable with the Agent Framework.

## Step 0 — find the real version

The authoritative pins are `src/Directory.Packages.props` (+ `src/global.json` for the Aspire
AppHost msbuild SDK). Read those first; the `docs/05` chapter is stale on purpose.

## Step 1 — grep the package's XML doc for signatures (fast)

NuGet ships an XML doc next to each assembly. It's the quickest way to confirm a member exists
and read its parameter list, without decompiling.

```bash
PKG=~/.nuget/packages/<package.id.lowercased>/<version>/lib/net10.0
ls "$PKG"                                   # find the .dll + .xml
XML="$PKG/<Assembly.Name>.xml"

# Methods on a type:
grep -oE 'name="M:<Namespace>.<Type>[^"]*"' "$XML"
# Properties / fields:
grep -oE 'name="(P|F):<Namespace>.<Type>[^"]*"' "$XML"
# All event/result types in a namespace:
grep -oE 'name="T:<Namespace>.[A-Za-z]*Event"' "$XML"
# Read the <summary>/<param> docs for one member:
grep -A15 'M:<Namespace>.<Type>.<Method>(' "$XML"
```

XML signatures encode generics/params verbosely, e.g.
`RunStreamingAsync``1(Workflow,``0,System.String,CancellationToken)` = a 1-generic method
taking `(Workflow, TInput, string, CancellationToken)`.

This tells you the **shape**. It does **not** tell you the **behaviour** (ordering,
required-but-undocumented calls like `TurnToken`, what ends a stream). For anything stateful
or stream-driven, go to Step 2.

## Step 2 — write a throwaway probe and observe (decisive)

When behaviour matters, prove it. The SDK (10.0.300) is on PATH at `~/.dotnet/dotnet`.

```bash
mkdir -p /tmp/probe && cd /tmp/probe
```

`/tmp/probe/probe.csproj` — pin the **exact** versions from `Directory.Packages.props`, and
turn OFF central package management so the probe is standalone:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Agents.AI" Version="1.9.0" />
    <PackageReference Include="Microsoft.Agents.AI.Workflows" Version="1.9.0" />
    <PackageReference Include="Microsoft.Extensions.AI" Version="10.6.0" />
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" Version="10.6.0" />
  </ItemGroup>
</Project>
```

In `Program.cs`, exercise the API with a **deterministic fake `IChatClient`** (so there's no
model dependency) and `Console.WriteLine` everything you're unsure about — for a workflow,
print **every** `WorkflowEvent`'s `GetType().Name`, the `ExecutorId`, message `AuthorName`,
`.Text`, the final status, and whether your aggregator/closure ran. Run it:

```bash
~/.dotnet/dotnet run -c Release 2>&1 | grep -E '<your markers>'
```

Iterate until the behaviour is unambiguous, then **delete the probe** (`rm -rf /tmp/probe`).
Fold what you learned into `agent-framework-1.9.md` (or the relevant reference) so the next
agent doesn't re-probe.

### What probing has already settled (don't re-discover these)

These came straight out of probes and are recorded in `agent-framework-1.9.md`:
- workflow input must be `List<ChatMessage>`, not a `string`;
- a `TurnToken(emitEvents: true)` is required or the run hangs `Idle`;
- aggregator message order ≠ input agent order; unnamed agents have empty `AuthorName`;
- `AgentResponseUpdateEvent.ExecutorId == AIAgent.Id`;
- `BuildConcurrent` runs its agents truly in parallel;
- `StreamingRun` is `IAsyncDisposable`; `WatchStreamAsync` ends (doesn't throw) on cancel;
- event subtype relationships that affect `switch` case ordering.

## Step 3 — prefer the live docs MCPs for breadth, the probe for truth

For "what's the recommended API/pattern", `microsoft_docs_search` /
`microsoft_docs_fetch` (Microsoft Learn MCP) and Context7 are good for breadth and current
guidance. But when the question is "does *this installed build* actually behave this way",
the probe is the authority — versions and behaviour drift between releases.
