import type en from "../../messages/en.json";

export type UiLocale = "en" | "es" | "pt" | "fr" | "de";

const uiLocales: UiLocale[] = ["en", "es", "pt", "fr", "de"];

export type Messages = typeof en;

export async function getMessages(locale: string): Promise<Messages> {
  const code = locale.slice(0, 2).toLowerCase();
  if (!uiLocales.includes(code as UiLocale)) {
    return (await import("../../messages/en.json")).default;
  }
  switch (code as UiLocale) {
    case "es":
      return (await import("../../messages/es.json")).default;
    case "pt":
      return (await import("../../messages/pt.json")).default;
    case "fr":
      return (await import("../../messages/fr.json")).default;
    case "de":
      return (await import("../../messages/de.json")).default;
    default:
      return (await import("../../messages/en.json")).default;
  }
}
