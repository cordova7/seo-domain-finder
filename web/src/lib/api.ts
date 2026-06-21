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
};

export type SearchRequest = {
  prompt: string;
  language?: string;
  tlds: string[];
  maxPriceUsd: number;
  useLlm: boolean;
};

function getSessionId(): string {
  if (typeof window === "undefined") return "";
  let id = sessionStorage.getItem("sdf-session");
  if (!id) {
    id = crypto.randomUUID();
    sessionStorage.setItem("sdf-session", id);
  }
  return id;
}

export async function searchDomains(req: SearchRequest): Promise<SearchResponse> {
  const res = await fetch(`${API_URL}/api/v1/domains/search`, {
    method: "POST",
    headers: {
      "Content-Type": "application/json",
      "X-Session-Id": getSessionId(),
    },
    body: JSON.stringify({
      prompt: req.prompt,
      language: req.language,
      tlds: req.tlds,
      maxPriceUsd: req.maxPriceUsd,
      useLlm: req.useLlm,
      maxCandidates: 15,
    }),
  });

  if (!res.ok) {
    const err = await res.json().catch(() => ({ error: res.statusText }));
    throw new Error(err.error ?? "Search failed");
  }

  const data = await res.json();
  return {
    candidates: data.candidates,
    generatorUsed: data.generatorUsed,
    extractedKeywords: data.extractedKeywords,
    warning: data.warning,
  };
}
