using System;
using System.Collections.Generic;

namespace HospitalFixes
{
    internal static class HfxLocalizationIds
    {
        internal const string Prefix =
            "L75_HFX_";

        internal const string WaitingRoomGuidance =
            "L75_HFX_WAITING_ROOM_GUIDANCE";

        internal const string LabEquipmentShortageNotification =
            "L75_HFX_NOTIF_LAB_EQUIPMENT_SHORTAGE";

        internal const string LabEquipmentShortageNotificationText =
            "L75_HFX_NOTIF_LAB_EQUIPMENT_SHORTAGE_TEXT";

        internal static readonly string[] PatientFilterLabels =
        {
            "L75_HFX_PATIENT_FILTER_ALL",
            "L75_HFX_PATIENT_FILTER_UNTREATED",
            "L75_HFX_PATIENT_FILTER_HOSPITALIZED",
            "L75_HFX_PATIENT_FILTER_CLINIC",
            "L75_HFX_PATIENT_FILTER_BOOKMARKED"
        };

        internal static readonly string[] VanillaPatientFilterTooltips =
        {
            "TOOLTIP_TABLE_PATIENTS_ALL",
            "TOOLTIP_TABLE_PATIENTS_UNTREATED",
            "TOOLTIP_TABLE_PATIENTS_HOSPITALIZED",
            "TOOLTIP_TABLE_PATIENTS_CLINIC",
            "TOOLTIP_TABLE_PATIENTS_BOOKMARKED"
        };
    }

    internal static class LocalizationManager
    {
        private static readonly Dictionary<string, string> WaitingRoomGuidance =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", "Check that a valid waiting room is available near the examination rooms of this department." },
                { "cz", "Zkontrolujte, zda je v blízkosti vyšetřoven tohoto oddělení k dispozici platná čekárna." },
                { "da", "Kontrollér, at der er et gyldigt venteværelse i nærheden af denne afdelings undersøgelsesrum." },
                { "de", "Stellen Sie sicher, dass sich in der Nähe der Untersuchungsräume dieser Abteilung ein gültiger Warteraum befindet." },
                { "es", "Comprueba que haya una sala de espera válida cerca de las salas de exploración de este departamento." },
                { "esla", "Comprueba que haya una sala de espera válida cerca de las salas de examen de este departamento." },
                { "fr", "Vérifiez qu’une salle d’attente valide est disponible à proximité des salles d’examen de ce service." },
                { "hu", "Ellenőrizze, hogy van-e érvényes váróterem az osztály vizsgálóhelyiségei közelében." },
                { "it", "Verifica che sia disponibile una sala d'attesa valida vicino alle sale visita di questo reparto." },
                { "jp", "この診療科の検査室の近くに有効な待合室があることを確認してください。" },
                { "kr", "이 진료과의 검사실 근처에 유효한 대기실이 있는지 확인하세요." },
                { "nl", "Controleer of er een geldige wachtkamer beschikbaar is in de buurt van de onderzoeksruimtes van deze afdeling." },
                { "pl", "Sprawdź, czy w pobliżu gabinetów badań tego oddziału znajduje się prawidłowa poczekalnia." },
                { "ptbr", "Verifique se há uma sala de espera válida disponível perto das salas de exame deste departamento." },
                { "ru", "Убедитесь, что рядом с кабинетами обследования этого отделения есть подходящая комната ожидания." },
                { "swe", "Kontrollera att det finns ett giltigt väntrum nära avdelningens undersökningsrum." },
                { "th", "ตรวจสอบว่ามีห้องรอที่ใช้งานได้อยู่ใกล้ห้องตรวจของแผนกนี้" },
                { "tr", "Bu bölümün muayene odalarının yakınında geçerli bir bekleme odası bulunduğunu kontrol edin." },
                { "uk", "Переконайтеся, що поруч із кабінетами обстеження цього відділення є придатна кімната очікування." },
                { "zhcn", "请检查该科室的检查室附近是否有可用的有效候诊室。" },
                { "zhtw", "請檢查該科室的檢查室附近是否有可用的有效候診室。" }
            };

        private static readonly Dictionary<string, string>
            LabEquipmentShortageTitles =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "en", "More equipment recommended!" },
                    { "cz", "Doporučeno více vybavení!" },
                    { "da", "Mere udstyr anbefales!" },
                    { "de", "Mehr Ausrüstung empfohlen!" },
                    { "es", "¡Se recomienda más equipamiento!" },
                    { "esla", "¡Se recomienda más equipo!" },
                    { "fr", "Davantage de matériel recommandé !" },
                    { "hu", "Több felszerelés ajánlott!" },
                    { "it", "Si consiglia più attrezzatura!" },
                    { "jp", "設備の追加をおすすめします！" },
                    { "kr", "장비 추가를 권장합니다!" },
                    { "nl", "Meer apparatuur aanbevolen!" },
                    { "pl", "Zalecane jest więcej sprzętu!" },
                    { "ptbr", "Mais equipamentos são recomendados!" },
                    { "ru", "Рекомендуется добавить оборудование!" },
                    { "swe", "Mer utrustning rekommenderas!" },
                    { "th", "แนะนำให้เพิ่มอุปกรณ์!" },
                    { "tr", "Daha fazla ekipman öneriliyor!" },
                    { "uk", "Рекомендується додати обладнання!" },
                    { "zhcn", "建议增加设备！" },
                    { "zhtw", "建議增加設備！" }
                };

        private static readonly Dictionary<string, string>
            LabEquipmentShortageTexts =
                new Dictionary<string, string>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    {
                        "en",
                        "Staff are complaining that not enough {1} are available in {2}. Consider adding more equipment."
                    },
                    {
                        "cz",
                        "Personál si stěžuje, že v {2} není k dispozici dostatek vybavení {1}. Zvažte přidání dalšího vybavení."
                    },
                    {
                        "da",
                        "Personalet klager over, at der ikke er nok {1} til rådighed i {2}. Overvej at tilføje mere udstyr."
                    },
                    {
                        "de",
                        "Das Personal beschwert sich, dass in {2} nicht genügend {1} verfügbar sind. Erwägen Sie, mehr Ausrüstung hinzuzufügen."
                    },
                    {
                        "es",
                        "El personal se queja de que no hay suficientes {1} disponibles en {2}. Considera añadir más equipamiento."
                    },
                    {
                        "esla",
                        "El personal se queja de que no hay suficientes {1} disponibles en {2}. Considera agregar más equipo."
                    },
                    {
                        "fr",
                        "Le personnel se plaint qu'il n'y a pas assez de {1} disponibles dans {2}. Envisagez d'ajouter davantage de matériel."
                    },
                    {
                        "hu",
                        "A személyzet arra panaszkodik, hogy nincs elég {1} elérhető itt: {2}. Fontolja meg további felszerelés elhelyezését."
                    },
                    {
                        "it",
                        "Il personale segnala che non ci sono abbastanza {1} disponibili in {2}. Valuta di aggiungere altra attrezzatura."
                    },
                    {
                        "jp",
                        "スタッフは{2}で利用できる{1}が不足していると訴えています。設備の追加を検討してください。"
                    },
                    {
                        "kr",
                        "직원들이 {2}에서 사용할 수 있는 {1}이(가) 부족하다고 불평하고 있습니다. 장비를 더 추가해 보세요."
                    },
                    {
                        "nl",
                        "Het personeel klaagt dat er niet genoeg {1} beschikbaar zijn in {2}. Overweeg meer apparatuur toe te voegen."
                    },
                    {
                        "pl",
                        "Personel skarży się, że w {2} nie ma wystarczającej liczby {1}. Rozważ dodanie większej ilości sprzętu."
                    },
                    {
                        "ptbr",
                        "A equipe está reclamando que não há {1} suficientes disponíveis em {2}. Considere adicionar mais equipamentos."
                    },
                    {
                        "ru",
                        "Персонал жалуется, что в {2} недостаточно доступного оборудования {1}. Рассмотрите возможность добавить ещё оборудование."
                    },
                    {
                        "swe",
                        "Personalen klagar på att det inte finns tillräckligt med {1} tillgängliga i {2}. Överväg att lägga till mer utrustning."
                    },
                    {
                        "th",
                        "เจ้าหน้าที่กำลังร้องเรียนว่ามี {1} ไม่เพียงพอใน {2} โปรดพิจารณาเพิ่มอุปกรณ์"
                    },
                    {
                        "tr",
                        "Personel, {2} içinde yeterli {1} bulunmadığından şikâyet ediyor. Daha fazla ekipman eklemeyi düşünün."
                    },
                    {
                        "uk",
                        "Персонал скаржиться, що в {2} недостатньо доступного обладнання {1}. Розгляньте можливість додати більше обладнання."
                    },
                    {
                        "zhcn",
                        "员工抱怨{2}中的{1}数量不足。请考虑增加更多设备。"
                    },
                    {
                        "zhtw",
                        "員工抱怨{2}中的{1}數量不足。請考慮增加更多設備。"
                    }
                };

        private static readonly string[] EnglishPatientFilters =
        {
            "All",
            "Untreated",
            "Hospitalized",
            "Clinic",
            "Bookmarked"
        };

        private static readonly string[] FrenchPatientFilters =
        {
            "Tous",
            "Non traités",
            "Hospitalisés",
            "Clinique",
            "Épinglés"
        };

        internal static string Get(string stringId)
        {
            StringTable table = StringTable.GetInstance();
            string languageCode =
                table == null ? null : table.GetCurrentLanguage();

            if (string.IsNullOrEmpty(languageCode) &&
                PlayerProfile.Instance != null)
            {
                languageCode = PlayerProfile.Instance.GetCurrentLanguage();
            }

            string result;
            return TryGetLocalizedText(
                    languageCode,
                    stringId,
                    null,
                    out result)
                ? result
                : stringId;
        }

        internal static bool TryGetLocalizedText(
            string languageCode,
            string stringId,
            string[] parameters,
            out string result)
        {
            result = null;
            if (string.IsNullOrEmpty(stringId) ||
                !stringId.StartsWith(
                    HfxLocalizationIds.Prefix,
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (stringId ==
                    HfxLocalizationIds.LabEquipmentShortageNotification ||
                stringId ==
                    HfxLocalizationIds.LabEquipmentShortageNotificationText)
            {
                string notificationLanguage =
                    NormalizeLanguageCode(languageCode);

                if (notificationLanguage == "test")
                {
                    result = stringId;
                    return true;
                }

                Dictionary<string, string> translations =
                    stringId ==
                        HfxLocalizationIds.LabEquipmentShortageNotification
                            ? LabEquipmentShortageTitles
                            : LabEquipmentShortageTexts;

                if (string.IsNullOrEmpty(notificationLanguage) ||
                    !translations.TryGetValue(
                        notificationLanguage,
                        out result))
                {
                    result = translations["en"];
                }

                ApplyParameters(
                    ref result,
                    parameters);
                return true;
            }

            if (stringId == HfxLocalizationIds.WaitingRoomGuidance)
            {
                string waitingLanguage =
                    NormalizeLanguageCode(languageCode);

                if (waitingLanguage == "test")
                {
                    return true;
                }

                if (string.IsNullOrEmpty(waitingLanguage) ||
                    !WaitingRoomGuidance.TryGetValue(
                        waitingLanguage,
                        out result))
                {
                    result = WaitingRoomGuidance["en"];
                }

                ApplyParameters(ref result, parameters);
                return true;
            }

            int patientFilterIndex =
                GetPatientFilterIndex(stringId);
            if (patientFilterIndex < 0)
            {
                return false;
            }

            string language =
                NormalizeLanguageCode(languageCode);

            if (language == "test")
            {
                result = stringId;
            }
            else if (language == "fr")
            {
                result = FrenchPatientFilters[patientFilterIndex];
            }
            else if (language == "en" ||
                string.IsNullOrEmpty(language))
            {
                result = EnglishPatientFilters[patientFilterIndex];
            }
            else
            {
                StringTable table = StringTable.GetInstance();
                result =
                    table == null
                        ? EnglishPatientFilters[patientFilterIndex]
                        : table.GetLocalizedText(
                            HfxLocalizationIds
                                .VanillaPatientFilterTooltips[
                                    patientFilterIndex]);

                if (string.IsNullOrEmpty(result))
                {
                    result = EnglishPatientFilters[patientFilterIndex];
                }
            }

            ApplyParameters(ref result, parameters);
            return true;
        }

        private static int GetPatientFilterIndex(
            string stringId)
        {
            for (int i = 0;
                 i < HfxLocalizationIds.PatientFilterLabels.Length;
                 i++)
            {
                if (stringId ==
                    HfxLocalizationIds.PatientFilterLabels[i])
                {
                    return i;
                }
            }

            return -1;
        }

        private static string NormalizeLanguageCode(
            string languageCode)
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
            if (text == null ||
                parameters == null)
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
}
