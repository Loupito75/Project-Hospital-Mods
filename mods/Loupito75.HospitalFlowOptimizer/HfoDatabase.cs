using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer
{
    internal static class HfoDatabaseIds
    {
        internal const string ObservationEquipmentNotification =
            "L75_HFO_NOTIF_OBSERVATION_EQUIPMENT_CAPACITY";
        internal const string ObservationEquipmentNotificationText =
            "L75_HFO_NOTIF_OBSERVATION_EQUIPMENT_CAPACITY_TEXT";
    }

    internal static class HfoDatabase
    {
        private static bool s_loading;
        private static bool s_loaded;

        internal static bool IsLoading
        {
            get { return s_loading; }
        }

        internal static void ResetForBaseLoad()
        {
            s_loaded = false;
            Plugin.ClearRuntimeState();
        }

        internal static void Load(Database database)
        {
            if (database == null || s_loading || s_loaded)
            {
                return;
            }

            string pluginRoot =
                Path.GetDirectoryName(
                    Assembly.GetExecutingAssembly().Location);
            string databaseDirectory =
                string.IsNullOrEmpty(pluginRoot)
                    ? null
                    : Path.Combine(pluginRoot, "Database");

            if (string.IsNullOrEmpty(databaseDirectory) ||
                !Directory.Exists(databaseDirectory))
            {
                Plugin.Log?.LogError(
                    "Hospital Flow Optimizer Database directory is missing; " +
                    "native HFO notifications cannot be loaded.");
                return;
            }

            s_loading = true;
            try
            {
                database.ReadFiles(pluginRoot, add: true);

                s_loaded =
                    database.GetEntry<GameDBNotification>(
                        HfoDatabaseIds.ObservationEquipmentNotification) != null;

                if (s_loaded)
                {
                    if (PlayerProfile.Instance != null)
                    {
                        PlayerProfile.Instance.FillNotificationLevels();
                    }
                }
                else
                {
                    Plugin.Log?.LogError(
                        "Hospital Flow Optimizer database file was read but " +
                        "the Observation equipment notification is missing.");
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError(
                    "Failed to load Hospital Flow Optimizer database entries: " +
                    exception);
            }
            finally
            {
                s_loading = false;
            }
        }
    }

    internal static class HfoLocalization
    {
        private sealed class NotificationTranslation
        {
            internal readonly string Title;
            internal readonly string Text;

            internal NotificationTranslation(string title, string text)
            {
                Title = title;
                Text = text;
            }
        }

        private static readonly Dictionary<string, NotificationTranslation>
            ObservationEquipmentTranslations =
                new Dictionary<string, NotificationTranslation>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "en", new NotificationTranslation(
                        "Observation staff need more equipment!",
                        "Staff are complaining that {1} is already in use in the Observation room for {3}. They had to use {2} instead. Add another one to increase Observation capacity.") },
                    { "cz", new NotificationTranslation(
                        "Personál observace potřebuje více vybavení!",
                        "Personál si stěžuje, že {1} je v observační místnosti již používáno pro {3}. Místo toho musel použít {2}. Přidejte další kus, abyste zvýšili kapacitu observace.") },
                    { "da", new NotificationTranslation(
                        "Observationspersonalet har brug for mere udstyr!",
                        "Personalet klager over, at {1} allerede er i brug på observationsstuen til {3}. De måtte bruge {2} i stedet. Tilføj endnu et for at øge observationskapaciteten.") },
                    { "de", new NotificationTranslation(
                        "Das Beobachtungspersonal benötigt mehr Ausrüstung!",
                        "Das Personal beschwert sich, dass {1} im Beobachtungsraum bereits für {3} verwendet wird. Stattdessen musste {2} benutzt werden. Fügen Sie ein weiteres Gerät hinzu, um die Beobachtungskapazität zu erhöhen.") },
                    { "es", new NotificationTranslation(
                        "¡El personal de Observación necesita más equipamiento!",
                        "El personal se queja de que {1} ya está en uso en la sala de Observación para {3}. Tuvieron que usar {2} en su lugar. Añade otro para aumentar la capacidad de Observación.") },
                    { "esla", new NotificationTranslation(
                        "¡El personal de Observación necesita más equipamiento!",
                        "El personal se queja de que {1} ya está en uso en la sala de Observación para {3}. Tuvieron que usar {2} en su lugar. Agrega otro para aumentar la capacidad de Observación.") },
                    { "fr", new NotificationTranslation(
                        "Le personnel réclame plus d'équipement en observation !",
                        "Le personnel se plaint que {1} est déjà utilisé en salle d'observation pour {3}. Il doit utiliser {2} à la place. Ajoutez-en un autre pour augmenter la capacité de l'Observation.") },
                    { "hu", new NotificationTranslation(
                        "A megfigyelő személyzetnek több felszerelésre van szüksége!",
                        "A személyzet arra panaszkodik, hogy a(z) {1} már használatban van a megfigyelőben ehhez: {3}. Helyette ezt kellett használniuk: {2}. Helyezzen el még egyet a megfigyelő kapacitásának növeléséhez.") },
                    { "it", new NotificationTranslation(
                        "Il personale di Osservazione ha bisogno di più attrezzature!",
                        "Il personale segnala che {1} è già in uso nella sala di Osservazione per {3}. Ha dovuto usare {2} al suo posto. Aggiungine un altro per aumentare la capacità dell'Osservazione.") },
                    { "jp", new NotificationTranslation(
                        "観察室のスタッフにはさらに設備が必要です！",
                        "スタッフから、観察室では {3} のために {1} がすでに使用中で、代わりに {2} を使わなければならなかったと報告されています。観察室の処理能力を上げるため、もう1台追加してください。") },
                    { "kr", new NotificationTranslation(
                        "관찰실 직원에게 장비가 더 필요합니다!",
                        "관찰실에서 {3} 때문에 {1}이(가) 이미 사용 중이라 직원들이 대신 {2}을(를) 사용해야 했습니다. 관찰실 수용 능력을 높이려면 장비를 하나 더 추가하세요.") },
                    { "nl", new NotificationTranslation(
                        "Het observatiepersoneel heeft meer apparatuur nodig!",
                        "Het personeel klaagt dat {1} in de observatiekamer al in gebruik is voor {3}. Ze moesten in plaats daarvan {2} gebruiken. Plaats er nog een om de observatiecapaciteit te vergroten.") },
                    { "pl", new NotificationTranslation(
                        "Personel obserwacji potrzebuje więcej sprzętu!",
                        "Personel skarży się, że {1} w sali obserwacyjnej jest już używane do {3}. Zamiast tego musieli użyć {2}. Dodaj kolejne urządzenie, aby zwiększyć przepustowość obserwacji.") },
                    { "ptbr", new NotificationTranslation(
                        "A equipe da Observação precisa de mais equipamentos!",
                        "A equipe reclama que {1} já está em uso na sala de Observação para {3}. Eles tiveram que usar {2} no lugar. Adicione outro para aumentar a capacidade da Observação.") },
                    { "ru", new NotificationTranslation(
                        "Персоналу палаты наблюдения нужно больше оборудования!",
                        "Персонал жалуется, что {1} в палате наблюдения уже используется для {3}. Вместо него пришлось использовать {2}. Добавьте ещё один такой аппарат, чтобы увеличить пропускную способность наблюдения.") },
                    { "swe", new NotificationTranslation(
                        "Observationspersonalen behöver mer utrustning!",
                        "Personalen klagar på att {1} redan används i observationsrummet för {3}. De fick använda {2} i stället. Lägg till en till för att öka observationskapaciteten.") },
                    { "tr", new NotificationTranslation(
                        "Gözlem personelinin daha fazla ekipmana ihtiyacı var!",
                        "Personel, {1} ekipmanının Gözlem odasında {3} için zaten kullanıldığından ve bunun yerine {2} kullanmak zorunda kaldıklarından şikâyet ediyor. Gözlem kapasitesini artırmak için bir tane daha ekleyin.") },
                    { "uk", new NotificationTranslation(
                        "Персоналу палати спостереження потрібно більше обладнання!",
                        "Персонал скаржиться, що {1} у палаті спостереження вже використовується для {3}. Замість нього довелося використати {2}. Додайте ще один такий пристрій, щоб збільшити пропускну здатність спостереження.") },
                    { "zhcn", new NotificationTranslation(
                        "观察室工作人员需要更多设备！",
                        "工作人员反映，观察室中的 {1} 已经用于 {3}，因此只能改用 {2}。请再添加一台，以提高观察室的处理能力。") },
                    { "zhtw", new NotificationTranslation(
                        "觀察室工作人員需要更多設備！",
                        "工作人員反映，觀察室中的 {1} 已經用於 {3}，因此只能改用 {2}。請再增加一台，以提高觀察室的處理能力。") }
                };

        internal static bool TryGetLocalizedText(
            string languageCode,
            string stringId,
            string[] parameters,
            out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(stringId))
            {
                return false;
            }

            if (stringId != HfoDatabaseIds.ObservationEquipmentNotification &&
                stringId != HfoDatabaseIds.ObservationEquipmentNotificationText)
            {
                return false;
            }

            string normalizedLanguage = NormalizeLanguageCode(languageCode);
            NotificationTranslation translation;
            if (string.IsNullOrEmpty(normalizedLanguage) ||
                !ObservationEquipmentTranslations.TryGetValue(
                    normalizedLanguage,
                    out translation))
            {
                translation = ObservationEquipmentTranslations["en"];
            }

            result =
                stringId == HfoDatabaseIds.ObservationEquipmentNotification
                    ? translation.Title
                    : translation.Text;

            ApplyParameters(ref result, parameters);
            return true;
        }

        private static string NormalizeLanguageCode(string languageCode)
        {
            if (string.IsNullOrEmpty(languageCode))
            {
                return null;
            }

            return languageCode
                .Trim()
                .ToLowerInvariant()
                .Replace("_", string.Empty)
                .Replace("-", string.Empty);
        }

        private static void ApplyParameters(
            ref string text,
            string[] parameters)
        {
            if (parameters == null)
            {
                return;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                text = text.Replace(
                    "{" + (i + 1) + "}",
                    parameters[i] ?? string.Empty);
            }
        }
    }

    [HarmonyPatch(
        typeof(Database),
        nameof(Database.ReadFiles),
        new Type[] { typeof(string), typeof(bool) })]
    internal static class HfoDatabaseReadFilesPatch
    {
        private static void Prefix(bool add)
        {
            if (!add && !HfoDatabase.IsLoading)
            {
                HfoDatabase.ResetForBaseLoad();
            }
        }

        private static void Postfix(Database __instance, bool add)
        {
            if (!add && !HfoDatabase.IsLoading)
            {
                HfoDatabase.Load(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(StringTable),
        nameof(StringTable.GetLocalizedText),
        new Type[] { typeof(string), typeof(string[]) })]
    internal static class HfoStringTableStringPatch
    {
        private static bool Prefix(
            StringTable __instance,
            string stringID,
            string[] parameters,
            ref string __result)
        {
            if (stringID != HfoDatabaseIds.ObservationEquipmentNotification &&
                stringID != HfoDatabaseIds.ObservationEquipmentNotificationText)
            {
                return true;
            }

            string languageCode =
                __instance == null
                    ? null
                    : __instance.GetCurrentLanguage();
            if (string.IsNullOrEmpty(languageCode) &&
                PlayerProfile.Instance != null)
            {
                languageCode =
                    PlayerProfile.Instance.GetCurrentLanguage();
            }

            string localizedText;
            if (!HfoLocalization.TryGetLocalizedText(
                    languageCode,
                    stringID,
                    parameters,
                    out localizedText))
            {
                return true;
            }

            __result = localizedText;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(StringTable),
        nameof(StringTable.GetLocalizedText),
        new Type[] { typeof(DatabaseEntry) })]
    internal static class HfoStringTableDatabaseEntryPatch
    {
        private static bool Prefix(
            StringTable __instance,
            DatabaseEntry databaseItem,
            ref string __result)
        {
            if (databaseItem == null)
            {
                return true;
            }

            string stringId =
                databaseItem.DatabaseID.ToString();
            if (stringId != HfoDatabaseIds.ObservationEquipmentNotification &&
                stringId != HfoDatabaseIds.ObservationEquipmentNotificationText)
            {
                return true;
            }

            string languageCode =
                __instance == null
                    ? null
                    : __instance.GetCurrentLanguage();
            if (string.IsNullOrEmpty(languageCode) &&
                PlayerProfile.Instance != null)
            {
                languageCode =
                    PlayerProfile.Instance.GetCurrentLanguage();
            }

            string localizedText;
            if (!HfoLocalization.TryGetLocalizedText(
                    languageCode,
                    stringId,
                    null,
                    out localizedText))
            {
                return true;
            }

            __result = localizedText;
            return false;
        }
    }
}
