using System;
using System.Collections.Generic;

namespace HospitalModUpdateChecker
{
    internal static class UpdatePanelLocalization
    {
        internal const string DashboardTitleId = "L75_HMUC_DASHBOARD_TITLE";
        internal const string NewsTitleId = "L75_HMUC_NEWS_TITLE";
        internal const string UpdatesTitleId = "L75_HMUC_UPDATES_TITLE";
        internal const string PluginsTitleId = "L75_HMUC_PLUGINS_TITLE";
        internal const string LoadingId = "L75_HMUC_LOADING";
        internal const string NoNewsId = "L75_HMUC_NO_NEWS";
        internal const string NewsErrorId = "L75_HMUC_NEWS_ERROR";
        internal const string NoUpdatesId = "L75_HMUC_NO_UPDATES";
        internal const string NoUpdatesCachedId = "L75_HMUC_NO_UPDATES_CACHED";
        internal const string UpdateErrorId = "L75_HMUC_UPDATE_ERROR";
        internal const string NoPluginsId = "L75_HMUC_NO_PLUGINS";
        internal const string StatusLoadedId = "L75_HMUC_STATUS_LOADED";
        internal const string StatusWarningId = "L75_HMUC_STATUS_WARNING";
        internal const string StatusErrorId = "L75_HMUC_STATUS_ERROR";
        internal const string UnknownAuthorId = "L75_HMUC_UNKNOWN_AUTHOR";

        private sealed class Translation
        {
            internal readonly string DashboardTitle;
            internal readonly string NewsTitle;
            internal readonly string UpdatesTitle;
            internal readonly string PluginsTitle;
            internal readonly string Loading;
            internal readonly string NoNews;
            internal readonly string NewsError;
            internal readonly string NoUpdates;
            internal readonly string NoUpdatesCached;
            internal readonly string UpdateError;
            internal readonly string NoPlugins;
            internal readonly string StatusLoaded;
            internal readonly string StatusWarning;
            internal readonly string StatusError;
            internal readonly string UnknownAuthor;

            internal Translation(
                string dashboardTitle,
                string newsTitle,
                string updatesTitle,
                string pluginsTitle,
                string loading,
                string noNews,
                string newsError,
                string noUpdates,
                string noUpdatesCached,
                string updateError,
                string noPlugins,
                string statusLoaded,
                string statusWarning,
                string statusError,
                string unknownAuthor)
            {
                DashboardTitle = dashboardTitle;
                NewsTitle = newsTitle;
                UpdatesTitle = updatesTitle;
                PluginsTitle = pluginsTitle;
                Loading = loading;
                NoNews = noNews;
                NewsError = newsError;
                NoUpdates = noUpdates;
                NoUpdatesCached = noUpdatesCached;
                UpdateError = updateError;
                NoPlugins = noPlugins;
                StatusLoaded = statusLoaded;
                StatusWarning = statusWarning;
                StatusError = statusError;
                UnknownAuthor = unknownAuthor;
            }
        }

        private static readonly Translation English =
            new Translation(
                "HMUC - BepInEx Mod Center",
                "News",
                "Available Updates",
                "Loaded BepInEx Mods",
                "Checking...",
                "No news available.",
                "Unable to load news. See the BepInEx log.",
                "No updates available.",
                "No updates were found during the last successful check.",
                "Unable to check for updates. See the BepInEx log.",
                "No BepInEx mods loaded.",
                "Loaded",
                "Warning",
                "Error",
                "Unknown author");

        private static readonly Dictionary<string, Translation> Translations =
            CreateTranslations();

        internal static string Get(string stringId)
        {
            string language =
                NormalizeLanguageCode(GetCurrentLanguage());

            Translation translation;
            if (!Translations.TryGetValue(
                    language,
                    out translation))
            {
                translation = English;
            }

            if (stringId == DashboardTitleId)
            {
                return translation.DashboardTitle;
            }

            if (stringId == NewsTitleId)
            {
                return translation.NewsTitle;
            }

            if (stringId == UpdatesTitleId)
            {
                return translation.UpdatesTitle;
            }

            if (stringId == PluginsTitleId)
            {
                return translation.PluginsTitle;
            }

            if (stringId == LoadingId)
            {
                return translation.Loading;
            }

            if (stringId == NoNewsId)
            {
                return translation.NoNews;
            }

            if (stringId == NewsErrorId)
            {
                return translation.NewsError;
            }

            if (stringId == NoUpdatesId)
            {
                return translation.NoUpdates;
            }

            if (stringId == NoUpdatesCachedId)
            {
                return translation.NoUpdatesCached;
            }

            if (stringId == UpdateErrorId)
            {
                return translation.UpdateError;
            }

            if (stringId == NoPluginsId)
            {
                return translation.NoPlugins;
            }

            if (stringId == StatusLoadedId)
            {
                return translation.StatusLoaded;
            }

            if (stringId == StatusWarningId)
            {
                return translation.StatusWarning;
            }

            if (stringId == StatusErrorId)
            {
                return translation.StatusError;
            }

            if (stringId == UnknownAuthorId)
            {
                return translation.UnknownAuthor;
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
                "HMUC - Centrum modů BepInEx",
                "Novinky",
                "Dostupné aktualizace",
                "Načtené mody BepInEx",
                "Kontrola...",
                "Nejsou k dispozici žádné novinky.",
                "Novinky se nepodařilo načíst. Viz protokol BepInEx.",
                "Nejsou k dispozici žádné aktualizace.",
                "Při poslední úspěšné kontrole nebyly nalezeny žádné aktualizace.",
                "Aktualizace se nepodařilo zkontrolovat. Viz protokol BepInEx.",
                "Nejsou načteny žádné mody BepInEx.",
                "Načteno",
                "Varování",
                "Chyba",
                "Neznámý autor");

            translations["da"] = new Translation(
                "HMUC - BepInEx-modcenter",
                "Nyheder",
                "Tilgængelige opdateringer",
                "Indlæste BepInEx-mods",
                "Kontrollerer...",
                "Ingen nyheder tilgængelige.",
                "Nyheder kunne ikke indlæses. Se BepInEx-loggen.",
                "Ingen opdateringer tilgængelige.",
                "Der blev ikke fundet nogen opdateringer ved den seneste vellykkede kontrol.",
                "Kunne ikke søge efter opdateringer. Se BepInEx-loggen.",
                "Ingen BepInEx-mods er indlæst.",
                "Indlæst",
                "Advarsel",
                "Fejl",
                "Ukendt forfatter");

            translations["de"] = new Translation(
                "HMUC - BepInEx-Mod-Center",
                "Neuigkeiten",
                "Verfügbare Updates",
                "Geladene BepInEx-Mods",
                "Prüfung...",
                "Keine Neuigkeiten verfügbar.",
                "Neuigkeiten konnten nicht geladen werden. Siehe BepInEx-Log.",
                "Keine Updates verfügbar.",
                "Bei der letzten erfolgreichen Prüfung wurden keine Updates gefunden.",
                "Updates konnten nicht geprüft werden. Siehe BepInEx-Log.",
                "Keine BepInEx-Mods geladen.",
                "Geladen",
                "Warnung",
                "Fehler",
                "Unbekannter Autor");

            translations["es"] = new Translation(
                "HMUC - Centro de mods BepInEx",
                "Novedades",
                "Actualizaciones disponibles",
                "Mods BepInEx cargados",
                "Comprobando...",
                "No hay novedades disponibles.",
                "No se pudieron cargar las novedades. Consulta el registro de BepInEx.",
                "No hay actualizaciones disponibles.",
                "No se encontraron actualizaciones en la última comprobación correcta.",
                "No se pudieron comprobar las actualizaciones. Consulta el registro de BepInEx.",
                "No hay mods BepInEx cargados.",
                "Cargado",
                "Advertencia",
                "Error",
                "Autor desconocido");

            translations["es-419"] = new Translation(
                "HMUC - Centro de mods BepInEx",
                "Novedades",
                "Actualizaciones disponibles",
                "Mods BepInEx cargados",
                "Comprobando...",
                "No hay novedades disponibles.",
                "No se pudieron cargar las novedades. Consulta el registro de BepInEx.",
                "No hay actualizaciones disponibles.",
                "No se encontraron actualizaciones en la última comprobación correcta.",
                "No se pudieron comprobar las actualizaciones. Consulta el registro de BepInEx.",
                "No hay mods BepInEx cargados.",
                "Cargado",
                "Advertencia",
                "Error",
                "Autor desconocido");

            translations["fr"] = new Translation(
                "HMUC - Centre des mods BepInEx",
                "Nouveautés",
                "Mises à jour disponibles",
                "Mods BepInEx chargés",
                "Vérification en cours...",
                "Aucune nouveauté disponible.",
                "Impossible de charger les nouveautés. Voir le journal BepInEx.",
                "Aucune mise à jour disponible.",
                "Aucune mise à jour lors de la dernière vérification réussie.",
                "Impossible de vérifier les mises à jour. Voir le journal BepInEx.",
                "Aucun mod BepInEx chargé.",
                "Chargé",
                "Avertissement",
                "Erreur",
                "Auteur inconnu");

            translations["hu"] = new Translation(
                "HMUC - BepInEx modközpont",
                "Újdonságok",
                "Elérhető frissítések",
                "Betöltött BepInEx modok",
                "Ellenőrzés...",
                "Nincs elérhető újdonság.",
                "Az újdonságok nem tölthetők be. Lásd a BepInEx naplót.",
                "Nincs elérhető frissítés.",
                "A legutóbbi sikeres ellenőrzés nem talált frissítést.",
                "A frissítések ellenőrzése sikertelen. Lásd a BepInEx naplót.",
                "Nincs betöltött BepInEx mod.",
                "Betöltve",
                "Figyelmeztetés",
                "Hiba",
                "Ismeretlen szerző");

            translations["it"] = new Translation(
                "HMUC - Centro mod BepInEx",
                "Novità",
                "Aggiornamenti disponibili",
                "Mod BepInEx caricati",
                "Controllo...",
                "Nessuna novità disponibile.",
                "Impossibile caricare le novità. Vedi il log di BepInEx.",
                "Nessun aggiornamento disponibile.",
                "Nell'ultimo controllo riuscito non sono stati trovati aggiornamenti.",
                "Impossibile controllare gli aggiornamenti. Vedi il log di BepInEx.",
                "Nessun mod BepInEx caricato.",
                "Caricato",
                "Avviso",
                "Errore",
                "Autore sconosciuto");

            translations["ja"] = new Translation(
                "HMUC - BepInEx Modセンター",
                "ニュース",
                "利用可能なアップデート",
                "読み込み済みBepInEx Mod",
                "確認中...",
                "利用可能なニュースはありません。",
                "ニュースを読み込めません。BepInExログを確認してください。",
                "利用可能なアップデートはありません。",
                "前回の正常な確認ではアップデートは見つかりませんでした。",
                "アップデートを確認できません。BepInExログを確認してください。",
                "読み込まれているBepInEx Modはありません。",
                "読み込み済み",
                "警告",
                "エラー",
                "不明な作者");

            translations["ko"] = new Translation(
                "HMUC - BepInEx 모드 센터",
                "소식",
                "사용 가능한 업데이트",
                "로드된 BepInEx 모드",
                "확인 중...",
                "사용 가능한 소식이 없습니다.",
                "소식을 불러올 수 없습니다. BepInEx 로그를 확인하세요.",
                "사용 가능한 업데이트가 없습니다.",
                "마지막으로 성공한 확인에서 업데이트를 찾지 못했습니다.",
                "업데이트를 확인할 수 없습니다. BepInEx 로그를 확인하세요.",
                "로드된 BepInEx 모드가 없습니다.",
                "로드됨",
                "경고",
                "오류",
                "알 수 없는 작성자");

            translations["nl"] = new Translation(
                "HMUC - BepInEx-modcentrum",
                "Nieuws",
                "Beschikbare updates",
                "Geladen BepInEx-mods",
                "Controleren...",
                "Geen nieuws beschikbaar.",
                "Nieuws kon niet worden geladen. Zie het BepInEx-logboek.",
                "Geen updates beschikbaar.",
                "Bij de laatste geslaagde controle zijn geen updates gevonden.",
                "Kan niet controleren op updates. Zie het BepInEx-logboek.",
                "Geen BepInEx-mods geladen.",
                "Geladen",
                "Waarschuwing",
                "Fout",
                "Onbekende auteur");

            translations["pl"] = new Translation(
                "HMUC - Centrum modów BepInEx",
                "Aktualności",
                "Dostępne aktualizacje",
                "Wczytane mody BepInEx",
                "Sprawdzanie...",
                "Brak dostępnych aktualności.",
                "Nie można wczytać aktualności. Zobacz dziennik BepInEx.",
                "Brak dostępnych aktualizacji.",
                "Podczas ostatniego udanego sprawdzania nie znaleziono aktualizacji.",
                "Nie można sprawdzić aktualizacji. Zobacz dziennik BepInEx.",
                "Nie wczytano żadnych modów BepInEx.",
                "Załadowany",
                "Ostrzeżenie",
                "Błąd",
                "Nieznany autor");

            translations["pt-br"] = new Translation(
                "HMUC - Central de mods BepInEx",
                "Novidades",
                "Atualizações disponíveis",
                "Mods BepInEx carregados",
                "Verificando...",
                "Nenhuma novidade disponível.",
                "Não foi possível carregar as novidades. Consulte o log do BepInEx.",
                "Nenhuma atualização disponível.",
                "Nenhuma atualização foi encontrada na última verificação bem-sucedida.",
                "Não foi possível verificar as atualizações. Consulte o log do BepInEx.",
                "Nenhum mod BepInEx carregado.",
                "Carregado",
                "Aviso",
                "Erro",
                "Autor desconhecido");

            translations["ru"] = new Translation(
                "HMUC - Центр модов BepInEx",
                "Новости",
                "Доступные обновления",
                "Загруженные моды BepInEx",
                "Проверка...",
                "Нет доступных новостей.",
                "Не удалось загрузить новости. См. журнал BepInEx.",
                "Нет доступных обновлений.",
                "Во время последней успешной проверки обновления не найдены.",
                "Не удалось проверить обновления. См. журнал BepInEx.",
                "Нет загруженных модов BepInEx.",
                "Загружен",
                "Предупреждение",
                "Ошибка",
                "Неизвестный автор");

            translations["sv"] = new Translation(
                "HMUC - BepInEx-modcenter",
                "Nyheter",
                "Tillgängliga uppdateringar",
                "Laddade BepInEx-mods",
                "Kontrollerar...",
                "Inga nyheter tillgängliga.",
                "Kunde inte läsa in nyheter. Se BepInEx-loggen.",
                "Inga uppdateringar tillgängliga.",
                "Inga uppdateringar hittades vid den senaste lyckade kontrollen.",
                "Kunde inte söka efter uppdateringar. Se BepInEx-loggen.",
                "Inga BepInEx-mods är laddade.",
                "Laddad",
                "Varning",
                "Fel",
                "Okänd skapare");

            translations["tr"] = new Translation(
                "HMUC - BepInEx Mod Merkezi",
                "Haberler",
                "Mevcut Güncellemeler",
                "Yüklü BepInEx modları",
                "Kontrol ediliyor...",
                "Kullanılabilir haber yok.",
                "Haberler yüklenemedi. BepInEx günlüğüne bakın.",
                "Kullanılabilir güncelleme yok.",
                "Son başarılı kontrolde güncelleme bulunamadı.",
                "Güncellemeler kontrol edilemedi. BepInEx günlüğüne bakın.",
                "Yüklü BepInEx modu yok.",
                "Yüklendi",
                "Uyarı",
                "Hata",
                "Bilinmeyen yazar");

            translations["uk"] = new Translation(
                "HMUC - Центр модів BepInEx",
                "Новини",
                "Доступні оновлення",
                "Завантажені моди BepInEx",
                "Перевірка...",
                "Немає доступних новин.",
                "Не вдалося завантажити новини. Див. журнал BepInEx.",
                "Немає доступних оновлень.",
                "Під час останньої успішної перевірки оновлень не знайдено.",
                "Не вдалося перевірити оновлення. Див. журнал BepInEx.",
                "Немає завантажених модів BepInEx.",
                "Завантажено",
                "Попередження",
                "Помилка",
                "Невідомий автор");

            translations["zh-cn"] = new Translation(
                "HMUC - BepInEx 模组中心",
                "新闻",
                "可用更新",
                "已加载的 BepInEx 模组",
                "正在检查...",
                "暂无新闻。",
                "无法加载新闻。请查看 BepInEx 日志。",
                "暂无可用更新。",
                "上次成功检查时未发现更新。",
                "无法检查更新。请查看 BepInEx 日志。",
                "未加载 BepInEx 模组。",
                "已加载",
                "警告",
                "错误",
                "未知作者");

            translations["zh-tw"] = new Translation(
                "HMUC - BepInEx 模組中心",
                "新聞",
                "可用更新",
                "已載入的 BepInEx 模組",
                "正在檢查...",
                "目前沒有新聞。",
                "無法載入新聞。請查看 BepInEx 記錄。",
                "目前沒有可用更新。",
                "上次成功檢查時未發現更新。",
                "無法檢查更新。請查看 BepInEx 記錄。",
                "未載入 BepInEx 模組。",
                "已載入",
                "警告",
                "錯誤",
                "未知作者");

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

            if (code == "ptbr" ||
                code == "pt" ||
                code.StartsWith("pt-br"))
            {
                return "pt-br";
            }

            if (code == "esla" ||
                code == "es-419" ||
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

            if (code == "zhtw" ||
                code == "zh-tw" ||
                code == "zh-hant" ||
                code.StartsWith("zh-tw") ||
                code.StartsWith("zh-hk"))
            {
                return "zh-tw";
            }

            if (code == "zhcn" ||
                code == "zh-cn" ||
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
