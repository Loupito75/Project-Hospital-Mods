using System.Xml;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch(typeof(MapPersistentData), nameof(MapPersistentData.WriteXml))]
    internal static class OneWayMapWritePatch
    {
        private static void Prefix(MapPersistentData __instance, XmlWriter writer)
        {
            OneWayPersistenceManager.WriteSaveAttribute(__instance, writer);
        }
    }

    [HarmonyPatch(typeof(MapPersistentData), nameof(MapPersistentData.ReadXml))]
    internal static class OneWayMapReadPatch
    {
        private static void Prefix(MapPersistentData __instance, XmlReader readerMap)
        {
            OneWayPersistenceManager.ReadSaveAttribute(__instance, readerMap);
        }
    }

    [HarmonyPatch(typeof(MapPersistentData), nameof(MapPersistentData.Clone))]
    internal static class OneWayMapClonePatch
    {
        private static void Postfix(
            MapPersistentData __instance,
            MapPersistentData __result)
        {
            OneWayPersistenceManager.CaptureClone(__instance, __result);
        }
    }

    [HarmonyPatch(typeof(MapPersistentData), nameof(MapPersistentData.ApplyClone))]
    internal static class OneWayMapApplyClonePatch
    {
        private static void Postfix(
            MapPersistentData __instance,
            MapPersistentData clone)
        {
            OneWayPersistenceManager.ApplyClone(__instance, clone);
        }
    }

    [HarmonyPatch(typeof(Floor), nameof(Floor.LoadEntities))]
    internal static class OneWayFloorLoadEntitiesPatch
    {
        private static void Postfix(Floor __instance)
        {
            OneWayPersistenceManager.ApplyLoadedRules(__instance);
        }
    }

    [HarmonyPatch(typeof(Floor), nameof(Floor.ValidateLoad))]
    internal static class OneWayFloorValidateLoadPatch
    {
        private static void Postfix(Floor __instance)
        {
            OneWayPersistenceManager.FinalizeLoadedRules(__instance);
        }
    }
}
