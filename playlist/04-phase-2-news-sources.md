# 04 — Phase 2: News Sources / Ingestion (Episodes 4–6)

[← Phase 1](03-phase-1-foundations.md) · [Index](README.md) · [Next: Phase 3 →](05-phase-3-model-providers.md)

**Phase accent color suggestion:** teal. **Goal of the phase:** implement the three `INewsSource`
adapters and the aggregation/dedup service so the app ingests **real tech news** — giving us
authentic data to feed the agents later, and a satisfying mid-series payoff (a live, merged feed)
*before* any AI is involved.

> **Teaching throughline for the phase:** every source is just an *adapter* implementing the
> `INewsSource` port from Ep 3. Same contract, three very different backends (Firebase, RSS XML,
> GitHub REST). This is the Dependency Inversion principle paying rent.

---

## Episode 4 — Hacker News source (~24–28 min)

**Hook.** "First real data: we'll pull the top stories from Hacker News — no API key, no SDK — and
learn a concurrency pattern we'll reuse all series."

**Learning objectives:**
- Implement `HackerNewsSource : INewsSource` over the keyless Firebase HN API.
- Use `IHttpClientFactory` (typed/named clients) instead of `new HttpClient()`.
- Learn the **bounded fan-out** pattern: `SemaphoreSlim` + `Task.WhenAll`, order-preserving.
- Test HTTP code **without the network** using `FakeHttpMessageHandler`.

**Prerequisites.** Ep 3 (the `INewsSource` port + `SourceOptions`). Tag: `start-ep04` → `end-ep04`.

**Talk segment.** How the HN Firebase API works: `GET /v0/topstories.json` → an array of IDs, then
`GET /v0/item/{id}.json` per story. That's an N+1 fetch — the perfect motivation for **bounded
parallelism**: fast, but capped so we don't hammer the API. Introduce the repo's house pattern
(from `Sources/HackerNewsSource.cs`): `using var gate = new SemaphoreSlim(Math.Max(1, max));`,
`Task<T>[] tasks = [.. ids.Select(id => FetchAsync(id, gate, ct))];`, `await Task.WhenAll(tasks)`
(preserves input order), acquire inside the task, release in `finally`.

**Hands-on build (in order):**
1. Register a named `HttpClient` for HN (base address, timeout) — note: full DI wiring lives in the
   composition root; show the registration shape.
2. `Infrastructure/Sources/HackerNewsSource.cs` — fetch top story IDs (cap at `MaxItems`), fan out
   per-item detail fetches under the semaphore, map each to `NewsItem` (`Source = "HackerNews"`),
   honor `CancellationToken`.
3. `Tests/Fakes/FakeHttpMessageHandler.cs` — a controllable handler returning canned JSON.
4. `Tests/Sources/HackerNewsSourceTests.cs`.

**Tests to show (selective — yes).** With `FakeHttpMessageHandler`: returns N mapped `NewsItem`s in
order; respects `MaxItems`; cancellation throws. This is the template for testing *any* HTTP source
offline — emphasize it, because Ep 5 and Ep 6 reuse it.

**Demo.** A tiny console/test harness that fetches **real** HN stories and prints titles + URLs —
the first time the app touches the outside world. (Pre-warm so it's snappy on camera.)

**Gotchas.** Don't `new` an `HttpClient` per call (socket exhaustion) — that's why we use
`IHttpClientFactory`. Acquire the semaphore *inside* the task and release in `finally` or you can
deadlock/leak permits. `Task.WhenAll` preserves the *task array* order, not completion order — call
that out.

**Visuals.** A fan-out/fan-in animation (IDs → parallel fetches under a gate → ordered list).

**Repo tag.** `start-ep04` → `end-ep04`.

**Title/thumbnail.** `Fetch Hacker News in .NET (Bounded Concurrency Done Right)` · "REAL DATA."

---

## Episode 5 — RSS/Atom source (~22–26 min)

**Hook.** "Half the web still speaks RSS. We'll consume any feed — Ars Technica, The Verge, your
blog — with one config-driven adapter."

**Learning objectives:**
- Implement `RssNewsSource : INewsSource` using `System.ServiceModel.Syndication`.
- Parse both RSS and Atom transparently with `SyndicationFeed`.
- Make the feed list **configuration-driven** (Options pattern) — add a feed with zero code change.
- Fan out across multiple feeds with the same bounded pattern from Ep 4.

**Prerequisites.** Ep 4 (the pattern + fake handler). Tag: `start-ep05` → `end-ep05`.

**Talk segment.** What `System.ServiceModel.Syndication` gives you: load XML into a `SyndicationFeed`
and read `Items` uniformly whether the source is RSS 2.0 or Atom. Map `SyndicationItem` →
`NewsItem` (title, the alternate link as URL, summary as content, `LastUpdatedTime`/`PublishDate`).
Tie back to a `docs/01 §1.6` success criterion: *adding an RSS feed is a configuration-only change*.

**Hands-on build (in order):**
1. Confirm the `Rss` section of `SourceOptions` (feeds list, `MaxItemsPerFeed`, `TimeoutSeconds`,
   `MaxConcurrency`).
2. `Infrastructure/Sources/RssNewsSource.cs` — for each configured feed: fetch XML via
   `IHttpClientFactory`, `SyndicationFeed.Load(XmlReader)`, take top `MaxItemsPerFeed`, map to
   `NewsItem` (`Source = "RSS"`); fan out across feeds under the semaphore; total/robust per feed.
3. `Tests/Sources/RssNewsSourceTests.cs` (feed XML via `FakeHttpMessageHandler`).

**Tests to show (selective — light).** One test: a canned RSS + a canned Atom document both parse to
the expected `NewsItem`s. Reinforces "one adapter, two formats." Run the rest fast.

**Demo.** Point it at the two real feeds from `appsettings.json`
(`feeds.arstechnica.com/...`, `theverge.com/rss/index.xml`) and print the merged items.

**Gotchas.** Feeds vary — some put the real link in `item.Links` alternates, some lack content;
map defensively and fall back. Wrap each feed fetch so one bad feed doesn't sink the batch
(per-feed try/catch, depending on the source's fail-fast policy — match the repo).

**Visuals.** A side-by-side of raw RSS XML vs. the mapped `NewsItem`.

**Repo tag.** `start-ep05` → `end-ep05`.

**Title/thumbnail.** `Parse Any RSS/Atom Feed in .NET (One Adapter)` · "RSS + ATOM."

---

## Episode 6 — GitHub source + aggregation & dedup (~26–32 min)

**Hook.** "Releases *are* tech news. We'll add GitHub as a third source — then merge all three into
one clean, de-duplicated feed."

**Learning objectives:**
- Implement `GitHubNewsSource : INewsSource` over the public GitHub Releases REST API.
- Handle the unauthenticated rate limit gracefully (detect & degrade).
- Implement `NewsAggregationService` (Core): fan out across **all** sources concurrently and merge.
- **De-duplicate** by canonical URL *or* normalized title; produce a deterministic ordering.

**Prerequisites.** Eps 4–5 (sources) + Ep 3 (the `INewsAggregationService` port). Tag: `start-ep06`
→ `end-ep06`.

**Talk segment.** Two ideas. (1) Another source, same port — `GET /repos/{owner}/{repo}/releases`,
map each release to a `NewsItem` (`Source = "GitHub"`), respecting `MaxReleasesPerRepo`; note the
GitHub `User-Agent` requirement and rate-limit headers. (2) **Aggregation & dedup** — the same
story shows up on HN *and* a blog, so we canonicalize URLs (lowercase scheme/host, drop fragment,
trim trailing slash) and normalize titles (case-insensitive, collapse whitespace) to drop
duplicates, then sort by `PublishedAt` desc with a stable `Id` tie-break (deterministic output the
agents can rely on).

**Hands-on build (in order):**
1. `Infrastructure/Sources/GitHubNewsSource.cs` — releases per configured repo, bounded fan-out,
   rate-limit detection, map to `NewsItem`.
2. `Core/Application/Services/NewsAggregationService.cs` — inject `IEnumerable<INewsSource>`, fan
   out with `Task.WhenAll`, merge, dedup (canonical URL / normalized title), deterministic sort.
3. Tests for the GitHub source (fake handler) and for the dedup/ordering logic.

**Tests to show (selective — yes).** The **dedup tests** are the star: two items with URLs differing
only by trailing slash/fragment collapse to one; same-title-different-source collapses; ordering is
deterministic. This is pure Core logic — fast, deterministic, perfect on camera.

**Demo (phase payoff).** Run **all three** sources together and print a single merged, deduped,
time-sorted feed of real tech news. Big moment: "we now have a real aggregator — and we haven't
touched AI yet."

**Gotchas.** GitHub requires a `User-Agent` header or it 403s. Unauthenticated calls are rate-limited
(~60/hr) — detect and surface it rather than crashing (matches `GitHubNewsSource`). Canonicalization
must be total and side-effect-free so dedup is deterministic.

**Visuals.** A "three pipes merging into one, with duplicates falling out" animation.

**Repo tag.** `start-ep06` → `end-ep06`.

**Title/thumbnail.** `Aggregate & De-dupe News from 3 APIs in .NET` · "3 SOURCES → 1 FEED."

---

### Phase 2 wrap (say this on camera at the end of Ep 6)

"We've got a real, multi-source, de-duplicated tech-news feed — all behind one `INewsSource`
contract, all swappable and config-driven. Now the fun part: teaching language models to read it.
Next phase, we plug in our model providers."

[← Phase 1](03-phase-1-foundations.md) · [Index](README.md) · [Next: Phase 3 →](05-phase-3-model-providers.md)
