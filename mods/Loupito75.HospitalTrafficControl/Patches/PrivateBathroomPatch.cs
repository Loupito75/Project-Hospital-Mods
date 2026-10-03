using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    [HarmonyPatch]
    internal static class HospitalizedBladderPrivateBathroomFallbackPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureSceneFactory),
                nameof(ProcedureSceneFactory.CreateProcedureScene),
                new Type[]
                {
                    typeof(GameDBProcedure),
                    typeof(Entity),
                    typeof(Department),
                    typeof(Room),
                    typeof(AccessRights),
                    typeof(ProcedureSceneType),
                    typeof(EquipmentListRules),
                    typeof(StaffSelectionRules)
                });
        }

        private static void Postfix(
            GameDBProcedure procedure,
            Entity patient,
            EquipmentListRules equipmentListRules,
            ref ProcedureScene __result)
        {
            if (__result == null ||
                equipmentListRules != EquipmentListRules.ONLY_FREE_SAME_FLOOR ||
                !PrivateBathroomManager.IsBladderProcedure(procedure) ||
                !PrivateBathroomManager.IsHospitalizedPatient(patient) ||
                __result.m_equipment == null ||
                __result.m_equipment.Length == 0 ||
                __result.m_equipment[0] != null)
            {
                return;
            }

            TileObject privateBathroom =
                PrivateBathroomManager.FindOwnPrivateBathroom(
                    patient,
                    procedure,
                    AccessRights.PATIENT_PROCEDURE);

            if (privateBathroom == null)
            {
                return;
            }

            __result.m_equipment[0] = privateBathroom;
            __result.m_availability &=
                ~ProcedureSceneAvailability.EQUIPMENT_UNAVAILABLE;

            if (__result.m_availability == (ProcedureSceneAvailability)0)
            {
                __result.m_availability =
                    ProcedureSceneAvailability.AVAILABLE;
            }

            LogFallback(patient, privateBathroom);
        }

        private static void LogFallback(
            Entity character,
            TileObject replacement)
        {
            if (!TrafficControlConfig.BathroomFlowDebug)
            {
                return;
            }

            string characterName =
                character == null
                    ? "<unknown>"
                    : (character.Name ?? string.Empty).Trim();

            Plugin.Log?.LogInfo("[BathroomDebug] PRIVATE_WC_AVAILABILITY_FALLBACK entity='" +
                characterName +
                "' replacement=" + Describe(replacement) + ".");
        }

        private static string Describe(TileObject tileObject)
        {
            if (tileObject == null || tileObject.m_state == null)
            {
                return "<none>";
            }

            string id =
                tileObject.m_state.m_gameDBObject.Entry == null
                    ? "<unknown>"
                    : tileObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();

            return id + "@" + tileObject.m_state.m_position +
                   ",floor=" + tileObject.GetFloorIndex();
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptNeedBladder), nameof(ProcedureScriptNeedBladder.Activate))]
    internal static class PrivateBathroomNeedActivationPatch
    {
        private static bool Prefix(ProcedureScriptNeedBladder __instance)
        {
            if (__instance == null ||
                __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null)
            {
                return true;
            }

            ProcedureScene scene = __instance.m_stateData.m_procedureScene;
            Entity character = scene.MainCharacter;

            if (character == null ||
                scene.m_equipment == null ||
                scene.m_equipment.Length == 0)
            {
                return true;
            }

            GameDBNeed bladderNeed =
                Database.Instance == null
                    ? null
                    : Database.Instance.GetEntry<GameDBNeed>("NEED_BLADDER");
            GameDBProcedure bladderProcedure =
                bladderNeed == null ? null : bladderNeed.Procedure;

            TileObject selected =
                scene.m_equipment[0] == null
                    ? null
                    : scene.m_equipment[0].GetEntity();

            // HTC owns only the private-bathroom rule. Vanilla/HPL remain
            // authoritative for the general WC search. If this hospitalized patient
            // has a valid private WC attached to the assigned hospitalization room,
            // prefer that exact HTC-classified endpoint using PATIENT_PROCEDURE.
            if (PrivateBathroomManager.IsHospitalizedPatient(character) &&
                bladderProcedure != null)
            {
                TileObject ownPrivate =
                    PrivateBathroomManager.FindOwnPrivateBathroom(
                        character,
                        bladderProcedure,
                        AccessRights.PATIENT_PROCEDURE);

                if (ownPrivate != null)
                {
                    if (!object.ReferenceEquals(selected, ownPrivate))
                    {
                        scene.m_equipment[0] = ownPrivate;
                        LogPreference(character, selected, ownPrivate);
                    }

                    return true;
                }
            }

            if (selected == null ||
                !PrivateBathroomManager.IsPrivateHospitalBathroom(selected))
            {
                return true;
            }

            if (PrivateBathroomManager.IsPrivateHospitalBathroomForPatient(
                    selected,
                    character))
            {
                return true;
            }

            Behavior behavior = character.GetComponent<Behavior>();
            AccessRights accessRights =
                behavior == null
                    ? AccessRights.STAFF
                    : behavior.GetAccessRights();

            TileObject replacement =
                PrivateBathroomManager.FindAllowedBladderReplacement(
                    character,
                    bladderProcedure,
                    accessRights);

            if (selected.Owner == __instance)
            {
                selected.Owner = null;
            }

            if (replacement == null)
            {
                scene.m_equipment[0] = null;
                __instance.SwitchState(ProcedureScriptNeedBladder.STATE_IDLE);
                LogPolicy(character, selected, null);
                return false;
            }

            scene.m_equipment[0] = replacement;
            if (replacement.Owner == null)
            {
                replacement.Owner = __instance;
            }

            LogPolicy(character, selected, replacement);
            return true;
        }

        private static void LogPreference(
            Entity character,
            TileObject original,
            TileObject preferred)
        {
            if (!TrafficControlConfig.BathroomFlowDebug)
            {
                return;
            }

            string characterName =
                character == null
                    ? "<unknown>"
                    : (character.Name ?? string.Empty).Trim();

            Plugin.Log?.LogInfo(
                "[BathroomDebug] PRIVATE_WC_PREFERRED entity='" +
                characterName +
                "' original=" + Describe(original) +
                " preferred=" + Describe(preferred) + ".");
        }

        private static void LogPolicy(
            Entity character,
            TileObject blocked,
            TileObject replacement)
        {
            if (!TrafficControlConfig.BathroomFlowDebug)
            {
                return;
            }

            string characterName =
                character == null
                    ? "<unknown>"
                    : (character.Name ?? string.Empty).Trim();

            Plugin.Log?.LogInfo("[BathroomDebug] PRIVATE_WC_POLICY entity='" +
                characterName +
                "' blocked=" + Describe(blocked) +
                " replacement=" + Describe(replacement) + ".");
        }

        private static string Describe(TileObject tileObject)
        {
            if (tileObject == null || tileObject.m_state == null)
            {
                return "<none>";
            }

            string id =
                tileObject.m_state.m_gameDBObject.Entry == null
                    ? "<unknown>"
                    : tileObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();

            return id + "@" + tileObject.m_state.m_position +
                   ",floor=" + tileObject.GetFloorIndex();
        }
    }

    [HarmonyPatch]
    internal static class PrivateBathroomFixtureAccessPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                nameof(MapScriptInterface.FindClosestFreeObjectWithTag),
                new Type[]
                {
                    typeof(Entity),
                    typeof(Entity),
                    typeof(Vector2i),
                    typeof(Room),
                    typeof(string),
                    typeof(AccessRights),
                    typeof(bool),
                    typeof(DatabaseEntryRef<GameDBRoomType>[]),
                    typeof(bool)
                });
        }

        private static void Prefix(
            Entity character,
            Entity owner,
            Room room,
            string tag,
            ref AccessRights accessRights)
        {
            if (accessRights != AccessRights.PATIENT ||
                (tag != "washing" && tag != "dryer") ||
                character == null ||
                owner == null ||
                (owner as ProcedureScriptNeedBladder) == null ||
                !PrivateBathroomManager.IsHospitalizedPatient(character) ||
                room == null ||
                Hospital.Instance == null)
            {
                return;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            if (PrivateBathroomManager.IsPrivateHospitalBathroom(
                    room,
                    Hospital.Instance.m_floors[floorIndex]))
            {
                accessRights = AccessRights.PATIENT_PROCEDURE;
            }
        }
    }
}
