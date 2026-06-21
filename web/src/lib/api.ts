const API_URL = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:8080";

export type DomainCandidate = {
  name: string;
  tld: string;
  fullDomain: string;
  seoScore: number;
  seoExplanation: string;
  available: boolean | null;
  priceUsd: number | null;
  priceType: string | null;
  totalScore: number;
  unavailableReason: string | null;
};

export type SearchResponse = {
  candidates: DomainCandidate[];
  generatorUsed: string;
  extractedKeywords: string[];
  warning: string | null;
  advice: string | null;
};

export type SearchRequest = {
  prompt: string;
  language?: string;
  tlds: string[];
  maxPriceUsd: number;
  useLlm: boolean;
};

export type { SearchProgressEvent, SearchStreamDone } from "./search-progress";

import type {
  SearchProgressEvent,
  SearchProgressFoundCandidate,
  SearchStreamDone,
} from "./search-progress";

function getSessionId(): string {
  if (typeof window === "undefined") return "";
  let id = sessionStorage.getItem("sdf-session");
  if (!id) {
    id = crypto.randomUUID();
    sessionStorage.setItem("sdf-session", id);
  }
  return id;
}

function buildBody(req: SearchRequest) {
  return JSON.stringify({
    prompt: req.prompt,
    language: req.language,
    tlds: req.tlds,
    maxPriceUsd: req.maxPriceUsd,
    useLlm: req.useLlm,
    maxCandidates: 15,
  });
}

function toCandidate(found: SearchProgressFoundCandidate): DomainCandidate {
  return {
    name: found.name,
    tld: found.tld,
    fullDomain: found.fullDomain,
    seoScore: found.seoScore,
    seoExplanation: found.seoExplanation,
    available: true,
    priceUsd: found.priceUsd,
    priceType: "standard",
    totalScore: found.seoScore + 10,
    unavailableReason: null,
  };
}

export async function searchDomainsStream(
  req: SearchRequest,
  onProgress: (event: SearchProgressEvent) => void,
  onFound?: (candidate: DomainCandidate) => void
): Promise<SearchResponse> {
  const res = await fetch(`${API_URL}/api/v1/domains/search/stream`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Session-Id": getSessionId(),
    },
    body: buildBody(req),
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(err.error ?? "Search failed");
  }

  if (!res.body) {
    throw new Error("Search failed");
  }

  const reader = res.body.getReader();
  const decoder = new TextDecoder();
  let buffer = "";
  let finalResult: SearchResponse | null = null;

  while (true) {
    const { done, value } = await reader.read();
    if (done) break;

    buffer += decoder.decode(value, { stream: true });
    const parts = buffer.split("\n\n");
    buffer = parts.pop() ?? "";

    for (const part of parts) {
      const line = part.trim();
      if (!line.startsWith("data:")) continue;

      const json = line.slice(5).trim();
      if (!json) continue;

      const event = JSON.parse(json) as SearchProgressEvent | SearchStreamDone;

      if (event.phase === "error") {
        throw new Error((event as { message?: string }).message ?? "Search failed");
      }

      if (event.phase === "done" && "result" in event) {
        const done = event as SearchStreamDone;
        finalResult = {
          candidates: done.result.candidates,
          generatorUsed: done.result.generatorUsed,
          extractedKeywords: done.result.extractedKeywords,
          warning: done.result.warning,
          advice: done.result.advice,
        };
        onProgress({
          phase: "done",
          checksUsed: event.checksUsed,
          maxChecks: event.maxChecks,
          foundCount: event.foundCount,
          currentDomain: null,
          etaSeconds: 0,
        });
      } else {
        onProgress(event);
        if (event.phase === "found" && event.foundCandidate) {
          onFound?.(toCandidate(event.foundCandidate));
        }
      }
    }
  }

  if (!finalResult) {
    throw new Error("Search ended without results");
  }

  return finalResult;
}
