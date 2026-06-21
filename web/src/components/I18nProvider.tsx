"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { getMessages, type Locale, locales } from "@/lib/i18n";
import type en from "../../messages/en.json";

type Messages = typeof en;

type I18nContextValue = {
  locale: Locale;
  setLocale: (l: Locale) => void;
  t: Messages;
};

const I18nContext = createContext<I18nContextValue | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState<Locale>("en");
  const [messages, setMessages] = useState<Messages | null>(null);

  useEffect(() => {
    const saved = localStorage.getItem("sdf-locale") as Locale | null;
    if (saved && locales.includes(saved)) setLocaleState(saved);
  }, []);

  useEffect(() => {
    getMessages(locale).then(setMessages);
    localStorage.setItem("sdf-locale", locale);
  }, [locale]);

  const setLocale = (l: Locale) => setLocaleState(l);

  if (!messages) return null;

  return (
    <I18nContext.Provider value={{ locale, setLocale, t: messages }}>
      {children}
    </I18nContext.Provider>
  );
}

export function useI18n() {
  const ctx = useContext(I18nContext);
  if (!ctx) throw new Error("useI18n must be used within I18nProvider");
  return ctx;
}
