"use client";

import { useMemo, useState } from "react";
import tldData from "@/data/tlds.json";
import { useI18n } from "./I18nProvider";

type CountryTld = {
  code: string;
  name: string;
  tld: string;
  languages: string[];
};

type RegionKey = keyof typeof tldData.regions;

const regionOrder: RegionKey[] = ["americas", "europe", "asia", "africa", "oceania"];

type Props = {
  universal: string[];
  countryTlds: string[];
  onUniversalChange: (tlds: string[]) => void;
  onCountryChange: (tlds: string[]) => void;
};

export function CountryTldPicker({
  universal,
  countryTlds,
  onUniversalChange,
  onCountryChange,
}: Props) {
  const { t } = useI18n();
  const [search, setSearch] = useState("");

  const regionLabels: Record<RegionKey, string> = {
    americas: t.regionAmericas,
    europe: t.regionEurope,
    asia: t.regionAsia,
    africa: t.regionAfrica,
    oceania: t.regionOceania,
  };

  const filteredRegions = useMemo(() => {
    const q = search.trim().toLowerCase();
    const result: Record<string, CountryTld[]> = {};

    for (const region of regionOrder) {
      const countries = tldData.regions[region] as CountryTld[];
      const filtered = q
        ? countries.filter(
            (c) =>
              c.name.toLowerCase().includes(q) ||
              c.tld.includes(q) ||
              c.code.toLowerCase().includes(q)
          )
        : countries;
      if (filtered.length > 0) result[region] = filtered;
    }
    return result;
  }, [search]);

  const toggleUniversal = (tld: string) => {
    onUniversalChange(
      universal.includes(tld) ? universal.filter((x) => x !== tld) : [...universal, tld]
    );
  };

  const toggleCountry = (tld: string) => {
    onCountryChange(
      countryTlds.includes(tld) ? countryTlds.filter((x) => x !== tld) : [...countryTlds, tld]
    );
  };

  return (
    <div className="space-y-4">
      <div>
        <span className="text-sm font-medium">{t.tldsUniversal}</span>
        <div className="mt-2 flex flex-wrap gap-3">
          {tldData.universal.map((tld) => (
            <label key={tld} className="flex cursor-pointer items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={universal.includes(tld)}
                onChange={() => toggleUniversal(tld)}
                className="rounded border-slate-300"
              />
              .{tld}
            </label>
          ))}
        </div>
      </div>

      <details className="rounded-xl border border-slate-200 dark:border-zinc-700">
        <summary className="cursor-pointer px-4 py-3 text-sm font-medium select-none">
          {t.tldsCountry}
          {countryTlds.length > 0 && (
            <span className="ml-2 text-slate-500 dark:text-zinc-400">
              ({countryTlds.map((tld) => `.${tld}`).join(", ")})
            </span>
          )}
        </summary>
        <div className="border-t border-slate-200 px-4 py-3 dark:border-zinc-700">
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder={t.tldsSearch}
            className="mb-3 w-full rounded-lg border border-slate-300 bg-slate-50 px-3 py-2 text-sm focus:border-blue-500 focus:outline-none dark:border-zinc-600 dark:bg-zinc-800"
          />
          <div className="max-h-64 space-y-3 overflow-y-auto">
            {Object.entries(filteredRegions).map(([region, countries]) => (
              <div key={region}>
                <p className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-zinc-400">
                  {regionLabels[region as RegionKey]}
                </p>
                <div className="mt-1 flex flex-wrap gap-2">
                  {(countries as CountryTld[]).map((c) => (
                    <label
                      key={c.tld}
                      className="flex cursor-pointer items-center gap-1.5 rounded-md border border-slate-200 px-2 py-1 text-xs dark:border-zinc-600"
                      title={c.name}
                    >
                      <input
                        type="checkbox"
                        checked={countryTlds.includes(c.tld)}
                        onChange={() => toggleCountry(c.tld)}
                        className="rounded"
                      />
                      .{c.tld}
                      <span className="text-slate-500 dark:text-zinc-400">{c.name}</span>
                    </label>
                  ))}
                </div>
              </div>
            ))}
            {Object.keys(filteredRegions).length === 0 && (
              <p className="text-sm text-slate-500">{t.tldsNoMatch}</p>
            )}
          </div>
        </div>
      </details>
    </div>
  );
}
