"use client";

import { useState } from "react";
import { useI18n } from "@/components/I18nProvider";
import { LanguageSwitcher } from "@/components/LanguageSwitcher";
import { CountryTldPicker } from "@/components/CountryTldPicker";
import { SearchProgress } from "@/components/SearchProgress";
import { searchDomainsStream, type DomainCandidate } from "@/lib/api";
import type { SearchProgressEvent } from "@/lib/search-progress";

const DEFAULT_UNIVERSAL = ["com", "io"];

export default function HomePage() {
  const { t, locale } = useI18n();
  const [prompt, setPrompt] = useState("");
  const [universalTlds, setUniversalTlds] = useState<string[]>(DEFAULT_UNIVERSAL);
  const [countryTlds, setCountryTlds] = useState<string[]>([]);
  const [maxPrice, setMaxPrice] = useState(15);
  const [useAi, setUseAi] = useState(false);
  const [loading, setLoading] = useState(false);
  const [progress, setProgress] = useState<SearchProgressEvent | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [results, setResults] = useState<DomainCandidate[]>([]);
  const [meta, setMeta] = useState<{
    keywords: string[];
    generator: string;
    warning: string | null;
  } | null>(null);

  const allTlds = [...new Set([...universalTlds, ...countryTlds])];

  const handleSearch = async () => {
    if (!prompt.trim()) return;
    setLoading(true);
    setError(null);
    setProgress({
      phase: "generating",
      checksUsed: 0,
      maxChecks: 25,
      foundCount: 0,
      currentDomain: null,
      etaSeconds: null,
    });
    try {
      const res = await searchDomainsStream(
        {
          prompt: prompt.trim(),
          language: locale,
          tlds: allTlds.length ? allTlds : ["com"],
          maxPriceUsd: maxPrice,
          useLlm: useAi,
        },
        setProgress
      );
      setResults(res.candidates);
      setMeta({
        keywords: res.extractedKeywords,
        generator: res.generatorUsed,
        warning: res.warning,
      });
    } catch (e) {
      setError(e instanceof Error ? e.message : t.error);
      setResults([]);
      setMeta(null);
      setProgress(null);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="min-h-screen bg-gradient-to-b from-slate-50 to-slate-100 text-slate-900 dark:from-zinc-950 dark:to-zinc-900 dark:text-zinc-100">
      <header className="mx-auto flex max-w-4xl items-center justify-between px-4 py-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">{t.title}</h1>
          <p className="mt-1 text-sm text-slate-600 dark:text-zinc-400">{t.subtitle}</p>
        </div>
        <LanguageSwitcher />
      </header>

      <main className="mx-auto max-w-4xl px-4 pb-16">
        <section className="rounded-2xl border border-slate-200 bg-white p-6 shadow-sm dark:border-zinc-700 dark:bg-zinc-900">
          <label className="block text-sm font-medium">{t.promptLabel}</label>
          <textarea
            value={prompt}
            onChange={(e) => setPrompt(e.target.value)}
            placeholder={t.promptPlaceholder}
            rows={4}
            maxLength={500}
            disabled={loading}
            className="mt-2 w-full rounded-xl border border-slate-300 bg-slate-50 p-3 text-sm focus:border-blue-500 focus:outline-none focus:ring-2 focus:ring-blue-200 disabled:opacity-60 dark:border-zinc-600 dark:bg-zinc-800"
          />

          <div className="mt-4">
            <span className="text-sm font-medium">{t.tldsLabel}</span>
            <div className="mt-2">
              <CountryTldPicker
                universal={universalTlds}
                countryTlds={countryTlds}
                onUniversalChange={setUniversalTlds}
                onCountryChange={setCountryTlds}
              />
            </div>
          </div>

          <div className="mt-4">
            <label className="text-sm font-medium">
              {t.maxPriceLabel}: ${maxPrice}
            </label>
            <input
              type="range"
              min={5}
              max={50}
              step={1}
              value={maxPrice}
              onChange={(e) => setMaxPrice(Number(e.target.value))}
              disabled={loading}
              className="mt-2 w-full disabled:opacity-60"
            />
          </div>

          <label className="mt-4 flex cursor-pointer items-center gap-2 text-sm">
            <input
              type="checkbox"
              checked={useAi}
              onChange={(e) => setUseAi(e.target.checked)}
              disabled={loading}
              className="rounded"
            />
            {t.useAiLabel}
          </label>

          <button
            type="button"
            onClick={handleSearch}
            disabled={loading || !prompt.trim()}
            className="mt-6 w-full rounded-xl bg-blue-600 py-3 text-sm font-semibold text-white transition hover:bg-blue-700 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {loading ? t.searching : t.searchButton}
          </button>
        </section>

        {loading && progress && (
          <SearchProgress
            progress={progress}
            labels={{
              generating: t.progressGenerating,
              checking: t.progressChecking,
              found: t.progressFound,
              remaining: t.progressRemaining,
              complete: t.progressComplete,
            }}
          />
        )}

        {error && (
          <p className="mt-4 rounded-lg bg-red-50 px-4 py-3 text-sm text-red-700 dark:bg-red-950 dark:text-red-300">
            {t.error}: {error}
          </p>
        )}

        {meta && (
          <div className="mt-6 text-sm text-slate-600 dark:text-zinc-400">
            <p>
              <span className="font-medium">{t.keywords}:</span>{" "}
              {meta.keywords.join(", ") || "-"}
            </p>
            <p>
              <span className="font-medium">{t.generator}:</span> {meta.generator}
            </p>
            {meta.warning && (
              <p className="mt-1 text-amber-700 dark:text-amber-400">
                {t.warning}: {meta.warning}
              </p>
            )}
          </div>
        )}

        {results.length > 0 && (
          <section className="mt-8 overflow-hidden rounded-2xl border border-slate-200 bg-white shadow-sm dark:border-zinc-700 dark:bg-zinc-900">
            <h2 className="border-b border-slate-200 px-4 py-3 font-semibold dark:border-zinc-700">
              {t.resultsTitle}
            </h2>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-sm">
                <thead className="bg-slate-50 text-xs uppercase text-slate-500 dark:bg-zinc-800 dark:text-zinc-400">
                  <tr>
                    <th className="px-4 py-3">{t.domain}</th>
                    <th className="px-4 py-3">{t.seo}</th>
                    <th className="px-4 py-3">{t.price}</th>
                  </tr>
                </thead>
                <tbody>
                  {results.map((r) => (
                    <tr
                      key={r.fullDomain}
                      className="border-t border-slate-100 dark:border-zinc-800"
                    >
                      <td className="px-4 py-3 font-mono font-medium">{r.fullDomain}</td>
                      <td className="px-4 py-3">
                        <span className="font-semibold">{r.seoScore}</span>
                        <span className="mt-0.5 block text-xs text-slate-500 dark:text-zinc-500">
                          {r.seoExplanation}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        {r.priceUsd != null ? `$${r.priceUsd.toFixed(2)}` : "-"}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </section>
        )}

        {!loading && results.length === 0 && meta && (
          <p className="mt-6 text-center text-sm text-slate-500">{t.noResults}</p>
        )}
      </main>

      <footer className="pb-8 text-center text-xs text-slate-500 dark:text-zinc-500">
        {t.footer}
      </footer>
    </div>
  );
}
