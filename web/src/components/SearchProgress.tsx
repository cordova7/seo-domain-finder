"use client";

import { useEffect, useState } from "react";
import type { SearchProgressEvent } from "@/lib/search-progress";

type Props = {
  progress: SearchProgressEvent;
  labels: {
    generating: string;
    briefing: string;
    planning: string;
    checking: string;
    refining: string;
    advising: string;
    found: string;
    remaining: string;
    complete: string;
  };
};

function formatEta(seconds: number): string {
  if (seconds <= 0) return "";
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  if (m === 0) return `${s}s`;
  return `${m}:${s.toString().padStart(2, "0")}`;
}

function percentFor(progress: SearchProgressEvent): number {
  if (progress.phase === "generating" || progress.phase === "briefing" || progress.phase === "planning") return 8;
  if (progress.phase === "refining" || progress.phase === "advising") return 95;
  if (progress.phase === "done") return 100;
  if (progress.maxChecks <= 0) return 0;
  const checkPct = (progress.checksUsed / progress.maxChecks) * 92;
  return Math.min(92, Math.round(8 + checkPct));
}

export function SearchProgress({ progress, labels }: Props) {
  const [countdown, setCountdown] = useState<number | null>(progress.etaSeconds);

  useEffect(() => {
    setCountdown(progress.etaSeconds);
  }, [progress.etaSeconds, progress.checksUsed, progress.phase]);

  useEffect(() => {
    if (countdown == null || countdown <= 0) return;
    const timer = setInterval(() => {
      setCountdown((prev) => (prev != null && prev > 0 ? prev - 1 : prev));
    }, 1000);
    return () => clearInterval(timer);
  }, [countdown]);

  const pct = percentFor(progress);
  const eta = countdown != null ? formatEta(countdown) : "";

  let status = labels.generating;
  if (progress.phase === "briefing") status = labels.briefing;
  else if (progress.phase === "planning") status = labels.planning;
  else if (progress.phase === "refining") status = labels.refining;
  else if (progress.phase === "advising") status = labels.advising;
  else if (progress.phase === "checking" || progress.phase === "found") {
    status = progress.currentDomain
      ? `${labels.checking} ${progress.currentDomain} (${progress.checksUsed}/${progress.maxChecks})`
      : `${labels.checking} (${progress.checksUsed}/${progress.maxChecks})`;
  } else if (progress.phase === "done") {
    status = labels.complete;
  }

  return (
    <div className="mt-6 rounded-2xl border border-slate-200 bg-white p-5 shadow-sm dark:border-zinc-700 dark:bg-zinc-900">
      <div className="mb-2 flex items-center justify-between text-sm">
        <span className="font-medium text-slate-700 dark:text-zinc-200">{status}</span>
        <span className="tabular-nums text-slate-500 dark:text-zinc-400">{pct}%</span>
      </div>

      <div className="h-2.5 overflow-hidden rounded-full bg-slate-100 dark:bg-zinc-800">
        <div
          className="h-full rounded-full bg-gradient-to-r from-blue-500 to-blue-600 transition-all duration-500 ease-out"
          style={{ width: `${pct}%` }}
        />
      </div>

      <div className="mt-2 flex flex-wrap gap-x-4 gap-y-1 text-xs text-slate-500 dark:text-zinc-400">
        {(progress.phase === "checking" || progress.phase === "found") && (
          <>
            <span>
              {labels.found}: {progress.foundCount}
            </span>
            {eta && (
              <span>
                {labels.remaining}: ~{eta}
              </span>
            )}
          </>
        )}
        {(progress.phase === "generating" || progress.phase === "briefing" || progress.phase === "planning") && (
          <span className="animate-pulse">{status}</span>
        )}
      </div>
    </div>
  );
}
