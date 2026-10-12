using System;
using System.Collections.Generic;

namespace HospitalPorters
{
    internal static class PorterLocalizationIds
    {
        internal const string Occupation = "L75_HPO_OCCUPATION_PORTER";
        internal const string PorterCandidates = "L75_HPO_PORTERS";
        internal const string PorterTooltip = "L75_HPO_PORTERS_TOOLTIP";
        internal const string PorterStaffingTooltip = "L75_HPO_PORTERS_STAFFING_TOOLTIP";
        internal const string EmployeesHeading = "L75_HPO_EMPLOYEES_HEADING_PORTERS";
        internal const string PorterLevel1 = "L75_HPO_PORTER_LEVEL_1";
        internal const string PorterLevel2 = "L75_HPO_PORTER_LEVEL_2";
        internal const string PorterLevel3 = "L75_HPO_PORTER_LEVEL_3";
        internal const string PorterQualification = "L75_HPO_SKILL_PORTER_QUALIFICATION";
        internal const string PorterQualificationDescription = "L75_HPO_SKILL_PORTER_QUALIFICATION_DESCRIPTION";
        internal const string PorterStationRoom = "L75_HPO_ROOM_TYPE_PORTER_STATION";
        internal const string PorterStationRoomDescription = "L75_HPO_ROOM_TYPE_PORTER_STATION_DESCRIPTION";
        internal const string PatientTransportRole = "L75_HPO_EMPL_ROLE_PATIENT_TRANSPORT";
        internal const string PatientTransportRoleDescription = "L75_HPO_EMPL_ROLE_PATIENT_TRANSPORT_DESCRIPTION";
        internal const string SampleTransportRole = "L75_HPO_EMPL_ROLE_SAMPLE_TRANSPORT";
        internal const string SampleTransportRoleDescription = "L75_HPO_EMPL_ROLE_SAMPLE_TRANSPORT_DESCRIPTION";
        internal const string SampleCart = "L75_HPO_OBJECT_SAMPLE_CART";
        internal const string SampleCartDescription = "L75_HPO_OBJECT_SAMPLE_CART_DESCRIPTION";
        internal const string SampleCartNotification = "L75_HPO_NOTIF_NOT_ENOUGH_SAMPLE_CARTS";
        internal const string SampleCartNotificationText = "L75_HPO_NOTIF_NOT_ENOUGH_SAMPLE_CARTS_TEXT";
        internal const string SamplePorterCapacityNotification = "L75_HPO_NOTIF_SAMPLE_PORTER_CAPACITY";
        internal const string SamplePorterCapacityNotificationText = "L75_HPO_NOTIF_SAMPLE_PORTER_CAPACITY_TEXT";
        internal const string PatientPorterCapacityNotification = "L75_HPO_NOTIF_PATIENT_PORTER_CAPACITY";
        internal const string PatientPorterCapacityNotificationText = "L75_HPO_NOTIF_PATIENT_PORTER_CAPACITY_TEXT";

        internal static string Normalize(string stringId)
        {
            if (stringId == PorterIds.Occupation) return Occupation;
            if (stringId == PorterIds.PorterCandidates) return PorterCandidates;
            if (stringId == PorterIds.PorterTooltip) return PorterTooltip;
            if (stringId == PorterIds.PorterStaffingTooltip) return PorterStaffingTooltip;
            if (stringId == PorterIds.EmployeesHeading) return EmployeesHeading;
            if (stringId == PorterIds.PorterLevel1) return PorterLevel1;
            if (stringId == PorterIds.PorterLevel2) return PorterLevel2;
            if (stringId == PorterIds.PorterLevel3) return PorterLevel3;
            if (stringId == PorterIds.PorterQualification) return PorterQualification;
            if (stringId == PorterIds.PorterQualification + "_DESCRIPTION") return PorterQualificationDescription;
            if (stringId == PorterIds.PorterStationRoom) return PorterStationRoom;
            if (stringId == PorterIds.PorterStationRoom + "_DESCRIPTION") return PorterStationRoomDescription;
            if (stringId == PorterIds.PatientTransportRole) return PatientTransportRole;
            if (stringId == PorterIds.PatientTransportRole + "_DESCRIPTION") return PatientTransportRoleDescription;
            if (stringId == PorterIds.SampleTransportRole) return SampleTransportRole;
            if (stringId == PorterIds.SampleTransportRole + "_DESCRIPTION") return SampleTransportRoleDescription;
            if (stringId == PorterIds.SampleCart) return SampleCart;
            if (stringId == PorterIds.SampleCart + "_DESCRIPTION") return SampleCartDescription;
            if (stringId == PorterIds.SampleCartNotification) return SampleCartNotification;
            if (stringId == PorterIds.SampleCartNotificationText) return SampleCartNotificationText;
            if (stringId == PorterIds.SamplePorterCapacityNotification) return SamplePorterCapacityNotification;
            if (stringId == PorterIds.SamplePorterCapacityNotificationText) return SamplePorterCapacityNotificationText;
            if (stringId == PorterIds.PatientPorterCapacityNotification) return PatientPorterCapacityNotification;
            if (stringId == PorterIds.PatientPorterCapacityNotificationText) return PatientPorterCapacityNotificationText;
            return stringId;
        }
    }

    internal static class LocalizationManager
    {
        private sealed class Translation
        {
            internal readonly string Occupation;
            internal readonly string Candidates;
            internal readonly string PorterTooltip;
            internal readonly string StaffingTooltip;
            internal readonly string EmployeesHeading;
            internal readonly string Level1;
            internal readonly string Level2;
            internal readonly string Level3;
            internal readonly string Qualification;
            internal readonly string QualificationDescription;
            internal readonly string Station;
            internal readonly string StationDescription;
            internal readonly string PatientTransport;
            internal readonly string SampleTransport;
            internal readonly string PatientTransportDescription;
            internal readonly string SampleTransportDescription;
            internal readonly string SampleCart;
            internal readonly string SampleCartDescription;
            internal readonly string NotificationTitle;
            internal readonly string NotificationText;

            internal Translation(
                string occupation,
                string candidates,
                string porterTooltip,
                string staffingTooltip,
                string employeesHeading,
                string level1,
                string level2,
                string level3,
                string qualification,
                string qualificationDescription,
                string station,
                string stationDescription,
                string patientTransport,
                string sampleTransport,
                string patientTransportDescription,
                string sampleTransportDescription,
                string sampleCart,
                string sampleCartDescription,
                string notificationTitle,
                string notificationText)
            {
                Occupation = occupation;
                Candidates = candidates;
                PorterTooltip = porterTooltip;
                StaffingTooltip = staffingTooltip;
                EmployeesHeading = employeesHeading;
                Level1 = level1;
                Level2 = level2;
                Level3 = level3;
                Qualification = qualification;
                QualificationDescription = qualificationDescription;
                Station = station;
                StationDescription = stationDescription;
                PatientTransport = patientTransport;
                SampleTransport = sampleTransport;
                PatientTransportDescription = patientTransportDescription;
                SampleTransportDescription = sampleTransportDescription;
                SampleCart = sampleCart;
                SampleCartDescription = sampleCartDescription;
                NotificationTitle = notificationTitle;
                NotificationText = notificationText;
            }
        }

        private sealed class CapacityNotificationTranslation
        {
            internal readonly string Title;
            internal readonly string SampleText;
            internal readonly string PatientText;

            internal CapacityNotificationTranslation(
                string title,
                string sampleText,
                string patientText)
            {
                Title = title;
                SampleText = sampleText;
                PatientText = patientText;
            }
        }

        private static readonly Translation English = new Translation(
            "Porter",
            "Porters",
            "Dedicated internal logistics staff for patient and sample transport. A valid Porter station is required before hiring a new porter.",
            "Dedicated internal logistics staff for patient and sample transport. Porters require the Porter qualification and an assigned Porter station.",
            "Employees - Porters",
            "Junior porter",
            "Experienced porter",
            "Senior porter",
            "Porter",
            "Porter qualification. Represents training and experience in internal hospital transport.",
            "Porter station",
            "Porter station",
            "Patient transport",
            "Sample transport",
            "Moves hospitalized patients by stretcher or wheelchair.",
            "Carries collected samples to the laboratory.",
            "Sample cart",
            "Mobile cart for transporting collected laboratory samples.",
            "More sample carts recommended!",
            "Porters are complaining that they have to carry samples one by one at {1} because no sample cart is available.");

        private static readonly Dictionary<string, CapacityNotificationTranslation>
            CapacityNotifications =
                new Dictionary<string, CapacityNotificationTranslation>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    { "en", new CapacityNotificationTranslation(
                        "Not enough Porters!",
                        "At {1}, lab specialists are complaining about a shortage of Porters and too often have to transport samples themselves. Assign more Porters to Sample transport.",
                        "At {1}, nurses are complaining about a shortage of Porters and too often have to transport patients themselves. Assign more Porters to Patient transport.") },
                    { "cz", new CapacityNotificationTranslation(
                        "Nedostatek sanitářů!",
                        "V {1} si laboratorní pracovníci stěžují na nedostatek sanitářů a příliš často musejí sami přepravovat vzorky. Přidělte více sanitářů k přepravě vzorků.",
                        "V {1} si sestry stěžují na nedostatek sanitářů a příliš často musejí samy přepravovat pacienty. Přidělte více sanitářů k přepravě pacientů.") },
                    { "da", new CapacityNotificationTranslation(
                        "For få portører!",
                        "På {1} klager laboratoriepersonalet over mangel på portører og må alt for ofte selv transportere prøver. Tildel flere portører til prøvetransport.",
                        "På {1} klager sygeplejerskerne over mangel på portører og må alt for ofte selv transportere patienter. Tildel flere portører til patienttransport.") },
                    { "de", new CapacityNotificationTranslation(
                        "Zu wenige Transportmitarbeiter!",
                        "In {1} beklagen sich die Laborspezialisten über zu wenige Transportmitarbeiter und müssen zu oft selbst Proben transportieren. Weisen Sie mehr Transportmitarbeiter dem Probentransport zu.",
                        "In {1} beklagen sich die Pflegekräfte über zu wenige Transportmitarbeiter und müssen zu oft selbst Patienten transportieren. Weisen Sie mehr Transportmitarbeiter dem Patiententransport zu.") },
                    { "es", new CapacityNotificationTranslation(
                        "¡Faltan camilleros!",
                        "En {1}, los especialistas de laboratorio se quejan de la falta de camilleros y con demasiada frecuencia tienen que transportar ellos mismos las muestras. Asigna más camilleros al transporte de muestras.",
                        "En {1}, el personal de enfermería se queja de la falta de camilleros y con demasiada frecuencia tiene que transportar pacientes. Asigna más camilleros al transporte de pacientes.") },
                    { "esla", new CapacityNotificationTranslation(
                        "¡Faltan camilleros!",
                        "En {1}, los especialistas de laboratorio se quejan de la falta de camilleros y con demasiada frecuencia tienen que transportar ellos mismos las muestras. Asigna más camilleros al transporte de muestras.",
                        "En {1}, el personal de enfermería se queja de la falta de camilleros y con demasiada frecuencia tiene que transportar pacientes. Asigna más camilleros al transporte de pacientes.") },
                    { "fr", new CapacityNotificationTranslation(
                        "Manque de brancardiers !",
                        "À {1}, les techniciens de laboratoire se plaignent du manque de brancardiers et doivent trop souvent transporter eux-mêmes les échantillons. Affectez davantage de brancardiers au transport d'échantillons.",
                        "À {1}, les infirmières se plaignent du manque de brancardiers et doivent trop souvent transporter elles-mêmes les patients. Affectez davantage de brancardiers au transport des patients.") },
                    { "hu", new CapacityNotificationTranslation(
                        "Nincs elég betegszállító!",
                        "A(z) {1} részlegen a laboratóriumi szakemberek arra panaszkodnak, hogy nincs elég betegszállító, ezért túl gyakran nekik kell szállítaniuk a mintákat. Rendelj több betegszállítót a mintaszállításhoz.",
                        "A(z) {1} részlegen az ápolók arra panaszkodnak, hogy nincs elég betegszállító, ezért túl gyakran nekik kell szállítaniuk a betegeket. Rendelj több betegszállítót a betegszállításhoz.") },
                    { "it", new CapacityNotificationTranslation(
                        "Barellieri insufficienti!",
                        "In {1}, gli specialisti di laboratorio lamentano la carenza di barellieri e troppo spesso devono trasportare personalmente i campioni. Assegna più barellieri al trasporto dei campioni.",
                        "In {1}, gli infermieri lamentano la carenza di barellieri e troppo spesso devono trasportare personalmente i pazienti. Assegna più barellieri al trasporto dei pazienti.") },
                    { "jp", new CapacityNotificationTranslation(
                        "院内搬送員が不足しています！",
                        "「{1}」では、臨床検査技師が院内搬送員の不足を訴えており、検体を自分たちで運ぶことが多すぎます。検体搬送に割り当てる院内搬送員を増やしてください。",
                        "「{1}」では、看護師が院内搬送員の不足を訴えており、患者を自分たちで搬送することが多すぎます。患者搬送に割り当てる院内搬送員を増やしてください。") },
                    { "kr", new CapacityNotificationTranslation(
                        "원내 이송 요원이 부족합니다!",
                        "{1}에서 검사실 직원들이 원내 이송 요원 부족을 호소하며 너무 자주 직접 검체를 운반하고 있습니다. 검체 이송 업무에 더 많은 원내 이송 요원을 배정하세요.",
                        "{1}에서 간호사들이 원내 이송 요원 부족을 호소하며 너무 자주 직접 환자를 이송하고 있습니다. 환자 이송 업무에 더 많은 원내 이송 요원을 배정하세요.") },
                    { "nl", new CapacityNotificationTranslation(
                        "Te weinig patiëntenvervoerders!",
                        "Bij {1} klaagt het laboratoriumpersoneel over een tekort aan patiëntenvervoerders en moet het te vaak zelf monsters vervoeren. Wijs meer patiëntenvervoerders toe aan monstertransport.",
                        "Bij {1} klagen de verpleegkundigen over een tekort aan patiëntenvervoerders en moeten zij te vaak zelf patiënten vervoeren. Wijs meer patiëntenvervoerders toe aan patiëntentransport.") },
                    { "pl", new CapacityNotificationTranslation(
                        "Za mało pracowników transportu!",
                        "W {1} pracownicy laboratorium skarżą się na brak pracowników transportu i zbyt często muszą samodzielnie przewozić próbki. Przydziel więcej pracowników transportu do transportu próbek.",
                        "W {1} pielęgniarki skarżą się na brak pracowników transportu i zbyt często muszą samodzielnie przewozić pacjentów. Przydziel więcej pracowników transportu do transportu pacjentów.") },
                    { "ptbr", new CapacityNotificationTranslation(
                        "Maqueiros insuficientes!",
                        "Em {1}, os especialistas de laboratório reclamam da falta de maqueiros e precisam transportar as amostras por conta própria com frequência demais. Designe mais maqueiros para o transporte de amostras.",
                        "Em {1}, os enfermeiros reclamam da falta de maqueiros e precisam transportar os pacientes por conta própria com frequência demais. Designe mais maqueiros para o transporte de pacientes.") },
                    { "ru", new CapacityNotificationTranslation(
                        "Не хватает санитаров-транспортировщиков!",
                        "В {1} сотрудники лаборатории жалуются на нехватку санитаров-транспортировщиков и слишком часто вынуждены сами перевозить образцы. Назначьте больше санитаров-транспортировщиков на перевозку образцов.",
                        "В {1} медсёстры жалуются на нехватку санитаров-транспортировщиков и слишком часто вынуждены сами перевозить пациентов. Назначьте больше санитаров-транспортировщиков на перевозку пациентов.") },
                    { "swe", new CapacityNotificationTranslation(
                        "För få patienttransportörer!",
                        "På {1} klagar laboratoriepersonalen på bristen på patienttransportörer och måste alltför ofta transportera prover själva. Tilldela fler patienttransportörer till provtransport.",
                        "På {1} klagar sjuksköterskorna på bristen på patienttransportörer och måste alltför ofta transportera patienter själva. Tilldela fler patienttransportörer till patienttransport.") },
                    { "tr", new CapacityNotificationTranslation(
                        "Yeterli hasta taşıma görevlisi yok!",
                        "{1} bölümünde laboratuvar uzmanları hasta taşıma görevlisi eksikliğinden şikâyet ediyor ve numuneleri çok sık kendileri taşımak zorunda kalıyor. Numune taşıma görevine daha fazla hasta taşıma görevlisi atayın.",
                        "{1} bölümünde hemşireler hasta taşıma görevlisi eksikliğinden şikâyet ediyor ve hastaları çok sık kendileri taşımak zorunda kalıyor. Hasta taşıma görevine daha fazla hasta taşıma görevlisi atayın.") },
                    { "uk", new CapacityNotificationTranslation(
                        "Не вистачає санітарів-транспортувальників!",
                        "У {1} працівники лабораторії скаржаться на нестачу санітарів-транспортувальників і надто часто змушені самі перевозити зразки. Призначте більше санітарів-транспортувальників на транспортування зразків.",
                        "У {1} медсестри скаржаться на нестачу санітарів-транспортувальників і надто часто змушені самі перевозити пацієнтів. Призначте більше санітарів-транспортувальників на транспортування пацієнтів.") },
                    { "zhcn", new CapacityNotificationTranslation(
                        "院内运送员不足！",
                        "在 {1}，检验科人员抱怨院内运送员不足，经常不得不亲自运送样本。请为样本运送分配更多院内运送员。",
                        "在 {1}，护士抱怨院内运送员不足，经常不得不亲自运送患者。请为患者运送分配更多院内运送员。") },
                    { "zhtw", new CapacityNotificationTranslation(
                        "院內運送員不足！",
                        "在 {1}，檢驗室人員抱怨院內運送員不足，經常不得不親自運送檢體。請為檢體運送分配更多院內運送員。",
                        "在 {1}，護理人員抱怨院內運送員不足，經常不得不親自運送病患。請為病患運送分配更多院內運送員。") }
                };

        private static readonly Dictionary<string, Translation> Translations =
            new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", English },
                { "cz", new Translation(
                    "Sanitář",
                    "Sanitáři",
                    "Personál vnitřní logistiky určený k přepravě pacientů a vzorků. Před přijetím nového sanitáře je nutná platná stanice sanitářů.",
                    "Personál vnitřní logistiky určený k přepravě pacientů a vzorků. Sanitáři potřebují kvalifikaci sanitáře a přiřazenou stanici sanitářů.",
                    "Zaměstnanci - Sanitáři",
                    "Začínající sanitář",
                    "Zkušený sanitář",
                    "Seniorní sanitář",
                    "Sanitář",
                    "Kvalifikace sanitáře. Představuje výcvik a zkušenosti s vnitřní nemocniční přepravou.",
                    "Stanice sanitářů",
                    "Stanice sanitářů",
                    "Přeprava pacientů",
                    "Přeprava vzorků",
                    "Převáží hospitalizované pacienty na nosítkách nebo invalidním vozíku.",
                    "Přepravuje odebrané vzorky do laboratoře.",
                    "Vozík na vzorky",
                    "Pojízdný vozík pro přepravu odebraných laboratorních vzorků.",
                    "Nedostatek vozíků na vzorky!",
                    "Sanitáři si stěžují, že v {1} musí přenášet vzorky po jednom, protože není k dispozici žádný vozík na vzorky.") },
                { "da", new Translation(
                    "Portør",
                    "Portører",
                    "Internt logistikpersonale til transport af patienter og prøver. En gyldig portørstation er nødvendig, før en ny portør kan ansættes.",
                    "Internt logistikpersonale til transport af patienter og prøver. Portører kræver portørkvalifikationen og en tildelt portørstation.",
                    "Medarbejdere - Portører",
                    "Juniorportør",
                    "Erfaren portør",
                    "Seniorportør",
                    "Portør",
                    "Portørkvalifikation. Repræsenterer træning og erfaring med intern hospitalstransport.",
                    "Portørstation",
                    "Portørstation",
                    "Patienttransport",
                    "Prøvetransport",
                    "Transporterer indlagte patienter med båre eller kørestol.",
                    "Transporterer indsamlede prøver til laboratoriet.",
                    "Prøvevogn",
                    "Mobil vogn til transport af indsamlede laboratorieprøver.",
                    "Ikke nok prøvevogne!",
                    "Portørerne klager over, at de må transportere prøver én ad gangen i {1}, fordi der ikke er nogen prøvevogn ledig.") },
                { "de", new Translation(
                    "Transportmitarbeiter",
                    "Transportmitarbeiter",
                    "Mitarbeiter der internen Logistik für Patienten- und Probentransporte. Vor der Einstellung eines neuen Transportmitarbeiters ist eine gültige Transportstation erforderlich.",
                    "Mitarbeiter der internen Logistik für Patienten- und Probentransporte. Transportmitarbeiter benötigen die Transportqualifikation und eine zugewiesene Transportstation.",
                    "Mitarbeiter - Transportdienst",
                    "Transportmitarbeiter in Ausbildung",
                    "Erfahrener Transportmitarbeiter",
                    "Leitender Transportmitarbeiter",
                    "Transportdienst",
                    "Qualifikation für den Transportdienst. Sie steht für Ausbildung und Erfahrung im internen Krankenhaus-Transport.",
                    "Transportstation",
                    "Transportstation",
                    "Patiententransport",
                    "Probentransport",
                    "Transportiert stationäre Patienten mit Trage oder Rollstuhl.",
                    "Bringt entnommene Proben ins Labor.",
                    "Probenwagen",
                    "Mobiler Wagen zum Transport entnommener Laborproben.",
                    "Nicht genügend Probenwagen!",
                    "Die Transportmitarbeiter beschweren sich, dass sie die Proben in {1} einzeln transportieren müssen, weil kein Probenwagen verfügbar ist.") },
                { "es", new Translation(
                    "Camillero",
                    "Camilleros",
                    "Personal de logística interna dedicado al traslado de pacientes y muestras. Se necesita una estación de camilleros válida antes de contratar a un nuevo camillero.",
                    "Personal de logística interna dedicado al traslado de pacientes y muestras. Los camilleros necesitan la cualificación de camillero y una estación de camilleros asignada.",
                    "Empleados - Camilleros",
                    "Camillero principiante",
                    "Camillero experimentado",
                    "Camillero sénior",
                    "Camillero",
                    "Cualificación de camillero. Representa la formación y la experiencia en transporte interno hospitalario.",
                    "Estación de camilleros",
                    "Estación de camilleros",
                    "Traslado de pacientes",
                    "Transporte de muestras",
                    "Traslada a pacientes hospitalizados en camilla o silla de ruedas.",
                    "Lleva las muestras recogidas al laboratorio.",
                    "Carro de muestras",
                    "Carro móvil para transportar muestras de laboratorio recogidas.",
                    "¡No hay suficientes carros de muestras!",
                    "Los camilleros se quejan de tener que transportar las muestras de una en una en {1} porque no hay ningún carro de muestras disponible.") },
                { "esla", new Translation(
                    "Camillero",
                    "Camilleros",
                    "Personal de logística interna dedicado al traslado de pacientes y muestras. Se requiere una estación de camilleros válida antes de contratar a un nuevo camillero.",
                    "Personal de logística interna dedicado al traslado de pacientes y muestras. Los camilleros requieren la calificación de camillero y una estación de camilleros asignada.",
                    "Empleados - Camilleros",
                    "Camillero junior",
                    "Camillero experimentado",
                    "Camillero sénior",
                    "Camillero",
                    "Calificación de camillero. Representa la capacitación y experiencia en transporte interno hospitalario.",
                    "Estación de camilleros",
                    "Estación de camilleros",
                    "Traslado de pacientes",
                    "Transporte de muestras",
                    "Traslada pacientes hospitalizados en camilla o silla de ruedas.",
                    "Lleva las muestras recolectadas al laboratorio.",
                    "Carro de muestras",
                    "Carro móvil para transportar muestras de laboratorio recolectadas.",
                    "¡No hay suficientes carros de muestras!",
                    "Los camilleros se quejan de tener que transportar las muestras una por una en {1} porque no hay ningún carro de muestras disponible.") },
                { "fr", new Translation(
                    "Brancardier",
                    "Brancardiers",
                    "Personnel dédié à la logistique interne pour le transport des patients et des échantillons. Un poste des brancardiers valide est requis avant toute nouvelle embauche.",
                    "Personnel dédié à la logistique interne pour le transport des patients et des échantillons. Les brancardiers requièrent la qualification de brancardier et un poste des brancardiers assigné.",
                    "Employés - Brancardiers",
                    "Brancardier débutant",
                    "Brancardier confirmé",
                    "Brancardier senior",
                    "Brancardier",
                    "Qualification de brancardier. Représente la formation et l'expérience en transport interne hospitalier.",
                    "Poste des brancardiers",
                    "Poste des brancardiers",
                    "Transport de patients",
                    "Transport d'échantillons",
                    "Transporte les patients hospitalisés en brancard ou en fauteuil roulant.",
                    "Transporte les échantillons prélevés jusqu'au laboratoire.",
                    "Chariot à échantillons",
                    "Chariot mobile pour le transport des échantillons prélevés jusqu'au laboratoire.",
                    "Davantage de chariots à échantillons recommandés !",
                    "Les brancardiers se plaignent de devoir transporter les échantillons un par un en {1}, faute de chariot à échantillons disponible.") },
                { "hu", new Translation(
                    "Betegszállító",
                    "Betegszállítók",
                    "Belső logisztikai személyzet betegek és minták szállítására. Új betegszállító felvétele előtt érvényes betegszállító-állomás szükséges.",
                    "Belső logisztikai személyzet betegek és minták szállítására. A betegszállítóknak betegszállítói képesítésre és kijelölt betegszállító-állomásra van szükségük.",
                    "Alkalmazottak - Betegszállítók",
                    "Kezdő betegszállító",
                    "Tapasztalt betegszállító",
                    "Senior betegszállító",
                    "Betegszállító",
                    "Betegszállítói képesítés. A kórházon belüli szállítással kapcsolatos képzést és tapasztalatot jelzi.",
                    "Betegszállító-állomás",
                    "Betegszállító-állomás",
                    "Betegszállítás",
                    "Mintaszállítás",
                    "Hordágyon vagy kerekesszékben szállítja a kórházi betegeket.",
                    "A levett mintákat a laboratóriumba szállítja.",
                    "Mintaszállító kocsi",
                    "Mozgatható kocsi a levett laboratóriumi minták szállításához.",
                    "Nincs elég mintaszállító kocsi!",
                    "A betegszállítók arra panaszkodnak, hogy {1} területén egyenként kell szállítaniuk a mintákat, mert nincs szabad mintaszállító kocsi.") },
                { "it", new Translation(
                    "Barelliere",
                    "Barellieri",
                    "Personale di logistica interna dedicato al trasporto di pazienti e campioni. Prima di assumere un nuovo barelliere è necessaria una postazione barellieri valida.",
                    "Personale di logistica interna dedicato al trasporto di pazienti e campioni. I barellieri richiedono la qualifica da barelliere e una postazione barellieri assegnata.",
                    "Dipendenti - Barellieri",
                    "Barelliere junior",
                    "Barelliere esperto",
                    "Barelliere senior",
                    "Barelliere",
                    "Qualifica da barelliere. Rappresenta formazione ed esperienza nel trasporto interno ospedaliero.",
                    "Postazione barellieri",
                    "Postazione barellieri",
                    "Trasporto pazienti",
                    "Trasporto campioni",
                    "Trasporta i pazienti ricoverati in barella o sedia a rotelle.",
                    "Porta i campioni raccolti al laboratorio.",
                    "Carrello campioni",
                    "Carrello mobile per il trasporto dei campioni di laboratorio raccolti.",
                    "Carrelli campioni insufficienti!",
                    "I barellieri si lamentano di dover trasportare i campioni uno alla volta in {1} perché non è disponibile alcun carrello campioni.") },
                { "jp", new Translation(
                    "院内搬送員",
                    "院内搬送員",
                    "患者と検体の搬送を担当する院内物流スタッフです。新しい搬送員を雇用するには、有効な搬送員ステーションが必要です。",
                    "患者と検体の搬送を担当する院内物流スタッフです。搬送員には搬送員資格と割り当てられた搬送員ステーションが必要です。",
                    "従業員 - 院内搬送員",
                    "新人搬送員",
                    "熟練搬送員",
                    "主任搬送員",
                    "院内搬送員",
                    "院内搬送員の資格です。院内搬送に関する訓練と経験を表します。",
                    "搬送員ステーション",
                    "搬送員ステーション",
                    "患者搬送",
                    "検体搬送",
                    "入院患者をストレッチャーまたは車椅子で搬送します。",
                    "採取済みの検体を検査室へ運びます。",
                    "検体カート",
                    "採取済みの検査検体を運ぶための移動式カートです。",
                    "検体カートが不足しています！",
                    "{1} では検体カートが空いていないため、搬送員が検体を1つずつ運ばなければならないと不満を訴えています。") },
                { "kr", new Translation(
                    "원내 이송 요원",
                    "원내 이송 요원",
                    "환자와 검체 이송을 담당하는 원내 물류 직원입니다. 새 이송 요원을 고용하려면 유효한 이송 요원 스테이션이 필요합니다.",
                    "환자와 검체 이송을 담당하는 원내 물류 직원입니다. 이송 요원은 이송 요원 자격과 배정된 이송 요원 스테이션이 필요합니다.",
                    "직원 - 원내 이송 요원",
                    "초급 이송 요원",
                    "숙련 이송 요원",
                    "선임 이송 요원",
                    "원내 이송 요원",
                    "원내 이송 요원 자격입니다. 병원 내부 이송에 대한 교육과 경험을 나타냅니다.",
                    "이송 요원 스테이션",
                    "이송 요원 스테이션",
                    "환자 이송",
                    "검체 이송",
                    "입원 환자를 들것이나 휠체어로 이송합니다.",
                    "채취된 검체를 검사실로 운반합니다.",
                    "검체 카트",
                    "채취된 검사실 검체를 운반하는 이동식 카트입니다.",
                    "검체 카트가 부족합니다!",
                    "{1}에서 사용할 수 있는 검체 카트가 없어 이송 요원들이 검체를 하나씩 옮겨야 한다고 불평하고 있습니다.") },
                { "nl", new Translation(
                    "Patiëntenvervoerder",
                    "Patiëntenvervoerders",
                    "Intern logistiek personeel voor het vervoer van patiënten en monsters. Voordat een nieuwe patiëntenvervoerder kan worden aangenomen, is een geldig transportstation vereist.",
                    "Intern logistiek personeel voor het vervoer van patiënten en monsters. Patiëntenvervoerders hebben de transportkwalificatie en een toegewezen transportstation nodig.",
                    "Medewerkers - Patiëntenvervoerders",
                    "Junior patiëntenvervoerder",
                    "Ervaren patiëntenvervoerder",
                    "Senior patiëntenvervoerder",
                    "Patiëntenvervoerder",
                    "Transportkwalificatie. Staat voor opleiding en ervaring met intern ziekenhuisvervoer.",
                    "Transportstation",
                    "Transportstation",
                    "Patiëntenvervoer",
                    "Monstertransport",
                    "Vervoert opgenomen patiënten per brancard of rolstoel.",
                    "Brengt verzamelde monsters naar het laboratorium.",
                    "Monsterkar",
                    "Mobiele kar voor het vervoeren van verzamelde laboratoriummonsters.",
                    "Niet genoeg monsterkarren!",
                    "Patiëntenvervoerders klagen dat ze in {1} monsters één voor één moeten vervoeren omdat er geen monsterkar beschikbaar is.") },
                { "pl", new Translation(
                    "Pracownik transportu",
                    "Pracownicy transportu",
                    "Personel logistyki wewnętrznej przeznaczony do transportu pacjentów i próbek. Przed zatrudnieniem nowego pracownika transportu wymagane jest prawidłowe stanowisko transportowe.",
                    "Personel logistyki wewnętrznej przeznaczony do transportu pacjentów i próbek. Pracownicy transportu wymagają kwalifikacji transportowej i przypisanego stanowiska transportowego.",
                    "Pracownicy - Transport",
                    "Początkujący pracownik transportu",
                    "Doświadczony pracownik transportu",
                    "Starszy pracownik transportu",
                    "Pracownik transportu",
                    "Kwalifikacja transportowa. Oznacza szkolenie i doświadczenie w transporcie wewnątrzszpitalnym.",
                    "Stanowisko transportowe",
                    "Stanowisko transportowe",
                    "Transport pacjentów",
                    "Transport próbek",
                    "Transportuje hospitalizowanych pacjentów na noszach lub wózku inwalidzkim.",
                    "Dostarcza pobrane próbki do laboratorium.",
                    "Wózek na próbki",
                    "Mobilny wózek do transportu pobranych próbek laboratoryjnych.",
                    "Za mało wózków na próbki!",
                    "Pracownicy transportu skarżą się, że w {1} muszą przenosić próbki pojedynczo, ponieważ żaden wózek na próbki nie jest dostępny.") },
                { "ptbr", new Translation(
                    "Maqueiro",
                    "Maqueiros",
                    "Equipe de logística interna dedicada ao transporte de pacientes e amostras. É necessária uma estação de maqueiros válida antes de contratar um novo maqueiro.",
                    "Equipe de logística interna dedicada ao transporte de pacientes e amostras. Os maqueiros precisam da qualificação de maqueiro e de uma estação de maqueiros atribuída.",
                    "Funcionários - Maqueiros",
                    "Maqueiro júnior",
                    "Maqueiro experiente",
                    "Maqueiro sênior",
                    "Maqueiro",
                    "Qualificação de maqueiro. Representa treinamento e experiência em transporte interno hospitalar.",
                    "Estação de maqueiros",
                    "Estação de maqueiros",
                    "Transporte de pacientes",
                    "Transporte de amostras",
                    "Transporta pacientes internados em maca ou cadeira de rodas.",
                    "Leva as amostras coletadas ao laboratório.",
                    "Carrinho de amostras",
                    "Carrinho móvel para transportar amostras laboratoriais coletadas.",
                    "Carrinhos de amostras insuficientes!",
                    "Os maqueiros reclamam que precisam transportar as amostras uma por uma em {1}, porque não há nenhum carrinho de amostras disponível.") },
                { "ru", new Translation(
                    "Санитар-транспортировщик",
                    "Санитары-транспортировщики",
                    "Сотрудники внутренней логистики для перевозки пациентов и образцов. Перед наймом нового санитара-транспортировщика требуется действующая станция транспортировщиков.",
                    "Сотрудники внутренней логистики для перевозки пациентов и образцов. Санитарам-транспортировщикам нужна квалификация транспортировщика и назначенная станция.",
                    "Сотрудники - Транспортировщики",
                    "Младший транспортировщик",
                    "Опытный транспортировщик",
                    "Старший транспортировщик",
                    "Транспортировщик",
                    "Квалификация транспортировщика. Отражает обучение и опыт внутренней перевозки в больнице.",
                    "Станция транспортировщиков",
                    "Станция транспортировщиков",
                    "Перевозка пациентов",
                    "Перевозка образцов",
                    "Перевозит госпитализированных пациентов на каталке или в инвалидном кресле.",
                    "Доставляет собранные образцы в лабораторию.",
                    "Тележка для образцов",
                    "Передвижная тележка для перевозки собранных лабораторных образцов.",
                    "Недостаточно тележек для образцов!",
                    "Транспортировщики жалуются, что в {1} им приходится переносить образцы по одному, потому что нет свободной тележки для образцов.") },
                { "swe", new Translation(
                    "Patienttransportör",
                    "Patienttransportörer",
                    "Intern logistikpersonal för transport av patienter och prover. En giltig transportstation krävs innan en ny patienttransportör kan anställas.",
                    "Intern logistikpersonal för transport av patienter och prover. Patienttransportörer kräver transportkvalifikationen och en tilldelad transportstation.",
                    "Anställda - Patienttransportörer",
                    "Junior patienttransportör",
                    "Erfaren patienttransportör",
                    "Senior patienttransportör",
                    "Patienttransportör",
                    "Transportkvalifikation. Representerar utbildning och erfarenhet av intern sjukhustransport.",
                    "Transportstation",
                    "Transportstation",
                    "Patienttransport",
                    "Provtransport",
                    "Transporterar inlagda patienter med bår eller rullstol.",
                    "Transporterar insamlade prover till laboratoriet.",
                    "Provvagn",
                    "Mobil vagn för transport av insamlade laboratorieprover.",
                    "Inte tillräckligt med provvagnar!",
                    "Patienttransportörerna klagar på att de måste transportera prover ett i taget i {1} eftersom ingen provvagn är ledig.") },
                { "tr", new Translation(
                    "Hasta taşıma görevlisi",
                    "Hasta taşıma görevlileri",
                    "Hasta ve numune taşımaya ayrılmış dahili lojistik personeli. Yeni bir taşıma görevlisi işe alınmadan önce geçerli bir taşıma istasyonu gerekir.",
                    "Hasta ve numune taşımaya ayrılmış dahili lojistik personeli. Taşıma görevlileri taşıma yeterliliğine ve atanmış bir taşıma istasyonuna ihtiyaç duyar.",
                    "Çalışanlar - Taşıma görevlileri",
                    "Yeni taşıma görevlisi",
                    "Deneyimli taşıma görevlisi",
                    "Kıdemli taşıma görevlisi",
                    "Hasta taşıma görevlisi",
                    "Taşıma yeterliliği. Hastane içi taşıma eğitimini ve deneyimini temsil eder.",
                    "Taşıma istasyonu",
                    "Taşıma istasyonu",
                    "Hasta taşıma",
                    "Numune taşıma",
                    "Yatan hastaları sedye veya tekerlekli sandalye ile taşır.",
                    "Toplanan numuneleri laboratuvara taşır.",
                    "Numune arabası",
                    "Toplanan laboratuvar numunelerini taşımak için kullanılan hareketli araba.",
                    "Yeterli numune arabası yok!",
                    "Taşıma görevlileri, kullanılabilir numune arabası olmadığı için {1} konumunda numuneleri tek tek taşımak zorunda kalmaktan şikâyet ediyor.") },
                { "uk", new Translation(
                    "Санітар-транспортувальник",
                    "Санітари-транспортувальники",
                    "Працівники внутрішньої логістики для перевезення пацієнтів і зразків. Перед наймом нового санітара-транспортувальника потрібна дійсна станція транспортувальників.",
                    "Працівники внутрішньої логістики для перевезення пацієнтів і зразків. Транспортувальникам потрібні відповідна кваліфікація та призначена станція.",
                    "Працівники - Транспортувальники",
                    "Молодший транспортувальник",
                    "Досвідчений транспортувальник",
                    "Старший транспортувальник",
                    "Транспортувальник",
                    "Кваліфікація транспортувальника. Відображає навчання та досвід внутрішньолікарняного транспортування.",
                    "Станція транспортувальників",
                    "Станція транспортувальників",
                    "Транспортування пацієнтів",
                    "Транспортування зразків",
                    "Перевозить госпіталізованих пацієнтів на каталці або у кріслі колісному.",
                    "Доставляє зібрані зразки до лабораторії.",
                    "Візок для зразків",
                    "Пересувний візок для транспортування зібраних лабораторних зразків.",
                    "Недостатньо візків для зразків!",
                    "Транспортувальники скаржаться, що в {1} їм доводиться переносити зразки по одному, оскільки немає вільного візка для зразків.") },
                { "zhcn", new Translation(
                    "院内运送员",
                    "院内运送员",
                    "负责患者和样本运送的院内物流人员。雇用新的运送员之前，必须有一个有效的运送员工作站。",
                    "负责患者和样本运送的院内物流人员。运送员需要运送员资质以及已分配的运送员工作站。",
                    "员工 - 院内运送员",
                    "初级运送员",
                    "熟练运送员",
                    "高级运送员",
                    "院内运送员",
                    "运送员资质。代表院内运输方面的培训和经验。",
                    "运送员工作站",
                    "运送员工作站",
                    "患者运送",
                    "样本运送",
                    "使用担架或轮椅运送住院患者。",
                    "将采集的样本送往实验室。",
                    "样本推车",
                    "用于运送已采集实验室样本的移动推车。",
                    "样本推车不足！",
                    "运送员抱怨在 {1} 没有可用的样本推车，只能逐个运送样本。") },
                { "zhtw", new Translation(
                    "院內運送員",
                    "院內運送員",
                    "負責病患與檢體運送的院內物流人員。雇用新的運送員前，必須有有效的運送員工作站。",
                    "負責病患與檢體運送的院內物流人員。運送員需要運送員資格以及已指派的運送員工作站。",
                    "員工 - 院內運送員",
                    "初級運送員",
                    "熟練運送員",
                    "資深運送員",
                    "院內運送員",
                    "運送員資格。代表院內運送方面的訓練與經驗。",
                    "運送員工作站",
                    "運送員工作站",
                    "病患運送",
                    "檢體運送",
                    "使用擔架或輪椅運送住院病患。",
                    "將採集的檢體送往實驗室。",
                    "檢體推車",
                    "用於運送已採集實驗室檢體的移動推車。",
                    "檢體推車不足！",
                    "運送員抱怨在 {1} 沒有可用的檢體推車，只能逐一運送檢體。") }
            };

        internal static string Get(string stringId)
        {
            string language = StringTable.GetInstance()?.GetCurrentLanguage();
            if (string.IsNullOrEmpty(language) &&
                PlayerProfile.Instance != null)
            {
                language = PlayerProfile.Instance.GetCurrentLanguage();
            }

            string result;
            return TryGetLocalizedText(language, stringId, null, out result)
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
            if (string.IsNullOrEmpty(stringId))
            {
                return false;
            }

            string normalizedId = PorterLocalizationIds.Normalize(stringId);
            Translation translation;
            string normalizedLanguage = NormalizeLanguageCode(languageCode);
            if (string.IsNullOrEmpty(normalizedLanguage) ||
                !Translations.TryGetValue(normalizedLanguage, out translation))
            {
                translation = English;
            }

            if (normalizedId == PorterLocalizationIds.SamplePorterCapacityNotification ||
                normalizedId == PorterLocalizationIds.SamplePorterCapacityNotificationText ||
                normalizedId == PorterLocalizationIds.PatientPorterCapacityNotification ||
                normalizedId == PorterLocalizationIds.PatientPorterCapacityNotificationText)
            {
                CapacityNotificationTranslation capacity;
                if (!CapacityNotifications.TryGetValue(
                        normalizedLanguage ?? string.Empty,
                        out capacity))
                {
                    capacity = CapacityNotifications["en"];
                }

                if (normalizedId == PorterLocalizationIds.SamplePorterCapacityNotification)
                {
                    result = capacity.Title;
                }
                else if (normalizedId == PorterLocalizationIds.SamplePorterCapacityNotificationText)
                {
                    result = capacity.SampleText;
                }
                else if (normalizedId == PorterLocalizationIds.PatientPorterCapacityNotification)
                {
                    result = capacity.Title;
                }
                else
                {
                    result = capacity.PatientText;
                }

                ApplyParameters(ref result, parameters);
                return true;
            }

            if (normalizedId == PorterLocalizationIds.Occupation) result = translation.Occupation;
            else if (normalizedId == PorterLocalizationIds.PorterCandidates) result = translation.Candidates;
            else if (normalizedId == PorterLocalizationIds.PorterTooltip) result = translation.PorterTooltip;
            else if (normalizedId == PorterLocalizationIds.PorterStaffingTooltip) result = translation.StaffingTooltip;
            else if (normalizedId == PorterLocalizationIds.EmployeesHeading) result = translation.EmployeesHeading;
            else if (normalizedId == PorterLocalizationIds.PorterLevel1) result = translation.Level1;
            else if (normalizedId == PorterLocalizationIds.PorterLevel2) result = translation.Level2;
            else if (normalizedId == PorterLocalizationIds.PorterLevel3) result = translation.Level3;
            else if (normalizedId == PorterLocalizationIds.PorterQualification) result = translation.Qualification;
            else if (normalizedId == PorterLocalizationIds.PorterQualificationDescription) result = translation.QualificationDescription;
            else if (normalizedId == PorterLocalizationIds.PorterStationRoom) result = translation.Station;
            else if (normalizedId == PorterLocalizationIds.PorterStationRoomDescription) result = translation.StationDescription;
            else if (normalizedId == PorterLocalizationIds.PatientTransportRole) result = translation.PatientTransport;
            else if (normalizedId == PorterLocalizationIds.SampleTransportRole) result = translation.SampleTransport;
            else if (normalizedId == PorterLocalizationIds.PatientTransportRoleDescription) result = translation.PatientTransportDescription;
            else if (normalizedId == PorterLocalizationIds.SampleTransportRoleDescription) result = translation.SampleTransportDescription;
            else if (normalizedId == PorterLocalizationIds.SampleCart) result = translation.SampleCart;
            else if (normalizedId == PorterLocalizationIds.SampleCartDescription) result = translation.SampleCartDescription;
            else if (normalizedId == PorterLocalizationIds.SampleCartNotification) result = translation.NotificationTitle;
            else if (normalizedId == PorterLocalizationIds.SampleCartNotificationText) result = translation.NotificationText;
            else return false;

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
}
