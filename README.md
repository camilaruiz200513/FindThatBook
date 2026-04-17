# Find That Book

LLM-assisted search for noisy book queries. The backend takes a messy string like `"mark huckleberry 1884"`, uses Google Gemini to reconstruct the user's intent, searches Open Library, enriches the shortlist with the authoritative author metadata, and returns ranked candidates with explanations for why each one was chosen.

## Overview

The implementation is organized into four layers that each own one concern. The split is deliberate: the domain (rules, normalization, orchestration) is pure and testable without any HTTP or LLM dependency, and the infrastructure adapters can be swapped without touching the handler.

- **Frontend (`frontend/`)** — React 19 + Vite 8 + TypeScript. Drives an explicit `idle → loading → success / error` state machine, renders ranked candidates with cover art and the tier-tagged explanation, and surfaces `ProblemDetails` errors as typed UI states.
- **API (`backend/src/FindThatBook.Api`)** — ASP.NET Core host. Owns HTTP concerns: controller routing, CORS, token-bucket rate limiting, RFC 7807 exception middleware, correlation-ID middleware, Swagger, and DI composition.
- **Core (`backend/src/FindThatBook.Core`)** — pure domain. The five matching rules, `TextNormalizer`, the `FindBookQueryHandler` orchestration (extract → search → augment → enrich → rank+rerank), and the port interfaces that infrastructure implements.
- **Infrastructure (`backend/src/FindThatBook.Infrastructure`)** — adapters. Google Gemini (`GeminiLlmService` for extraction and rerank) and Open Library (`/search.json`, `/works/{id}.json`, `/authors/{id}.json`, `/authors/{id}/works.json`), plus caching, stampede coordination, and standard retry + circuit-breaker resilience on every outbound call.

```
┌───────────┐   ┌──────────────┐   ┌──────────────────┐   ┌─────────────────────────────┐
│ React UI  │──▶│ ASP.NET API  │──▶│ Gemini (extract) │──▶│ Open Library                │
└───────────┘   └──────────────┘   └──────────────────┘   │  /search.json               │
                       │                                   │  /works/{id}.json           │
                       │                                   │  /authors/{id}.json         │
                       │                                   │  /authors/{id}/works.json   │
                       │                                   └─────────────────────────────┘
                       │
                 5-rule matcher → (optional) Gemini rerank → ranked candidates
```

---

## Stack

| Layer       | Tech                                                                                       |
| ----------- | ------------------------------------------------------------------------------------------ |
| Backend     | **.NET 8**, ASP.NET Core Web API, Clean Architecture (3 src projects + tests)              |
| LLM         | Google Gemini (`gemini-2.5-flash`) with schema-bound JSON output (`responseSchema`)        |
| LLM rerank  | Optional second pass (`Matching:UseLlmRerank=true`) over the top-5 shortlist               |
| Catalog     | Open Library — all four documented endpoints, `IMemoryCache` (10 min TTL, empties skipped) |
| Enrichment  | Top-N candidates re-resolve primary authors via `/works/{id}.json` + `/authors/{id}.json`  |
| Resilience  | `Microsoft.Extensions.Http.Resilience` standard handler (retry + circuit breaker + timeout)|
| Rate limit  | `AddRateLimiter` token bucket, 20 req/min per client IP, applied to API routes only        |
| Stampede    | Per-key `SemaphoreSlim` coordinator (`CatalogCacheCoordinator`) prevents thundering herds  |
| Fallback    | Heuristic extractor (year regex + capitalized-token author) runs when Gemini is unavailable|
| Validation  | FluentValidation                                                                            |
| Errors      | RFC 7807 `ProblemDetails` via middleware + correlation IDs (`X-Correlation-Id`)             |
| Tracing     | `System.Diagnostics.ActivitySource("FindThatBook.Matching")` spans around the handler       |
| Docs        | Swagger UI (`Swashbuckle.AspNetCore`) at `/swagger`                                         |
| Tests       | xUnit + FluentAssertions + Moq (71 backend) + Vitest + Testing-Library (9 frontend) = **80**|
| Frontend    | React 19 + Vite 8 + TypeScript + Tailwind CSS 3                                             |

---

## Features

The items below are the non-obvious pieces of the implementation — the ones a reviewer is most likely to want to know exist before reading the code.

- **Graceful LLM degradation.** If `Gemini:ApiKey` is missing or the provider times out, the pipeline drops to a deterministic heuristic extractor (`HeuristicExtract` in `GeminiLlmService.cs`). The UI stays functional, so a reviewer without a key can still exercise the full search → rank → render flow.
- **Transparent startup signal.** On boot the backend logs exactly one of `Gemini LLM provider ready (model=…)` or `…using heuristic extractor fallback…`, so the active mode is obvious from the console instead of being discovered on the first failed request.
- **Correlation IDs end-to-end.** `CorrelationIdMiddleware` accepts an inbound `X-Correlation-Id` or mints a new GUID, opens a logger scope with the ID, and echoes it on the response header. Every log line inside the request — handler, infrastructure, middleware — carries the same ID.
- **Structured error contract (RFC 7807).** `ExceptionHandlingMiddleware` converts validation failures, LLM outages, and Open Library 5xx responses into `application/problem+json` bodies with the correlation ID attached as an extension. The frontend consumes a typed `ApiError`, so the UI never shows a raw stack trace.
- **Tracing on the matching path.** `ActivitySource("FindThatBook.Matching")` wraps the `FindBook` handler span so a tracer like OpenTelemetry can observe where time is spent across extract → search → augment → enrich → rank+rerank with no code changes. The exporter wire-up is the only missing piece and is listed under *Future Improvements*.
- **Schema-bound LLM output.** Both the extraction and rerank requests carry a strict JSON schema via Gemini's `responseSchema`. The model is prevented from emitting prose or markdown-wrapped JSON at the provider level; the parser keeps a defensive `StripMarkdownFences` step as a safety net.
- **Five-tier matcher with subtitle-aware normalization.** `MatchTier` goes `Exact → Strong → Good → Weak` (two Weak rules), with `TextNormalizer` stripping diacritics, English and Spanish stopwords, naïve plurals, and subtitles separated by `:`, `(`, `;`, or `or`. This is what lets `"The Hobbit"` still match `"The Hobbit, or There and Back Again"`.
- **Authoritative primary-author resolution.** For the top-N candidates, `OpenLibraryBookEnricher` re-derives primary authors from `/works/{id}.json` + `/authors/{key}.json`. An `author_name` from `/search.json` that doesn't appear in the works record is demoted from primary to contributor — the subtle difference that makes the `Exact` vs `Strong` split trustworthy when a compiler or illustrator sits at index 0.
- **Resilience on every outbound call.** `AddStandardResilienceHandler` is configured explicitly on the Gemini and Open Library HTTP clients: max 3 retries, 10 s per-attempt timeout, 30 s circuit-breaker sampling window, and a total-request timeout tuned per provider (60 s Gemini, 45 s Open Library, since catalog calls should finish faster than LLM ones). A flaky dependency degrades to a `ProblemDetails` response instead of hanging the request.
- **Rate-limited API, liveness-exempt health endpoint.** The book-finding endpoint is a 20 req/min token-bucket partitioned by client IP; `/health` is intentionally exempt so orchestrators can probe liveness even while the limiter is saturated by a noisy client.
- **Per-key cache coordinator.** `CatalogCacheCoordinator` holds a `SemaphoreSlim` per normalized cache key. Parallel requests for the same query wait on each other's cache fill; parallel requests for *different* queries run concurrently. Prevents thundering-herd against Open Library without serializing unrelated traffic.

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (builds on a 9 SDK as well if the 8 targeting pack is installed)
- Node.js 20 or newer (tested with 22)
- A Google Gemini API key — free.
  - Docs: <https://ai.google.dev/gemini-api/docs/api-key>
  - Create a key: <https://aistudio.google.com/app/apikey> (keys start with `AIzaSy…`)
  - Optional: the app falls back to a heuristic extractor if the key is missing, so the UI still works without one.

---

## Quick start

```bash
# 1. Store your Gemini key (one-time, never committed). Keys look like "AIzaSy…".
cd backend/src/FindThatBook.Api
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "AIzaSy...replace-with-your-real-key..."

# 2. Run everything with one command (backend + frontend + browser)
cd ../../..
dotnet run --project backend/src/FindThatBook.Api
```

Thanks to `Microsoft.AspNetCore.SpaProxy`, `dotnet run` (or **F5 in Visual Studio**) spawns `npm install` and `npm run dev` automatically, waits for Vite to be ready, and opens the browser at <http://localhost:5173>. The first run is slower because `npm install` has to populate `node_modules/`; after that it's cached.

- Frontend → <http://localhost:5173> (Vite, proxies `/api` to the backend)
- Backend  → <http://localhost:5186>
- Swagger  → <http://localhost:5186/swagger>

If you only want the API without the SPA, use the `Swagger only (no frontend)` launch profile (`dotnet run --launch-profile "Swagger only (no frontend)"`).

### Verify the API key setup

On startup the backend logs exactly one of these lines so you know which mode you're in:

```
info: FindThatBook.Startup[0]
      Gemini LLM provider ready (model=gemini-2.5-flash).
```

or, if the key was not picked up:

```
warn: FindThatBook.Startup[0]
      Gemini API key not configured; using heuristic extractor fallback. See README § Configuration.
```

Quick liveness check once the app is running:

```bash
curl http://localhost:5186/health
# => {"status":"ok","timestamp":"…"}
```

**Alternative setup methods** if `dotnet user-secrets` isn't a fit (CI, Docker, containers):

```bash
# Unix / macOS
export Gemini__ApiKey="AIzaSy...your-key..."

# Windows PowerShell
$env:Gemini__ApiKey = "AIzaSy...your-key..."
```

ASP.NET Core reads `Gemini__ApiKey` (double underscore) as `Gemini:ApiKey`. Do **not** put the key in `appsettings.json` — that file is committed and the field is left empty on purpose. `.env.example` documents the variable name but the .NET backend does **not** auto-load `.env` files; export the variable in your shell (or the container env) before `dotnet run`.

### Configuration

All settings live in `backend/src/FindThatBook.Api/appsettings.json`. The ones worth tweaking:

| Section       | Key                       | Default            | Notes                                                              |
| ------------- | ------------------------- | ------------------ | ------------------------------------------------------------------ |
| `Gemini`      | `ApiKey`                  | _(empty)_          | User secrets or env var `Gemini__ApiKey`. Sent via `x-goog-api-key` header (never in URL). |
| `Gemini`      | `Model`                   | `gemini-2.5-flash` | Any Gemini model supporting `responseSchema`                       |
| `OpenLibrary` | `SearchLimit`             | `25`               | Candidates pulled from `/search.json`                              |
| `OpenLibrary` | `CacheTtlMinutes`         | `10`               | Cache TTL for search results, works, and author resolutions        |
| `OpenLibrary` | `EnableEnrichment`        | `true`             | Toggles the `/works/{id}.json` + `/authors/{id}.json` second pass  |
| `OpenLibrary` | `EnrichTopN`              | `5`                | How many top candidates to enrich                                  |
| `OpenLibrary` | `EnableAuthorWorks`       | `true`             | Toggles `/authors/{id}/works.json` fallback for author-only queries|
| `Matching`    | `NearTitleThreshold`      | `0.7`              | Jaccard similarity for the `Good` tier                             |
| `Matching`    | `WeakMatchThreshold`      | `0.35`             | Minimum title overlap before keyword-only matches count            |
| `Matching`    | `UseLlmRerank`            | `true`             | Run Gemini as a reranker over the top-K shortlist                  |
| `Matching`    | `RerankTopK`              | `5`                | Shortlist size passed to the reranker                              |
| `Matching`    | `AuthorWorksFallbackLimit`| `10`               | Works fetched from `/authors/{id}/works.json` in author-only fallback |

No secret ever lives in `appsettings.json` — `ApiKey` is a placeholder. In production, inject via environment variables.

---

## LLM Parameters & Rationale

Gemini's `generationConfig` exposes several knobs that shape how the model samples. The values below were chosen deliberately for the two jobs this app asks of it — hypothesis extraction and candidate reranking — rather than left at their defaults.

### `temperature`

Extraction runs at `0.1`. Pulling `{title, author, year, keywords}` out of a noisy string is closer to classification than to creative writing, so we want the same query to yield the same hypothesis run after run. Reranking drops to `0.0` (hardcoded in `BuildRerankRequest`, not read from config): ordering a shortlist is a judgment call, not an opportunity for variety, and any accidental reshuffling between runs would erode trust in the explanations.

### `maxOutputTokens`

Capped at `512`. Gemini 2.5 counts the model's internal "thinking" tokens against this budget, so a tight cap would starve the reasoning step before it finishes. At 512 the reasoning gets enough room and the structured JSON output still has roughly 200 tokens of real headroom — comfortably above any realistic response the schema would emit.

### `topP`

Set to `0.95`. Nucleus sampling trims the extremely-improbable tail of the vocabulary without clipping the useful middle. Paired with a low `temperature`, the practical effect is that the model rarely emits anything surprising, but when two close paraphrasings are both plausible (e.g. `"J.R.R. Tolkien"` vs `"J. R. R. Tolkien"`) it still picks cleanly.

### `seed`

Fixed at `42`. Combined with the low temperature, a fixed seed gives a reviewer something worth checking: the same query produces the same hypothesis across runs, so manual spot-checks line up with what CI saw. `Seed` is nullable in the options — set it to `null` in `appsettings.Development.json` if you want to observe stochasticity during tuning.

### `responseSchema`

Both the extraction and rerank requests ride on a strict JSON schema (`ExtractionSchema` and `RerankSchema` in `GeminiLlmService.cs`). The schema lists the exact property names, types, and which fields are required, and Gemini refuses to emit anything that doesn't fit. This eliminates the whole class of failures where the model answers in prose or wraps JSON in markdown fences — the contract is enforced at the provider, not by our parser. The parser keeps a defensive `StripMarkdownFences` step anyway, because belt-and-suspenders is cheap.

### `systemInstruction`

The prompt carries two roles: the system role ("you are a book identification assistant, output this schema, follow these rules") and the user role (the noisy query). Today both pieces are concatenated into a single `contents[0].parts[0].text` payload. Gemini 1.5+ also supports a dedicated `systemInstruction` field in the request body that is designed exactly for this split; migrating to it is a small future improvement and is listed under *Future Improvements*.

All of the values above live in `appsettings.json` under the `Gemini` section and can be overridden per environment (user secrets, env vars, `appsettings.Development.json`). `TopP` and `Seed` are declared as nullable — setting either to `null` drops the field from the request payload entirely, deferring to the model's server-side default.

---

## How matching works

The pipeline runs five stages and exposes tracing spans at each boundary (`ActivitySource("FindThatBook.Matching")`):

1. **Extract** — Gemini turns the noisy query into `{ title?, author?, year?, keywords[] }` using a schema-bound JSON response. When the key is missing or Gemini is down, a built-in heuristic extractor (year regex + capitalized-token author) keeps the app useful.
2. **Search** — Open Library `/search.json`, with title/author/year/keywords parameters depending on what the hypothesis has. Wrapped with an `IMemoryCache` + per-key lock to absorb repeated queries and prevent stampedes.
3. **Author-works augmentation (optional)** — when the hypothesis is author-only, also resolve the author key via `/search/authors.json` and fetch the canonical list from `/authors/{key}/works.json`, merged with the search results.
4. **Enrichment (optional)** — for the top-N candidates, resolve the authoritative primary-author list via `/works/{id}.json` → `/authors/{key}.json`. Any `author_name` from `/search.json` that doesn't appear in the works record is reclassified as a contributor. This is what lets the "Dixon listed as adaptor" edge case actually work.
5. **Rank + rerank** — five `IMatchRule` strategies emit `(candidate, tier, explanation)`; the matcher keeps the highest-tier assignment per canonical `(normalized stripped title, year)` group. When `UseLlmRerank` is on, Gemini gets the top-K shortlist and returns its preferred ordering; the deterministic order is the fallback if the LLM can't respond or returns a partial permutation.

### Rule table

| Tier       | Rule                                 | Fires when                                                                    |
| ---------- | ------------------------------------ | ----------------------------------------------------------------------------- |
| **Exact**  | `ExactTitlePrimaryAuthorRule`        | Normalized title matches (subtitle-aware) _and_ queried author is a primary author |
| **Strong** | `ExactTitleContributorAuthorRule`    | Same title match, author is a **contributor** but not primary                 |
| **Good**   | `NearTitleAuthorRule`                | Token Jaccard similarity ≥ 0.7 (post subtitle stripping) and any author matches |
| **Weak**   | `AuthorOnlyFallbackRule`             | Title didn't match or wasn't provided, but primary author does                |
| **Weak**   | `WeakMatchRule`                      | Keyword hits in subjects/title and/or similarity above the weak threshold     |

`TextNormalizer` drives every comparison: lowercase + Unicode NFD diacritic stripping + punctuation stripping + English **and** Spanish stopword removal + naïve plural stemming ("philosophers" → "philosopher", "anillos" → "anillo") + **subtitle-aware matching** (so `"The Hobbit"` still matches `"The Hobbit, or There and Back Again"` — cut points are `:`, `(`, `;`, `, or `). The same normalizer is used for cache keys, which means "García Márquez" and "Garcia Marquez" collide on a single entry instead of producing two wasted lookups.

### Distinguishing primary vs contributor authors

Open Library's `/search.json` does not label primary authors. The first pass uses the pragmatic heuristic "first entry of `author_name` = primary". For the top-N candidates the enricher replaces that with the authoritative `/works/{id}.json` + `/authors/{key}.json` resolution, so the `Exact` vs `Strong` distinction holds even when a compiler/illustrator sits at index 0.

---

## API

### `POST /api/books/find`

Rate-limited (20 req/min per IP, token bucket).

```jsonc
// request
{ "query": "tolkien hobbit illustrated 1937", "maxResults": 5 }

// 200 OK
{
  "originalQuery": "tolkien hobbit illustrated 1937",
  "hypothesis": {
    "title": "The Hobbit",
    "author": "J.R.R. Tolkien",
    "year": 1937,
    "keywords": ["illustrated"]
  },
  "candidates": [
    {
      "book": {
        "workId": "/works/OL262758W",
        "title": "The Hobbit",
        "primaryAuthors": ["J.R.R. Tolkien"],
        "contributors": ["Alan Lee"],
        "firstPublishYear": 1937,
        "coverUrl": "https://covers.openlibrary.org/b/id/…-M.jpg",
        "openLibraryUrl": "https://openlibrary.org/works/OL262758W"
        // coverId, subjects, and isbns are also serialized but omitted here for brevity
      },
      "tier": "Exact",
      "ruleName": "ExactTitlePrimaryAuthorRule",
      "explanation": "Exact title match and primary author match ('J.R.R. Tolkien'); year 1937 matches."
    }
  ],
  "totalCandidates": 1,
  "processingTime": "00:00:00.8420000"
}
```

### `GET /health`

Cheap liveness probe, not rate-limited. Returns `{ status, timestamp }`.

Validation errors, LLM failures, and Open Library outages all come back as RFC 7807 `ProblemDetails` with a `correlationId` extension so you can trace the request through the server logs.

---

## Testing

### Strategy

The suite splits cleanly along the architecture boundary. `FindThatBook.Core` — the five matching rules, `TextNormalizer`, `BookMatcher`, and the `FindBookQueryHandler` orchestration — is exercised by pure unit tests that construct domain objects directly and assert on returned tiers, explanations, and candidate ordering. None of those tests touch HTTP, Gemini, or Open Library. `FindThatBook.Api` is exercised end-to-end through `WebApplicationFactory`, which spins up the real ASP.NET host with real middleware, DI graph, rate limiter, and exception pipeline, so the integration tests catch regressions in the wiring as well as in the domain.

The only pieces mocked are the outbound network calls. `StubHttpMessageHandler` sits in front of both the Gemini and Open Library HTTP clients and returns canned JSON that reproduces the real services' response shapes. Everything inside the app — the matcher, normalization, deduplication, primary-author resolution, the cache coordinator, the correlation-ID middleware, and the RFC 7807 error contract — runs unmocked. That is deliberate: the integration tests should catch subtitle-stripping regressions, a misplaced rate-limit policy, a missing correlation ID, or an explanation that stops citing the right field.

The five queries the PDF uses to illustrate the three query archetypes are each covered by a dedicated integration test in `ChallengeQueryIntegrationTests.cs` as a permanent regression suite: **sparse** (`dickens, tale two cities`), **dense/noisy** (`tolkien hobbit illustrated deluxe 1937`), and **ambiguous** (`mark huckleberry`, `twilight meyer`, `austen bennet`). Test method names mirror the query verbatim — for example `dickens_tale_two_cities_sparse_query_resolves_to_Dickens_primary_author_Exact_tier`, `tolkien_hobbit_illustrated_deluxe_1937_matches_with_subtitle_and_year_bonus`, and `austen_bennet_falls_back_to_AuthorOnly_when_bennet_is_a_character_not_the_author`. Each asserts on the concrete shape of the response: hypothesis fields, candidate count, expected tier, and that the explanation cites the right field.

Totals: **71 backend** (xUnit + FluentAssertions + Moq, unit and integration combined) and **9 frontend** (Vitest + Testing-Library), for **80 tests** overall. Every assertion targets concrete behaviour; nothing exists just to prove the code compiles.

### Commands

```bash
# Backend: 71 tests (unit + integration via WebApplicationFactory)
dotnet test backend/FindThatBook.sln

# Frontend: 9 tests (component + UI state transitions)
cd frontend && npm run test

# Full check (type check + build)
cd frontend && npm run lint && npm run build
```

The backend suite covers:

- Every matching rule with positive/negative/contributor-vs-primary cases (`Core/Matching/Rules/MatchRuleTests.cs`).
- `BookMatcher` ordering, `maxResults` clamp, deduplication by canonical title+year.
- `TextNormalizer` diacritic stripping, stopword removal (English + Spanish), plural stemming, subtitle stripping, Jaccard edges.
- `GeminiLlmService` happy path, HTTP failure, unparseable JSON, markdown fence stripping, **heuristic fallback when the key is missing**, **implausible-year rejection**, **`responseSchema` payload shape**, **prompt-injection robustness**.
- `OpenLibraryBookCatalogSource` mapping (incl. primary vs contributor heuristic), HTTP failure, empty-hypothesis short-circuit, URL shape.
- `OpenLibraryBookEnricher` — `/works/{id}.json` + `/authors/{key}.json` reassigning contributor→primary, graceful passthrough on failure, honors `EnrichTopN`.
- `OpenLibraryAuthorWorksSource` — `/search/authors.json` → `/authors/{id}/works.json` resolution, empty when author cannot be resolved.
- `CachingBookCatalogSource` cache hit/miss, empty results not cached, diacritic-folding cache keys.
- `CatalogCacheCoordinator` per-key serialization vs cross-key parallelism.
- `FindBookQueryHandler` orchestration: enrichment before matching, author-works augmentation for author-only queries, LLM reranking ordering.
- Full endpoint integration tests with `WebApplicationFactory` covering the **five PDF query examples** across the sparse, dense/noisy, and ambiguous archetypes (`dickens, tale two cities`, `tolkien hobbit illustrated deluxe 1937`, `mark huckleberry`, `twilight meyer`, `austen bennet`); see *Testing → Strategy* above for the breakdown.

The frontend suite covers the `SearchForm` submit flow, `BookCard` rendering (title link, tier badge, fallback cover), and `App`-level idle → loading → success / error transitions.

---

## Assumptions

- Gemini is the default LLM. If the key is missing, the heuristic extractor runs and the UI just shows a reduced hypothesis — no hard failure.
- Open Library's author ordering inside `/search.json` is not reliable; the enricher re-derives primary authors from `/works/{id}.json` for the top-N candidates and accepts the heuristic for the rest.
- Explanations are template-based (deterministic, traceable to the rule that fired) rather than LLM-written. The LLM is used for ordering (`UseLlmRerank`) instead — that keeps the explanations reproducible and free of hallucination, while still getting an AI second-opinion where it adds value.
- Empty search results are **not** cached so a transient Open Library timeout doesn't lock a query out for 10 minutes.
- No persistence: everything is process-local. Restarting drops caches.
- Stopwords cover English + Spanish. Other languages fall back to diacritic-stripped token matching, which still works for title equality but not for semantic similarity.

## Future Improvements

Future improvements I would make with more time, in rough priority order:

- **Vector embeddings for semantic retrieval.** The current matcher is lexical: `TextNormalizer` plus token Jaccard. Replacing the title-similarity step with Gemini embeddings stored in pgvector (or a lightweight FAISS index) would let the system match paraphrased queries like *"there and back again"* → *The Hobbit* that today fall into the `Weak` tier or miss entirely. It also subsumes the stopword/stemmer work that would otherwise need language-by-language curation.
- **Accuracy test harness with a labeled query dataset.** Correctness today rests on ~71 unit and integration tests plus the five PDF query examples. A larger labeled set (~200 realistic noisy queries with expected `workId` per tier) plus a batch runner reporting `precision@1` and `precision@5` would turn *"did we break matching?"* into a number, which is a prerequisite for any change to normalization or ranking.
- **OpenTelemetry exporter wire-up.** `ActivitySource("FindThatBook.Matching")` already emits a span around the `FindBook` handler; the remaining step is `AddOpenTelemetry().AddSource("FindThatBook.Matching")` with an OTLP exporter pointed at Jaeger, Tempo, or Datadog. Closes the tracing loop flagged in *Features*.
- **Redis for distributed cache and rate limiting.** `IMemoryCache` resets on restart and does not coordinate across instances. Swapping in Redis would amortize enrichment cost across deploys, and moving the rate limiter to a Redis-backed partition keeps the 20 req/min budget honest behind a load balancer.
- **Migrate the system prompt to Gemini's `systemInstruction` field.** Today the system role is concatenated into `contents[0].parts[0].text`. Gemini 1.5+ exposes a dedicated `systemInstruction` field designed for exactly this split; moving it there is idiomatic and may improve adherence on longer user inputs. Flagged in *LLM Parameters & Rationale*.
- **Assert outgoing request bodies in the HTTP stubs.** `StubHttpMessageHandler` inspects response shapes but does not verify what we *send*. Extending it to capture the request body would enable tests asserting that `seed` and `topP` are actually emitted (and omitted when nulled), closing a minor coverage gap in the `GeminiGenerationConfig` wiring.
- **CI/CD pipeline on GitHub Actions.** A workflow running `dotnet test backend/FindThatBook.sln` plus `npm run lint && npm run test && npm run build` on every PR, with required-check status and artifact upload on failure, would make the 80 tests load-bearing instead of aspirational.
- **Single-container deploy path.** A multi-stage Dockerfile (Node → Vite bundle → .NET → self-hosted static files) plus a Compose file with a Redis sidecar would turn the current "install two SDKs and run a command" flow into `docker compose up`. Mostly ergonomics, but also unlocks free-tier hosting for a live demo URL.

---

## Layout

```
findthatbook/
├── backend/
│   ├── FindThatBook.sln
│   ├── src/
│   │   ├── FindThatBook.Core/            # Domain, ports, matching rules, handler (pure)
│   │   ├── FindThatBook.Infrastructure/  # Gemini + Open Library adapters (search, enrich, author-works, caching, stampede)
│   │   └── FindThatBook.Api/             # Controllers, middleware, DI composition, rate limit, Swagger
│   └── tests/
│       └── FindThatBook.Tests/           # xUnit — unit + integration
├── frontend/
│   ├── src/
│   │   ├── components/                   # SearchForm, ResultsList, BookCard, …
│   │   ├── services/api.ts               # Typed fetch client
│   │   ├── types/api.ts                  # Mirrors backend DTOs
│   │   ├── test/setup.ts                 # Vitest + Testing-Library bootstrap
│   │   └── App.tsx                       # idle → loading → error/success state machine
│   ├── vite.config.ts                    # Vite + Vitest config (proxies /api and /health)
│   └── tailwind.config.js
├── .env.example
└── .gitignore
```
