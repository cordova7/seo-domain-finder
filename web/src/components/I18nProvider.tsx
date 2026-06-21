"use client";

import { createContext, useContext, useEffect, useState, type ReactNode } from "react";
import { getMessages } from "@/lib/i18n";
import en from "../../messages/en.json";

type Messages = typeof en;

type I18nContextValue = {
  locale: string;
  setLocale: (l: string) => void;
  t: Messages;
};

const I18nContext = createContext<I18nContextValue | null>(null);

export function I18nProvider({ children }: { children: ReactNode }) {
  const [locale, setLocaleState] = useState("en");
  const [messages, setMessages] = useState<Messages>(en);

  useEffect(() => {
    const saved = localStorage.getItem("sdf-locale");
    if (saved) setLocaleState(saved);
  }, []);

  useEffect(() => {
    let cancelled = false;
    getMessages(locale).then((m) => {
      if (!cancelled) setMessages(m);
    });
    localStorage.setItem("sdf-locale", locale);
    return () => {
      cancelled = true;
    };
  }, [locale]);

  const setLocale = (l: string) => setLocaleState(l);

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
