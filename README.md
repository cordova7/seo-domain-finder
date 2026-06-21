# SEO Domain Finder

**Discover SEO-optimized domain names that are available to register at the lowest prices.**

Describe your business or project. The app generates SEO-ranked name ideas, checks real availability and registration price via [Porkbun](https://porkbun.com), and shows **only domains you can register** within your price limit.

[![CI](https://github.com/cordova7/seo-domain-finder/actions/workflows/ci.yml/badge.svg)](https://github.com/cordova7/seo-domain-finder/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Live demo

- **Frontend:** [seo-domain-finder.vercel.app](https://seo-domain-finder.vercel.app)
- **API:** [seo-domain-finder-api.onrender.com](https://seo-domain-finder-api.onrender.com) (Render Docker, free tier)

> Free Render tier sleeps after 15 min inactivity. First request may take 30-60 seconds.

## Architecture

```
┌─────────────────┐     HTTPS      ┌──────────────────────────┐
│  Next.js        │ ─────────────► │  ASP.NET Core 10 API     │
│  (Vercel)       │                │  (Render Docker)         │
│  Multi-language │                │  Core · Infrastructure   │
└─────────────────┘                └──────────┬───────────────┘
                                              │
                                    ┌─────────┴─────────┐
                                    │ Porkbun API       │
                                    │ OpenRouter (opt.) │
                                    └───────────────────┘
```

## Features

- **Available domains only** with live Porkbun price checks (no fake "unavailable" results)
- SEO scoring from extracted keywords in your description
- Popular TLDs (`.com`, `.io`, `.net`, `.app`) plus optional country TLDs by region
- Max price filter to skip premium or expensive registrations
- Heuristic name generation (works without AI)
- Optional AI enhancement via [OpenRouter free models](https://openrouter.ai/openrouter/free)
- Global language selector; UI translated in EN / ES / PT / FR / DE
- Demo rate limits (90 domain checks per session)

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
| `OpenRouter__ApiKey` | Optional | For demo AI enhancement |
| `OpenRouter__Model` | No | Default: `openrouter/free` |
| `Cors__AllowedOrigins` | Yes | Vercel URL(s), comma-separated |
| `DemoRateLimit__LlmPerHour` | No | Default: 5 |
| `DemoRateLimit__ChecksPerSession` | No | Default: 90 |
| `PORT` | Render | Set by Render (8080) |

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

### Vercel (frontend)

1. Import repo, root directory: `web`
2. Set `NEXT_PUBLIC_API_URL` to your Render URL
3. Update Render `Cors__AllowedOrigins` with Vercel URL

## API

### `GET /api/v1/health`

### `POST /api/v1/domains/search`

```json
{
  "prompt": "judicial alert monitoring for lawyers in Mexico",
  "language": "es",
  "tlds": ["com", "io", "net", "app", "mx"],
  "maxPriceUsd": 15,
  "useLlm": false
}
```

Response includes only **available** domains with registration price.

## Project structure

```
seo-domain-finder/
├── src/
│   ├── SeoDomainFinder.Core/           # Domain logic, heuristics, scoring
│   ├── SeoDomainFinder.Infrastructure/   # Porkbun, OpenRouter, rate limits
│   └── SeoDomainFinder.Api/            # Minimal API
├── tests/SeoDomainFinder.Core.Tests/
├── web/                                # Next.js frontend
├── Dockerfile
├── render.yaml
└── .github/workflows/ci.yml
```

## Security

- API keys are **server-side only** (Render env vars or local `appsettings.Development.json`)
- Never commit secrets; rotate keys if exposed

## License

MIT. See [LICENSE](LICENSE).
