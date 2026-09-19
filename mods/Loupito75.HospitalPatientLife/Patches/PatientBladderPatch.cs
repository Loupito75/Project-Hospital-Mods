using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class PatientBladderRules
    {
        private const string InfectiousDiseasesDepartmentId =
            "DPT_INFECTIOUS_DISEASES_DEPARTMENT";

        internal static bool IsPatientBladderProcedure(GameDBProcedure procedure)
        {
            if (procedure == null || Database.Instance == null)
            {
                return false;
            }

            GameDBNeed bladderNeed = Database.Instance.GetEntry<GameDBNeed>("NEED_BLADDER");
            return bladderNeed != null && object.ReferenceEquals(bladderNeed.Procedure, procedure);
        }

        internal static AccessRights GetEffectiveSearchAccess(Entity patient, AccessRights requestedAccess)
        {
            if (patient == null || requestedAccess != AccessRights.PATIENT)
            {
                return requestedAccess;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            if (behavior != null && behavior.GetAccessRights() == AccessRights.PATIENT_PROCEDURE)
            {
                return AccessRights.PATIENT_PROCEDURE;
            }

            return requestedAccess;
        }

        internal static bool IsInfectiousDiseasesPatient(Entity patient)
        {
            if (patient == null || Database.Instance == null)
            {
                return false;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            if (behavior == null ||
                behavior.m_state == null ||
                behavior.m_state.m_department == null)
            {
                return false;
            }

            Department department = behavior.m_state.m_department.GetEntity();
            if (department == null)
            {
                return false;
            }

            GameDBDepartment infectiousDepartment =
                Database.Instance.GetEntry<GameDBDepartment>(InfectiousDiseasesDepartmentId);
            return infectiousDepartment != null &&
                department.GetDepartmentType() == infectiousDepartment;
        }

        internal static bool IsBiohazardPaintedWc(TileObject target)
        {
            if (target == null ||
                target.m_state == null ||
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
            Vector2i objectPosition = target.m_state.m_position;
            Vector2f nativeUsePosition = target.GetDefaultUsePosition();
            Vector2i usePosition = new Vector2i(
                (int)(nativeUsePosition.m_x + 0.5f),
                (int)(nativeUsePosition.m_y + 0.5f));

            if (!IsInsideFloor(floor, objectPosition) ||
                !IsInsideFloor(floor, usePosition))
            {
                return false;
            }

            return floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                       objectPosition.m_x,
                       objectPosition.m_y] == AccessRights.BIOHAZARD ||
                   floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                       usePosition.m_x,
                       usePosition.m_y] == AccessRights.BIOHAZARD;
        }

        internal static TileObject FindClosestAllowedBladderTarget(
            Entity patient,
            AccessRights accessRights,
            string[] roomTags)
        {
            if (patient == null || Hospital.Instance == null)
            {
                return null;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return null;
            }

            int floorIndex = walk.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Vector2i origin = walk.GetCurrentTile();
            if (!IsInsideFloor(floor, origin))
            {
                return null;
            }

            bool infectiousDiseasesPatient = IsInfectiousDiseasesPatient(patient);
            int bestDistance = int.MaxValue;
            TileObject bestObject = null;

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    for (int objectIndex = 0; objectIndex <= 1; objectIndex++)
                    {
                        TileObject candidate = objectIndex == 0
                            ? floor.m_tileObjects[x, y].m_centerObject
                            : floor.m_tileObjects[x, y].m_attachmentObject;

                        if (candidate == null ||
                            candidate.m_state == null ||
                            !candidate.HasTag("wc") ||
                            candidate.IsBroken() ||
                            !candidate.IsValid() ||
                            candidate.User != null ||
                            candidate.Owner != null)
                        {
                            continue;
                        }

                        Vector2i candidatePosition = candidate.m_state.m_position;
                        Vector2i usePosition;
                        if (!PatientProcedureNeedAccessRules.IsObjectAndUseDestinationAllowed(
                                floor,
                                candidate,
                                accessRights,
                                out usePosition))
                        {
                            continue;
                        }

                        if (!infectiousDiseasesPatient &&
                            (floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                                 candidatePosition.m_x,
                                 candidatePosition.m_y] == AccessRights.BIOHAZARD ||
                             floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                                 usePosition.m_x,
                                 usePosition.m_y] == AccessRights.BIOHAZARD))
                        {
                            continue;
                        }

                        Room room = floor.m_roomTiles[
                            candidatePosition.m_x,
                            candidatePosition.m_y];
                        if (room == null ||
                            room.m_roomPersistentData == null ||
                            (room.m_roomPersistentData.m_valid != RoomValidity.OK &&
                             room.m_roomPersistentData.m_valid != RoomValidity.MISSING_STAFF) ||
                            !HasAllowedRoomTag(room, roomTags))
                        {
                            continue;
                        }

                        int distance = (int)GridMap.GetInstance().GetDistance(
                            floorIndex,
                            origin,
                            candidate.GetFloorIndex(),
                            usePosition,
                            accessRights);

                        if (distance == -1 || distance >= bestDistance)
                        {
                            continue;
                        }

                        bestDistance = distance;
                        bestObject = candidate;
                    }
                }
            }

            return bestObject;
        }

        internal static string DescribeWc(TileObject target)
        {
            if (target == null || target.m_state == null)
            {
                return "<none>";
            }

            string objectId = target.m_state.m_gameDBObject.Entry == null
                ? "<unknown>"
                : target.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
            Vector2i position = target.m_state.m_position;

            return objectId + "@" +
                position.m_x + "," +
                position.m_y +
                ",floor=" + target.GetFloorIndex();
        }

        private static bool HasAllowedRoomTag(Room room, string[] roomTags)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                roomTags == null)
            {
                return false;
            }

            for (int i = 0; i < roomTags.Length; i++)
            {
                if (room.m_roomPersistentData.m_roomType.Entry.HasTag(roomTags[i]))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsInsideFloor(Floor floor, Vector2i position)
        {
            return floor != null &&
                position.m_x >= 0 &&
                position.m_y >= 0 &&
                position.m_x < floor.Size.m_x &&
                position.m_y < floor.Size.m_y;
        }
    }

    [HarmonyPatch]
    internal static class PatientBladderProcedureScenePatch
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
            ref AccessRights accessRights)
        {
            if (!PatientBladderRules.IsPatientBladderProcedure(procedure))
            {
                return;
            }

            // BehaviorPatient.CheckNeeds() requests PATIENT access for personal needs.
            // Only this bladder scene widens the search to the patient's current
            // movement rights when appropriate.
            accessRights = PatientBladderRules.GetEffectiveSearchAccess(patient, accessRights);
        }

        private static void Postfix(
            GameDBProcedure procedure,
            Entity patient,
            AccessRights accessRights,
            EquipmentListRules equipmentListRules,
            ProcedureScene __result)
        {
            if (!PatientBladderRules.IsPatientBladderProcedure(procedure) ||
                patient == null ||
                patient.GetComponent<BehaviorPatient>() == null ||
                __result == null ||
                equipmentListRules != EquipmentListRules.ONLY_FREE_SAME_FLOOR ||
                __result.m_equipment == null ||
                __result.m_equipment.Length == 0 ||
                __result.m_equipment[0] == null)
            {
                return;
            }

            TileObject selected = __result.m_equipment[0].GetEntity();
            if (selected == null ||
                !PatientBladderRules.IsBiohazardPaintedWc(selected) ||
                PatientBladderRules.IsInfectiousDiseasesPatient(patient))
            {
                return;
            }

            AccessRights effectiveAccess =
                PatientBladderRules.GetEffectiveSearchAccess(patient, accessRights);
            TileObject replacement = PatientBladderRules.FindClosestAllowedBladderTarget(
                patient,
                effectiveAccess,
                procedure.RequiredRoomTags);

            ref EntityIDPointer<TileObject> equipment = ref __result.m_equipment[0];
            equipment = replacement;
            if (replacement == null)
            {
                __result.m_availability &= ~ProcedureSceneAvailability.AVAILABLE;
                __result.m_availability |= ProcedureSceneAvailability.EQUIPMENT_UNAVAILABLE;
            }

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            Department patientDepartment = behavior.m_state == null ||
                behavior.m_state.m_department == null
                    ? null
                    : behavior.m_state.m_department.GetEntity();

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | bladder-biohazard-wc=REJECTED_NON_DID" +
                " | patientDpt=" +
                NeedDestinationDiagnostics.GetDepartmentId(patientDepartment) +
                " | rejected=" + PatientBladderRules.DescribeWc(selected) +
                " | replacement=" + PatientBladderRules.DescribeWc(replacement) +
                " | searchAccess=" + effectiveAccess);
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptNeedBladder), "Activate")]
    internal static class PatientBladderDestinationTracePatch
    {
        private static void Postfix(ProcedureScriptNeedBladder __instance)
        {
            if (__instance == null || __instance.m_stateData == null ||
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
                    HospitalizedPatientTrace.GetName(patient) +
                    " | bladder-target=<null>");
                return;
            }

            string objectId = target.m_state.m_gameDBObject == null ||
                target.m_state.m_gameDBObject.Entry == null
                ? "<unknown>"
                : target.m_state.m_gameDBObject.Entry.DatabaseID.ToString();

            BehaviorPatient behavior = patient.GetComponent<BehaviorPatient>();
            Department patientDepartment = behavior == null || behavior.m_state.m_department == null
                ? null
                : behavior.m_state.m_department.GetEntity();
            Department targetDepartment = target.m_state.m_department == null
                ? null
                : target.m_state.m_department.GetEntity();
            Room targetRoom = ScheduledCafeteriaMealState.GetRoomForObject(target);
            Department roomDepartment = targetRoom == null ||
                targetRoom.m_roomPersistentData == null ||
                targetRoom.m_roomPersistentData.m_department == null
                    ? null
                    : targetRoom.m_roomPersistentData.m_department.GetEntity();

            AccessRights paintedAccess = AccessRights.PEDESTRIAN;
            AccessRights roomAccess = AccessRights.PEDESTRIAN;
            int floorIndex = target.GetFloorIndex();
            Vector2i position = target.m_state.m_position;
            if (Hospital.Instance != null && floorIndex >= 0 && floorIndex < Hospital.Instance.m_floors.Count)
            {
                Floor floor = Hospital.Instance.m_floors[floorIndex];
                if (position.m_x >= 0 && position.m_y >= 0 &&
                    position.m_x < floor.m_size.m_x && position.m_y < floor.m_size.m_y)
                {
                    paintedAccess = floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                        position.m_x,
                        position.m_y];
                    roomAccess = floor.m_roomAccessRights[position.m_x, position.m_y];
                }
            }

            AccessRights movementAccess = behavior == null
                ? AccessRights.PATIENT
                : behavior.GetAccessRights();

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | bladder-target=" + objectId +
                " | targetFloor=" + target.GetFloorIndex() +
                " | targetPos=" + position.m_x + "," + position.m_y +
                " | paintedAccess=" + paintedAccess +
                " | roomAccess=" + roomAccess +
                " | movementAccess=" + movementAccess +
                " | patientDpt=" + NeedDestinationDiagnostics.GetDepartmentId(patientDepartment) +
                " | targetObjectDpt=" + NeedDestinationDiagnostics.GetDepartmentId(targetDepartment) +
                " | targetRoomDpt=" + NeedDestinationDiagnostics.GetDepartmentId(roomDepartment));
        }
    }
}
