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

        private static readonly Translation English = new Translation(
            "Porter",
            "Porters",
            "Dedicated internal logistics staff for patient and sample transport. A valid Porter station with a free locker for the selected shift is required before hiring a new porter.",
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
            "Not enough sample carts!",
            "Porters are waiting for a sample cart in {1} because all carts assigned to their Porter station are already in use. Add another sample cart to increase transport capacity.");

        private static readonly Dictionary<string, Translation> Translations =
            new Dictionary<string, Translation>(StringComparer.OrdinalIgnoreCase)
            {
                { "en", English },
                { "cz", new Translation(
                    "Sanitář",
                    "Sanitáři",
                    "Personál vnitřní logistiky určený k přepravě pacientů a vzorků. Před přijetím nového sanitáře je nutná platná stanice sanitářů s volnou skříňkou pro zvolenou směnu.",
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
                    "Sanitáři čekají na vozík na vzorky v {1}, protože všechny vozíky přiřazené k jejich stanici jsou právě používány. Přidejte další vozík na vzorky pro zvýšení přepravní kapacity.") },
                { "da", new Translation(
                    "Portør",
                    "Portører",
                    "Internt logistikpersonale til transport af patienter og prøver. En gyldig portørstation med et ledigt skab til den valgte vagt er nødvendig, før en ny portør kan ansættes.",
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
                    "Portører venter på en prøvevogn i {1}, fordi alle vogne tilknyttet deres portørstation allerede er i brug. Tilføj en ekstra prøvevogn for at øge transportkapaciteten.") },
                { "de", new Translation(
                    "Transportmitarbeiter",
                    "Transportmitarbeiter",
                    "Mitarbeiter der internen Logistik für Patienten- und Probentransporte. Vor der Einstellung eines neuen Transportmitarbeiters ist eine gültige Transportstation mit einem freien Spind für die ausgewählte Schicht erforderlich.",
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
                    "Transportmitarbeiter warten in {1} auf einen Probenwagen, weil alle ihrer Transportstation zugewiesenen Wagen bereits verwendet werden. Fügen Sie einen weiteren Probenwagen hinzu, um die Transportkapazität zu erhöhen.") },
                { "es", new Translation(
                    "Camillero",
                    "Camilleros",
                    "Personal de logística interna dedicado al traslado de pacientes y muestras. Se necesita una estación de camilleros válida con una taquilla libre para el turno seleccionado antes de contratar a un nuevo camillero.",
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
                    "Los camilleros esperan un carro de muestras en {1} porque todos los carros asignados a su estación ya están en uso. Añade otro carro de muestras para aumentar la capacidad de transporte.") },
                { "esla", new Translation(
                    "Camillero",
                    "Camilleros",
                    "Personal de logística interna dedicado al traslado de pacientes y muestras. Se requiere una estación de camilleros válida con un casillero libre para el turno seleccionado antes de contratar a un nuevo camillero.",
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
                    "Los camilleros esperan un carro de muestras en {1} porque todos los carros asignados a su estación ya están en uso. Agrega otro carro de muestras para aumentar la capacidad de transporte.") },
                { "fr", new Translation(
                    "Brancardier",
                    "Brancardiers",
                    "Personnel dédié à la logistique interne pour le transport des patients et des échantillons. Un poste des brancardiers valide avec un casier libre pour le quart sélectionné est requis avant toute nouvelle embauche.",
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
                    "Nombre de chariots à échantillons insuffisant !",
                    "Les brancardiers attendent un chariot à échantillons en {1} car tous les chariots de leur poste sont déjà utilisés. Ajoutez un chariot supplémentaire pour augmenter la capacité de transport.") },
                { "hu", new Translation(
                    "Betegszállító",
                    "Betegszállítók",
                    "Belső logisztikai személyzet betegek és minták szállítására. Új betegszállító felvétele előtt érvényes betegszállító-állomás és a kiválasztott műszakhoz egy szabad szekrény szükséges.",
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
                    "A betegszállítók mintaszállító kocsira várnak itt: {1}, mert az állomásukhoz rendelt összes kocsi használatban van. A szállítási kapacitás növeléséhez helyezzen el még egy mintaszállító kocsit.") },
                { "it", new Translation(
                    "Barelliere",
                    "Barellieri",
                    "Personale di logistica interna dedicato al trasporto di pazienti e campioni. Prima di assumere un nuovo barelliere è necessaria una postazione barellieri valida con un armadietto libero per il turno selezionato.",
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
                    "I barellieri attendono un carrello campioni in {1} perché tutti i carrelli assegnati alla loro postazione sono già in uso. Aggiungi un altro carrello campioni per aumentare la capacità di trasporto.") },
                { "jp", new Translation(
                    "院内搬送員",
                    "院内搬送員",
                    "患者と検体の搬送を担当する院内物流スタッフです。新しい搬送員を雇用するには、選択したシフト用の空きロッカーがある有効な搬送員ステーションが必要です。",
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
                    "{1} で搬送員が検体カートを待っています。搬送員ステーションに割り当てられたすべてのカートが使用中です。搬送能力を増やすには検体カートを追加してください。") },
                { "kr", new Translation(
                    "원내 이송 요원",
                    "원내 이송 요원",
                    "환자와 검체 이송을 담당하는 원내 물류 직원입니다. 새 이송 요원을 고용하려면 선택한 근무조에 빈 사물함이 있는 유효한 이송 요원 스테이션이 필요합니다.",
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
                    "{1}에서 이송 요원이 검체 카트를 기다리고 있습니다. 이송 요원 스테이션에 배정된 모든 카트가 이미 사용 중입니다. 이송 용량을 늘리려면 검체 카트를 추가하세요.") },
                { "nl", new Translation(
                    "Patiëntenvervoerder",
                    "Patiëntenvervoerders",
                    "Intern logistiek personeel voor het vervoer van patiënten en monsters. Voordat een nieuwe patiëntenvervoerder kan worden aangenomen, is een geldig transportstation met een vrije locker voor de gekozen dienst vereist.",
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
                    "Patiëntenvervoerders wachten in {1} op een monsterkar omdat alle karren van hun transportstation al in gebruik zijn. Plaats een extra monsterkar om de transportcapaciteit te vergroten.") },
                { "pl", new Translation(
                    "Pracownik transportu",
                    "Pracownicy transportu",
                    "Personel logistyki wewnętrznej przeznaczony do transportu pacjentów i próbek. Przed zatrudnieniem nowego pracownika transportu wymagane jest prawidłowe stanowisko transportowe z wolną szafką na wybraną zmianę.",
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
                    "Pracownicy transportu czekają na wózek na próbki w {1}, ponieważ wszystkie wózki przypisane do ich stanowiska są już używane. Dodaj kolejny wózek na próbki, aby zwiększyć wydajność transportu.") },
                { "ptbr", new Translation(
                    "Maqueiro",
                    "Maqueiros",
                    "Equipe de logística interna dedicada ao transporte de pacientes e amostras. É necessária uma estação de maqueiros válida com um armário livre para o turno selecionado antes de contratar um novo maqueiro.",
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
                    "Os maqueiros estão esperando um carrinho de amostras em {1}, pois todos os carrinhos atribuídos à estação já estão em uso. Adicione outro carrinho de amostras para aumentar a capacidade de transporte.") },
                { "ru", new Translation(
                    "Санитар-транспортировщик",
                    "Санитары-транспортировщики",
                    "Сотрудники внутренней логистики для перевозки пациентов и образцов. Перед наймом нового санитара-транспортировщика требуется действующая станция транспортировщиков со свободным шкафчиком для выбранной смены.",
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
                    "Транспортировщики ждут тележку для образцов в {1}, потому что все тележки, закреплённые за их станцией, уже используются. Добавьте ещё одну тележку, чтобы увеличить пропускную способность.") },
                { "swe", new Translation(
                    "Patienttransportör",
                    "Patienttransportörer",
                    "Intern logistikpersonal för transport av patienter och prover. En giltig transportstation med ett ledigt skåp för valt skift krävs innan en ny patienttransportör kan anställas.",
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
                    "Patienttransportörer väntar på en provvagn i {1} eftersom alla vagnar som tillhör deras transportstation redan används. Lägg till en extra provvagn för att öka transportkapaciteten.") },
                { "tr", new Translation(
                    "Hasta taşıma görevlisi",
                    "Hasta taşıma görevlileri",
                    "Hasta ve numune taşımaya ayrılmış dahili lojistik personeli. Yeni bir taşıma görevlisi işe alınmadan önce seçilen vardiya için boş dolabı bulunan geçerli bir taşıma istasyonu gerekir.",
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
                    "Taşıma görevlileri {1} konumunda numune arabası bekliyor; istasyonlarına atanmış tüm arabalar kullanımda. Taşıma kapasitesini artırmak için ek bir numune arabası ekleyin.") },
                { "uk", new Translation(
                    "Санітар-транспортувальник",
                    "Санітари-транспортувальники",
                    "Працівники внутрішньої логістики для перевезення пацієнтів і зразків. Перед наймом нового санітара-транспортувальника потрібна дійсна станція транспортувальників із вільною шафкою для вибраної зміни.",
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
                    "Транспортувальники чекають на візок для зразків у {1}, оскільки всі візки, призначені їхній станції, уже використовуються. Додайте ще один візок, щоб збільшити транспортну спроможність.") },
                { "zhcn", new Translation(
                    "院内运送员",
                    "院内运送员",
                    "负责患者和样本运送的院内物流人员。雇用新的运送员之前，必须有一个有效的运送员工作站，并且所选班次有空闲储物柜。",
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
                    "运送员正在 {1} 等待样本推车，因为分配给其工作站的所有推车都已在使用。添加另一辆样本推车以提高运输能力。") },
                { "zhtw", new Translation(
                    "院內運送員",
                    "院內運送員",
                    "負責病患與檢體運送的院內物流人員。雇用新的運送員前，必須有有效的運送員工作站，且所選班次需有空閒置物櫃。",
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
                    "運送員正在 {1} 等待檢體推車，因為分配給其工作站的所有推車都已在使用。新增另一台檢體推車以提高運送能力。") }
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
