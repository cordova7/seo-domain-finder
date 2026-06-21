export type SearchProgressEvent = {
  phase: "generating" | "checking" | "done" | "error";
  checksUsed: number;
  maxChecks: number;
  foundCount: number;
  currentDomain: string | null;
  etaSeconds: number | null;
};

export type SearchStreamDone = SearchProgressEvent & {
  phase: "done";
  result: {
    candidates: import("./api").DomainCandidate[];
    generatorUsed: string;
    extractedKeywords: string[];
    warning: string | null;
  };
};
