# Find That Book

LLM-assisted search for noisy book queries. The backend takes a messy string like `"mark huckleberry 1884"`, uses Google Gemini to reconstruct the user's intent, searches Open Library, enriches the shortlist with the authoritative author metadata, and returns ranked candidates with explanations for why each one was chosen.

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
| Tests       | xUnit + FluentAssertions + Moq (70 backend) + Vitest + Testing-Library (9 frontend) = **79**|
| Frontend    | React 19 + Vite 8 + TypeScript + Tailwind CSS 3                                             |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) (builds on a 9 SDK as well if the 8 targeting pack is installed)
- Node.js 20 or newer (tested with 22)
- A Google Gemini API key — free at <https://aistudio.google.com/app/apikey>. Optional: the app falls back to a heuristic extractor if the key is missing.

---

## Quick start

```bash
# 1. Store your Gemini key (one-time, never committed)
cd backend/src/FindThatBook.Api
dotnet user-secrets init
dotnet user-secrets set "Gemini:ApiKey" "<YOUR_KEY>"

# 2. Run everything with one command (backend + frontend + browser)
cd ../../..
dotnet run --project backend/src/FindThatBook.Api
```

Thanks to `Microsoft.AspNetCore.SpaProxy`, `dotnet run` (or **F5 in Visual Studio**) spawns `npm install` and `npm run dev` automatically, waits for Vite to be ready, and opens the browser at <http://localhost:5173>. The first run is slower because `npm install` has to populate `node_modules/`; after that it's cached.

- Frontend → <http://localhost:5173> (Vite, proxies `/api` to the backend)
- Backend  → <http://localhost:5186>
- Swagger  → <http://localhost:5186/swagger>

If you only want the API without the SPA, use the `Swagger only (no frontend)` launch profile (`dotnet run --launch-profile "Swagger only (no frontend)"`).

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

```bash
# Backend: 70 tests (unit + integration via WebApplicationFactory)
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
- Full endpoint integration tests with `WebApplicationFactory` including the **four challenge queries** (`mark huckleberry`, `twilight meyer`, `tolkien hobbit illustrated deluxe 1937`, `austen bennet`).

The frontend suite covers the `SearchForm` submit flow, `BookCard` rendering (title link, tier badge, fallback cover), and `App`-level idle → loading → success / error transitions.

---

## Assumptions

- Gemini is the default LLM. If the key is missing, the heuristic extractor runs and the UI just shows a reduced hypothesis — no hard failure.
- Open Library's author ordering inside `/search.json` is not reliable; the enricher re-derives primary authors from `/works/{id}.json` for the top-N candidates and accepts the heuristic for the rest.
- Explanations are template-based (deterministic, traceable to the rule that fired) rather than LLM-written. The LLM is used for ordering (`UseLlmRerank`) instead — that keeps the explanations reproducible and free of hallucination, while still getting an AI second-opinion where it adds value.
- Empty search results are **not** cached so a transient Open Library timeout doesn't lock a query out for 10 minutes.
- No persistence: everything is process-local. Restarting drops caches.
- Stopwords cover English + Spanish. Other languages fall back to diacritic-stripped token matching, which still works for title equality but not for semantic similarity.

## What a next iteration would do

- Expand the stopword list to FR/DE/IT and switch to a Porter/Snowball stemmer.
- Persist the cache (Redis) across restarts so the enrichment cost amortizes over more sessions.
- Emit OpenTelemetry traces via `AddOpenTelemetry().AddSource("FindThatBook.Matching")`; the spans are already present, only the exporter setup is missing.
- End-to-end Playwright coverage of the golden UI flow + screenshot baseline.
- Containerize (backend serving the built frontend static files) and deploy to a free-tier host for a live demo URL.

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
