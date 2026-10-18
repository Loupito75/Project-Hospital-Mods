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
        internal const string ConfigureTooltipId = "L75_HMUC_CONFIGURE_TOOLTIP";
        internal const string ConfigureActionId = "L75_HMUC_CONFIGURE_ACTION";
        internal const string RecommendedTitleId = "L75_HMUC_RECOMMENDED_TITLE";
        internal const string RecommendedInstallId = "L75_HMUC_RECOMMENDED_INSTALL";
        internal const string RecommendedEmptyId = "L75_HMUC_RECOMMENDED_EMPTY";
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
        internal const string UpdateActionId = "L75_HMUC_UPDATE_ACTION";
        internal const string CancelActionId = "L75_HMUC_CANCEL_ACTION";
        internal const string RetryActionId = "L75_HMUC_RETRY_ACTION";
        internal const string QueuedActionId = "L75_HMUC_QUEUED_ACTION";
        internal const string WorkingActionId = "L75_HMUC_WORKING_ACTION";
        internal const string DownloadingId = "L75_HMUC_DOWNLOADING";
        internal const string VerifyingId = "L75_HMUC_VERIFYING";
        internal const string PreparingInstallId = "L75_HMUC_PREPARING_INSTALL";
        internal const string RestartRequiredId = "L75_HMUC_RESTART_REQUIRED";
        internal const string CancelledId = "L75_HMUC_CANCELLED";
        internal const string PackageCatalogLoadingId = "L75_HMUC_PACKAGE_CATALOG_LOADING";
        internal const string PackageUnavailableId = "L75_HMUC_PACKAGE_UNAVAILABLE";
        internal const string PackageCatalogErrorId = "L75_HMUC_PACKAGE_CATALOG_ERROR";
        internal const string UpdateBusyId = "L75_HMUC_UPDATE_BUSY";
        internal const string ReadyToUpdateId = "L75_HMUC_READY_TO_UPDATE";
        internal const string DownloadErrorId = "L75_HMUC_DOWNLOAD_ERROR";
        internal const string InstallPreparationErrorId = "L75_HMUC_INSTALL_PREPARATION_ERROR";
        internal const string NotEnoughSpaceId = "L75_HMUC_NOT_ENOUGH_SPACE";
        internal const string InstallSucceededId = "L75_HMUC_INSTALL_SUCCEEDED";
        internal const string InstallFailedId = "L75_HMUC_INSTALL_FAILED";
        internal const string ConfigAddedId = "L75_HMUC_CONFIG_ADDED";
        internal const string ConfigWarningId = "L75_HMUC_CONFIG_WARNING";
        internal const string ConfigHoverHelpId = "L75_HMUC_CONFIG_HOVER_HELP";
        internal const string ConfigListHelpId = "L75_HMUC_CONFIG_LIST_HELP";
        internal const string ConfigRestartId = "L75_HMUC_CONFIG_RESTART";
        internal const string ConfigInfoId = "L75_HMUC_CONFIG_INFO";
        internal const string ConfigSaveId = "L75_HMUC_CONFIG_SAVE";
        internal const string ConfigCloseId = "L75_HMUC_CONFIG_CLOSE";
        internal const string ConfigDiscardConfirmId = "L75_HMUC_CONFIG_DISCARD_CONFIRM";
        internal const string ConfigErrorId = "L75_HMUC_CONFIG_ERROR";
        internal const string ConfigNoEditableId = "L75_HMUC_CONFIG_NO_EDITABLE";
        internal const string ConfigProcessingErrorId = "L75_HMUC_CONFIG_PROCESSING_ERROR";

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

        // Tooltip text is resolved at hover time so changing the title-screen
        // language does not require recreating every mod row.
        private static readonly Dictionary<string, string> ConfigureTooltipTranslations =
            CreateConfigureTooltipTranslations();

        private static Dictionary<string, string> CreateConfigureTooltipTranslations()
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            values["en"] = "Configure this mod";
            values["cs"] = "Nastavit tento mod";
            values["da"] = "Konfigurer denne mod";
            values["de"] = "Diesen Mod konfigurieren";
            values["es"] = "Configurar este mod";
            values["es-419"] = "Configurar este mod";
            values["fr"] = "Configurer ce mod";
            values["hu"] = "Mod beállítása";
            values["it"] = "Configura questa mod";
            values["ja"] = "このMODを設定";
            values["ko"] = "이 모드 설정";
            values["nl"] = "Deze mod configureren";
            values["pl"] = "Skonfiguruj ten mod";
            values["pt-br"] = "Configurar este mod";
            values["ru"] = "Настроить этот мод";
            values["sv"] = "Konfigurera denna mod";
            values["tr"] = "Bu modu yapılandır";
            values["uk"] = "Налаштувати цей мод";
            values["zh-cn"] = "配置此模组";
            values["zh-tw"] = "設定此模組";
            return values;
        }

        private static readonly Dictionary<string, string> ConfigureActionTranslations =
            CreateConfigureActionTranslations();

        private static Dictionary<string, string> CreateConfigureActionTranslations()
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            values["en"] = "Configure";
            values["cs"] = "Nastavit";
            values["da"] = "Indstil";
            values["de"] = "Konfig.";
            values["es"] = "Configurar";
            values["es-419"] = "Configurar";
            values["fr"] = "Configurer";
            values["hu"] = "Beállítás";
            values["it"] = "Configura";
            values["ja"] = "設定";
            values["ko"] = "설정";
            values["nl"] = "Instellen";
            values["pl"] = "Ustaw";
            values["pt-br"] = "Configurar";
            values["ru"] = "Настроить";
            values["sv"] = "Konfigurera";
            values["tr"] = "Yapılandır";
            values["uk"] = "Налаштувати";
            values["zh-cn"] = "配置";
            values["zh-tw"] = "設定";
            return values;
        }

        private static readonly Dictionary<string, string[]> RecommendedTranslations =
            CreateRecommendedTranslations();

        private static Dictionary<string, string[]> CreateRecommendedTranslations()
        {
            Dictionary<string, string[]> items =
                new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            items["en"] = new string[] { "Mods you might like", "Install", "No other mods available." };
            items["cs"] = new string[] { "Mody, které by vás mohly zajímat", "Instalovat", "Žádné další mody nejsou k dispozici." };
            items["da"] = new string[] { "Mods, du måske vil synes om", "Installer", "Ingen andre mods tilgængelige." };
            items["de"] = new string[] { "Mods, die dir gefallen könnten", "Installieren", "Keine weiteren Mods verfügbar." };
            items["es"] = new string[] { "Mods que podrían interesarte", "Instalar", "No hay más mods disponibles." };
            items["es-419"] = new string[] { "Mods que podrían interesarte", "Instalar", "No hay más mods disponibles." };
            items["fr"] = new string[] { "D'autres mods qui pourraient vous intéresser", "Installer", "Aucun autre mod disponible." };
            items["hu"] = new string[] { "Modok, amelyek érdekelhetnek", "Telepítés", "Nincs több elérhető mod." };
            items["it"] = new string[] { "Mod che potrebbero interessarti", "Installa", "Nessun altro mod disponibile." };
            items["ja"] = new string[] { "おすすめのMOD", "インストール", "ほかに利用可能なMODはありません。" };
            items["ko"] = new string[] { "관심 있을 만한 모드", "설치", "사용 가능한 다른 모드가 없습니다." };
            items["nl"] = new string[] { "Mods die je misschien leuk vindt", "Installeren", "Geen andere mods beschikbaar." };
            items["pl"] = new string[] { "Mody, które mogą Cię zainteresować", "Zainstaluj", "Brak innych dostępnych modów." };
            items["pt-br"] = new string[] { "Mods que podem interessar a você", "Instalar", "Nenhum outro mod disponível." };
            items["ru"] = new string[] { "Моды, которые могут вас заинтересовать", "Установить", "Других доступных модов нет." };
            items["sv"] = new string[] { "Mods du kanske gillar", "Installera", "Inga andra mods tillgängliga." };
            items["tr"] = new string[] { "İlginizi çekebilecek modlar", "Yükle", "Başka kullanılabilir mod yok." };
            items["uk"] = new string[] { "Моди, які можуть вас зацікавити", "Встановити", "Інших доступних модів немає." };
            items["zh-cn"] = new string[] { "您可能感兴趣的模组", "安装", "没有其他可用模组。" };
            items["zh-tw"] = new string[] { "您可能感興趣的模組", "安裝", "沒有其他可用模組。" };
            return items;
        }

        private static readonly string[] ConfigEditorStringIds =
            new string[]
            {
                ConfigHoverHelpId,
                ConfigListHelpId,
                ConfigRestartId,
                ConfigInfoId,
                ConfigSaveId,
                ConfigCloseId,
                ConfigDiscardConfirmId,
                ConfigErrorId,
                ConfigNoEditableId,
                ConfigProcessingErrorId
            };

        private static readonly Dictionary<string, string[]>
            ConfigEditorTranslations =
                CreateConfigEditorTranslations();

        private static Dictionary<string, string[]> CreateConfigEditorTranslations()
        {
            Dictionary<string, string[]> values =
                new Dictionary<string, string[]>(
                    StringComparer.OrdinalIgnoreCase);

            values["en"] = new string[]
            {
                "Hover over an option to view its full name and description.",
                "One value per line. A listed value is included; remove it to exclude it.",
                "Once saved, changes will take effect after restarting the game.",
                "Info",
                "Save",
                "Close",
                "Settings have been changed. Click again to close without saving.",
                "Error",
                "There are no editable settings in this configuration.",
                "The configuration could not be processed safely. See the BepInEx log."
            };
            values["cs"] = new string[]
            {
                "Umístěte kurzor na možnost, chcete-li zobrazit její úplný název a popis.",
                "Jedna hodnota na řádek. Uvedená hodnota je zahrnuta; odeberte ji, chcete-li ji vyloučit.",
                "Po uložení se změny projeví po restartování hry.",
                "Informace",
                "Uložit",
                "Zavřít",
                "Nastavení byla změněna. Kliknutím znovu zavřete bez uložení.",
                "Chyba",
                "V této konfiguraci nejsou žádná upravitelná nastavení.",
                "Konfiguraci se nepodařilo bezpečně zpracovat. Viz protokol BepInEx."
            };
            values["da"] = new string[]
            {
                "Hold markøren over en indstilling for at se dens fulde navn og beskrivelse.",
                "Én værdi pr. linje. En værdi på listen er medtaget; fjern den for at udelukke den.",
                "Når ændringerne er gemt, træder de i kraft efter genstart af spillet.",
                "Info",
                "Gem",
                "Luk",
                "Indstillingerne er ændret. Klik igen for at lukke uden at gemme.",
                "Fejl",
                "Der er ingen redigerbare indstillinger i denne konfiguration.",
                "Konfigurationen kunne ikke behandles sikkert. Se BepInEx-loggen."
            };
            values["de"] = new string[]
            {
                "Bewege den Mauszeiger über eine Option, um den vollständigen Namen und die Beschreibung anzuzeigen.",
                "Ein Wert pro Zeile. Ein aufgeführter Wert ist enthalten; entferne ihn, um ihn auszuschließen.",
                "Nach dem Speichern werden die Änderungen nach einem Neustart des Spiels wirksam.",
                "Info",
                "Speichern",
                "Schließen",
                "Einstellungen wurden geändert. Klicke erneut, um ohne Speichern zu schließen.",
                "Fehler",
                "Diese Konfiguration enthält keine bearbeitbaren Einstellungen.",
                "Die Konfiguration konnte nicht sicher verarbeitet werden. Siehe BepInEx-Log."
            };
            values["es"] = new string[]
            {
                "Pasa el cursor sobre una opción para ver su nombre completo y su descripción.",
                "Un valor por línea. Un valor incluido en la lista está activo; elimínalo para excluirlo.",
                "Una vez guardados, los cambios se aplicarán después de reiniciar el juego.",
                "Información",
                "Guardar",
                "Cerrar",
                "La configuración ha cambiado. Vuelve a hacer clic para cerrar sin guardar.",
                "Error",
                "No hay ajustes editables en esta configuración.",
                "No se pudo procesar la configuración de forma segura. Consulta el registro de BepInEx."
            };
            values["es-419"] = new string[]
            {
                "Pasa el cursor sobre una opción para ver su nombre completo y su descripción.",
                "Un valor por línea. Un valor incluido en la lista está activo; elimínalo para excluirlo.",
                "Una vez guardados, los cambios se aplicarán después de reiniciar el juego.",
                "Información",
                "Guardar",
                "Cerrar",
                "La configuración ha cambiado. Vuelve a hacer clic para cerrar sin guardar.",
                "Error",
                "No hay ajustes editables en esta configuración.",
                "No se pudo procesar la configuración de forma segura. Consulta el registro de BepInEx."
            };
            values["fr"] = new string[]
            {
                "Survolez une option pour afficher son nom complet et sa description.",
                "Une valeur par ligne. Une valeur présente est incluse; retirez-la pour l'exclure.",
                "Une fois enregistrées, les options seront prises en compte au prochain démarrage.",
                "Info",
                "Enregistrer",
                "Fermer",
                "Des réglages ont été modifiés. Cliquez à nouveau pour fermer sans enregistrer.",
                "Erreur",
                "Aucun paramètre modifiable dans cette configuration.",
                "La configuration n'a pas pu être traitée en toute sécurité. Voir le journal BepInEx."
            };
            values["hu"] = new string[]
            {
                "Vidd az egérmutatót egy beállítás fölé a teljes név és leírás megtekintéséhez.",
                "Soronként egy érték. A felsorolt érték szerepel; töröld a kizárásához.",
                "Mentés után a módosítások a játék újraindításakor lépnek életbe.",
                "Információ",
                "Mentés",
                "Bezárás",
                "A beállítások módosultak. Kattints újra a mentés nélküli bezáráshoz.",
                "Hiba",
                "Ebben a konfigurációban nincs szerkeszthető beállítás.",
                "A konfigurációt nem sikerült biztonságosan feldolgozni. Lásd a BepInEx naplót."
            };
            values["it"] = new string[]
            {
                "Passa il puntatore su un'opzione per visualizzarne il nome completo e la descrizione.",
                "Un valore per riga. Un valore elencato è incluso; rimuovilo per escluderlo.",
                "Dopo il salvataggio, le modifiche avranno effetto al riavvio del gioco.",
                "Info",
                "Salva",
                "Chiudi",
                "Le impostazioni sono state modificate. Fai di nuovo clic per chiudere senza salvare.",
                "Errore",
                "Non ci sono impostazioni modificabili in questa configurazione.",
                "Non è stato possibile elaborare la configurazione in modo sicuro. Consulta il log di BepInEx."
            };
            values["ja"] = new string[]
            {
                "オプションにカーソルを合わせると、完全な名前と説明が表示されます。",
                "1 行に 1 つの値を入力します。記載された値は含まれます。除外するには削除してください。",
                "保存した変更は、ゲームの再起動後に反映されます。",
                "情報",
                "保存",
                "閉じる",
                "設定が変更されています。保存せずに閉じるには、もう一度クリックしてください。",
                "エラー",
                "この設定には編集可能な項目がありません。",
                "設定を安全に処理できませんでした。BepInEx ログを確認してください。"
            };
            values["ko"] = new string[]
            {
                "옵션 위에 마우스를 올리면 전체 이름과 설명을 볼 수 있습니다.",
                "한 줄에 값 하나를 입력합니다. 목록에 있는 값은 포함되며, 제외하려면 삭제하세요.",
                "저장한 변경 사항은 게임을 다시 시작한 후 적용됩니다.",
                "정보",
                "저장",
                "닫기",
                "설정이 변경되었습니다. 저장하지 않고 닫으려면 다시 클릭하세요.",
                "오류",
                "이 구성에는 편집 가능한 설정이 없습니다.",
                "구성을 안전하게 처리하지 못했습니다. BepInEx 로그를 확인하세요."
            };
            values["nl"] = new string[]
            {
                "Plaats de muisaanwijzer op een optie om de volledige naam en beschrijving te bekijken.",
                "Eén waarde per regel. Een vermelde waarde is opgenomen; verwijder deze om hem uit te sluiten.",
                "Na het opslaan worden de wijzigingen toegepast nadat het spel opnieuw is gestart.",
                "Info",
                "Opslaan",
                "Sluiten",
                "De instellingen zijn gewijzigd. Klik opnieuw om te sluiten zonder op te slaan.",
                "Fout",
                "Deze configuratie bevat geen bewerkbare instellingen.",
                "De configuratie kon niet veilig worden verwerkt. Zie het BepInEx-logboek."
            };
            values["pl"] = new string[]
            {
                "Najedź kursorem na opcję, aby zobaczyć jej pełną nazwę i opis.",
                "Jedna wartość w każdym wierszu. Wartość na liście jest uwzględniona; usuń ją, aby ją wykluczyć.",
                "Po zapisaniu zmiany zostaną zastosowane po ponownym uruchomieniu gry.",
                "Informacja",
                "Zapisz",
                "Zamknij",
                "Ustawienia zostały zmienione. Kliknij ponownie, aby zamknąć bez zapisywania.",
                "Błąd",
                "W tej konfiguracji nie ma ustawień, które można edytować.",
                "Nie udało się bezpiecznie przetworzyć konfiguracji. Zobacz dziennik BepInEx."
            };
            values["pt-br"] = new string[]
            {
                "Passe o cursor sobre uma opção para ver o nome completo e a descrição.",
                "Um valor por linha. Um valor listado está incluído; remova-o para excluí-lo.",
                "Depois de salvar, as alterações serão aplicadas ao reiniciar o jogo.",
                "Informações",
                "Salvar",
                "Fechar",
                "As configurações foram alteradas. Clique novamente para fechar sem salvar.",
                "Erro",
                "Não há configurações editáveis nesta configuração.",
                "Não foi possível processar a configuração com segurança. Consulte o log do BepInEx."
            };
            values["ru"] = new string[]
            {
                "Наведите указатель на параметр, чтобы увидеть его полное название и описание.",
                "Одно значение в строке. Значение в списке включено; удалите его, чтобы исключить.",
                "После сохранения изменения вступят в силу после перезапуска игры.",
                "Информация",
                "Сохранить",
                "Закрыть",
                "Настройки изменены. Нажмите ещё раз, чтобы закрыть без сохранения.",
                "Ошибка",
                "В этой конфигурации нет редактируемых параметров.",
                "Не удалось безопасно обработать конфигурацию. См. журнал BepInEx."
            };
            values["sv"] = new string[]
            {
                "Håll pekaren över ett alternativ för att visa dess fullständiga namn och beskrivning.",
                "Ett värde per rad. Ett listat värde ingår; ta bort det för att utesluta det.",
                "När ändringarna har sparats börjar de gälla efter att spelet har startats om.",
                "Info",
                "Spara",
                "Stäng",
                "Inställningarna har ändrats. Klicka igen för att stänga utan att spara.",
                "Fel",
                "Det finns inga redigerbara inställningar i den här konfigurationen.",
                "Konfigurationen kunde inte behandlas säkert. Se BepInEx-loggen."
            };
            values["tr"] = new string[]
            {
                "Tam adını ve açıklamasını görmek için bir seçeneğin üzerine gelin.",
                "Her satıra bir değer. Listelenen değer dahildir; hariç tutmak için kaldırın.",
                "Kaydedildikten sonra değişiklikler oyun yeniden başlatıldığında uygulanır.",
                "Bilgi",
                "Kaydet",
                "Kapat",
                "Ayarlar değiştirildi. Kaydetmeden kapatmak için tekrar tıklayın.",
                "Hata",
                "Bu yapılandırmada düzenlenebilir ayar yok.",
                "Yapılandırma güvenli şekilde işlenemedi. BepInEx günlüğüne bakın."
            };
            values["uk"] = new string[]
            {
                "Наведіть вказівник на параметр, щоб переглянути його повну назву й опис.",
                "Одне значення на рядок. Значення у списку включено; видаліть його, щоб виключити.",
                "Після збереження зміни набудуть чинності після перезапуску гри.",
                "Інформація",
                "Зберегти",
                "Закрити",
                "Налаштування змінено. Натисніть ще раз, щоб закрити без збереження.",
                "Помилка",
                "У цій конфігурації немає параметрів, які можна редагувати.",
                "Не вдалося безпечно обробити конфігурацію. Див. журнал BepInEx."
            };
            values["zh-cn"] = new string[]
            {
                "将鼠标悬停在选项上可查看完整名称和说明。",
                "每行一个值。列出的值会被包含；删除该值可将其排除。",
                "保存后，更改将在重新启动游戏后生效。",
                "信息",
                "保存",
                "关闭",
                "设置已更改。再次点击可在不保存的情况下关闭。",
                "错误",
                "此配置中没有可编辑的设置。",
                "无法安全处理配置。请查看 BepInEx 日志。"
            };
            values["zh-tw"] = new string[]
            {
                "將滑鼠游標停在選項上可查看完整名稱和說明。",
                "每行一個值。列出的值會被包含；刪除該值可將其排除。",
                "儲存後，變更將在重新啟動遊戲後生效。",
                "資訊",
                "儲存",
                "關閉",
                "設定已變更。再次點擊可在不儲存的情況下關閉。",
                "錯誤",
                "此設定中沒有可編輯的項目。",
                "無法安全處理設定。請查看 BepInEx 記錄。"
            };

            return values;
        }

        private static bool TryGetConfigEditorString(
            string language,
            string stringId,
            out string value)
        {
            value = null;
            int index = -1;

            for (int i = 0; i < ConfigEditorStringIds.Length; i++)
            {
                if (string.Equals(
                        ConfigEditorStringIds[i],
                        stringId,
                        StringComparison.Ordinal))
                {
                    index = i;
                    break;
                }
            }

            if (index < 0)
            {
                return false;
            }

            string[] strings;
            if (!ConfigEditorTranslations.TryGetValue(
                    language,
                    out strings))
            {
                strings = ConfigEditorTranslations["en"];
            }

            if (strings == null ||
                strings.Length != ConfigEditorStringIds.Length)
            {
                strings = ConfigEditorTranslations["en"];
            }

            value = strings[index];
            return true;
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

        private static readonly Dictionary<string, string> EnglishInstallerStrings =
            CreateEnglishInstallerStrings();

        private static readonly Dictionary<string, string> FrenchInstallerStrings =
            CreateFrenchInstallerStrings();

        private static readonly Dictionary<string, Dictionary<string, string>>
            InstallerTranslations =
                CreateInstallerTranslations();

        internal static string Get(string stringId)
        {
            string language =
                NormalizeLanguageCode(GetCurrentLanguage());

            if (stringId == RecommendedTitleId ||
                stringId == RecommendedInstallId ||
                stringId == RecommendedEmptyId)
            {
                string[] labels;
                if (!RecommendedTranslations.TryGetValue(language, out labels))
                    labels = RecommendedTranslations["en"];
                return labels[stringId == RecommendedTitleId ? 0 :
                    stringId == RecommendedInstallId ? 1 : 2];
            }

            if (stringId == ConfigureActionId)
            {
                string caption;
                if (ConfigureActionTranslations.TryGetValue(language, out caption))
                    return caption;
                return ConfigureActionTranslations["en"];
            }

            if (stringId == ConfigureTooltipId)
            {
                string caption;
                if (ConfigureTooltipTranslations.TryGetValue(language, out caption))
                    return caption;
                return ConfigureTooltipTranslations["en"];
            }

            string configEditorValue;
            if (TryGetConfigEditorString(
                    language,
                    stringId,
                    out configEditorValue))
            {
                return configEditorValue;
            }

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

            Dictionary<string, string> installerStrings;

            if (!InstallerTranslations.TryGetValue(
                    language,
                    out installerStrings))
            {
                installerStrings =
                    EnglishInstallerStrings;
            }

            string installerValue;
            if (installerStrings.TryGetValue(
                    stringId,
                    out installerValue))
            {
                return installerValue;
            }

            return stringId;
        }

        private static Dictionary<string, string> CreateEnglishInstallerStrings()
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(
                    StringComparer.Ordinal);

            values[UpdateActionId] = "Update";
            values[CancelActionId] = "Cancel";
            values[RetryActionId] = "Retry";
            values[QueuedActionId] = "Queued";
            values[WorkingActionId] = "Working...";
            values[DownloadingId] = "Downloading";
            values[VerifyingId] = "Verifying package";
            values[PreparingInstallId] = "Preparing installation";
            values[RestartRequiredId] = "Ready - restart Project Hospital to install.";
            values[CancelledId] = "Download cancelled.";
            values[PackageCatalogLoadingId] = "Loading update package information...";
            values[PackageUnavailableId] = "Automatic installation is not available for this update.";
            values[PackageCatalogErrorId] = "Update package information is unavailable. Check your Internet connection and retry.";
            values[UpdateBusyId] = "Another update is currently being prepared.";
            values[ReadyToUpdateId] = "Ready to download and install after restart.";
            values[DownloadErrorId] = "Download failed. Check your Internet connection and try again.";
            values[InstallPreparationErrorId] = "The update could not be prepared safely. See the BepInEx log.";
            values[NotEnoughSpaceId] = "Not enough free disk space to prepare this update.";
            values[InstallSucceededId] = "Update installed successfully.";
            values[InstallFailedId] = "Update installation failed; the previous version was preserved or restored.";
            values[ConfigAddedId] = "New configuration options added:";
            values[ConfigWarningId] = "The existing configuration could not be migrated safely and was left unchanged.";

            return values;
        }

        private static Dictionary<string, string> CreateFrenchInstallerStrings()
        {
            Dictionary<string, string> values =
                new Dictionary<string, string>(
                    StringComparer.Ordinal);

            values[UpdateActionId] = "Mettre à jour";
            values[CancelActionId] = "Annuler";
            values[RetryActionId] = "Réessayer";
            values[QueuedActionId] = "En attente";
            values[WorkingActionId] = "En cours...";
            values[DownloadingId] = "Téléchargement";
            values[VerifyingId] = "Vérification du paquet";
            values[PreparingInstallId] = "Préparation de l'installation";
            values[RestartRequiredId] = "Prêt - redémarrez Project Hospital pour installer.";
            values[CancelledId] = "Téléchargement annulé.";
            values[PackageCatalogLoadingId] = "Chargement des informations d'installation...";
            values[PackageUnavailableId] = "L'installation automatique n'est pas disponible pour cette mise à jour.";
            values[PackageCatalogErrorId] = "Les informations d'installation sont indisponibles. Vérifiez votre connexion Internet puis réessayez.";
            values[UpdateBusyId] = "Une autre mise à jour est en cours de préparation.";
            values[ReadyToUpdateId] = "Prêt à télécharger et à installer après redémarrage.";
            values[DownloadErrorId] = "Échec du téléchargement. Vérifiez votre connexion Internet puis réessayez.";
            values[InstallPreparationErrorId] = "La mise à jour n'a pas pu être préparée en toute sécurité. Voir le journal BepInEx.";
            values[NotEnoughSpaceId] = "Espace disque insuffisant pour préparer cette mise à jour.";
            values[InstallSucceededId] = "Mise à jour installée avec succès.";
            values[InstallFailedId] = "Échec de l'installation ; la version précédente a été conservée ou restaurée.";
            values[ConfigAddedId] = "Nouvelles options de configuration ajoutées :";
            values[ConfigWarningId] = "La configuration existante n'a pas pu être migrée en toute sécurité et a été laissée inchangée.";

            return values;
        }


        private static Dictionary<string, Dictionary<string, string>>
            CreateInstallerTranslations()
        {
            Dictionary<string, Dictionary<string, string>> translations =
                new Dictionary<string, Dictionary<string, string>>(
                    StringComparer.OrdinalIgnoreCase);

            translations["en"] = EnglishInstallerStrings;
            translations["fr"] = FrenchInstallerStrings;

            translations["cs"] =
                CreateInstallerStrings(
                    "Aktualizovat",
                    "Zrušit",
                    "Zkusit znovu",
                    "Ve frontě",
                    "Probíhá...",
                    "Stahování",
                    "Ověřování balíčku",
                    "Příprava instalace",
                    "Připraveno – restartujte Project Hospital pro instalaci.",
                    "Stahování zrušeno.",
                    "Načítání informací o aktualizačním balíčku...",
                    "Automatická instalace není pro tuto aktualizaci dostupná.",
                    "Informace o aktualizačním balíčku nejsou dostupné. Zkontrolujte připojení k internetu a zkuste to znovu.",
                    "Právě se připravuje jiná aktualizace.",
                    "Připraveno ke stažení a instalaci po restartu.",
                    "Stahování se nezdařilo. Zkontrolujte připojení k internetu a zkuste to znovu.",
                    "Aktualizaci se nepodařilo bezpečně připravit. Viz protokol BepInEx.",
                    "Nedostatek volného místa na disku pro přípravu této aktualizace.",
                    "Aktualizace byla úspěšně nainstalována.",
                    "Instalace aktualizace selhala; předchozí verze byla zachována nebo obnovena.",
                    "Přidány nové možnosti konfigurace:",
                    "Stávající konfiguraci se nepodařilo bezpečně migrovat a zůstala beze změny.");

            translations["da"] =
                CreateInstallerStrings(
                    "Opdater",
                    "Annuller",
                    "Prøv igen",
                    "I kø",
                    "Arbejder...",
                    "Downloader",
                    "Kontrollerer pakke",
                    "Forbereder installation",
                    "Klar – genstart Project Hospital for at installere.",
                    "Download annulleret.",
                    "Indlæser oplysninger om opdateringspakken...",
                    "Automatisk installation er ikke tilgængelig for denne opdatering.",
                    "Oplysninger om opdateringspakken er ikke tilgængelige. Kontrollér din internetforbindelse, og prøv igen.",
                    "En anden opdatering er ved at blive forberedt.",
                    "Klar til download og installation efter genstart.",
                    "Download mislykkedes. Kontrollér din internetforbindelse, og prøv igen.",
                    "Opdateringen kunne ikke forberedes sikkert. Se BepInEx-loggen.",
                    "Der er ikke nok ledig diskplads til at forberede denne opdatering.",
                    "Opdateringen blev installeret.",
                    "Installationen af opdateringen mislykkedes; den tidligere version blev bevaret eller gendannet.",
                    "Nye konfigurationsindstillinger tilføjet:",
                    "Den eksisterende konfiguration kunne ikke migreres sikkert og blev ikke ændret.");

            translations["de"] =
                CreateInstallerStrings(
                    "Aktualisieren",
                    "Abbrechen",
                    "Erneut versuchen",
                    "Warteschlange",
                    "Wird verarbeitet...",
                    "Wird heruntergeladen",
                    "Paket wird überprüft",
                    "Installation wird vorbereitet",
                    "Bereit – Project Hospital neu starten, um zu installieren.",
                    "Download abgebrochen.",
                    "Update-Paketinformationen werden geladen...",
                    "Automatische Installation ist für dieses Update nicht verfügbar.",
                    "Update-Paketinformationen sind nicht verfügbar. Internetverbindung prüfen und erneut versuchen.",
                    "Ein anderes Update wird gerade vorbereitet.",
                    "Bereit zum Herunterladen und Installieren nach dem Neustart.",
                    "Download fehlgeschlagen. Internetverbindung prüfen und erneut versuchen.",
                    "Das Update konnte nicht sicher vorbereitet werden. Siehe BepInEx-Log.",
                    "Nicht genügend freier Speicherplatz, um dieses Update vorzubereiten.",
                    "Update erfolgreich installiert.",
                    "Installation des Updates fehlgeschlagen; die vorherige Version wurde beibehalten oder wiederhergestellt.",
                    "Neue Konfigurationsoptionen hinzugefügt:",
                    "Die vorhandene Konfiguration konnte nicht sicher migriert werden und blieb unverändert.");

            translations["es"] =
                CreateInstallerStrings(
                    "Actualizar",
                    "Cancelar",
                    "Reintentar",
                    "En cola",
                    "En curso...",
                    "Descargando",
                    "Verificando paquete",
                    "Preparando instalación",
                    "Listo: reinicia Project Hospital para instalar.",
                    "Descarga cancelada.",
                    "Cargando información del paquete de actualización...",
                    "La instalación automática no está disponible para esta actualización.",
                    "La información del paquete de actualización no está disponible. Comprueba tu conexión a Internet y vuelve a intentarlo.",
                    "Se está preparando otra actualización.",
                    "Listo para descargar e instalar tras reiniciar.",
                    "La descarga ha fallado. Comprueba tu conexión a Internet y vuelve a intentarlo.",
                    "No se pudo preparar la actualización de forma segura. Consulta el registro de BepInEx.",
                    "No hay suficiente espacio libre en disco para preparar esta actualización.",
                    "Actualización instalada correctamente.",
                    "La instalación de la actualización ha fallado; se conservó o restauró la versión anterior.",
                    "Nuevas opciones de configuración añadidas:",
                    "La configuración existente no se pudo migrar de forma segura y se dejó sin cambios.");

            translations["es-419"] =
                CreateInstallerStrings(
                    "Actualizar",
                    "Cancelar",
                    "Reintentar",
                    "En cola",
                    "Procesando...",
                    "Descargando",
                    "Verificando paquete",
                    "Preparando instalación",
                    "Listo: reinicia Project Hospital para instalar.",
                    "Descarga cancelada.",
                    "Cargando información del paquete de actualización...",
                    "La instalación automática no está disponible para esta actualización.",
                    "La información del paquete de actualización no está disponible. Verifica tu conexión a Internet y vuelve a intentarlo.",
                    "Se está preparando otra actualización.",
                    "Listo para descargar e instalar después de reiniciar.",
                    "La descarga falló. Verifica tu conexión a Internet y vuelve a intentarlo.",
                    "No se pudo preparar la actualización de forma segura. Revisa el registro de BepInEx.",
                    "No hay suficiente espacio libre en disco para preparar esta actualización.",
                    "Actualización instalada correctamente.",
                    "La instalación de la actualización falló; se conservó o restauró la versión anterior.",
                    "Nuevas opciones de configuración agregadas:",
                    "La configuración existente no se pudo migrar de forma segura y se dejó sin cambios.");

            translations["hu"] =
                CreateInstallerStrings(
                    "Frissítés",
                    "Mégse",
                    "Újrapróbálás",
                    "Várólistán",
                    "Folyamatban...",
                    "Letöltés",
                    "Csomag ellenőrzése",
                    "Telepítés előkészítése",
                    "Kész – indítsd újra a Project Hospitalt a telepítéshez.",
                    "Letöltés megszakítva.",
                    "Frissítési csomag adatainak betöltése...",
                    "Az automatikus telepítés ehhez a frissítéshez nem érhető el.",
                    "A frissítési csomag adatai nem érhetők el. Ellenőrizd az internetkapcsolatot, majd próbáld újra.",
                    "Egy másik frissítés előkészítése folyamatban van.",
                    "Letöltésre kész; az újraindítás után települ.",
                    "A letöltés sikertelen. Ellenőrizd az internetkapcsolatot, majd próbáld újra.",
                    "A frissítést nem sikerült biztonságosan előkészíteni. Lásd a BepInEx naplót.",
                    "Nincs elég szabad lemezterület a frissítés előkészítéséhez.",
                    "A frissítés sikeresen telepítve.",
                    "A frissítés telepítése sikertelen; az előző verzió megmaradt vagy vissza lett állítva.",
                    "Új konfigurációs beállítások hozzáadva:",
                    "A meglévő konfiguráció nem volt biztonságosan migrálható, ezért változatlan maradt.");

            translations["it"] =
                CreateInstallerStrings(
                    "Aggiorna",
                    "Annulla",
                    "Riprova",
                    "In coda",
                    "In corso...",
                    "Download",
                    "Verifica pacchetto",
                    "Preparazione installazione",
                    "Pronto: riavvia Project Hospital per installare.",
                    "Download annullato.",
                    "Caricamento delle informazioni sul pacchetto di aggiornamento...",
                    "L'installazione automatica non è disponibile per questo aggiornamento.",
                    "Le informazioni sul pacchetto di aggiornamento non sono disponibili. Controlla la connessione Internet e riprova.",
                    "È in preparazione un altro aggiornamento.",
                    "Pronto per il download e l'installazione dopo il riavvio.",
                    "Download non riuscito. Controlla la connessione Internet e riprova.",
                    "Non è stato possibile preparare l'aggiornamento in modo sicuro. Consulta il log di BepInEx.",
                    "Spazio libero su disco insufficiente per preparare questo aggiornamento.",
                    "Aggiornamento installato correttamente.",
                    "Installazione dell'aggiornamento non riuscita; la versione precedente è stata mantenuta o ripristinata.",
                    "Nuove opzioni di configurazione aggiunte:",
                    "Non è stato possibile migrare in modo sicuro la configurazione esistente, che è rimasta invariata.");

            translations["ja"] =
                CreateInstallerStrings(
                    "更新",
                    "キャンセル",
                    "再試行",
                    "待機中",
                    "処理中...",
                    "ダウンロード中",
                    "パッケージを検証中",
                    "インストールを準備中",
                    "準備完了 - Project Hospital を再起動するとインストールされます。",
                    "ダウンロードをキャンセルしました。",
                    "更新パッケージ情報を読み込み中...",
                    "この更新では自動インストールを利用できません。",
                    "更新パッケージ情報を取得できません。インターネット接続を確認して再試行してください。",
                    "別の更新を準備しています。",
                    "ダウンロードの準備完了。再起動後にインストールされます。",
                    "ダウンロードに失敗しました。インターネット接続を確認して再試行してください。",
                    "更新を安全に準備できませんでした。BepInEx ログを確認してください。",
                    "この更新を準備するための空きディスク容量が不足しています。",
                    "更新を正常にインストールしました。",
                    "更新のインストールに失敗しました。以前のバージョンは保持または復元されました。",
                    "新しい設定項目を追加しました:",
                    "既存の設定を安全に移行できなかったため、変更せずに保持しました。");

            translations["ko"] =
                CreateInstallerStrings(
                    "업데이트",
                    "취소",
                    "다시 시도",
                    "대기 중",
                    "작업 중...",
                    "다운로드 중",
                    "패키지 확인 중",
                    "설치 준비 중",
                    "준비 완료 - 설치하려면 Project Hospital을 다시 시작하세요.",
                    "다운로드가 취소되었습니다.",
                    "업데이트 패키지 정보 불러오는 중...",
                    "이 업데이트에서는 자동 설치를 사용할 수 없습니다.",
                    "업데이트 패키지 정보를 가져올 수 없습니다. 인터넷 연결을 확인한 후 다시 시도하세요.",
                    "다른 업데이트를 준비 중입니다.",
                    "다운로드 준비 완료. 다시 시작한 후 설치됩니다.",
                    "다운로드에 실패했습니다. 인터넷 연결을 확인한 후 다시 시도하세요.",
                    "업데이트를 안전하게 준비하지 못했습니다. BepInEx 로그를 확인하세요.",
                    "이 업데이트를 준비할 디스크 여유 공간이 부족합니다.",
                    "업데이트가 성공적으로 설치되었습니다.",
                    "업데이트 설치에 실패했습니다. 이전 버전이 유지되거나 복원되었습니다.",
                    "새 구성 옵션이 추가되었습니다:",
                    "기존 구성을 안전하게 마이그레이션할 수 없어 변경하지 않았습니다.");

            translations["nl"] =
                CreateInstallerStrings(
                    "Bijwerken",
                    "Annuleren",
                    "Opnieuw proberen",
                    "In wachtrij",
                    "Bezig...",
                    "Downloaden",
                    "Pakket verifiëren",
                    "Installatie voorbereiden",
                    "Gereed - start Project Hospital opnieuw om te installeren.",
                    "Download geannuleerd.",
                    "Informatie over updatepakket laden...",
                    "Automatische installatie is niet beschikbaar voor deze update.",
                    "Informatie over het updatepakket is niet beschikbaar. Controleer je internetverbinding en probeer opnieuw.",
                    "Er wordt al een andere update voorbereid.",
                    "Gereed om te downloaden en na opnieuw starten te installeren.",
                    "Download mislukt. Controleer je internetverbinding en probeer opnieuw.",
                    "De update kon niet veilig worden voorbereid. Zie het BepInEx-logboek.",
                    "Onvoldoende vrije schijfruimte om deze update voor te bereiden.",
                    "Update is geïnstalleerd.",
                    "Installatie van de update is mislukt; de vorige versie is behouden of hersteld.",
                    "Nieuwe configuratieopties toegevoegd:",
                    "De bestaande configuratie kon niet veilig worden gemigreerd en is ongewijzigd gebleven.");

            translations["pl"] =
                CreateInstallerStrings(
                    "Aktualizuj",
                    "Anuluj",
                    "Spróbuj ponownie",
                    "W kolejce",
                    "W toku...",
                    "Pobieranie",
                    "Weryfikacja pakietu",
                    "Przygotowywanie instalacji",
                    "Gotowe — uruchom ponownie Project Hospital, aby zainstalować.",
                    "Pobieranie anulowane.",
                    "Ładowanie informacji o pakiecie aktualizacji...",
                    "Automatyczna instalacja nie jest dostępna dla tej aktualizacji.",
                    "Informacje o pakiecie aktualizacji są niedostępne. Sprawdź połączenie z Internetem i spróbuj ponownie.",
                    "Trwa przygotowywanie innej aktualizacji.",
                    "Gotowe do pobrania i instalacji po ponownym uruchomieniu.",
                    "Pobieranie nie powiodło się. Sprawdź połączenie z Internetem i spróbuj ponownie.",
                    "Nie udało się bezpiecznie przygotować aktualizacji. Zobacz dziennik BepInEx.",
                    "Za mało wolnego miejsca na dysku, aby przygotować tę aktualizację.",
                    "Aktualizacja została pomyślnie zainstalowana.",
                    "Instalacja aktualizacji nie powiodła się; poprzednia wersja została zachowana lub przywrócona.",
                    "Dodano nowe opcje konfiguracji:",
                    "Nie udało się bezpiecznie zmigrować istniejącej konfiguracji, więc pozostawiono ją bez zmian.");

            translations["pt-br"] =
                CreateInstallerStrings(
                    "Atualizar",
                    "Cancelar",
                    "Tentar novamente",
                    "Na fila",
                    "Em andamento...",
                    "Baixando",
                    "Verificando pacote",
                    "Preparando instalação",
                    "Pronto — reinicie o Project Hospital para instalar.",
                    "Download cancelado.",
                    "Carregando informações do pacote de atualização...",
                    "A instalação automática não está disponível para esta atualização.",
                    "As informações do pacote de atualização não estão disponíveis. Verifique sua conexão com a Internet e tente novamente.",
                    "Outra atualização está sendo preparada.",
                    "Pronto para baixar e instalar após reiniciar.",
                    "Falha no download. Verifique sua conexão com a Internet e tente novamente.",
                    "Não foi possível preparar a atualização com segurança. Consulte o log do BepInEx.",
                    "Não há espaço livre em disco suficiente para preparar esta atualização.",
                    "Atualização instalada com sucesso.",
                    "Falha na instalação da atualização; a versão anterior foi mantida ou restaurada.",
                    "Novas opções de configuração adicionadas:",
                    "A configuração existente não pôde ser migrada com segurança e foi mantida sem alterações.");

            translations["ru"] =
                CreateInstallerStrings(
                    "Обновить",
                    "Отмена",
                    "Повторить",
                    "В очереди",
                    "Выполняется...",
                    "Загрузка",
                    "Проверка пакета",
                    "Подготовка установки",
                    "Готово — перезапустите Project Hospital для установки.",
                    "Загрузка отменена.",
                    "Загрузка сведений о пакете обновления...",
                    "Автоматическая установка недоступна для этого обновления.",
                    "Сведения о пакете обновления недоступны. Проверьте подключение к Интернету и повторите попытку.",
                    "Сейчас подготавливается другое обновление.",
                    "Готово к загрузке и установке после перезапуска.",
                    "Ошибка загрузки. Проверьте подключение к Интернету и повторите попытку.",
                    "Не удалось безопасно подготовить обновление. См. журнал BepInEx.",
                    "Недостаточно свободного места на диске для подготовки этого обновления.",
                    "Обновление успешно установлено.",
                    "Не удалось установить обновление; предыдущая версия была сохранена или восстановлена.",
                    "Добавлены новые параметры конфигурации:",
                    "Не удалось безопасно перенести существующую конфигурацию, поэтому она оставлена без изменений.");

            translations["sv"] =
                CreateInstallerStrings(
                    "Uppdatera",
                    "Avbryt",
                    "Försök igen",
                    "I kö",
                    "Arbetar...",
                    "Laddar ned",
                    "Verifierar paket",
                    "Förbereder installation",
                    "Klart – starta om Project Hospital för att installera.",
                    "Nedladdningen avbröts.",
                    "Läser in information om uppdateringspaketet...",
                    "Automatisk installation är inte tillgänglig för den här uppdateringen.",
                    "Information om uppdateringspaketet är inte tillgänglig. Kontrollera internetanslutningen och försök igen.",
                    "En annan uppdatering förbereds just nu.",
                    "Klart för nedladdning och installation efter omstart.",
                    "Nedladdningen misslyckades. Kontrollera internetanslutningen och försök igen.",
                    "Uppdateringen kunde inte förberedas på ett säkert sätt. Se BepInEx-loggen.",
                    "Det finns inte tillräckligt med ledigt diskutrymme för att förbereda uppdateringen.",
                    "Uppdateringen installerades.",
                    "Installationen av uppdateringen misslyckades; den tidigare versionen behölls eller återställdes.",
                    "Nya konfigurationsalternativ har lagts till:",
                    "Den befintliga konfigurationen kunde inte migreras på ett säkert sätt och lämnades oförändrad.");

            translations["tr"] =
                CreateInstallerStrings(
                    "Güncelle",
                    "İptal",
                    "Yeniden dene",
                    "Sırada",
                    "İşleniyor...",
                    "İndiriliyor",
                    "Paket doğrulanıyor",
                    "Kurulum hazırlanıyor",
                    "Hazır — yüklemek için Project Hospital'ı yeniden başlatın.",
                    "İndirme iptal edildi.",
                    "Güncelleme paketi bilgileri yükleniyor...",
                    "Bu güncelleme için otomatik kurulum kullanılamıyor.",
                    "Güncelleme paketi bilgileri kullanılamıyor. İnternet bağlantınızı kontrol edip yeniden deneyin.",
                    "Başka bir güncelleme hazırlanıyor.",
                    "İndirmeye ve yeniden başlatmadan sonra kurulmaya hazır.",
                    "İndirme başarısız. İnternet bağlantınızı kontrol edip yeniden deneyin.",
                    "Güncelleme güvenli şekilde hazırlanamadı. BepInEx günlüğüne bakın.",
                    "Bu güncellemeyi hazırlamak için yeterli boş disk alanı yok.",
                    "Güncelleme başarıyla yüklendi.",
                    "Güncelleme kurulumu başarısız oldu; önceki sürüm korundu veya geri yüklendi.",
                    "Yeni yapılandırma seçenekleri eklendi:",
                    "Mevcut yapılandırma güvenli şekilde taşınamadı ve değiştirilmeden bırakıldı.");

            translations["uk"] =
                CreateInstallerStrings(
                    "Оновити",
                    "Скасувати",
                    "Повторити",
                    "У черзі",
                    "Виконується...",
                    "Завантаження",
                    "Перевірка пакета",
                    "Підготовка встановлення",
                    "Готово — перезапустіть Project Hospital для встановлення.",
                    "Завантаження скасовано.",
                    "Завантаження відомостей про пакет оновлення...",
                    "Автоматичне встановлення недоступне для цього оновлення.",
                    "Відомості про пакет оновлення недоступні. Перевірте підключення до Інтернету та повторіть спробу.",
                    "Зараз готується інше оновлення.",
                    "Готово до завантаження та встановлення після перезапуску.",
                    "Помилка завантаження. Перевірте підключення до Інтернету та повторіть спробу.",
                    "Не вдалося безпечно підготувати оновлення. Див. журнал BepInEx.",
                    "Недостатньо вільного місця на диску для підготовки цього оновлення.",
                    "Оновлення успішно встановлено.",
                    "Не вдалося встановити оновлення; попередню версію збережено або відновлено.",
                    "Додано нові параметри конфігурації:",
                    "Не вдалося безпечно перенести наявну конфігурацію, тому її залишено без змін.");

            translations["zh-cn"] =
                CreateInstallerStrings(
                    "更新",
                    "取消",
                    "重试",
                    "已排队",
                    "处理中...",
                    "正在下载",
                    "正在验证更新包",
                    "正在准备安装",
                    "已准备好 - 重启 Project Hospital 即可安装。",
                    "下载已取消。",
                    "正在加载更新包信息...",
                    "此更新不支持自动安装。",
                    "无法获取更新包信息。请检查网络连接后重试。",
                    "正在准备另一项更新。",
                    "已准备好下载，并将在重启后安装。",
                    "下载失败。请检查网络连接后重试。",
                    "无法安全准备此更新。请查看 BepInEx 日志。",
                    "磁盘可用空间不足，无法准备此更新。",
                    "更新安装成功。",
                    "更新安装失败；已保留或恢复上一版本。",
                    "已添加新的配置选项：",
                    "无法安全迁移现有配置，因此未作更改。");

            translations["zh-tw"] =
                CreateInstallerStrings(
                    "更新",
                    "取消",
                    "重試",
                    "已排入佇列",
                    "處理中...",
                    "正在下載",
                    "正在驗證更新套件",
                    "正在準備安裝",
                    "已準備完成 - 重新啟動 Project Hospital 即可安裝。",
                    "下載已取消。",
                    "正在載入更新套件資訊...",
                    "此更新不支援自動安裝。",
                    "無法取得更新套件資訊。請檢查網路連線後重試。",
                    "正在準備另一項更新。",
                    "已準備好下載，並將在重新啟動後安裝。",
                    "下載失敗。請檢查網路連線後重試。",
                    "無法安全準備此更新。請查看 BepInEx 記錄。",
                    "磁碟可用空間不足，無法準備此更新。",
                    "更新安裝成功。",
                    "更新安裝失敗；已保留或還原上一個版本。",
                    "已新增設定選項：",
                    "無法安全移轉現有設定，因此未做變更。");

            return translations;
        }

        private static Dictionary<string, string> CreateInstallerStrings(
            params string[] strings)
        {
            if (strings == null ||
                strings.Length != 22)
            {
                throw new ArgumentException(
                    "HMUC installer localization requires exactly 22 strings.");
            }

            Dictionary<string, string> values =
                new Dictionary<string, string>(
                    StringComparer.Ordinal);

            values[UpdateActionId] = strings[0];
            values[CancelActionId] = strings[1];
            values[RetryActionId] = strings[2];
            values[QueuedActionId] = strings[3];
            values[WorkingActionId] = strings[4];
            values[DownloadingId] = strings[5];
            values[VerifyingId] = strings[6];
            values[PreparingInstallId] = strings[7];
            values[RestartRequiredId] = strings[8];
            values[CancelledId] = strings[9];
            values[PackageCatalogLoadingId] = strings[10];
            values[PackageUnavailableId] = strings[11];
            values[PackageCatalogErrorId] = strings[12];
            values[UpdateBusyId] = strings[13];
            values[ReadyToUpdateId] = strings[14];
            values[DownloadErrorId] = strings[15];
            values[InstallPreparationErrorId] = strings[16];
            values[NotEnoughSpaceId] = strings[17];
            values[InstallSucceededId] = strings[18];
            values[InstallFailedId] = strings[19];
            values[ConfigAddedId] = strings[20];
            values[ConfigWarningId] = strings[21];

            return values;
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
