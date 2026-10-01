using System;
using HarmonyLib;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(
        typeof(Database),
        nameof(Database.ReadFiles),
        new Type[] { typeof(string), typeof(bool) })]
    internal static class HtcDatabaseReadFilesPatch
    {
        private static void Prefix(bool add)
        {
            if (!add && !HtcDatabase.IsLoading)
            {
                HtcDatabase.ResetForBaseLoad();
            }
        }

        private static void Postfix(Database __instance, bool add)
        {
            if (!add && !HtcDatabase.IsLoading)
            {
                HtcDatabase.Load(__instance);
            }
        }
    }
}
