# SEO Domain Finder

Generate **SEO-optimized domain names** from a business description, score them, and check **availability + price** via [Porkbun](https://porkbun.com). Works **without API keys** (heuristic engine). Optional AI enhancement via [OpenRouter free models](https://openrouter.ai/openrouter/free).

[![CI](https://github.com/cordova7/seo-domain-finder/actions/workflows/ci.yml/badge.svg)](https://github.com/cordova7/seo-domain-finder/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

## Live demo

- **Frontend:** Vercel → `https://seo-domain-finder.vercel.app`
- **API:** Render (Docker, free tier) → `https://seo-domain-finder-api.onrender.com`

> Free Render tier sleeps after 15 min inactivity. First request may take ~30–60s.

## Architecture

```
┌─────────────────┐     HTTPS      ┌──────────────────────────┐
│  Next.js        │ ─────────────► │  ASP.NET Core 10 API     │
│  (Vercel)       │                │  (Render Docker)         │
│  EN ES PT FR DE │                │  Core · Infrastructure   │
└─────────────────┘                └──────────┬───────────────┘
                                              │
                                    ┌─────────┴─────────┐
                                    │ Porkbun API       │
                                    │ OpenRouter (opt.) │
                                    └───────────────────┘
```

## Features

- Multi-language UI: **EN / ES / PT / FR / DE**
- Heuristic name generation (no keys required)
- SEO scoring from extracted keywords
- Porkbun domain checks: `.com`, `.mx`, `.io`, `.net`
- Max price filter (skip premium/expensive)
- Optional AI via `openrouter/free`
- Demo rate limits; bring your own Porkbun/OpenRouter keys in Advanced mode

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
| `Porkbun__ApiKey` | Demo yes | Porkbun API key |
| `Porkbun__SecretKey` | Demo yes | Porkbun secret |
| `OpenRouter__ApiKey` | Optional | For demo AI enhancement |
| `OpenRouter__Model` | No | Default: `openrouter/free` |
| `Cors__AllowedOrigins` | Yes | Vercel URL(s), comma-separated |
| `DemoRateLimit__LlmPerHour` | No | Default: 5 |
| `DemoRateLimit__ChecksPerSession` | No | Default: 30 |
| `PORT` | Render | Set by Render (8080) |

### Frontend (Vercel)

| Variable | Description |
|----------|-------------|
| `NEXT_PUBLIC_API_URL` | Render API URL |

## Deploy

### Render (API)

1. Push repo to GitHub
2. [Render Dashboard](https://dashboard.render.com) → **New +** → **Blueprint**
3. Connect repo — uses `render.yaml`
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
  "prompt": "judicial alert monitoring for lawyers",
  "language": "en",
  "tlds": ["com", "mx"],
  "maxPriceUsd": 15,
  "useLlm": false
}
```

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

- API keys are **server-side only** (Render env vars)
- User-provided keys in Advanced mode are **not stored**
- Rotate keys if exposed; never commit `appsettings.Development.json`

## License

MIT — see [LICENSE](LICENSE)
