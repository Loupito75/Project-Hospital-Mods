using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPatientLife.Patches
{
    internal enum ClinicCafeteriaChoice
    {
        VANILLA,
        SNACK,
        FULL_MEAL
    }

    internal sealed class ClinicCafeteriaDecision
    {
        internal ClinicCafeteriaChoice Choice;
        internal TileObject Target;
        internal int Roll;
        internal int TargetFloor;
    }

    internal static class ClinicCafeteriaState
    {
        private const int ClinicMealPaidParam = 18;
        private const int ClinicMealMarkerParam = 19;

        private static readonly Dictionary<Entity, ClinicCafeteriaDecision> Decisions =
            new Dictionary<Entity, ClinicCafeteriaDecision>();

        private static TileObject ForcedTarget;
        private static string ForcedTag;
        private static bool SuppressCafeteriaFood;

        internal static bool IsClinicPatient(Entity patient)
        {
            if (patient == null || patient.GetComponent<BehaviorPatient>() == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            return hospitalization == null || !hospitalization.IsHospitalized();
        }

        internal static ClinicCafeteriaDecision CreateDecision(Entity patient)
        {
            ClinicCafeteriaDecision decision = new ClinicCafeteriaDecision
            {
                Choice = ClinicCafeteriaChoice.VANILLA,
                Target = null,
                Roll = -1,
                TargetFloor = -1
            };

            if (!IsClinicPatient(patient))
            {
                return decision;
            }

            int roll;
            bool wantsCafeteria =
                HospitalPatientLifeConfig.RollClinicCafeteriaVisit(out roll);
            decision.Roll = roll;

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null)
            {
                Decisions[patient] = decision;
                return decision;
            }

            Vector2i origin = walk.GetCurrentTile();
            int originFloor = walk.GetFloorIndex();

            if (wantsCafeteria)
            {
                bool mealWindow =
                    ScheduledCafeteriaMealState.GetCurrentMealScheduleId() != null;

                TileObject target;
                if (mealWindow &&
                    TryFindGreenCafeteriaTarget(
                        origin,
                        originFloor,
                        "staff_lunch",
                        out target))
                {
                    if (ApplyClinicFloorWeight(
                            patient,
                            target,
                            "clinic-full-meal-floor-weight"))
                    {
                        decision.Choice = ClinicCafeteriaChoice.FULL_MEAL;
                        decision.Target = target;
                        decision.TargetFloor = target.GetFloorIndex();
                    }
                    else
                    {
                        PopulateVanillaTarget(patient, origin, originFloor, decision);
                    }

                    Decisions[patient] = decision;
                    TraceDecision(patient, decision);
                    return decision;
                }

                if (TryFindGreenCafeteriaTarget(
                        origin,
                        originFloor,
                        "food",
                        out target) &&
                    ApplyClinicFloorWeight(
                        patient,
                        target,
                        "clinic-snack-floor-weight"))
                {
                    decision.Choice = ClinicCafeteriaChoice.SNACK;
                    decision.Target = target;
                    decision.TargetFloor = target.GetFloorIndex();
                }
                else
                {
                    PopulateVanillaTarget(patient, origin, originFloor, decision);
                }
            }
            else
            {
                PopulateVanillaTarget(patient, origin, originFloor, decision);
            }

            Decisions[patient] = decision;
            TraceDecision(patient, decision);
            return decision;
        }

        internal static ClinicCafeteriaDecision GetDecision(Entity patient)
        {
            ClinicCafeteriaDecision decision;
            return patient != null && Decisions.TryGetValue(patient, out decision)
                ? decision
                : null;
        }

        internal static void ClearDecision(Entity patient)
        {
            if (patient != null)
            {
                Decisions.Remove(patient);
            }
        }

        internal static bool IsDecisionTargetStillAvailable(
            ClinicCafeteriaDecision decision)
        {
            if (decision == null)
            {
                return false;
            }

            if (decision.Target == null)
            {
                return decision.Choice == ClinicCafeteriaChoice.VANILLA;
            }

            int floorIndex = decision.Target.GetFloorIndex();
            if (Hospital.Instance == null || floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (decision.Choice == ClinicCafeteriaChoice.VANILLA)
            {
                return IsAvailableNonCafeteriaFood(floor, decision.Target);
            }

            string tag = decision.Choice == ClinicCafeteriaChoice.FULL_MEAL
                ? "staff_lunch"
                : "food";
            return IsAvailableGreenCafeteriaObject(floor, decision.Target, tag);
        }

        internal static void ApplyDecisionToProcedure(
            Entity patient,
            ClinicCafeteriaDecision decision,
            ref GameDBProcedure procedure,
            ref AccessRights accessRights,
            ref EquipmentListRules equipmentListRules)
        {
            ClearForcedTarget();

            if (decision == null)
            {
                return;
            }

            if (decision.Choice == ClinicCafeteriaChoice.VANILLA)
            {
                if (decision.Target != null)
                {
                    SetForcedTarget(decision.Target, "food");
                }
                else
                {
                    SuppressCafeteriaFood = true;
                }
                return;
            }

            if (decision.Target == null)
            {
                return;
            }

            if (decision.Choice == ClinicCafeteriaChoice.FULL_MEAL)
            {
                GameDBProcedure staffLunch =
                    ScheduledCafeteriaMealState.GetStaffLunchProcedure();
                if (staffLunch == null)
                {
                    return;
                }

                procedure = staffLunch;
                accessRights = AccessRights.PATIENT;
                equipmentListRules =
                    EquipmentListRules.ONLY_FREE_SAME_FLOOR_PREFER_DPT;
                SetForcedTarget(decision.Target, "staff_lunch");
                return;
            }

            SetForcedTarget(decision.Target, "food");
        }

        internal static bool IsClinicFullMealInProgress(Entity patient)
        {
            if (!IsClinicPatient(patient))
            {
                return false;
            }

            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (procedures == null || procedures.m_state == null ||
                procedures.m_state.m_currentProcedureScript == null ||
                procedures.m_state.m_currentProcedureScript.GetEntity() == null)
            {
                return false;
            }

            ProcedureScriptStaffLunch lunch =
                procedures.m_state.m_currentProcedureScript.GetEntity()
                    as ProcedureScriptStaffLunch;
            return lunch != null && IsMarkedClinicMeal(lunch);
        }

        internal static void MarkClinicMeal(ProcedureScriptStaffLunch lunch)
        {
            if (lunch == null)
            {
                return;
            }

            lunch.SetParam(ClinicMealPaidParam, 0f);
            lunch.SetParam(ClinicMealMarkerParam, 1f);
        }

        internal static bool IsMarkedClinicMeal(ProcedureScriptStaffLunch lunch)
        {
            return lunch != null && lunch.GetParam(ClinicMealMarkerParam) >= 1f;
        }

        internal static bool IsClinicMealPaid(ProcedureScriptStaffLunch lunch)
        {
            return lunch != null && lunch.GetParam(ClinicMealPaidParam) >= 1f;
        }

        internal static void MarkClinicMealPaid(ProcedureScriptStaffLunch lunch)
        {
            if (lunch != null)
            {
                lunch.SetParam(ClinicMealPaidParam, 1f);
            }
        }

        internal static void SetForcedTarget(TileObject target, string tag)
        {
            ForcedTarget = target;
            ForcedTag = tag;
            SuppressCafeteriaFood = false;
        }

        internal static void ClearForcedTarget()
        {
            ForcedTarget = null;
            ForcedTag = null;
            SuppressCafeteriaFood = false;
        }

        internal static bool TryGetForcedTarget(string tag, out TileObject target)
        {
            target = null;
            if (ForcedTarget == null || string.IsNullOrEmpty(ForcedTag) ||
                ForcedTag != tag)
            {
                return false;
            }

            target = ForcedTarget;
            return true;
        }

        internal static bool ShouldSuppressCafeteriaTarget(
            string tag,
            TileObject target)
        {
            if (!SuppressCafeteriaFood || tag != "food" || target == null)
            {
                return false;
            }

            Room room = ScheduledCafeteriaMealState.GetRoomForObject(target);
            return PatientCafeteriaRules.IsCafeteriaRoom(room);
        }

        internal static bool TryFindGreenCafeteriaTarget(
            Vector2i origin,
            int originFloor,
            string tag,
            out TileObject target)
        {
            target = null;
            if (Hospital.Instance == null || GridMap.GetInstance() == null ||
                string.IsNullOrEmpty(tag) || originFloor < 0 ||
                originFloor >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            int bestDistance = int.MaxValue;

            for (int floorIndex = 0;
                 floorIndex < Hospital.Instance.m_floors.Count;
                 floorIndex++)
            {
                Floor floor = Hospital.Instance.m_floors[floorIndex];
                if (floor == null)
                {
                    continue;
                }

                for (int x = 1; x < floor.Size.m_x - 1; x++)
                {
                    for (int y = 1; y < floor.Size.m_y - 1; y++)
                    {
                        for (int objectIndex = 0; objectIndex <= 1; objectIndex++)
                        {
                            TileObject candidate = objectIndex == 0
                                ? floor.m_tileObjects[x, y].m_centerObject
                                : floor.m_tileObjects[x, y].m_attachmentObject;

                            if (!IsAvailableGreenCafeteriaObject(
                                    floor,
                                    candidate,
                                    tag))
                            {
                                continue;
                            }

                            Vector2i usePosition;
                            if (!PatientProcedureNeedAccessRules.IsFoodDestinationAllowed(
                                    floor,
                                    candidate,
                                    AccessRights.PATIENT,
                                    out usePosition))
                            {
                                continue;
                            }

                            int distance = (int)GridMap.GetInstance().GetDistance(
                                originFloor,
                                origin,
                                floorIndex,
                                usePosition,
                                AccessRights.PATIENT);

                            if (distance == -1 || distance >= bestDistance)
                            {
                                continue;
                            }

                            bestDistance = distance;
                            target = candidate;
                        }
                    }
                }
            }

            return target != null;
        }

        internal static bool ApplyClinicFloorWeight(
            Entity patient,
            TileObject target,
            string traceName)
        {
            if (patient == null || target == null)
            {
                return false;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return false;
            }

            int patientFloor = walk.GetFloorIndex();
            int targetFloor = target.GetFloorIndex();
            int floorDifference = Math.Abs(targetFloor - patientFloor);
            if (floorDifference <= 0)
            {
                return true;
            }

            int roll;
            int retainedChancePercent;
            bool accepted = HospitalPatientLifeConfig.RollClinicOtherFloorCafeteria(
                floorDifference,
                out roll,
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
                    HospitalPatientLifeConfig.ClinicOtherFloorCafeteriaChanceMultiplierPercentPerFloor +
                    " | retainedChance=" + retainedChancePercent +
                    " | roll=" + roll);
            }

            return accepted;
        }

        private static void PopulateVanillaTarget(
            Entity patient,
            Vector2i origin,
            int floorIndex,
            ClinicCafeteriaDecision decision)
        {
            TileObject normalFood;
            if (!TryFindSameFloorNonCafeteriaFood(
                    origin,
                    floorIndex,
                    out normalFood))
            {
                return;
            }

            Room room = ScheduledCafeteriaMealState.GetRoomForObject(normalFood);
            BehaviorPatient behavior =
                patient == null ? null : patient.GetComponent<BehaviorPatient>();
            Department preferredDepartment =
                behavior == null ||
                behavior.m_state == null ||
                behavior.m_state.m_department == null
                    ? null
                    : behavior.m_state.m_department.GetEntity();
            Department candidateDepartment =
                normalFood.m_state == null ||
                normalFood.m_state.m_department == null
                    ? null
                    : normalFood.m_state.m_department.GetEntity();

            if (room != null &&
                preferredDepartment != null &&
                candidateDepartment != preferredDepartment)
            {
                return;
            }

            decision.Target = normalFood;
            decision.TargetFloor = normalFood.GetFloorIndex();
        }

        private static bool TryFindSameFloorNonCafeteriaFood(
            Vector2i origin,
            int floorIndex,
            out TileObject target)
        {
            target = null;
            if (Hospital.Instance == null || GridMap.GetInstance() == null ||
                floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return false;
            }

            int bestDistance = int.MaxValue;

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    for (int objectIndex = 0; objectIndex <= 1; objectIndex++)
                    {
                        TileObject candidate = objectIndex == 0
                            ? floor.m_tileObjects[x, y].m_centerObject
                            : floor.m_tileObjects[x, y].m_attachmentObject;

                        if (!IsAvailableNonCafeteriaFood(floor, candidate))
                        {
                            continue;
                        }

                        Vector2i usePosition;
                        if (!PatientProcedureNeedAccessRules.IsFoodDestinationAllowed(
                                floor,
                                candidate,
                                AccessRights.PATIENT,
                                out usePosition))
                        {
                            continue;
                        }

                        int distance = (int)GridMap.GetInstance().GetDistance(
                            floorIndex,
                            origin,
                            floorIndex,
                            usePosition,
                            AccessRights.PATIENT);

                        if (distance == -1 || distance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = distance;
                        target = candidate;
                    }
                }
            }

            return target != null;
        }

        private static bool IsAvailableGreenCafeteriaObject(
            Floor floor,
            TileObject candidate,
            string tag)
        {
            if (floor == null || candidate == null || !candidate.HasTag(tag) ||
                candidate.IsBroken() || !candidate.IsValid() ||
                candidate.User != null || candidate.Owner != null)
            {
                return false;
            }

            Vector2i position = candidate.m_state.m_position;
            if (!IsInsideFloor(floor, position))
            {
                return false;
            }

            Room room = floor.m_roomTiles[position.m_x, position.m_y];
            if (!PatientCafeteriaRules.IsCafeteriaRoom(room) ||
                (room.m_roomPersistentData.m_valid != RoomValidity.OK &&
                 room.m_roomPersistentData.m_valid != RoomValidity.MISSING_STAFF))
            {
                return false;
            }

            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            return logisticsAccess == AccessRights.PATIENT &&
                   IsPatientAccessible(floor, position);
        }

        private static bool IsAvailableNonCafeteriaFood(
            Floor floor,
            TileObject candidate)
        {
            if (floor == null || candidate == null || !candidate.HasTag("food") ||
                candidate.IsBroken() || !candidate.IsValid() ||
                candidate.User != null || candidate.Owner != null)
            {
                return false;
            }

            Vector2i position = candidate.m_state.m_position;
            if (!IsInsideFloor(floor, position) ||
                !IsPatientAccessible(floor, position))
            {
                return false;
            }

            Room room = floor.m_roomTiles[position.m_x, position.m_y];
            if (PatientCafeteriaRules.IsCafeteriaRoom(room))
            {
                return false;
            }

            return room == null ||
                   room.m_roomPersistentData.m_valid == RoomValidity.OK ||
                   room.m_roomPersistentData.m_valid == RoomValidity.MISSING_STAFF;
        }

        private static bool IsPatientAccessible(Floor floor, Vector2i position)
        {
            if (!IsInsideFloor(floor, position))
            {
                return false;
            }

            AccessRights roomAccess =
                floor.m_roomAccessRights[position.m_x, position.m_y];
            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            return (int)roomAccess <= (int)AccessRights.PATIENT &&
                   (int)logisticsAccess <= (int)AccessRights.PATIENT;
        }

        private static bool IsInsideFloor(Floor floor, Vector2i position)
        {
            return floor != null && position.m_x >= 0 && position.m_y >= 0 &&
                   position.m_x < floor.Size.m_x && position.m_y < floor.Size.m_y;
        }

        private static void TraceDecision(
            Entity patient,
            ClinicCafeteriaDecision decision)
        {
            if (!HospitalizedPatientTrace.Enabled || patient == null || decision == null)
            {
                return;
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | clinic-cafeteria-choice=" + decision.Choice +
                " | chance=" + HospitalPatientLifeConfig.ClinicCafeteriaVisitChancePercent +
                " | roll=" + decision.Roll +
                " | targetFloor=" + decision.TargetFloor +
                " | fullMealPrice=" + HospitalPatientLifeConfig.ClinicFullMealPrice);
        }
    }

    [HarmonyPatch]
    internal static class ClinicCafeteriaProcedureScenePatch
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

        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref GameDBProcedure procedure,
            Entity patient,
            ref AccessRights accessRights,
            ProcedureSceneType procedureSceneType,
            ref EquipmentListRules equipmentListRules)
        {
            ClinicCafeteriaState.ClearForcedTarget();

            if (procedureSceneType != ProcedureSceneType.QUERY ||
                !ClinicCafeteriaState.IsClinicPatient(patient) ||
                !PatientCafeteriaRules.IsPatientHungerProcedure(procedure) ||
                accessRights != AccessRights.PATIENT)
            {
                return;
            }

            ClinicCafeteriaDecision decision =
                ClinicCafeteriaState.CreateDecision(patient);
            ClinicCafeteriaState.ApplyDecisionToProcedure(
                patient,
                decision,
                ref procedure,
                ref accessRights,
                ref equipmentListRules);
        }

        private static void Postfix()
        {
            ClinicCafeteriaState.ClearForcedTarget();
        }

        private static Exception Finalizer(Exception __exception)
        {
            ClinicCafeteriaState.ClearForcedTarget();
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class ClinicPatientCafeteriaStartPatch
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

        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            ref GameDBProcedure procedure,
            Entity mainCharacter,
            Department department,
            ref AccessRights accessRights,
            ref EquipmentListRules equipmentListRules)
        {
            ClinicCafeteriaState.ClearForcedTarget();

            if (!ClinicCafeteriaState.IsClinicPatient(mainCharacter) ||
                !PatientCafeteriaRules.IsPatientHungerProcedure(procedure) ||
                accessRights != AccessRights.PATIENT)
            {
                return;
            }

            ClinicCafeteriaDecision decision =
                ClinicCafeteriaState.GetDecision(mainCharacter);
            if (decision == null ||
                !ClinicCafeteriaState.IsDecisionTargetStillAvailable(decision))
            {
                decision = ClinicCafeteriaState.CreateDecision(mainCharacter);
            }

            ClinicCafeteriaState.ApplyDecisionToProcedure(
                mainCharacter,
                decision,
                ref procedure,
                ref accessRights,
                ref equipmentListRules);
        }

        private static void Postfix(Entity mainCharacter)
        {
            ClinicCafeteriaState.ClearForcedTarget();
            ClinicCafeteriaState.ClearDecision(mainCharacter);
        }

        private static Exception Finalizer(Exception __exception, Entity mainCharacter)
        {
            ClinicCafeteriaState.ClearForcedTarget();
            ClinicCafeteriaState.ClearDecision(mainCharacter);
            return __exception;
        }
    }

    [HarmonyPatch]
    internal static class ClinicForcedShortestPathTargetPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                "FindClosestCenterObjectWithTagShortestPath",
                new Type[]
                {
                    typeof(Vector2i),
                    typeof(int),
                    typeof(string),
                    typeof(AccessRights),
                    typeof(string[]),
                    typeof(bool),
                    typeof(bool),
                    typeof(Department),
                    typeof(string)
                });
        }

        private static void Postfix(string tag, ref TileObject __result)
        {
            TileObject forced;
            if (ClinicCafeteriaState.TryGetForcedTarget(tag, out forced))
            {
                __result = forced;
                return;
            }

            if (ClinicCafeteriaState.ShouldSuppressCafeteriaTarget(tag, __result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch]
    internal static class ClinicForcedDepartmentTargetPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(MapScriptInterface),
                "FindClosestObjectWithTag",
                new Type[]
                {
                    typeof(Vector2i),
                    typeof(int),
                    typeof(Department),
                    typeof(string),
                    typeof(AccessRights),
                    typeof(string[]),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool),
                    typeof(bool)
                });
        }

        private static void Postfix(string tag, ref TileObject __result)
        {
            TileObject forced;
            if (ClinicCafeteriaState.TryGetForcedTarget(tag, out forced))
            {
                __result = forced;
                return;
            }

            if (ClinicCafeteriaState.ShouldSuppressCafeteriaTarget(tag, __result))
            {
                __result = null;
            }
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptStaffLunch), "Activate")]
    internal static class ClinicFullMealMarkerPatch
    {
        private static void Prefix(ProcedureScriptStaffLunch __instance)
        {
            if (__instance == null || __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null)
            {
                return;
            }

            Entity patient = __instance.m_stateData.m_procedureScene.MainCharacter;
            if (ClinicCafeteriaState.IsClinicPatient(patient))
            {
                ClinicCafeteriaState.MarkClinicMeal(__instance);
            }
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptStaffLunch), "ScriptUpdate")]
    internal static class ClinicFullMealPaymentPatch
    {
        private static void Postfix(ProcedureScriptStaffLunch __instance)
        {
            if (__instance == null || !__instance.IsIdle() ||
                !ClinicCafeteriaState.IsMarkedClinicMeal(__instance) ||
                ClinicCafeteriaState.IsClinicMealPaid(__instance) ||
                __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null)
            {
                return;
            }

            ClinicCafeteriaState.MarkClinicMealPaid(__instance);

            Entity patient = __instance.m_stateData.m_procedureScene.MainCharacter;
            if (!ClinicCafeteriaState.IsClinicPatient(patient))
            {
                return;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            int price = HospitalPatientLifeConfig.ClinicFullMealPrice;
            if (behavior == null || behavior.m_state == null ||
                behavior.m_state.m_department == null || price <= 0 ||
                !behavior.ShouldPatientPay())
            {
                return;
            }

            Department department = behavior.m_state.m_department.GetEntity();
            if (department == null)
            {
                return;
            }

            behavior.m_state.m_moneySpent += price;
            department.Pay(price, PaymentCategory.FOOD, patient);

            if (SettingsManager.Instance.m_gameSettings.m_showPaymentsInGame.m_value)
            {
                NotificationManager.GetInstance().AddFloatingIngameNotification(
                    patient,
                    "$" + price,
                    new Color(0.5f, 1f, 0.5f));
            }

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | clinic-full-meal-paid=$" + price);
            }
        }
    }
}
