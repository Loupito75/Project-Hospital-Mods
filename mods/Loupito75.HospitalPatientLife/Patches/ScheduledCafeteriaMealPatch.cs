using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal sealed class ScheduledMealDecision
    {
        internal bool UseCafeteria;
        internal int Roll;
        internal string MealScheduleId;
        internal int TargetFloor;
    }

    internal static class ScheduledCafeteriaMealState
    {
        private const string BreakfastSchedule = "SCHEDULE_HOSPITALIZED_PATIENTS_BREAKFAST";
        private const string LunchSchedule = "SCHEDULE_HOSPITALIZED_PATIENTS_LUNCH";
        private const string DinnerSchedule = "SCHEDULE_HOSPITALIZED_PATIENTS_DINNER";
        private const string StaffLunchProcedureId = "CONTROL_PROCEDURE_STAFF_LUNCH";
        private const string PatientHungerNeedId = "NEED_HUNGER_PATIENT";

        private static readonly Dictionary<Entity, bool> PendingPatients =
            new Dictionary<Entity, bool>();

        private static readonly Dictionary<Entity, ScheduledMealDecision> MealDecisions =
            new Dictionary<Entity, ScheduledMealDecision>();

        internal static bool IsPending(Entity patient)
        {
            return patient != null && PendingPatients.ContainsKey(patient);
        }

        internal static void MarkPending(Entity patient)
        {
            if (patient != null)
            {
                PendingPatients[patient] = true;
            }
        }

        internal static void ClearPending(Entity patient)
        {
            if (patient != null)
            {
                PendingPatients.Remove(patient);
            }
        }

        internal static void ResetMeal(Entity patient)
        {
            if (patient == null)
            {
                return;
            }

            PendingPatients.Remove(patient);
            MealDecisions.Remove(patient);
        }

        internal static bool IsScheduledCafeteriaMealInProgress(Entity patient)
        {
            if (patient == null || patient.GetComponent<BehaviorPatient>() == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || hospitalization.m_state == null ||
                !hospitalization.IsHospitalized())
            {
                return false;
            }

            if (IsPending(patient))
            {
                return true;
            }

            // PendingPatients is transient. After a save/load the native procedure
            // script itself is the authoritative marker that the patient is still in
            // an HPL full cafeteria meal. Project Hospital never starts StaffLunch for
            // BehaviorPatient on its own.
            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (procedures == null || procedures.m_state == null ||
                procedures.m_state.m_currentProcedureScript == null ||
                procedures.m_state.m_currentProcedureScript.GetEntity() == null)
            {
                return false;
            }

            return procedures.m_state.m_currentProcedureScript.GetEntity()
                is ProcedureScriptStaffLunch;
        }

        internal static string GetCurrentMealScheduleId()
        {
            if (DayTime.Instance == null)
            {
                return null;
            }

            if (DayTime.Instance.IsScheduledActionTime(BreakfastSchedule))
            {
                return BreakfastSchedule;
            }

            if (DayTime.Instance.IsScheduledActionTime(LunchSchedule))
            {
                return LunchSchedule;
            }

            if (DayTime.Instance.IsScheduledActionTime(DinnerSchedule))
            {
                return DinnerSchedule;
            }

            return null;
        }

        internal static GameDBProcedure GetStaffLunchProcedure()
        {
            return Database.Instance == null
                ? null
                : Database.Instance.GetEntry<GameDBProcedure>(StaffLunchProcedureId);
        }

        internal static GameDBProcedure GetPatientHungerProcedure()
        {
            if (Database.Instance == null)
            {
                return null;
            }

            GameDBNeed hungerNeed = Database.Instance.GetEntry<GameDBNeed>(PatientHungerNeedId);
            return hungerNeed == null ? null : hungerNeed.Procedure;
        }

        internal static ScheduledMealDecision GetOrCreateDecision(Entity patient)
        {
            if (patient == null || Database.Instance == null)
            {
                return null;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            string mealScheduleId = GetCurrentMealScheduleId();

            if (hospitalization == null || hospitalization.m_state == null ||
                mealScheduleId == null || hospitalization.m_state.m_lunchEaten)
            {
                ResetMeal(patient);
                return null;
            }

            ScheduledMealDecision existing;
            if (MealDecisions.TryGetValue(patient, out existing))
            {
                if (existing.MealScheduleId == mealScheduleId)
                {
                    return existing;
                }

                ResetMeal(patient);
            }

            ScheduledMealDecision decision = new ScheduledMealDecision
            {
                UseCafeteria = false,
                Roll = -1,
                MealScheduleId = mealScheduleId,
                TargetFloor = -1
            };

            // A tray already delivered always wins, including saves loaded mid-meal.
            // Only patients actually waiting in bed can be removed from the native tray
            // route; patients busy with an examination/treatment keep the safe tray path.
            if (!hospitalization.m_state.m_lunchReady &&
                IsEligibleForCafeteria(patient))
            {
                int roll;
                bool wantsCafeteria =
                    HospitalPatientLifeConfig.RollScheduledMealCafeteria(out roll);
                decision.Roll = roll;

                if (wantsCafeteria)
                {
                    TileObject target;
                    Department department;
                    if (TryGetCafeteriaMealTarget(patient, out target, out department) &&
                        ApplyFloorWeight(patient, target, "scheduled-meal-floor-weight"))
                    {
                        decision.UseCafeteria = true;
                        decision.TargetFloor = target.GetFloorIndex();
                    }
                }
            }

            MealDecisions[patient] = decision;

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | scheduled-meal-choice=" +
                    (decision.UseCafeteria ? "CAFETERIA" : "TRAY") +
                    " | schedule=" + mealScheduleId +
                    " | chance=" +
                    HospitalPatientLifeConfig.ScheduledMealCafeteriaChancePercent +
                    " | roll=" + decision.Roll +
                    " | targetFloor=" + decision.TargetFloor);
            }

            return decision;
        }

        internal static bool ShouldReceiveTray(Entity patient)
        {
            if (patient == null)
            {
                return true;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || hospitalization.m_state == null)
            {
                return true;
            }

            if (hospitalization.m_state.m_lunchReady ||
                hospitalization.m_state.m_lunchEaten)
            {
                return false;
            }

            ScheduledMealDecision decision = GetOrCreateDecision(patient);
            return decision == null || !decision.UseCafeteria;
        }

        internal static void ForceTrayFallback(Entity patient)
        {
            if (patient == null)
            {
                return;
            }

            ScheduledMealDecision decision;
            if (!MealDecisions.TryGetValue(patient, out decision))
            {
                decision = new ScheduledMealDecision
                {
                    MealScheduleId = GetCurrentMealScheduleId(),
                    Roll = -1,
                    TargetFloor = -1
                };
                MealDecisions[patient] = decision;
            }

            decision.UseCafeteria = false;
            decision.TargetFloor = -1;
        }

        internal static bool TryGetCafeteriaMealTarget(
            Entity patient,
            out TileObject target,
            out Department department)
        {
            GameDBProcedure procedure = GetStaffLunchProcedure();
            if (procedure == null)
            {
                target = null;
                department = null;
                return false;
            }

            return TryGetCafeteriaTarget(
                procedure,
                patient,
                AccessRights.PATIENT_PROCEDURE,
                out target,
                out department);
        }

        internal static bool TryChooseFreeTimeSnack(
            Entity patient,
            Department department,
            out GameDBProcedure snackProcedure)
        {
            snackProcedure = null;

            if (patient == null ||
                HospitalPatientLifeConfig.FreeTimeCafeteriaSnackChancePercent <= 0 ||
                GetCurrentMealScheduleId() != null ||
                !IsEligibleForCafeteria(patient))
            {
                return false;
            }

            int roll;
            if (!HospitalPatientLifeConfig.RollFreeTimeCafeteriaSnack(out roll))
            {
                if (HospitalizedPatientTrace.Enabled)
                {
                    HospitalizedPatientTrace.Log(
                        HospitalizedPatientTrace.GetName(patient) +
                        " | free-time-cafeteria=SKIPPED" +
                        " | chance=" +
                        HospitalPatientLifeConfig.FreeTimeCafeteriaSnackChancePercent +
                        " | roll=" + roll);
                }
                return false;
            }

            GameDBProcedure hungerProcedure = GetPatientHungerProcedure();
            if (hungerProcedure == null)
            {
                return false;
            }

            Department resolvedDepartment = department;
            if (resolvedDepartment == null)
            {
                BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
                resolvedDepartment = behavior == null || behavior.m_state == null ||
                    behavior.m_state.m_department == null
                        ? null
                        : behavior.m_state.m_department.GetEntity();
            }

            TileObject target;
            Department ignoredDepartment;
            if (resolvedDepartment == null ||
                !TryGetCafeteriaTarget(
                    hungerProcedure,
                    patient,
                    AccessRights.PATIENT_PROCEDURE,
                    out target,
                    out ignoredDepartment) ||
                !ApplyFloorWeight(
                    patient,
                    target,
                    "free-time-cafeteria-floor-weight"))
            {
                return false;
            }

            snackProcedure = hungerProcedure;

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | free-time-cafeteria=SNACK" +
                    " | chance=" +
                    HospitalPatientLifeConfig.FreeTimeCafeteriaSnackChancePercent +
                    " | roll=" + roll +
                    " | targetFloor=" + target.GetFloorIndex());
            }

            return true;
        }

        internal static Room GetRoomForObject(TileObject target)
        {
            if (target == null || Hospital.Instance == null)
            {
                return null;
            }

            int floorIndex = target.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i position = target.m_state.m_position;
            if (position.m_x < 0 || position.m_y < 0 ||
                position.m_x >= floor.m_size.m_x || position.m_y >= floor.m_size.m_y)
            {
                return null;
            }

            return floor.m_roomTiles[position.m_x, position.m_y];
        }

        private static bool TryGetCafeteriaTarget(
            GameDBProcedure procedure,
            Entity patient,
            AccessRights accessRights,
            out TileObject target,
            out Department department)
        {
            target = null;
            department = null;

            if (procedure == null || patient == null)
            {
                return false;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            if (behavior == null || behavior.m_state == null ||
                behavior.m_state.m_department == null)
            {
                return false;
            }

            department = behavior.m_state.m_department.GetEntity();
            if (department == null)
            {
                return false;
            }

            ProcedureScene queryScene = ProcedureSceneFactory.CreateProcedureScene(
                procedure,
                patient,
                department,
                null,
                accessRights,
                ProcedureSceneType.QUERY,
                EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT,
                StaffSelectionRules.IGNORE);

            if (queryScene == null ||
                !ProcedureScene.IsProcedureAvailable(queryScene.m_availability) ||
                queryScene.m_equipment == null ||
                queryScene.m_equipment.Length == 0 ||
                queryScene.m_equipment[0] == null)
            {
                return false;
            }

            TileObject candidate = queryScene.m_equipment[0].GetEntity();
            Vector2i usePosition;
            if (!IsAllowedPatientCafeteriaTarget(
                    candidate,
                    accessRights,
                    out usePosition))
            {
                return false;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null ||
                GridMap.GetInstance().GetDistance(
                    walk.GetFloorIndex(),
                    walk.GetCurrentTile(),
                    candidate.GetFloorIndex(),
                    usePosition,
                    accessRights) < 0f)
            {
                return false;
            }

            target = candidate;
            return true;
        }

        private static bool IsAllowedPatientCafeteriaTarget(
            TileObject target,
            AccessRights accessRights,
            out Vector2i usePosition)
        {
            usePosition = Vector2i.ZERO_VECTOR;
            Room room = GetRoomForObject(target);
            if (target == null || !PatientCafeteriaRules.IsCafeteriaRoom(room) ||
                Hospital.Instance == null)
            {
                return false;
            }

            int floorIndex = target.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i position = target.m_state.m_position;
            if (position.m_x < 0 || position.m_y < 0 ||
                position.m_x >= floor.m_size.m_x || position.m_y >= floor.m_size.m_y)
            {
                return false;
            }

            AccessRights paintedRights =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            // Green and blue remain the only accepted cafeteria object tiles. The real
            // walk destination must independently be legal for the procedure access level.
            if (paintedRights != AccessRights.PATIENT &&
                paintedRights != AccessRights.PATIENT_PROCEDURE)
            {
                return false;
            }

            return PatientProcedureNeedAccessRules.IsObjectAndUseDestinationAllowed(
                floor,
                target,
                accessRights,
                out usePosition);
        }

        private static bool IsEligibleForCafeteria(Entity patient)
        {
            HospitalizationComponent hospitalization =
                patient == null ? null : patient.GetComponent<HospitalizationComponent>();
            BehaviorPatient behavior =
                patient == null ? null : patient.GetComponent<BehaviorPatient>();

            if (hospitalization == null || hospitalization.m_state == null ||
                behavior == null || behavior.m_state == null ||
                !hospitalization.IsHospitalized() || !hospitalization.IsAllowedToWalk() ||
                hospitalization.m_state.m_hospitalizationState != HospitalizationState.InBed ||
                behavior.GetWorstKnownHazard() >= SymptomHazard.High ||
                behavior.m_state.m_sentAway || behavior.m_state.m_deathTriggered ||
                behavior.m_state.m_collapseSymptom != null ||
                behavior.m_state.m_collapseProcedure != null)
            {
                return false;
            }

            if (hospitalization.m_state.m_hospitalizationTreatment == null ||
                hospitalization.m_state.m_hospitalizationTreatment.Entry == null)
            {
                return false;
            }

            GameDBTreatment treatment =
                hospitalization.m_state.m_hospitalizationTreatment.Entry;
            return treatment !=
                       Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_ICU") &&
                   treatment !=
                       Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_TRAUMA");
        }

        private static bool ApplyFloorWeight(
            Entity patient,
            TileObject target,
            string traceName)
        {
            if (patient == null || target == null)
            {
                return false;
            }

            int patientFloor = HospitalizedPatientTrace.GetFloor(patient);
            int targetFloor = target.GetFloorIndex();
            int floorDifference =
                patientFloor >= 0 && targetFloor >= 0
                    ? Math.Abs(targetFloor - patientFloor)
                    : 0;

            if (floorDifference <= 0)
            {
                return true;
            }

            int distanceRoll;
            int retainedChancePercent;
            bool accepted = HospitalPatientLifeConfig.RollOtherFloorCafeteria(
                floorDifference,
                out distanceRoll,
                out retainedChancePercent);

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | " + traceName + "=" +
                    (accepted ? "ACCEPTED" : "REJECTED") +
                    " | patientFloor=" + patientFloor +
                    " | targetFloor=" + targetFloor +
                    " | floorDifference=" + floorDifference +
                    " | perFloorMultiplier=" +
                    HospitalPatientLifeConfig.OtherFloorCafeteriaChanceMultiplierPercentPerFloor +
                    " | retainedChance=" + retainedChancePercent +
                    " | roll=" + distanceRoll);
            }

            return accepted;
        }
    }

    [HarmonyPatch(typeof(Room), "HasHospitalizedHungryPatientForLunch")]
    internal static class ScheduledMealRoomEligibilityPatch
    {
        private static bool Prefix(Room __instance, Floor floor, ref bool __result)
        {
            if (__instance == null || floor == null)
            {
                return true;
            }

            for (int x = __instance.m_roomPersistentData.m_positionBottom.m_x;
                 x <= __instance.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = __instance.m_roomPersistentData.m_positionBottom.m_y;
                     y <= __instance.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    if (!__instance.IsPositionInRoom(new Vector2i(x, y)))
                    {
                        continue;
                    }

                    TileObject centerObject = floor.m_tileObjects[x, y].m_centerObject;
                    if (centerObject == null || !centerObject.HasTag("hospitalization") ||
                        centerObject.Owner == null)
                    {
                        continue;
                    }

                    HospitalizationComponent hospitalization =
                        centerObject.Owner.GetComponent<HospitalizationComponent>();
                    if (hospitalization == null || hospitalization.m_state == null ||
                        hospitalization.m_state.m_lunchReady)
                    {
                        continue;
                    }

                    if (ScheduledCafeteriaMealState.ShouldReceiveTray(centerObject.Owner))
                    {
                        __result = true;
                        return false;
                    }
                }
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(MapScriptInterface), "GetFirstHungryHospitalizedPatient")]
    internal static class ScheduledMealPatientSelectionPatch
    {
        private static bool Prefix(Room room, ref TileObject __result)
        {
            if (room == null || Hospital.Instance == null)
            {
                return true;
            }

            Floor floor = Hospital.Instance.m_floors[room.GetFloorIndex()];
            for (int x = room.m_roomPersistentData.m_positionBottom.m_x;
                 x <= room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y = room.m_roomPersistentData.m_positionBottom.m_y;
                     y <= room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    if (!room.IsPositionInRoom(new Vector2i(x, y)))
                    {
                        continue;
                    }

                    TileObject centerObject = floor.m_tileObjects[x, y].m_centerObject;
                    if (centerObject == null || !centerObject.HasTag("hospitalization") ||
                        centerObject.Owner == null)
                    {
                        continue;
                    }

                    HospitalizationComponent hospitalization =
                        centerObject.Owner.GetComponent<HospitalizationComponent>();
                    if (hospitalization == null || hospitalization.m_state == null ||
                        hospitalization.m_state.m_lunchReady)
                    {
                        continue;
                    }

                    if (ScheduledCafeteriaMealState.ShouldReceiveTray(centerObject.Owner))
                    {
                        __result = centerObject;
                        return false;
                    }
                }
            }

            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "CheckNeeds")]
    internal static class ScheduledCafeteriaMealPatch
    {
        private static bool Prefix(HospitalizationComponent __instance)
        {
            if (__instance == null || __instance.m_entity == null ||
                __instance.m_state == null)
            {
                return true;
            }

            Entity patient = __instance.m_entity;
            string mealScheduleId =
                ScheduledCafeteriaMealState.GetCurrentMealScheduleId();

            if (mealScheduleId == null)
            {
                if (!ScheduledCafeteriaMealState.IsScheduledCafeteriaMealInProgress(patient))
                {
                    ScheduledCafeteriaMealState.ResetMeal(patient);
                }
                return true;
            }

            if (__instance.m_state.m_lunchEaten)
            {
                ScheduledCafeteriaMealState.ResetMeal(patient);
                return true;
            }

            ScheduledMealDecision decision =
                ScheduledCafeteriaMealState.GetOrCreateDecision(patient);
            if (decision == null || !decision.UseCafeteria)
            {
                return true;
            }

            if (__instance.m_state.m_lunchReady)
            {
                ScheduledCafeteriaMealState.ForceTrayFallback(patient);
                return true;
            }

            if (ScheduledCafeteriaMealState.IsScheduledCafeteriaMealInProgress(patient))
            {
                return false;
            }

            TileObject target;
            Department department;
            if (!ScheduledCafeteriaMealState.TryGetCafeteriaMealTarget(
                    patient,
                    out target,
                    out department) ||
                target == null || department == null)
            {
                ScheduledCafeteriaMealState.ForceTrayFallback(patient);
                return true;
            }

            if (decision.TargetFloor >= 0 &&
                target.GetFloorIndex() != decision.TargetFloor)
            {
                ScheduledCafeteriaMealState.ForceTrayFallback(patient);
                return true;
            }

            GameDBProcedure mealProcedure =
                ScheduledCafeteriaMealState.GetStaffLunchProcedure();
            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (mealProcedure == null || procedures == null)
            {
                ScheduledCafeteriaMealState.ForceTrayFallback(patient);
                return true;
            }

            __instance.GetCovered(LyingState.UNCOVERED);
            ScheduledCafeteriaMealState.MarkPending(patient);

            try
            {
                procedures.StartProcedure(
                    mealProcedure,
                    patient,
                    department,
                    AccessRights.PATIENT_PROCEDURE,
                    EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT);
            }
            catch
            {
                ScheduledCafeteriaMealState.ClearPending(patient);
                ScheduledCafeteriaMealState.ForceTrayFallback(patient);
                throw;
            }

            __instance.m_state.m_procedureReservationStatus =
                ProcedureReservationStatus.NONE;
            __instance.SwitchState(HospitalizationState.FulfillingNeeds);

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | scheduled-meal-cafeteria=STARTED" +
                    " | schedule=" + mealScheduleId +
                    " | targetFloor=" + target.GetFloorIndex() +
                    " | lunchReady=" + __instance.m_state.m_lunchReady);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptStaffLunch), "ScriptUpdate")]
    internal static class ScheduledCafeteriaMealCompletionPatch
    {
        private static void Postfix(ProcedureScriptStaffLunch __instance)
        {
            if (__instance == null || __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null ||
                !__instance.IsIdle())
            {
                return;
            }

            Entity patient = __instance.m_stateData.m_procedureScene.MainCharacter;
            if (!ScheduledCafeteriaMealState.IsScheduledCafeteriaMealInProgress(patient))
            {
                return;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || hospitalization.m_state == null)
            {
                ScheduledCafeteriaMealState.ResetMeal(patient);
                return;
            }

            hospitalization.m_state.m_lunchEaten = true;
            ScheduledCafeteriaMealState.ResetMeal(patient);

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | scheduled-meal-cafeteria=COMPLETED" +
                    " | hunger=" +
                    HospitalizedPatientTrace.GetNeedValue(
                        patient,
                        "NEED_HUNGER_PATIENT"));
            }
        }
    }

    [HarmonyPatch(typeof(BehaviorPatient), "ReceiveMessage")]
    internal static class ScheduledMealHungerReductionPatch
    {
        private static bool Prefix(BehaviorPatient __instance, Message message)
        {
            if (__instance == null || message.m_messageID != Messages.HUNGER_REDUCED)
            {
                return true;
            }

            HospitalizationComponent hospitalization =
                __instance.GetComponent<HospitalizationComponent>();
            if (hospitalization == null || hospitalization.m_state == null)
            {
                return true;
            }

            Entity patient = hospitalization.m_entity;
            bool cafeteriaMeal =
                ScheduledCafeteriaMealState.IsScheduledCafeteriaMealInProgress(patient);
            bool bedsideMeal =
                hospitalization.m_state.m_hospitalizationState ==
                    HospitalizationState.Eating &&
                hospitalization.m_state.m_lunchReady;

            if (!cafeteriaMeal && !bedsideMeal)
            {
                return true;
            }

            MoodComponent mood = __instance.GetComponent<MoodComponent>();
            Need hunger = mood == null
                ? null
                : mood.GetNeed("NEED_HUNGER_PATIENT");
            if (mood == null || hunger == null)
            {
                return true;
            }

            int minimum = cafeteriaMeal
                ? HospitalPatientLifeConfig.CafeteriaMealHungerReductionMin
                : HospitalPatientLifeConfig.BedsideMealHungerReductionMin;
            int maximum = cafeteriaMeal
                ? HospitalPatientLifeConfig.CafeteriaMealHungerReductionMax
                : HospitalPatientLifeConfig.BedsideMealHungerReductionMax;

            float reduction = HospitalPatientLifeConfig.RollMealHungerReduction(
                minimum,
                maximum);
            hunger.Reduce(reduction, mood);

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | scheduled-meal-hunger-reduction=" + reduction +
                    " | source=" + (cafeteriaMeal ? "CAFETERIA" : "BEDSIDE") +
                    " | configured=" + minimum + "-" + maximum +
                    " | hunger=" +
                    HospitalizedPatientTrace.GetNeedValue(
                        patient,
                        "NEED_HUNGER_PATIENT"));
            }

            return false;
        }
    }

    [HarmonyPatch]
    internal static class FreeTimeCafeteriaSnackStartPatch
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
            ref GameDBProcedure procedure,
            Entity mainCharacter,
            Department department,
            ref AccessRights accessRights,
            ref EquipmentListRules equipmentListRules)
        {
            if (procedure == null || Database.Instance == null ||
                mainCharacter == null)
            {
                return;
            }

            GameDBProcedure freeTimeProcedure =
                Database.Instance.GetEntry<GameDBProcedure>(
                    "CONTROL_PROCEDURE_HOSPITALZED_FREE_TIME");
            if (!object.ReferenceEquals(procedure, freeTimeProcedure))
            {
                return;
            }

            GameDBProcedure snackProcedure;
            if (!ScheduledCafeteriaMealState.TryChooseFreeTimeSnack(
                    mainCharacter,
                    department,
                    out snackProcedure))
            {
                return;
            }

            procedure = snackProcedure;
            accessRights = AccessRights.PATIENT_PROCEDURE;
            equipmentListRules =
                EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT;
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "SwitchState")]
    internal static class ScheduledCafeteriaMealInterruptionPatch
    {
        private static void Prefix(
            HospitalizationComponent __instance,
            HospitalizationState state)
        {
            if (__instance == null || __instance.m_entity == null ||
                __instance.m_state == null || __instance.m_state.m_lunchEaten)
            {
                return;
            }

            Entity patient = __instance.m_entity;
            if (!ScheduledCafeteriaMealState.IsScheduledCafeteriaMealInProgress(patient))
            {
                return;
            }

            if (__instance.m_state.m_hospitalizationState ==
                    HospitalizationState.FulfillingNeeds &&
                state != HospitalizationState.FulfillingNeeds)
            {
                ScheduledCafeteriaMealState.ClearPending(patient);
                ScheduledCafeteriaMealState.ForceTrayFallback(patient);

                if (HospitalizedPatientTrace.Enabled)
                {
                    HospitalizedPatientTrace.Log(
                        HospitalizedPatientTrace.GetName(patient) +
                        " | scheduled-meal-cafeteria=INTERRUPTED" +
                        " | fallback=TRAY" +
                        " | nextState=" + state);
                }
            }
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "ResetHospitalization")]
    internal static class ScheduledCafeteriaMealResetPatch
    {
        private static void Prefix(HospitalizationComponent __instance)
        {
            if (__instance != null && __instance.m_entity != null)
            {
                ScheduledCafeteriaMealState.ResetMeal(__instance.m_entity);
            }
        }
    }
}
