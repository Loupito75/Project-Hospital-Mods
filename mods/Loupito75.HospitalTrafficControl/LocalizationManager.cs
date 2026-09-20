using System;
using System.Collections.Generic;

namespace HospitalTrafficControl
{
    internal static class LocalizationManager
    {
        internal const string NoPathTitleId = "L75_HTC_NOTIF_NO_PATH_TITLE";
        internal const string NoPathTextId = "L75_HTC_NOTIF_NO_PATH_TEXT";
        internal const string NoPathPatientTextId = "L75_HTC_NOTIF_NO_PATH_PATIENT_TEXT";
        internal const string OneWayDirectionId = "L75_HTC_ONEWAY_DIRECTION";
        internal const string OneWayOffId = "L75_HTC_ONEWAY_OFF";
        internal const string OneWayNorthWestId = "L75_HTC_ONEWAY_NORTH_WEST";
        internal const string OneWayNorthEastId = "L75_HTC_ONEWAY_NORTH_EAST";
        internal const string OneWaySouthEastId = "L75_HTC_ONEWAY_SOUTH_EAST";
        internal const string OneWaySouthWestId = "L75_HTC_ONEWAY_SOUTH_WEST";

        private sealed class Translation
        {
            internal readonly string Title;
            internal readonly string Text;
            internal readonly string OneWayDirection;
            internal readonly string OneWayOff;
            internal readonly string NorthWest;
            internal readonly string NorthEast;
            internal readonly string SouthEast;
            internal readonly string SouthWest;

            internal Translation(
                string title,
                string text,
                string oneWayDirection,
                string oneWayOff,
                string northWest,
                string northEast,
                string southEast,
                string southWest)
            {
                Title = title;
                Text = text;
                OneWayDirection = oneWayDirection;
                OneWayOff = oneWayOff;
                NorthWest = northWest;
                NorthEast = northEast;
                SouthEast = southEast;
                SouthWest = southWest;
            }
        }

        private static readonly Translation English = new Translation(
            "Path blocked",
            "{1} cannot reach the destination because of access restrictions.",
            "One-way: {1}",
            "One-way: off",
            "↖ northwest",
            "↗ northeast",
            "↘ southeast",
            "↙ southwest");

        private static readonly Dictionary<string, Translation> Translations = CreateTranslations();

        internal static bool TryGetLocalizedText(
            string languageCode,
            string stringId,
            string[] parameters,
            out string result)
        {
            result = null;

            if (stringId != NoPathTitleId &&
                stringId != NoPathTextId &&
                stringId != NoPathPatientTextId &&
                stringId != OneWayDirectionId &&
                stringId != OneWayOffId &&
                stringId != OneWayNorthWestId &&
                stringId != OneWayNorthEastId &&
                stringId != OneWaySouthEastId &&
                stringId != OneWaySouthWestId)
            {
                return false;
            }

            Translation translation;
            string normalizedLanguage = NormalizeLanguageCode(languageCode);
            if (string.IsNullOrEmpty(normalizedLanguage) ||
                !Translations.TryGetValue(normalizedLanguage, out translation))
            {
                translation = English;
            }

            if (stringId == NoPathTitleId)
            {
                result = translation.Title;
            }
            else if (stringId == NoPathPatientTextId)
            {
                result = translation.Text;
            }
            else if (stringId == NoPathTextId)
            {
                result = translation.Text;
            }
            else if (stringId == OneWayDirectionId)
            {
                result = translation.OneWayDirection;
            }
            else if (stringId == OneWayOffId)
            {
                result = translation.OneWayOff;
            }
            else if (stringId == OneWayNorthWestId)
            {
                result = translation.NorthWest;
            }
            else if (stringId == OneWayNorthEastId)
            {
                result = translation.NorthEast;
            }
            else if (stringId == OneWaySouthEastId)
            {
                result = translation.SouthEast;
            }
            else
            {
                result = translation.SouthWest;
            }

            if (parameters != null)
            {
                for (int i = 0; i < parameters.Length; i++)
                {
                    result = result.Replace("{" + (i + 1) + "}", parameters[i] ?? string.Empty);
                }
            }

            return true;
        }

        private static Dictionary<string, Translation> CreateTranslations()
        {
            Dictionary<string, Translation> translations =
                new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase);

            translations["en"] = English;
            translations["fr"] = new Translation(
                "Chemin inaccessible",
                "{1} ne peut pas atteindre sa destination à cause des restrictions d’accès.",
                "Sens unique : {1}",
                "Sens unique : désactivé",
                "↖ nord-ouest",
                "↗ nord-est",
                "↘ sud-est",
                "↙ sud-ouest");

            translations["da"] = new Translation(
                "Ruten er blokeret",
                "{1} kan ikke nå destinationen på grund af adgangsbegrænsninger.",
                "Ensrettet: {1}",
                "Ensrettet: fra",
                "↖ nordvest",
                "↗ nordøst",
                "↘ sydøst",
                "↙ sydvest");

            translations["de"] = new Translation(
                "Weg blockiert",
                "{1} kann das Ziel aufgrund von Zugangsbeschränkungen nicht erreichen.",
                "Einbahnrichtung: {1}",
                "Einbahnrichtung: aus",
                "↖ Nordwesten",
                "↗ Nordosten",
                "↘ Südosten",
                "↙ Südwesten");

            translations["es"] = new Translation(
                "Ruta bloqueada",
                "{1} no puede llegar al destino debido a las restricciones de acceso.",
                "Sentido único: {1}",
                "Sentido único: desactivado",
                "↖ noroeste",
                "↗ noreste",
                "↘ sureste",
                "↙ suroeste");

            translations["es-419"] = new Translation(
                "Ruta bloqueada",
                "{1} no puede llegar al destino debido a las restricciones de acceso.",
                "Sentido único: {1}",
                "Sentido único: desactivado",
                "↖ noroeste",
                "↗ noreste",
                "↘ sureste",
                "↙ suroeste");

            translations["cs"] = new Translation(
                "Cesta je zablokovaná",
                "{1} se kvůli omezení přístupu nemůže dostat do cíle.",
                "Jednosměrně: {1}",
                "Jednosměrně: vypnuto",
                "↖ severozápad",
                "↗ severovýchod",
                "↘ jihovýchod",
                "↙ jihozápad");

            translations["pt-br"] = new Translation(
                "Caminho bloqueado",
                "{1} não consegue chegar ao destino devido às restrições de acesso.",
                "Sentido único: {1}",
                "Sentido único: desativado",
                "↖ noroeste",
                "↗ nordeste",
                "↘ sudeste",
                "↙ sudoeste");

            translations["zh-tw"] = new Translation(
                "路徑受阻",
                "{1} 因通行限制而無法到達目的地。",
                "單向：{1}",
                "單向：關閉",
                "↖ 西北",
                "↗ 東北",
                "↘ 東南",
                "↙ 西南");

            translations["ru"] = new Translation(
                "Путь заблокирован",
                "{1} не может добраться до места назначения из-за ограничений доступа.",
                "Одностороннее движение: {1}",
                "Одностороннее движение: выкл.",
                "↖ северо-запад",
                "↗ северо-восток",
                "↘ юго-восток",
                "↙ юго-запад");

            translations["tr"] = new Translation(
                "Yol engellendi",
                "{1} erişim kısıtlamaları nedeniyle hedefe ulaşamıyor.",
                "Tek yön: {1}",
                "Tek yön: kapalı",
                "↖ kuzeybatı",
                "↗ kuzeydoğu",
                "↘ güneydoğu",
                "↙ güneybatı");

            translations["uk"] = new Translation(
                "Шлях заблоковано",
                "{1} не може дістатися місця призначення через обмеження доступу.",
                "Односторонній рух: {1}",
                "Односторонній рух: вимкнено",
                "↖ північний захід",
                "↗ північний схід",
                "↘ південний схід",
                "↙ південний захід");

            translations["it"] = new Translation(
                "Percorso bloccato",
                "{1} non può raggiungere la destinazione a causa delle restrizioni di accesso.",
                "Senso unico: {1}",
                "Senso unico: disattivato",
                "↖ nord-ovest",
                "↗ nord-est",
                "↘ sud-est",
                "↙ sud-ovest");

            translations["zh-cn"] = new Translation(
                "路径受阻",
                "{1} 因通行限制而无法到达目的地。",
                "单向：{1}",
                "单向：关闭",
                "↖ 西北",
                "↗ 东北",
                "↘ 东南",
                "↙ 西南");

            translations["nl"] = new Translation(
                "Route geblokkeerd",
                "{1} kan de bestemming niet bereiken vanwege toegangsbeperkingen.",
                "Eenrichtingsverkeer: {1}",
                "Eenrichtingsverkeer: uit",
                "↖ noordwest",
                "↗ noordoost",
                "↘ zuidoost",
                "↙ zuidwest");

            translations["ja"] = new Translation(
                "経路が遮断されています",
                "{1} はアクセス制限のため目的地に到達できません。",
                "一方通行：{1}",
                "一方通行：オフ",
                "↖ 北西",
                "↗ 北東",
                "↘ 南東",
                "↙ 南西");

            translations["hu"] = new Translation(
                "Az útvonal blokkolva",
                "{1} a hozzáférési korlátozások miatt nem éri el a célállomást.",
                "Egyirányú: {1}",
                "Egyirányú: kikapcsolva",
                "↖ északnyugat",
                "↗ északkelet",
                "↘ délkelet",
                "↙ délnyugat");

            translations["pl"] = new Translation(
                "Droga zablokowana",
                "{1} nie może dotrzeć do celu z powodu ograniczeń dostępu.",
                "Ruch jednokierunkowy: {1}",
                "Ruch jednokierunkowy: wyłączony",
                "↖ północny zachód",
                "↗ północny wschód",
                "↘ południowy wschód",
                "↙ południowy zachód");

            translations["ko"] = new Translation(
                "경로가 차단됨",
                "{1}은(는) 접근 제한 때문에 목적지에 도달할 수 없습니다.",
                "일방통행: {1}",
                "일방통행: 끔",
                "↖ 북서",
                "↗ 북동",
                "↘ 남동",
                "↙ 남서");

            translations["sv"] = new Translation(
                "Vägen är blockerad",
                "{1} kan inte nå destinationen på grund av åtkomstbegränsningar.",
                "Enkelriktat: {1}",
                "Enkelriktat: av",
                "↖ nordväst",
                "↗ nordost",
                "↘ sydost",
                "↙ sydväst");

            return translations;
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                return "en";
            }

            string code = languageCode.Trim().ToLowerInvariant().Replace('_', '-');

            if (code == "pt" || code.StartsWith("pt-br"))
            {
                return "pt-br";
            }
            if (code == "es-419" || code == "es-la" || code == "es-latam" ||
                code.StartsWith("es-mx") || code.StartsWith("es-ar"))
            {
                return "es-419";
            }
            if (code.StartsWith("es"))
            {
                return "es";
            }
            if (code == "zh-tw" || code == "zh-hant" || code.StartsWith("zh-tw") ||
                code.StartsWith("zh-hk"))
            {
                return "zh-tw";
            }
            if (code == "zh-cn" || code == "zh-hans" || code.StartsWith("zh-cn") ||
                code.StartsWith("zh-sg"))
            {
                return "zh-cn";
            }

            int separator = code.IndexOf('-');
            return separator > 0 ? code.Substring(0, separator) : code;
        }
    }
}
