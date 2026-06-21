# SEO Domain Finder

**Discover SEO-optimized domain names that are available to register at the lowest prices.**

Describe your business or project. The app generates SEO-ranked name ideas, checks real availability and registration price via [Porkbun](https://porkbun.com), and shows **only domains you can register** within your price limit.

[![CI](https://github.com/cordova7/seo-domain-finder/actions/workflows/ci.yml/badge.svg)](https://github.com/cordova7/seo-domain-finder/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Live demo

- **Frontend:** [seo-domain-finder.vercel.app](https://seo-domain-finder.vercel.app)
- **API:** [seo-domain-finder-api.onrender.com](https://seo-domain-finder-api.onrender.com) (Render Docker, free tier)

> Free Render tier sleeps after 15 min inactivity. First request may take 30-60 seconds to wake the API.

## How it works

1. You describe your business (what it does, who it is for, optional location).
2. The API extracts keywords and generates SEO-friendly domain candidates (heuristics, or AI if enabled).
3. Each candidate is checked against Porkbun for **real** availability and registration price.
4. Only available domains within your max price are returned, ranked by SEO score.

Not every search finds a match. Short `.com` names are often taken. If nothing appears, try another description, add TLDs (e.g. `.io`, country codes), raise the max price, or run the search again.

Searches use **server-sent events** so the UI shows live progress (check count, current domain, ETA). A typical search checks up to **25 domains** and takes about **1-4 minutes**, because Porkbun allows roughly **one availability check every 10 seconds** per API key.

## Architecture

```
┌─────────────────┐     HTTPS      ┌──────────────────────────┐
│  Next.js        │ ─────────────► │  ASP.NET Core 10 API     │
│  (Vercel)       │   SSE stream   │  (Render Docker)         │
│  Multi-language │                │  Core · Infrastructure   │
└─────────────────┘                └──────────┬───────────────┘
                                              │
                                    ┌─────────┴─────────┐
                                    │ Porkbun API       │
                                    │ OpenRouter (opt.) │
                                    └───────────────────┘
```

## Features

- **Available domains only** with live Porkbun price checks (no fallback list of taken names)
- SEO scoring from extracted keywords in your description
- Default TLDs: `.com` and `.io`, plus optional popular and country TLDs by region
- Max price filter to skip premium or expensive registrations
- Heuristic name generation (works without AI)
- Optional AI enhancement via [OpenRouter free models](https://openrouter.ai/openrouter/free)
- Global language selector; UI translated in EN / ES / PT / FR / DE
- AI analysis, SEO explanations, and backend warnings localized to match the UI language (EN / ES / PT / FR / DE)
- Demo rate limits: 25 domain checks per session
- Live search progress via SSE (real check count and estimated time remaining)

## Quick start (local)

### 1. API

```bash
cd src/SeoDomainFinder.Api
cp appsettings.Development.example.json appsettings.Development.json
# Edit appsettings.Development.json with your Porkbun + OpenRouter keys

dotnet run
# API: http://localhost:8080
```

### 2. Frontend

```bash
cd web
cp .env.example .env.local
# NEXT_PUBLIC_API_URL=http://localhost:8080

npm install
npm run dev
# UI: http://localhost:3000
```

### 3. Tests

```bash
dotnet test
```

## Environment variables

### API (Render / local)

| Variable | Required | Description |
|----------|----------|-------------|
| `Porkbun__ApiKey` | Yes (demo) | Porkbun API key |
| `Porkbun__SecretKey` | Yes (demo) | Porkbun secret |
| `Porkbun__MinDelayMs` | No | Delay between checks. Default: `10000` local, `10500` on Render (Porkbun allows 1 check per 10s) |
| `OpenRouter__ApiKey` | Optional | For demo AI enhancement |
| `OpenRouter__Model` | No | Default: `openrouter/free` |
| `Cors__AllowedOrigins` | Yes | Vercel URL(s), comma-separated |
| `DemoRateLimit__LlmPerHour` | No | Default: `8` (3 calls per AI search: plan + refill + advice) |
| `DemoRateLimit__ChecksPerSession` | No | Default: `25` |
| `PORT` | Render | Set by Render (`8080`) |

### Frontend (Vercel)

| Variable | Description |
|----------|-------------|
| `NEXT_PUBLIC_API_URL` | Render API URL |

## Deploy

### Render (API)

1. Push repo to GitHub
2. [Render Dashboard](https://dashboard.render.com) → **New +** → **Blueprint**
3. Connect repo (uses `render.yaml`)
4. Set secret env vars: `Porkbun__ApiKey`, `Porkbun__SecretKey`, `OpenRouter__ApiKey`, `Cors__AllowedOrigins`

`render.yaml` also sets `Porkbun__MinDelayMs=10500` and `DemoRateLimit__ChecksPerSession=25`.

### Vercel (frontend)

1. Import repo, root directory: `web`
2. Set `NEXT_PUBLIC_API_URL` to your Render URL
3. Update Render `Cors__AllowedOrigins` with your Vercel URL

## API

### `GET /api/v1/health`

Returns `{ "status": "healthy", "version": "1.0.0" }`.

### `POST /api/v1/domains/search/stream` (used by the web UI)

Same JSON body as `/search`. Returns `text/event-stream` with progress events and a final result.

**Progress event fields:** `phase` (`generating` | `checking` | `done`), `checksUsed`, `maxChecks`, `foundCount`, `currentDomain`, `etaSeconds`

**Final event:** `phase: "done"` includes `result` with `candidates`, `generatorUsed`, `extractedKeywords`, `warning`.

### `POST /api/v1/domains/search`

Synchronous JSON response (no progress stream). Same request body.

```json
{
  "prompt": "dog walking service for busy professionals in Chicago",
  "language": "en",
  "tlds": ["com", "io"],
  "maxPriceUsd": 15,
  "useLlm": false
}
```

**Response:** only **available** domains with registration price in USD.

## Project structure

```
seo-domain-finder/
├── src/
│   ├── SeoDomainFinder.Core/              # Domain logic, heuristics, scoring
│   ├── SeoDomainFinder.Infrastructure/    # Porkbun, OpenRouter, rate limits
│   └── SeoDomainFinder.Api/               # Minimal API + SSE stream
├── tests/
│   ├── SeoDomainFinder.Core.Tests/
│   └── SeoDomainFinder.Infrastructure.Tests/
├── web/                                   # Next.js frontend
├── Dockerfile
├── render.yaml
└── .github/workflows/ci.yml
```

## Security

- API keys are **server-side only** (Render env vars or local `appsettings.Development.json`)
- Never commit secrets; rotate keys if exposed

## License

MIT. See [LICENSE](LICENSE).
