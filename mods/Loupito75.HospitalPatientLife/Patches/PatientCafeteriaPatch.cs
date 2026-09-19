using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class PatientCafeteriaRules
    {
        private const string PatientHungerNeedId = "NEED_HUNGER_PATIENT";
        internal const string CafeteriaRoomTag = "cafeteria";

        internal static bool IsPatientHungerProcedure(GameDBProcedure procedure)
        {
            if (procedure == null || Database.Instance == null)
            {
                return false;
            }

            GameDBNeed patientHunger = Database.Instance.GetEntry<GameDBNeed>(PatientHungerNeedId);
            return patientHunger != null && object.ReferenceEquals(patientHunger.Procedure, procedure);
        }

        internal static bool IsHospitalizedPatient(Entity patient)
        {
            if (patient == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization = patient.GetComponent<HospitalizationComponent>();
            return hospitalization != null && hospitalization.IsHospitalized();
        }

        internal static bool IsCafeteriaRoom(Room room)
        {
            if (room == null || room.m_roomPersistentData == null || room.m_roomPersistentData.m_roomType == null)
            {
                return false;
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            return roomType != null && roomType.HasTag(CafeteriaRoomTag);
        }
    }

    [HarmonyPatch]
    internal static class PatientHungerProcedureScenePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureSceneFactory),
                "CreateProcedureScene",
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

        private static void Prefix(
            GameDBProcedure procedure,
            Entity patient,
            ref AccessRights accessRights,
            ref EquipmentListRules equipmentListRules)
        {
            if (!PatientCafeteriaRules.IsPatientHungerProcedure(procedure))
            {
                return;
            }

            if (equipmentListRules == EquipmentListRules.ONLY_FREE_SAME_FLOOR)
            {
                // Reuse the native preferred-department branch used by staff hunger.
                equipmentListRules = EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT;
            }

            if (PatientCafeteriaRules.IsHospitalizedPatient(patient) && accessRights == AccessRights.PATIENT)
            {
                // BehaviorPatient.GetAccessRights() already grants hospitalized patients
                // PATIENT_PROCEDURE. Use the same level while selecting hunger equipment,
                // allowing an explicitly blue cafeteria to be selected.
                accessRights = AccessRights.PATIENT_PROCEDURE;
            }
        }
    }

    [HarmonyPatch]
    internal static class PatientHungerCafeteriaRoomTagPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.PropertyGetter(typeof(GameDBProcedure), "IgnoreDepratmentForRoom");
        }

        private static void Postfix(GameDBProcedure __instance, ref string __result)
        {
            if (!string.IsNullOrEmpty(__result))
            {
                return;
            }

            if (PatientCafeteriaRules.IsPatientHungerProcedure(__instance))
            {
                __result = PatientCafeteriaRules.CafeteriaRoomTag;
            }
        }
    }
}
