using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using HarmonyLib;

namespace HospitalFixes
{
    internal static class HfxDatabase
    {
        private const string GroundClearancePrefix =
            "L75_HFX_WALL_CLEARANCE_";
        private const string UpperHeightPrefix =
            "L75_HFX_WALL_HEIGHT_";
        private static readonly Dictionary<ID, float> GroundClearances =
            new Dictionary<ID, float>();
        private static readonly Dictionary<ID, float> UpperHeights =
            new Dictionary<ID, float>();
        private static bool s_loading;
        private static bool s_loaded;

        internal static bool IsLoading
        {
            get { return s_loading; }
        }

        internal static void ResetForBaseLoad()
        {
            s_loaded = false;
            GroundClearances.Clear();
            UpperHeights.Clear();
        }

        internal static bool TryGetGroundClearance(
            ID objectId,
            out float clearance)
        {
            clearance = 0f;
            return objectId != null &&
                GroundClearances.TryGetValue(
                    objectId,
                    out clearance);
        }

        internal static bool TryGetUpperHeight(
            ID objectId,
            out float height)
        {
            height = 0f;
            return objectId != null &&
                UpperHeights.TryGetValue(
                    objectId,
                    out height);
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
                    "Hospital Fixes Database directory is missing; " +
                    "construction clearance metadata cannot be loaded.");
                return;
            }

            s_loading = true;
            try
            {
                database.ReadFiles(pluginRoot, true);

                GameDBTweakableFloat sentinel =
                    database.GetEntry<GameDBTweakableFloat>(
                        "L75_HFX_WALL_CLEARANCE_OBJECT_CHAIR_SMALL");
                s_loaded = sentinel != null;

                if (!s_loaded)
                {
                    GroundClearances.Clear();
                    UpperHeights.Clear();
                    Plugin.Log?.LogError(
                        "Hospital Fixes database file was read but " +
                        "construction clearance metadata is missing.");
                }
                else
                {
                    RebuildCaches(database);
                    if (HfxDiagnostics.Enabled)
                    {
                        Plugin.Log?.LogInfo(
                            "[Diagnostics] Hospital Fixes database metadata loaded: " +
                            GroundClearances.Count +
                            " ground clearances, " +
                            UpperHeights.Count +
                            " upper-height overrides.");
                    }
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError(
                    "Failed to load Hospital Fixes database metadata: " +
                    exception);
            }
            finally
            {
                s_loading = false;
            }
        }
        private static void RebuildCaches(Database database)
        {
            GroundClearances.Clear();
            UpperHeights.Clear();

            GameDBTweakableFloat[] entries =
                database.GetEntries<GameDBTweakableFloat>();
            if (entries == null)
            {
                return;
            }

            foreach (GameDBTweakableFloat entry in entries)
            {
                if (entry == null ||
                    entry.DatabaseID == null ||
                    entry.Value <= 0f)
                {
                    continue;
                }

                string id = entry.DatabaseID.ToString();
                if (id.StartsWith(GroundClearancePrefix))
                {
                    string objectId =
                        id.Substring(
                            GroundClearancePrefix.Length);
                    if (objectId.Length > 0)
                    {
                        GroundClearances[
                            ID.CreateID(objectId)] =
                                entry.Value;
                    }
                }
                else if (id.StartsWith(UpperHeightPrefix))
                {
                    string objectId =
                        id.Substring(
                            UpperHeightPrefix.Length);
                    if (objectId.Length > 0)
                    {
                        UpperHeights[
                            ID.CreateID(objectId)] =
                                entry.Value;
                    }
                }
            }
        }
    }

    [HarmonyPatch(
        typeof(Database),
        nameof(Database.ReadFiles),
        new Type[] { typeof(string), typeof(bool) })]
    internal static class HfxDatabaseReadFilesPatch
    {
        private static void Prefix(bool add)
        {
            if (!add && !HfxDatabase.IsLoading)
            {
                HfxDatabase.ResetForBaseLoad();
            }
        }

        private static void Postfix(Database __instance, bool add)
        {
            if (!add && !HfxDatabase.IsLoading)
            {
                HfxDatabase.Load(__instance);
            }
        }
    }
}
