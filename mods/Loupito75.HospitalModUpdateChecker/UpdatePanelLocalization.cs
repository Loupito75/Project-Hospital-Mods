using System;
using System.Collections.Generic;

namespace HospitalModUpdateChecker
{
    internal static class UpdatePanelLocalization
    {
        internal const string TitleId = "L75_HMUC_TITLE";
        internal const string MoreUpdatesId = "L75_HMUC_MORE_UPDATES";

        private sealed class Translation
        {
            internal readonly string Title;
            internal readonly string MoreUpdates;

            internal Translation(string title, string moreUpdates)
            {
                Title = title;
                MoreUpdates = moreUpdates;
            }
        }

        private static readonly Translation English = new Translation(
            "HMUC - Available Mod Updates",
            "more updates");

        private static readonly Dictionary<string, Translation> Translations =
            CreateTranslations();

        internal static string Get(string stringId)
        {
            Translation translation = English;
            Translation localized;

            if (Translations.TryGetValue(
                    NormalizeLanguageCode(GetCurrentLanguage()),
                    out localized))
            {
                translation = localized;
            }

            if (stringId == TitleId)
            {
                return translation.Title;
            }

            if (stringId == MoreUpdatesId)
            {
                return translation.MoreUpdates;
            }

            return stringId;
        }

        private static string GetCurrentLanguage()
        {
            try
            {
                return PlayerProfile.Instance == null
                    ? "en"
                    : PlayerProfile.Instance.GetCurrentLanguage();
            }
            catch
            {
                return "en";
            }
        }

        private static Dictionary<string, Translation> CreateTranslations()
        {
            Dictionary<string, Translation> translations =
                new Dictionary<string, Translation>(
                    StringComparer.OrdinalIgnoreCase);

            translations["en"] = English;
            translations["cs"] = new Translation(
                "HMUC - Dostupné aktualizace modů",
                "dalších aktualizací");
            translations["da"] = new Translation(
                "HMUC - Tilgængelige modopdateringer",
                "yderligere opdateringer");
            translations["de"] = new Translation(
                "HMUC - Verfügbare Mod-Updates",
                "weitere Updates");
            translations["es"] = new Translation(
                "HMUC - Actualizaciones de mods disponibles",
                "actualizaciones más");
            translations["es-419"] = new Translation(
                "HMUC - Actualizaciones de mods disponibles",
                "actualizaciones más");
            translations["fr"] = new Translation(
                "HMUC - Mises à jour de mods disponibles",
                "autres mises à jour");
            translations["hu"] = new Translation(
                "HMUC - Elérhető modfrissítések",
                "további frissítés");
            translations["it"] = new Translation(
                "HMUC - Aggiornamenti mod disponibili",
                "altri aggiornamenti");
            translations["ja"] = new Translation(
                "HMUC - 利用可能なModアップデート",
                "件の追加アップデート");
            translations["ko"] = new Translation(
                "HMUC - 사용 가능한 모드 업데이트",
                "추가 업데이트");
            translations["nl"] = new Translation(
                "HMUC - Beschikbare mod-updates",
                "extra updates");
            translations["pl"] = new Translation(
                "HMUC - Dostępne aktualizacje modów",
                "kolejne aktualizacje");
            translations["pt-br"] = new Translation(
                "HMUC - Atualizações de mods disponíveis",
                "outras atualizações");
            translations["ru"] = new Translation(
                "HMUC - Доступные обновления модов",
                "других обновлений");
            translations["sv"] = new Translation(
                "HMUC - Tillgängliga moduppdateringar",
                "fler uppdateringar");
            translations["tr"] = new Translation(
                "HMUC - Mevcut mod güncellemeleri",
                "ek güncelleme");
            translations["uk"] = new Translation(
                "HMUC - Доступні оновлення модів",
                "інших оновлень");
            translations["zh-cn"] = new Translation(
                "HMUC - 可用模组更新",
                "个其他更新");
            translations["zh-tw"] = new Translation(
                "HMUC - 可用模組更新",
                "個其他更新");

            return translations;
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                return "en";
            }

            string code =
                languageCode.Trim().ToLowerInvariant().Replace('_', '-');

            if (code == "cz")
            {
                return "cs";
            }

            if (code == "jp")
            {
                return "ja";
            }

            if (code == "kr")
            {
                return "ko";
            }

            if (code == "swe")
            {
                return "sv";
            }

            if (code == "pt" || code.StartsWith("pt-br"))
            {
                return "pt-br";
            }

            if (code == "es-419" ||
                code == "es-la" ||
                code == "es-latam" ||
                code.StartsWith("es-mx") ||
                code.StartsWith("es-ar"))
            {
                return "es-419";
            }

            if (code.StartsWith("es"))
            {
                return "es";
            }

            if (code == "zh-tw" ||
                code == "zh-hant" ||
                code.StartsWith("zh-tw") ||
                code.StartsWith("zh-hk"))
            {
                return "zh-tw";
            }

            if (code == "zh-cn" ||
                code == "zh-hans" ||
                code.StartsWith("zh-cn") ||
                code.StartsWith("zh-sg"))
            {
                return "zh-cn";
            }

            int separator = code.IndexOf('-');
            return separator > 0
                ? code.Substring(0, separator)
                : code;
        }
    }
}
