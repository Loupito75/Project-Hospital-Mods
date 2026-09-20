using System;
using System.Collections.Generic;

namespace HospitalCareLevelTransfer
{
    internal static class LocalizationManager
    {
        internal const string TooltipTransferReadyId = "L75_HCLT_TOOLTIP_TRANSFER_READY";
        internal const string TooltipTransferReadyNoBedId = "L75_HCLT_TOOLTIP_TRANSFER_READY_NO_BED";
        internal const string TooltipTransferStillHduId = "L75_HCLT_TOOLTIP_TRANSFER_STILL_HDU";
        internal const string TooltipTransferStillHduHazardId = "L75_HCLT_TOOLTIP_TRANSFER_STILL_HDU_HAZARD";
        internal const string TooltipTransferStillHduImmobileId = "L75_HCLT_TOOLTIP_TRANSFER_STILL_HDU_IMMOBILE";
        internal const string TooltipTransferStillHduBothId = "L75_HCLT_TOOLTIP_TRANSFER_STILL_HDU_BOTH";
        internal const string TooltipTransferWaitBedId = "L75_HCLT_TOOLTIP_TRANSFER_WAIT_BED";
        internal const string TooltipTransferUnavailableId = "L75_HCLT_TOOLTIP_TRANSFER_UNAVAILABLE";

        internal const string TooltipCurrentReadyId = "L75_HCLT_TOOLTIP_CURRENT_READY";
        internal const string TooltipCurrentNoBedId = "L75_HCLT_TOOLTIP_CURRENT_NO_BED";
        internal const string TooltipCurrentStillHduId = "L75_HCLT_TOOLTIP_CURRENT_STILL_HDU";
        internal const string TooltipCurrentStillHduHazardId = "L75_HCLT_TOOLTIP_CURRENT_STILL_HDU_HAZARD";
        internal const string TooltipCurrentStillHduImmobileId = "L75_HCLT_TOOLTIP_CURRENT_STILL_HDU_IMMOBILE";
        internal const string TooltipCurrentStillHduBothId = "L75_HCLT_TOOLTIP_CURRENT_STILL_HDU_BOTH";
        internal const string TooltipCurrentWaitBedId = "L75_HCLT_TOOLTIP_CURRENT_WAIT_BED";
        internal const string TooltipCurrentUnavailableId = "L75_HCLT_TOOLTIP_CURRENT_UNAVAILABLE";

        internal const string FloatingRequestedId = "L75_HCLT_FLOATING_REQUESTED";
        internal const string FloatingNoBedId = "L75_HCLT_FLOATING_NO_BED";
        internal const string FloatingStillHduId = "L75_HCLT_FLOATING_STILL_HDU";
        internal const string FloatingStillHduHazardId = "L75_HCLT_FLOATING_STILL_HDU_HAZARD";
        internal const string FloatingStillHduImmobileId = "L75_HCLT_FLOATING_STILL_HDU_IMMOBILE";
        internal const string FloatingStillHduBothId = "L75_HCLT_FLOATING_STILL_HDU_BOTH";
        internal const string FloatingWaitBedId = "L75_HCLT_FLOATING_WAIT_BED";
        internal const string FloatingUnavailableId = "L75_HCLT_FLOATING_UNAVAILABLE";

        private sealed class Translation
        {
            internal readonly string TransferPatient;
            internal readonly string RegularAvailable;
            internal readonly string CurrentDepartment;
            internal readonly string ReadyForRegular;
            internal readonly string NoRegularBed;
            internal readonly string HduRequired;
            internal readonly string HighHazard;
            internal readonly string ImmobileCondition;
            internal readonly string HighHazardAndImmobile;
            internal readonly string HighPriorityNeeded;
            internal readonly string TransferUnavailable;
            internal readonly string NoActiveBed;
            internal readonly string CannotChangeCare;
            internal readonly string CurrentLabel;
            internal readonly string ClickToTransfer;
            internal readonly string TransferRequested;
            internal readonly string ToRegularWard;
            internal readonly string TransferImpossible;

            internal Translation(
                string transferPatient,
                string regularAvailable,
                string currentDepartment,
                string readyForRegular,
                string noRegularBed,
                string hduRequired,
                string highHazard,
                string immobileCondition,
                string highHazardAndImmobile,
                string highPriorityNeeded,
                string transferUnavailable,
                string noActiveBed,
                string cannotChangeCare,
                string currentLabel,
                string clickToTransfer,
                string transferRequested,
                string toRegularWard,
                string transferImpossible)
            {
                TransferPatient = transferPatient;
                RegularAvailable = regularAvailable;
                CurrentDepartment = currentDepartment;
                ReadyForRegular = readyForRegular;
                NoRegularBed = noRegularBed;
                HduRequired = hduRequired;
                HighHazard = highHazard;
                ImmobileCondition = immobileCondition;
                HighHazardAndImmobile = highHazardAndImmobile;
                HighPriorityNeeded = highPriorityNeeded;
                TransferUnavailable = transferUnavailable;
                NoActiveBed = noActiveBed;
                CannotChangeCare = cannotChangeCare;
                CurrentLabel = currentLabel;
                ClickToTransfer = clickToTransfer;
                TransferRequested = transferRequested;
                ToRegularWard = toRegularWard;
                TransferImpossible = transferImpossible;
            }
        }

        private static readonly Translation English = new Translation(
            "Transfer patient", "Regular ward available", "In the current department.",
            "Patient is ready for a regular ward", "No regular bed is free.",
            "HDU is still required", "Known hazard is high.",
            "The medical condition contains an immobile-classified symptom.",
            "High hazard and an immobile-classified symptom.",
            "High-priority hospitalization is still necessary.",
            "Transfer unavailable", "No active hospitalization bed is assigned to the patient.",
            "Care level cannot be changed right now.", "current",
            "Click to request the transfer.", "Transfer requested",
            "To a regular ward", "Transfer unavailable");

        private static readonly Dictionary<string, Translation> Translations =
            new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", English },
                { "cz", new Translation(
                    "Převést pacienta", "Standardní oddělení je dostupné", "V aktuálním oddělení.",
                    "Pacient je připraven na standardní oddělení", "Není volné standardní lůžko.",
                    "HDU je stále nutná", "Známé riziko je vysoké.",
                    "Zdravotní stav obsahuje příznak omezující pohyblivost.",
                    "Vysoké riziko a příznak omezující pohyblivost.",
                    "Hospitalizace s vysokou prioritou je stále nutná.",
                    "Převod není dostupný", "Pacient nemá přiřazené aktivní nemocniční lůžko.",
                    "Úroveň péče nyní nelze změnit.", "aktuální",
                    "Kliknutím požádejte o převod.", "Převod požadován",
                    "Na standardní oddělení", "Převod není možný") },
                { "da", new Translation(
                    "Overfør patient", "Almindelig sengeafdeling er tilgængelig", "I den nuværende afdeling.",
                    "Patienten er klar til en almindelig sengeafdeling", "Ingen almindelig seng er ledig.",
                    "HDU er stadig påkrævet", "Den kendte risiko er høj.",
                    "Den medicinske tilstand indeholder et symptom klassificeret som immobiliserende.",
                    "Høj risiko og et immobiliserende symptom.",
                    "Indlæggelse med høj prioritet er stadig nødvendig.",
                    "Overførsel er ikke tilgængelig", "Patienten har ingen aktiv hospitalsseng tildelt.",
                    "Plejeniveauet kan ikke ændres lige nu.", "nuværende",
                    "Klik for at anmode om overførsel.", "Overførsel anmodet",
                    "Til en almindelig sengeafdeling", "Overførsel er ikke mulig") },
                { "de", new Translation(
                    "Patient verlegen", "Normalstation verfügbar", "In der aktuellen Abteilung.",
                    "Patient ist bereit für die Normalstation", "Kein Bett auf der Normalstation ist frei.",
                    "HDU weiterhin erforderlich", "Das bekannte Risiko ist hoch.",
                    "Die Erkrankung enthält ein als immobilisierend eingestuftes Symptom.",
                    "Hohes Risiko und ein immobilisierendes Symptom.",
                    "Eine Hospitalisierung mit hoher Priorität ist weiterhin erforderlich.",
                    "Verlegung nicht verfügbar", "Dem Patienten ist kein aktives Krankenhausbett zugewiesen.",
                    "Die Versorgungsstufe kann derzeit nicht geändert werden.", "aktuell",
                    "Klicken, um die Verlegung anzufordern.", "Verlegung angefordert",
                    "Auf eine Normalstation", "Verlegung nicht möglich") },
                { "es", new Translation(
                    "Trasladar paciente", "Hay cama de hospitalización normal disponible", "En el departamento actual.",
                    "El paciente está listo para hospitalización normal", "No hay ninguna cama normal libre.",
                    "La HDU sigue siendo necesaria", "El riesgo conocido es alto.",
                    "La condición médica contiene un síntoma clasificado como inmovilizante.",
                    "Riesgo alto y un síntoma inmovilizante.",
                    "La hospitalización de alta prioridad sigue siendo necesaria.",
                    "Traslado no disponible", "El paciente no tiene asignada una cama de hospitalización activa.",
                    "El nivel de cuidados no puede cambiarse ahora.", "actual",
                    "Haz clic para solicitar el traslado.", "Traslado solicitado",
                    "A hospitalización normal", "Traslado no disponible") },
                { "esla", new Translation(
                    "Trasladar paciente", "Hay cama de hospitalización regular disponible", "En el departamento actual.",
                    "El paciente está listo para hospitalización regular", "No hay ninguna cama regular libre.",
                    "La HDU sigue siendo necesaria", "El riesgo conocido es alto.",
                    "La condición médica contiene un síntoma clasificado como inmovilizante.",
                    "Riesgo alto y un síntoma inmovilizante.",
                    "La hospitalización de alta prioridad sigue siendo necesaria.",
                    "Traslado no disponible", "El paciente no tiene asignada una cama de hospitalización activa.",
                    "El nivel de atención no puede cambiarse ahora.", "actual",
                    "Haz clic para solicitar el traslado.", "Traslado solicitado",
                    "A una sala regular", "Traslado no disponible") },
                { "fr", new Translation(
                    "Transférer le patient", "Hospitalisation standard disponible", "Dans le département actuel.",
                    "Patient prêt pour l'hospitalisation standard", "Aucun lit standard n'est libre.",
                    "HDU encore requise", "Danger connu élevé.",
                    "La condition médicale contient un symptôme classé immobilisant.",
                    "Danger élevé et symptôme classé immobilisant.",
                    "L'hospitalisation haute priorité reste nécessaire.",
                    "Transfert indisponible", "Aucun lit d'hospitalisation actif n'est associé au patient.",
                    "Le niveau de soins ne peut pas être changé actuellement.", "actuel",
                    "Cliquer pour demander le transfert.", "Transfert demandé",
                    "Vers une hospitalisation standard", "Transfert impossible") },
                { "hu", new Translation(
                    "Beteg áthelyezése", "Normál kórterem elérhető", "A jelenlegi osztályon.",
                    "A beteg készen áll a normál kórteremre", "Nincs szabad normál ágy.",
                    "A HDU továbbra is szükséges", "Az ismert kockázat magas.",
                    "Az egészségügyi állapot mozgásképtelenséget okozó tünetet tartalmaz.",
                    "Magas kockázat és mozgásképtelenséget okozó tünet.",
                    "A magas prioritású kórházi ellátás továbbra is szükséges.",
                    "Az áthelyezés nem elérhető", "A beteghez nincs aktív kórházi ágy rendelve.",
                    "Az ellátási szint jelenleg nem módosítható.", "jelenlegi",
                    "Kattintson az áthelyezés kéréséhez.", "Áthelyezés kérve",
                    "Normál kórterembe", "Az áthelyezés nem lehetséges") },
                { "it", new Translation(
                    "Trasferisci paziente", "Reparto ordinario disponibile", "Nel reparto attuale.",
                    "Il paziente è pronto per un reparto ordinario", "Nessun letto ordinario è libero.",
                    "La HDU è ancora necessaria", "Il rischio noto è elevato.",
                    "La condizione medica contiene un sintomo classificato come immobilizzante.",
                    "Rischio elevato e sintomo immobilizzante.",
                    "Il ricovero ad alta priorità è ancora necessario.",
                    "Trasferimento non disponibile", "Al paziente non è assegnato un letto di ricovero attivo.",
                    "Il livello di assistenza non può essere modificato ora.", "attuale",
                    "Fai clic per richiedere il trasferimento.", "Trasferimento richiesto",
                    "Verso un reparto ordinario", "Trasferimento non possibile") },
                { "jp", new Translation(
                    "患者を転棟", "一般病棟を利用できます", "現在の診療科内です。",
                    "患者は一般病棟へ移れます", "空いている一般病床がありません。",
                    "HDU が引き続き必要です", "既知の重症度が高い状態です。",
                    "病状に移動不能と分類された症状があります。",
                    "高い重症度と移動不能の症状があります。",
                    "高優先度の入院が引き続き必要です。",
                    "転棟できません", "患者に有効な入院ベッドが割り当てられていません。",
                    "現在はケアレベルを変更できません。", "現在",
                    "クリックして転棟を依頼します。", "転棟を依頼しました",
                    "一般病棟へ", "転棟できません") },
                { "kr", new Translation(
                    "환자 전실", "일반 병동 이용 가능", "현재 진료과 내입니다.",
                    "환자가 일반 병동으로 이동할 수 있습니다", "사용 가능한 일반 병상이 없습니다.",
                    "HDU가 계속 필요합니다", "확인된 위험도가 높습니다.",
                    "의학적 상태에 이동 불가로 분류된 증상이 있습니다.",
                    "높은 위험도와 이동 불가 증상이 있습니다.",
                    "고우선순위 입원이 계속 필요합니다.",
                    "전실할 수 없습니다", "환자에게 활성 입원 병상이 배정되어 있지 않습니다.",
                    "현재는 치료 수준을 변경할 수 없습니다.", "현재",
                    "클릭하여 전실을 요청합니다.", "전실 요청됨",
                    "일반 병동으로", "전실할 수 없습니다") },
                { "nl", new Translation(
                    "Patiënt overplaatsen", "Gewone verpleegafdeling beschikbaar", "Op de huidige afdeling.",
                    "Patiënt is klaar voor een gewone verpleegafdeling", "Er is geen gewoon bed vrij.",
                    "HDU is nog steeds vereist", "Het bekende risico is hoog.",
                    "De medische aandoening bevat een als immobiel geclassificeerd symptoom.",
                    "Hoog risico en een immobiliserend symptoom.",
                    "Opname met hoge prioriteit blijft noodzakelijk.",
                    "Overplaatsing niet beschikbaar", "Er is geen actief ziekenhuisbed aan de patiënt toegewezen.",
                    "Het zorgniveau kan nu niet worden gewijzigd.", "huidig",
                    "Klik om de overplaatsing aan te vragen.", "Overplaatsing aangevraagd",
                    "Naar een gewone verpleegafdeling", "Overplaatsing niet mogelijk") },
                { "pl", new Translation(
                    "Przenieś pacjenta", "Dostępny jest zwykły oddział", "W obecnym oddziale.",
                    "Pacjent jest gotowy na zwykły oddział", "Brak wolnego zwykłego łóżka.",
                    "HDU jest nadal wymagane", "Znane zagrożenie jest wysokie.",
                    "Stan medyczny zawiera objaw sklasyfikowany jako unieruchamiający.",
                    "Wysokie zagrożenie i objaw unieruchamiający.",
                    "Hospitalizacja o wysokim priorytecie jest nadal konieczna.",
                    "Przeniesienie niedostępne", "Pacjent nie ma przypisanego aktywnego łóżka szpitalnego.",
                    "Poziomu opieki nie można teraz zmienić.", "obecny",
                    "Kliknij, aby poprosić o przeniesienie.", "Zlecono przeniesienie",
                    "Na zwykły oddział", "Przeniesienie niemożliwe") },
                { "ptbr", new Translation(
                    "Transferir paciente", "Enfermaria regular disponível", "No departamento atual.",
                    "O paciente está pronto para uma enfermaria regular", "Não há leito regular disponível.",
                    "A HDU ainda é necessária", "O risco conhecido é alto.",
                    "A condição médica contém um sintoma classificado como imobilizante.",
                    "Risco alto e um sintoma imobilizante.",
                    "A internação de alta prioridade ainda é necessária.",
                    "Transferência indisponível", "O paciente não tem um leito de internação ativo atribuído.",
                    "O nível de cuidado não pode ser alterado agora.", "atual",
                    "Clique para solicitar a transferência.", "Transferência solicitada",
                    "Para uma enfermaria regular", "Transferência indisponível") },
                { "ru", new Translation(
                    "Перевести пациента", "Обычная палата доступна", "В текущем отделении.",
                    "Пациент готов к переводу в обычную палату", "Нет свободной обычной койки.",
                    "HDU всё ещё требуется", "Известный уровень опасности высокий.",
                    "Состояние содержит симптом, классифицированный как обездвиживающий.",
                    "Высокая опасность и обездвиживающий симптом.",
                    "Госпитализация высокого приоритета всё ещё необходима.",
                    "Перевод недоступен", "Пациенту не назначена активная больничная койка.",
                    "Уровень ухода сейчас нельзя изменить.", "текущее",
                    "Нажмите, чтобы запросить перевод.", "Перевод запрошен",
                    "В обычную палату", "Перевод невозможен") },
                { "swe", new Translation(
                    "Flytta patient", "Vanlig vårdavdelning är tillgänglig", "På den nuvarande avdelningen.",
                    "Patienten är redo för en vanlig vårdavdelning", "Ingen vanlig vårdplats är ledig.",
                    "HDU krävs fortfarande", "Den kända risken är hög.",
                    "Det medicinska tillståndet innehåller ett symtom klassat som immobiliserande.",
                    "Hög risk och ett immobiliserande symtom.",
                    "Sjukhusvård med hög prioritet krävs fortfarande.",
                    "Flytt är inte tillgänglig", "Patienten har ingen aktiv vårdplats tilldelad.",
                    "Vårdnivån kan inte ändras just nu.", "nuvarande",
                    "Klicka för att begära flytt.", "Flytt begärd",
                    "Till en vanlig vårdavdelning", "Flytt är inte möjlig") },
                { "tr", new Translation(
                    "Hastayı transfer et", "Normal servis kullanılabilir", "Mevcut bölümde.",
                    "Hasta normal servise geçmeye hazır", "Boş normal yatak yok.",
                    "HDU hâlâ gerekli", "Bilinen risk yüksek.",
                    "Tıbbi durumda hareketsiz olarak sınıflandırılmış bir belirti var.",
                    "Yüksek risk ve hareketsizleştirici bir belirti.",
                    "Yüksek öncelikli yatış hâlâ gerekli.",
                    "Transfer kullanılamıyor", "Hastaya atanmış aktif bir hastane yatağı yok.",
                    "Bakım seviyesi şu anda değiştirilemiyor.", "mevcut",
                    "Transfer istemek için tıklayın.", "Transfer istendi",
                    "Normal servise", "Transfer mümkün değil") },
                { "uk", new Translation(
                    "Перевести пацієнта", "Звичайна палата доступна", "У поточному відділенні.",
                    "Пацієнт готовий до звичайної палати", "Немає вільного звичайного ліжка.",
                    "HDU все ще потрібне", "Відомий рівень небезпеки високий.",
                    "Медичний стан містить симптом, класифікований як такий, що знерухомлює.",
                    "Висока небезпека та симптом, що знерухомлює.",
                    "Госпіталізація високого пріоритету все ще потрібна.",
                    "Переведення недоступне", "Пацієнту не призначено активне лікарняне ліжко.",
                    "Рівень догляду зараз не можна змінити.", "поточне",
                    "Натисніть, щоб запросити переведення.", "Переведення запитано",
                    "До звичайної палати", "Переведення неможливе") },
                { "zhcn", new Translation(
                    "转移患者", "普通病房可用", "在当前科室内。",
                    "患者可以转入普通病房", "没有空闲的普通病床。",
                    "仍需要 HDU", "已知风险较高。",
                    "病情包含被归类为无法行动的症状。",
                    "高风险且存在无法行动的症状。",
                    "仍需要高优先级住院。",
                    "无法转移", "患者未分配有效的住院病床。",
                    "目前无法更改护理级别。", "当前",
                    "点击申请转移。", "已申请转移",
                    "转入普通病房", "无法转移") },
                { "zhtw", new Translation(
                    "轉移病患", "普通病房可用", "在目前科別內。",
                    "病患可以轉入普通病房", "沒有空閒的普通病床。",
                    "仍需要 HDU", "已知風險較高。",
                    "病況包含被歸類為無法移動的症狀。",
                    "高風險且有無法移動的症狀。",
                    "仍需要高優先級住院。",
                    "無法轉移", "病患未分配有效的住院病床。",
                    "目前無法變更照護等級。", "目前",
                    "點擊以申請轉移。", "已申請轉移",
                    "轉入普通病房", "無法轉移") }
            };

        internal static string GetCurrentText(string stringId)
        {
            return GetCurrentText(stringId, null);
        }

        internal static string GetCurrentText(string stringId, string[] parameters)
        {
            string languageCode = StringTable.GetInstance().GetCurrentLanguage();
            if (string.IsNullOrEmpty(languageCode) && PlayerProfile.Instance != null)
            {
                languageCode = PlayerProfile.Instance.GetCurrentLanguage();
            }

            string result;
            return TryGetLocalizedText(languageCode, stringId, parameters, out result)
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

            Translation translation;
            string normalizedLanguage = NormalizeLanguageCode(languageCode);
            if (string.IsNullOrEmpty(normalizedLanguage) ||
                !Translations.TryGetValue(normalizedLanguage, out translation))
            {
                translation = English;
            }

            if (stringId == TooltipTransferReadyId)
            {
                result = Join(translation.TransferPatient, translation.RegularAvailable, translation.CurrentDepartment);
            }
            else if (stringId == TooltipTransferReadyNoBedId)
            {
                result = Join(translation.TransferPatient, translation.ReadyForRegular, translation.NoRegularBed);
            }
            else if (stringId == TooltipTransferStillHduHazardId)
            {
                result = Join(translation.TransferPatient, translation.HduRequired, translation.HighHazard);
            }
            else if (stringId == TooltipTransferStillHduImmobileId)
            {
                result = Join(translation.TransferPatient, translation.HduRequired, translation.ImmobileCondition);
            }
            else if (stringId == TooltipTransferStillHduBothId)
            {
                result = Join(translation.TransferPatient, translation.HduRequired, translation.HighHazardAndImmobile);
            }
            else if (stringId == TooltipTransferStillHduId)
            {
                result = Join(translation.TransferPatient, translation.HduRequired, translation.HighPriorityNeeded);
            }
            else if (stringId == TooltipTransferWaitBedId)
            {
                result = Join(translation.TransferPatient, translation.TransferUnavailable, translation.NoActiveBed);
            }
            else if (stringId == TooltipTransferUnavailableId)
            {
                result = Join(translation.TransferPatient, translation.TransferUnavailable, translation.CannotChangeCare);
            }
            else if (stringId == TooltipCurrentReadyId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.RegularAvailable, translation.ClickToTransfer);
            }
            else if (stringId == TooltipCurrentNoBedId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.TransferUnavailable, translation.NoRegularBed);
            }
            else if (stringId == TooltipCurrentStillHduHazardId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.HduRequired, translation.HighHazard);
            }
            else if (stringId == TooltipCurrentStillHduImmobileId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.HduRequired, translation.ImmobileCondition);
            }
            else if (stringId == TooltipCurrentStillHduBothId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.HduRequired, translation.HighHazardAndImmobile);
            }
            else if (stringId == TooltipCurrentStillHduId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.HduRequired, translation.HighPriorityNeeded);
            }
            else if (stringId == TooltipCurrentWaitBedId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.TransferUnavailable, translation.NoActiveBed);
            }
            else if (stringId == TooltipCurrentUnavailableId)
            {
                result = Join("{1} (" + translation.CurrentLabel + ")", translation.TransferUnavailable, translation.CannotChangeCare);
            }
            else if (stringId == FloatingRequestedId)
            {
                result = Join(translation.TransferRequested, translation.ToRegularWard, translation.CurrentDepartment);
            }
            else if (stringId == FloatingNoBedId)
            {
                result = Join(translation.TransferImpossible, translation.NoRegularBed, translation.CurrentDepartment);
            }
            else if (stringId == FloatingStillHduHazardId)
            {
                result = Join(translation.HduRequired, translation.HighHazard);
            }
            else if (stringId == FloatingStillHduImmobileId)
            {
                result = Join(translation.HduRequired, translation.ImmobileCondition);
            }
            else if (stringId == FloatingStillHduBothId)
            {
                result = Join(translation.HduRequired, translation.HighHazardAndImmobile);
            }
            else if (stringId == FloatingStillHduId)
            {
                result = Join(translation.HduRequired, translation.HighPriorityNeeded);
            }
            else if (stringId == FloatingWaitBedId)
            {
                result = Join(translation.TransferUnavailable, translation.NoActiveBed);
            }
            else if (stringId == FloatingUnavailableId)
            {
                result = Join(translation.TransferUnavailable, translation.CannotChangeCare);
            }
            else
            {
                return false;
            }

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

        private static string Join(string line1, string line2)
        {
            return line1 + "\n" + line2;
        }

        private static string Join(string line1, string line2, string line3)
        {
            return line1 + "\n" + line2 + "\n" + line3;
        }

        private static void ApplyParameters(ref string text, string[] parameters)
        {
            if (parameters == null)
            {
                return;
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                text = text.Replace("{" + (i + 1) + "}", parameters[i] ?? string.Empty);
            }
        }
    }
}
