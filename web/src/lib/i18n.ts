export type Locale = "en" | "es" | "pt" | "fr" | "de";

export const locales: Locale[] = ["en", "es", "pt", "fr", "de"];

export const localeNames: Record<Locale, string> = {
  en: "English",
  es: "Español",
  pt: "Português",
  fr: "Français",
  de: "Deutsch",
};

export type Messages = typeof import("../../messages/en.json");

export async function getMessages(locale: Locale) {
  switch (locale) {
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
