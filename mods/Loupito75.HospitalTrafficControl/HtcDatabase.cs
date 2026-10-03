using System;
using System.IO;
using System.Reflection;

namespace HospitalTrafficControl
{
    internal static class HtcDatabase
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
        }

        internal static void Load(Database database)
        {
            if (database == null || s_loading || s_loaded)
            {
                return;
            }

            string pluginRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string databaseDirectory = string.IsNullOrEmpty(pluginRoot)
                ? null
                : Path.Combine(pluginRoot, "Database");

            if (string.IsNullOrEmpty(databaseDirectory) || !Directory.Exists(databaseDirectory))
            {
                Plugin.Log?.LogError(
                    "HTC Database directory is missing; native notification registration cannot be loaded.");
                return;
            }

            string legacyDatabaseFile =
                Path.Combine(databaseDirectory, "HTCNotifications.xml");

            if (File.Exists(legacyDatabaseFile))
            {
                Plugin.Log?.LogError(
                    "Legacy HTC database file detected: Database\\HTCNotifications.xml. " +
                    "Delete this obsolete file from the HospitalTrafficControl plugin folder. " +
                    "Keeping it can load duplicate database entries.");
            }

            s_loading = true;
            try
            {
                database.ReadFiles(pluginRoot, true);

                GameDBNotification notification =
                    database.GetEntry<GameDBNotification>(LocalizationManager.NoPathTitleId);

                if (notification == null)
                {
                    Plugin.Log?.LogError(
                        "HTC database file was read but the blocked-path notification entry is missing.");
                    return;
                }

                s_loaded = true;
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError(
                    "Failed to load HTC database entries: " +
                    exception.GetType().FullName + ": " + exception.Message);
            }
            finally
            {
                s_loading = false;
            }
        }
    }
}
