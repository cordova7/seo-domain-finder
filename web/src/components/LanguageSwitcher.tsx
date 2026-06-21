"use client";

import { useMemo, useState } from "react";
import languages from "@/data/languages.json";
import { useI18n } from "./I18nProvider";

export function LanguageSwitcher() {
  const { locale, setLocale, t } = useI18n();
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState("");

  const filtered = useMemo(() => {
    const q = search.trim().toLowerCase();
    if (!q) return languages;
    return languages.filter(
      (l) =>
        l.code.includes(q) ||
        l.name.toLowerCase().includes(q) ||
        l.native.toLowerCase().includes(q)
    );
  }, [search]);

  const current = languages.find((l) => l.code === locale) ?? languages[0];

  return (
    <div className="relative">
      <button
        type="button"
        onClick={() => setOpen((o) => !o)}
        className="rounded-lg border border-zinc-300 bg-white px-3 py-1.5 text-sm dark:border-zinc-600 dark:bg-zinc-800"
        aria-label={t.languageLabel}
        aria-expanded={open}
      >
        {current.native}
      </button>
      {open && (
        <>
          <div className="fixed inset-0 z-10" onClick={() => setOpen(false)} aria-hidden />
          <div className="absolute right-0 z-20 mt-1 w-64 rounded-xl border border-slate-200 bg-white shadow-lg dark:border-zinc-600 dark:bg-zinc-900">
            <input
              type="search"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder={t.languageSearch}
              className="w-full border-b border-slate-200 px-3 py-2 text-sm focus:outline-none dark:border-zinc-700 dark:bg-zinc-900"
              autoFocus
            />
            <ul className="max-h-60 overflow-y-auto py-1">
              {filtered.map((l) => (
                <li key={l.code}>
                  <button
                    type="button"
                    onClick={() => {
                      setLocale(l.code);
                      setOpen(false);
                      setSearch("");
                    }}
                    className={`w-full px-3 py-2 text-left text-sm hover:bg-slate-50 dark:hover:bg-zinc-800 ${
                      locale === l.code ? "font-semibold text-blue-600 dark:text-blue-400" : ""
                    }`}
                  >
                    {l.native}
                    <span className="ml-2 text-slate-500 dark:text-zinc-400">{l.name}</span>
                  </button>
                </li>
              ))}
              {filtered.length === 0 && (
                <li className="px-3 py-2 text-sm text-slate-500">{t.languageNoMatch}</li>
              )}
            </ul>
          </div>
        </>
      )}
    </div>
  );
}
