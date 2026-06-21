export type SearchProgressFoundCandidate = {
  name: string;
  tld: string;
  fullDomain: string;
  seoScore: number;
  seoExplanation: string;
  priceUsd: number | null;
};

export type SearchProgressEvent = {
  phase:
    | "generating"
    | "planning"
    | "checking"
    | "found"
    | "refining"
    | "advising"
    | "done"
    | "error";
  checksUsed: number;
  maxChecks: number;
  foundCount: number;
  currentDomain: string | null;
  etaSeconds: number | null;
  foundCandidate?: SearchProgressFoundCandidate;
};

export type SearchStreamDone = SearchProgressEvent & {
  phase: "done";
  result: {
    candidates: import("./api").DomainCandidate[];
    generatorUsed: string;
    extractedKeywords: string[];
    warning: string | null;
    advice: string | null;
  };
};
