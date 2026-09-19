using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalizedPatientTrace
    {
        private const string Prefix = "[PatientTrace] ";
        private static readonly Dictionary<Entity, int> FailedNeedBuckets = new Dictionary<Entity, int>();

        internal static bool Enabled
        {
            get { return HospitalPatientLifeConfig.DebugLogging; }
        }

        internal static bool IsTrackedPatient(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization = entity.GetComponent<HospitalizationComponent>();
            return hospitalization != null && hospitalization.IsHospitalized();
        }

        internal static string GetName(Entity entity)
        {
            if (entity == null)
            {
                return "<null>";
            }

            return entity.Name == null ? string.Empty : entity.Name.Trim();
        }

        internal static int GetFloor(Entity entity)
        {
            if (entity == null)
            {
                return -1;
            }

            WalkComponent walk = entity.GetComponent<WalkComponent>();
            return walk == null ? -1 : walk.GetFloorIndex();
        }

        internal static string GetGameTime()
        {
            if (DayTime.Instance == null)
            {
                return "<none>";
            }

            float hours = DayTime.Instance.GetDayTimeHours();
            int totalSeconds = (int)(hours * 3600f + 0.5f);
            if (totalSeconds < 0)
            {
                totalSeconds = 0;
            }

            int hour = (totalSeconds / 3600) % 24;
            int minute = (totalSeconds / 60) % 60;
            int second = totalSeconds % 60;

            return "D" + DayTime.Instance.GetDay().ToString() + " " +
                hour.ToString("00") + ":" +
                minute.ToString("00") + ":" +
                second.ToString("00");
        }

        internal static float GetNeedRawValue(Entity entity, string needId)
        {
            if (entity == null)
            {
                return -1f;
            }

            MoodComponent mood = entity.GetComponent<MoodComponent>();
            if (mood == null)
            {
                return -1f;
            }

            Need need = mood.GetNeed(needId);
            return need == null ? -1f : need.m_currentValue;
        }

        internal static string GetNeedValue(Entity entity, string needId)
        {
            float value = GetNeedRawValue(entity, needId);
            return value < 0f ? "n/a" : value.ToString("0.0");
        }

        internal static string GetProcedureId(GameDBProcedure procedure)
        {
            if (procedure == null)
            {
                return "<null>";
            }

            if (!ID.IsNullOrNoID(procedure.DatabaseID))
            {
                return procedure.DatabaseID.ToString();
            }

            return string.IsNullOrEmpty(procedure.ProcedureScript)
                ? "<embedded-procedure>"
                : procedure.ProcedureScript;
        }

        internal static void ResetFailedNeedBucket(Entity patient)
        {
            if (patient != null)
            {
                FailedNeedBuckets.Remove(patient);
            }
        }

        internal static bool ShouldLogFailedNeedCheck(Entity patient, float hunger, float bladder)
        {
            if (!Enabled || patient == null)
            {
                return false;
            }

            float worstNeed = Math.Max(hunger, bladder);
            if (worstNeed < 50f)
            {
                return false;
            }

            int bucket = Math.Min(9, Math.Max(5, (int)(worstNeed / 10f)));
            int previousBucket;
            if (FailedNeedBuckets.TryGetValue(patient, out previousBucket) && previousBucket == bucket)
            {
                return false;
            }

            FailedNeedBuckets[patient] = bucket;
            return true;
        }

        internal static void Log(string message)
        {
            if (!Enabled || Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo(Prefix + message);
        }

        internal static void LogError(string message)
        {
            if (Plugin.Log != null)
            {
                Plugin.Log.LogError(Prefix + message);
            }
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "SwitchState")]
    internal static class HospitalizationStateTracePatch
    {
        private static void Prefix(
            HospitalizationComponent __instance,
            HospitalizationState state,
            out HospitalizationState __state)
        {
            __state = default(HospitalizationState);
            if (!HospitalizedPatientTrace.Enabled || __instance == null || __instance.m_state == null)
            {
                return;
            }

            __state = __instance.m_state.m_hospitalizationState;
        }

        private static void Postfix(
            HospitalizationComponent __instance,
            HospitalizationState state,
            HospitalizationState __state)
        {
            if (!HospitalizedPatientTrace.Enabled ||
                __instance == null ||
                __instance.m_entity == null ||
                !HospitalizedPatientTrace.IsTrackedPatient(__instance.m_entity))
            {
                return;
            }

            if (__state == state)
            {
                return;
            }

            Entity patient = __instance.m_entity;
            string treatment = "none";
            if (__instance.m_state.m_hospitalizationTreatment != null && __instance.m_state.m_hospitalizationTreatment.Entry != null)
            {
                treatment = __instance.m_state.m_hospitalizationTreatment.Entry.DatabaseID.ToString();
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | state=" + __state + " -> " + state +
                " | floor=" + HospitalizedPatientTrace.GetFloor(patient) +
                " | treatment=" + treatment +
                " | canWalk=" + __instance.IsAllowedToWalk() +
                " | hunger=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_HUNGER_PATIENT") +
                " | bladder=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_BLADDER") +
                " | boredom=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_BOREDOM") +
                " | lunchReady=" + __instance.m_state.m_lunchReady +
                " | lunchEaten=" + __instance.m_state.m_lunchEaten);
        }
    }

    [HarmonyPatch(typeof(BehaviorPatient), "CheckNeeds")]
    internal static class HospitalizedNeedsDecisionTracePatch
    {
        private static void Postfix(
            BehaviorPatient __instance,
            AccessRights accessRights,
            bool __result)
        {
            if (!HospitalizedPatientTrace.Enabled ||
                __instance == null ||
                !HospitalizedPatientTrace.IsTrackedPatient(__instance.m_entity))
            {
                return;
            }

            Entity patient = __instance.m_entity;
            float hunger = HospitalizedPatientTrace.GetNeedRawValue(patient, "NEED_HUNGER_PATIENT");
            float bladder = HospitalizedPatientTrace.GetNeedRawValue(patient, "NEED_BLADDER");

            if (__result)
            {
                HospitalizedPatientTrace.ResetFailedNeedBucket(patient);
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | needs-check=STARTED" +
                    " | access=" + accessRights +
                    " | hunger=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_HUNGER_PATIENT") +
                    " | bladder=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_BLADDER") +
                    " | boredom=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_BOREDOM"));
                return;
            }

            if (HospitalizedPatientTrace.ShouldLogFailedNeedCheck(patient, hunger, bladder))
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | needs-check=NO_DESTINATION" +
                    " | access=" + accessRights +
                    " | hunger=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_HUNGER_PATIENT") +
                    " | bladder=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_BLADDER") +
                    " | boredom=" + HospitalizedPatientTrace.GetNeedValue(patient, "NEED_BOREDOM"));
            }
        }
    }

    [HarmonyPatch]
    internal static class HospitalizedProcedureStartTracePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureComponent),
                "StartProcedure",
                new Type[]
                {
                    typeof(GameDBProcedure),
                    typeof(Entity),
                    typeof(Department),
                    typeof(AccessRights),
                    typeof(EquipmentListRules)
                });
        }

        private static void Prefix(
            GameDBProcedure procedure,
            Entity mainCharacter,
            AccessRights accessRights,
            EquipmentListRules equipmentListRules)
        {
            if (!HospitalizedPatientTrace.Enabled ||
                !HospitalizedPatientTrace.IsTrackedPatient(mainCharacter))
            {
                return;
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(mainCharacter) +
                " | procedure-start=" + HospitalizedPatientTrace.GetProcedureId(procedure) +
                " | floor=" + HospitalizedPatientTrace.GetFloor(mainCharacter) +
                " | access=" + accessRights +
                " | equipmentRules=" + equipmentListRules);
        }
    }

    [HarmonyPatch]
    internal static class HospitalizedRoomProcedureStartTracePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureComponent),
                "StartProcedure",
                new Type[]
                {
                    typeof(GameDBProcedure),
                    typeof(Entity),
                    typeof(Department),
                    typeof(Room),
                    typeof(AccessRights),
                    typeof(EquipmentListRules)
                });
        }

        private static void Prefix(
            GameDBProcedure procedure,
            Entity patient,
            Room room,
            AccessRights accessRights,
            EquipmentListRules equipmentListRules)
        {
            if (!HospitalizedPatientTrace.Enabled ||
                !HospitalizedPatientTrace.IsTrackedPatient(patient))
            {
                return;
            }

            string roomType = "none";
            if (room != null && room.m_roomPersistentData != null && room.m_roomPersistentData.m_roomType != null && room.m_roomPersistentData.m_roomType.Entry != null)
            {
                roomType = room.m_roomPersistentData.m_roomType.Entry.DatabaseID.ToString();
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | procedure-start=" + HospitalizedPatientTrace.GetProcedureId(procedure) +
                " | room=" + roomType +
                " | floor=" + HospitalizedPatientTrace.GetFloor(patient) +
                " | access=" + accessRights +
                " | equipmentRules=" + equipmentListRules);
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptNeedHunger), "Activate")]
    internal static class HospitalizedHungerDestinationTracePatch
    {
        private static void Postfix(ProcedureScriptNeedHunger __instance)
        {
            if (!HospitalizedPatientTrace.Enabled ||
                __instance == null ||
                __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null)
            {
                return;
            }

            Entity patient = __instance.m_stateData.m_procedureScene.MainCharacter;
            if (!HospitalizedPatientTrace.IsTrackedPatient(patient))
            {
                return;
            }

            TileObject target = __instance.GetEquipment(0);
            if (target == null)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) + " | hunger-target=<null>");
                return;
            }

            int floorIndex = target.GetFloorIndex();
            Vector2i position = target.m_state.m_position;
            string objectId = target.m_state.m_gameDBObject == null || target.m_state.m_gameDBObject.Entry == null
                ? "<unknown>"
                : target.m_state.m_gameDBObject.Entry.DatabaseID.ToString();

            Room room = null;
            AccessRights paintedRights = AccessRights.PEDESTRIAN;
            AccessRights roomRights = AccessRights.PEDESTRIAN;

            if (Hospital.Instance != null && floorIndex >= 0 && floorIndex < Hospital.Instance.m_floors.Count)
            {
                Floor floor = Hospital.Instance.m_floors[floorIndex];
                if (position.m_x >= 0 && position.m_y >= 0 && position.m_x < floor.m_size.m_x && position.m_y < floor.m_size.m_y)
                {
                    room = floor.m_roomTiles[position.m_x, position.m_y];
                    paintedRights = floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[position.m_x, position.m_y];
                    roomRights = floor.m_roomAccessRights[position.m_x, position.m_y];
                }
            }

            string roomType = "none";
            if (room != null && room.m_roomPersistentData != null && room.m_roomPersistentData.m_roomType != null && room.m_roomPersistentData.m_roomType.Entry != null)
            {
                roomType = room.m_roomPersistentData.m_roomType.Entry.DatabaseID.ToString();
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | hunger-target=" + objectId +
                " | targetFloor=" + floorIndex +
                " | targetPos=" + position.m_x + "," + position.m_y +
                " | room=" + roomType +
                " | cafeteria=" + PatientCafeteriaRules.IsCafeteriaRoom(room) +
                " | paintedAccess=" + paintedRights +
                " | roomAccess=" + roomRights);
        }
    }
}
