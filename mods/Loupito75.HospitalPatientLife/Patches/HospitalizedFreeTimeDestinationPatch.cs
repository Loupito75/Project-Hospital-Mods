using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    [HarmonyPatch]
    internal static class HospitalizedFreeTimeDestinationPatch
    {
        private const string FancyChairObjectId = "OBJECT_FANCY_CHAIR";
        private const string OutsideBenchPartAId = "OBJECT_OUTSIDE_BENCH_A";
        private const string OutsideBenchPartBId = "OBJECT_OUTSIDE_BENCH_B";
        private const string LoungeRoomTypeId = "DLC_ROOM_TYPE_LOUNGE";
        private const string WaitingRoomTypeId = "ROOM_TYPE_WAITING";
        private const string ReceptionRoomTypeId = "ROOM_TYPE_RECEPTION";

        private enum LeisureActivityKind
        {
            Rest,
            Education,
            Visual
        }

        private sealed class NearbyRoom
        {
            internal Room Room;
            internal float DistanceSquared;
        }

        private sealed class FreeTimeCandidate
        {
            internal TileObject Target;
            internal float RouteDistance;
            internal LeisureActivityKind ActivityKind;
            internal string Source;
            internal int FloorDifference;
            internal int FloorRoll;
            internal int RetainedChancePercent;
        }

        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedFreeTime),
                "GetEntertainmentItem",
                new Type[]
                {
                    typeof(Entity),
                    typeof(string)
                });

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedFreeTime.GetEntertainmentItem(Entity, string) was not found.");
            }

            return method;
        }

        private static void Postfix(
            ProcedureScriptControlHopitalizedFreeTime __instance,
            Entity mainCharacter,
            string tag,
            ref TileObject __result)
        {
            if (__instance == null ||
                mainCharacter == null ||
                tag != "hospitalized_patient" ||
                MapScriptInterface.Instance == null)
            {
                return;
            }

            HospitalizationComponent hospitalization =
                mainCharacter.GetComponent<HospitalizationComponent>();
            WalkComponent walk = mainCharacter.GetComponent<WalkComponent>();

            if (hospitalization == null ||
                !hospitalization.IsHospitalized() ||
                !hospitalization.IsAllowedToWalk() ||
                walk == null ||
                walk.Floor == null)
            {
                return;
            }

            int leisureRoll;
            if (!HospitalPatientLifeConfig.RollSameFloorLeisure(out leisureRoll))
            {
                LogVanillaDecision(
                    mainCharacter,
                    __result,
                    leisureRoll,
                    "ROLL");
                return;
            }

            ApplyIndoorLeisure(
                __instance,
                mainCharacter,
                walk,
                leisureRoll,
                ref __result);

            HospitalizedOutdoorLeisure.TryOverride(
                mainCharacter,
                __instance,
                ref __result);
        }

        private static void ApplyIndoorLeisure(
            ProcedureScriptControlHopitalizedFreeTime __instance,
            Entity mainCharacter,
            WalkComponent walk,
            int leisureRoll,
            ref TileObject __result)
        {
            Floor floor = walk.Floor;
            if (floor.m_rooms == null)
            {
                LogVanillaDecision(
                    mainCharacter,
                    __result,
                    leisureRoll,
                    "NO_ROOMS");
                return;
            }

            Vector2i currentTile = walk.GetCurrentTile();
            Room currentRoom = MapScriptInterface.Instance.GetRoomAt(walk);
            BehaviorPatient behaviorPatient =
                mainCharacter.GetComponent<BehaviorPatient>();
            Department patientDepartment = behaviorPatient != null
                ? behaviorPatient.GetDepartment()
                : null;

            List<NearbyRoom> nearbyRooms = new List<NearbyRoom>();
            for (int i = 0; i < floor.m_rooms.Count; i++)
            {
                Room room = floor.m_rooms[i];
                if (room == null ||
                    room == currentRoom ||
                    !IsOrdinaryRoomEligibleForPatient(floor, room))
                {
                    continue;
                }

                Vector2f center = room.GetCenter();
                float dx = center.m_x - currentTile.m_x;
                float dy = center.m_y - currentTile.m_y;

                nearbyRooms.Add(new NearbyRoom
                {
                    Room = room,
                    DistanceSquared = dx * dx + dy * dy
                });
            }

            nearbyRooms.Sort(delegate(NearbyRoom left, NearbyRoom right)
            {
                return left.DistanceSquared.CompareTo(right.DistanceSquared);
            });

            int roomsToSearch = Math.Min(
                HospitalPatientLifeConfig.NearbyRoomsToSearch,
                nearbyRooms.Count);

            List<FreeTimeCandidate> candidates = new List<FreeTimeCandidate>();
            string[] hospitalizedPatientTags = new string[] { "hospitalized_patient" };
            string[] restTags = new string[] { "rest" };

            for (int i = 0; i < roomsToSearch; i++)
            {
                Room room = nearbyRooms[i].Room;
                AddRoomCandidates(
                    candidates,
                    mainCharacter,
                    __instance,
                    room,
                    hospitalizedPatientTags,
                    false,
                    "SAME_FLOOR",
                    -1,
                    100);
                AddRoomCandidates(
                    candidates,
                    mainCharacter,
                    __instance,
                    room,
                    restTags,
                    false,
                    "SAME_FLOOR",
                    -1,
                    100);
            }

            AddLoungeCandidates(
                candidates,
                mainCharacter,
                __instance,
                patientDepartment,
                currentRoom,
                hospitalizedPatientTags,
                restTags);

            if (candidates.Count == 0)
            {
                LogVanillaDecision(
                    mainCharacter,
                    __result,
                    leisureRoll,
                    "NO_EXTERNAL_TARGET");
                return;
            }

            List<FreeTimeCandidate> restCandidates =
                new List<FreeTimeCandidate>();
            List<FreeTimeCandidate> educationCandidates =
                new List<FreeTimeCandidate>();
            List<FreeTimeCandidate> visualCandidates =
                new List<FreeTimeCandidate>();

            for (int i = 0; i < candidates.Count; i++)
            {
                FreeTimeCandidate candidate = candidates[i];
                if (candidate.ActivityKind == LeisureActivityKind.Rest)
                {
                    restCandidates.Add(candidate);
                }
                else if (candidate.ActivityKind == LeisureActivityKind.Education)
                {
                    educationCandidates.Add(candidate);
                }
                else
                {
                    visualCandidates.Add(candidate);
                }
            }

            SortByRouteDistance(restCandidates);
            SortByRouteDistance(educationCandidates);
            SortByRouteDistance(visualCandidates);

            LeisureActivityKind selectedActivity;
            int activityRoll;
            int totalActivityWeight;
            if (!TryChooseActivity(
                    restCandidates.Count,
                    educationCandidates.Count,
                    visualCandidates.Count,
                    out selectedActivity,
                    out activityRoll,
                    out totalActivityWeight))
            {
                LogVanillaDecision(
                    mainCharacter,
                    __result,
                    leisureRoll,
                    "NO_WEIGHTED_ACTIVITY");
                return;
            }

            List<FreeTimeCandidate> selectedCandidates = visualCandidates;
            if (selectedActivity == LeisureActivityKind.Rest)
            {
                selectedCandidates = restCandidates;
            }
            else if (selectedActivity == LeisureActivityKind.Education)
            {
                selectedCandidates = educationCandidates;
            }

            int poolSize = Math.Min(
                HospitalPatientLifeConfig.CandidatePoolSize,
                selectedCandidates.Count);
            if (poolSize <= 0)
            {
                LogVanillaDecision(
                    mainCharacter,
                    __result,
                    leisureRoll,
                    "EMPTY_ACTIVITY_POOL");
                return;
            }

            int selectedIndex = UnityEngine.Random.Range(0, poolSize);
            FreeTimeCandidate selected = selectedCandidates[selectedIndex];

            if (selected == null || selected.Target == null)
            {
                LogVanillaDecision(
                    mainCharacter,
                    __result,
                    leisureRoll,
                    "INVALID_SELECTION");
                return;
            }

            TileObject vanillaTarget = __result;
            __result = selected.Target;

            if (HospitalizedPatientTrace.Enabled)
            {
                string loungeRollText = selected.FloorRoll >= 0
                    ? selected.FloorRoll + "/" + selected.RetainedChancePercent
                    : "n/a";

                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(mainCharacter) +
                    " | free-time-target=" + GetObjectId(selected.Target) +
                    " | source=" + selected.Source +
                    " | activity=" + selected.ActivityKind.ToString().ToUpperInvariant() +
                    " | floor=" + selected.Target.GetFloorIndex() +
                    " | position=" + selected.Target.m_state.m_position.m_x +
                    "," + selected.Target.m_state.m_position.m_y +
                    " | routeDistance=" + selected.RouteDistance.ToString("0.0") +
                    " | chance=" + HospitalPatientLifeConfig.SameFloorLeisureChancePercent +
                    " | roll=" + leisureRoll +
                    " | activityRoll=" + activityRoll + "/" + totalActivityWeight +
                    " | weights=" +
                    HospitalPatientLifeConfig.RestActivityWeight + "/" +
                    HospitalPatientLifeConfig.EducationActivityWeight + "/" +
                    HospitalPatientLifeConfig.VisualActivityWeight +
                    " | floorDifference=" + selected.FloorDifference +
                    " | loungeFloorRoll=" + loungeRollText +
                    " | roomsScanned=" + roomsToSearch +
                    " | candidates=" + candidates.Count +
                    " | rest=" + restCandidates.Count +
                    " | education=" + educationCandidates.Count +
                    " | visual=" + visualCandidates.Count +
                    " | pool=" + poolSize +
                    " | vanillaTarget=" + GetObjectId(vanillaTarget));
            }
        }

        private static void AddRoomCandidates(
            List<FreeTimeCandidate> candidates,
            Entity patient,
            Entity owner,
            Room room,
            string[] tags,
            bool allowDifferentFloor,
            string source,
            int floorRoll,
            int retainedChancePercent)
        {
            if (room == null || tags == null || tags.Length == 0)
            {
                return;
            }

            List<TileObject> roomTargets =
                MapScriptInterface.Instance.FindAllObjectWithTags(
                    room,
                    tags,
                    AccessRights.PATIENT);

            if (roomTargets == null)
            {
                return;
            }

            for (int i = 0; i < roomTargets.Count; i++)
            {
                AddCandidate(
                    candidates,
                    patient,
                    owner,
                    room,
                    roomTargets[i],
                    allowDifferentFloor,
                    source,
                    floorRoll,
                    retainedChancePercent);
            }
        }

        private static void AddLoungeCandidates(
            List<FreeTimeCandidate> candidates,
            Entity patient,
            Entity owner,
            Department patientDepartment,
            Room currentRoom,
            string[] hospitalizedPatientTags,
            string[] restTags)
        {
            if (patient == null ||
                patientDepartment == null ||
                patientDepartment.m_departmentPersistentData == null ||
                patientDepartment.m_departmentPersistentData.m_rooms == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return;
            }

            int currentFloorIndex = walk.GetFloorIndex();

            for (int i = 0;
                i < patientDepartment.m_departmentPersistentData.m_rooms.Count;
                i++)
            {
                Room lounge =
                    patientDepartment.m_departmentPersistentData.m_rooms[i].GetEntity();
                if (lounge == null || lounge == currentRoom || !IsLoungeRoom(lounge))
                {
                    continue;
                }

                if (lounge.m_roomPersistentData == null ||
                    lounge.m_roomPersistentData.m_department.GetEntity() != patientDepartment)
                {
                    continue;
                }

                int loungeFloorIndex = lounge.m_roomPersistentData.m_floorIndex;
                if (loungeFloorIndex < 0 ||
                    loungeFloorIndex >= Hospital.Instance.m_floors.Count)
                {
                    continue;
                }

                Floor loungeFloor = Hospital.Instance.m_floors[loungeFloorIndex];
                if (!IsRoomAccessibleForPatient(loungeFloor, lounge))
                {
                    continue;
                }

                int floorDifference = Math.Abs(loungeFloorIndex - currentFloorIndex);
                int floorRoll = -1;
                int retainedChancePercent = 100;
                if (floorDifference > 0 &&
                    !HospitalPatientLifeConfig.RollOtherFloorLounge(
                        floorDifference,
                        out floorRoll,
                        out retainedChancePercent))
                {
                    continue;
                }

                string source = floorDifference > 0
                    ? "LOUNGE_OTHER_FLOOR"
                    : "LOUNGE_SAME_FLOOR";

                AddRoomCandidates(
                    candidates,
                    patient,
                    owner,
                    lounge,
                    hospitalizedPatientTags,
                    true,
                    source,
                    floorRoll,
                    retainedChancePercent);
                AddRoomCandidates(
                    candidates,
                    patient,
                    owner,
                    lounge,
                    restTags,
                    true,
                    source,
                    floorRoll,
                    retainedChancePercent);
            }
        }

        private static bool IsOrdinaryRoomEligibleForPatient(
            Floor floor,
            Room room)
        {
            if (!IsRoomAccessibleForPatient(floor, room))
            {
                return false;
            }

            if (room.m_roomPersistentData.m_roomType.IsValid &&
                room.m_roomPersistentData.m_roomType.Entry != null)
            {
                GameDBRoomType roomType =
                    room.m_roomPersistentData.m_roomType.Entry;

                if (roomType.HasTag("hospitalization") ||
                    roomType.AcceptsOutpatients ||
                    IsLoungeRoom(room))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsRoomAccessibleForPatient(Floor floor, Room room)
        {
            if (floor == null ||
                room == null ||
                room.m_roomPersistentData == null ||
                floor.m_roomAccessRights == null)
            {
                return false;
            }

            RoomValidity validity = room.m_roomPersistentData.m_valid;
            if (validity != RoomValidity.OK &&
                validity != RoomValidity.MISSING_STAFF &&
                validity != RoomValidity.DEPARTMENT_CLOSED)
            {
                return false;
            }

            int minimumX = Math.Max(
                0,
                room.m_roomPersistentData.m_positionBottom.m_x);
            int minimumY = Math.Max(
                0,
                room.m_roomPersistentData.m_positionBottom.m_y);
            int maximumX = Math.Min(
                floor.m_size.m_x - 1,
                room.m_roomPersistentData.m_positionTop.m_x);
            int maximumY = Math.Min(
                floor.m_size.m_y - 1,
                room.m_roomPersistentData.m_positionTop.m_y);

            for (int x = minimumX; x <= maximumX; x++)
            {
                for (int y = minimumY; y <= maximumY; y++)
                {
                    Vector2i position = new Vector2i(x, y);
                    if (!room.IsPositionInRoom(position))
                    {
                        continue;
                    }

                    return (int)floor.m_roomAccessRights[x, y] <=
                        (int)AccessRights.PATIENT;
                }
            }

            return false;
        }

        private static void AddCandidate(
            List<FreeTimeCandidate> candidates,
            Entity patient,
            Entity owner,
            Room sourceRoom,
            TileObject target,
            bool allowDifferentFloor,
            string source,
            int floorRoll,
            int retainedChancePercent)
        {
            if (candidates == null ||
                patient == null ||
                target == null ||
                !IsSupportedLeisureTarget(target) ||
                target.IsBroken() ||
                !WallBookcaseLeisureAccess.IsValidForHplCandidate(target) ||
                (target.User != null && target.User != patient) ||
                (target.Owner != null && target.Owner != owner))
            {
                return;
            }

            LeisureActivityKind activityKind = GetActivityKind(target);
            if (activityKind == LeisureActivityKind.Rest &&
                sourceRoom != null &&
                IsWaitingOrReceptionRoom(sourceRoom))
            {
                return;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].Target == target)
                {
                    return;
                }
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            GridMap gridMap = GridMap.GetInstance();
            if (walk == null || walk.Floor == null || gridMap == null)
            {
                return;
            }

            int currentFloor = walk.GetFloorIndex();
            int targetFloor = target.GetFloorIndex();
            if (!allowDifferentFloor && targetFloor != currentFloor)
            {
                return;
            }

            Floor destinationFloor = GetFloor(targetFloor);
            if (destinationFloor == null)
            {
                return;
            }

            Vector2i objectPosition = target.m_state.m_position;
            Vector2i targetPosition = target.GetDefaultUseTile();
            if (!IsPositionAllowedForPatient(destinationFloor, objectPosition) ||
                !IsPositionAllowedForPatient(destinationFloor, targetPosition))
            {
                return;
            }

            float routeDistance = gridMap.GetDistance(
                currentFloor,
                walk.GetCurrentTile(),
                targetFloor,
                targetPosition,
                AccessRights.PATIENT);

            if (routeDistance < 0f)
            {
                return;
            }

            candidates.Add(new FreeTimeCandidate
            {
                Target = target,
                RouteDistance = routeDistance,
                ActivityKind = activityKind,
                Source = source ?? "SAME_FLOOR",
                FloorDifference = Math.Abs(targetFloor - currentFloor),
                FloorRoll = floorRoll,
                RetainedChancePercent = retainedChancePercent
            });
        }

        private static Floor GetFloor(int floorIndex)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            return Hospital.Instance.m_floors[floorIndex];
        }

        private static bool IsPositionAllowedForPatient(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                floor.m_roomAccessRights == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.m_size.m_x ||
                position.m_y >= floor.m_size.m_y)
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

        private static bool IsSupportedLeisureTarget(TileObject target)
        {
            if (target == null)
            {
                return false;
            }

            if (IsOutdoorBench(target) || target.HasTag("hospitalized_patient"))
            {
                return true;
            }

            return target.HasTag("rest") &&
                GetObjectId(target) == FancyChairObjectId;
        }

        internal static bool IsRestSeatForFreeTime(TileObject target, string tag)
        {
            if (target == null)
            {
                return false;
            }

            if (target.HasTag(tag))
            {
                return true;
            }

            return tag == "rest" && IsOutdoorBench(target);
        }

        private static bool IsOutdoorBench(TileObject target)
        {
            string objectId = GetObjectId(target);
            return objectId == OutsideBenchPartAId ||
                objectId == OutsideBenchPartBId;
        }

        private static bool IsWaitingOrReceptionRoom(Room room)
        {
            string roomTypeId = GetRoomTypeId(room);
            return roomTypeId == WaitingRoomTypeId ||
                roomTypeId == ReceptionRoomTypeId;
        }

        private static bool IsLoungeRoom(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                !room.m_roomPersistentData.m_roomType.IsValid ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return false;
            }

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            return GetRoomTypeId(room) == LoungeRoomTypeId ||
                roomType.HasTag("lounge");
        }

        private static string GetRoomTypeId(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                !room.m_roomPersistentData.m_roomType.IsValid ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                ID.IsNullOrNoID(room.m_roomPersistentData.m_roomType.Entry.DatabaseID))
            {
                return string.Empty;
            }

            return room.m_roomPersistentData.m_roomType.Entry.DatabaseID.ToString();
        }

        private static LeisureActivityKind GetActivityKind(TileObject target)
        {
            if (target != null &&
                (target.HasTag("rest") || IsOutdoorBench(target)))
            {
                return LeisureActivityKind.Rest;
            }

            if (target != null && target.HasTag("education"))
            {
                return LeisureActivityKind.Education;
            }

            return LeisureActivityKind.Visual;
        }

        private static void SortByRouteDistance(
            List<FreeTimeCandidate> candidates)
        {
            candidates.Sort(delegate(FreeTimeCandidate left, FreeTimeCandidate right)
            {
                return left.RouteDistance.CompareTo(right.RouteDistance);
            });
        }

        private static bool TryChooseActivity(
            int restCount,
            int educationCount,
            int visualCount,
            out LeisureActivityKind selectedActivity,
            out int roll,
            out int totalWeight)
        {
            int restWeight = restCount > 0
                ? HospitalPatientLifeConfig.RestActivityWeight
                : 0;
            int educationWeight = educationCount > 0
                ? HospitalPatientLifeConfig.EducationActivityWeight
                : 0;
            int visualWeight = visualCount > 0
                ? HospitalPatientLifeConfig.VisualActivityWeight
                : 0;

            totalWeight = restWeight + educationWeight + visualWeight;
            roll = 0;
            selectedActivity = LeisureActivityKind.Visual;

            if (totalWeight <= 0)
            {
                return false;
            }

            roll = UnityEngine.Random.Range(0, totalWeight);
            if (roll < restWeight)
            {
                selectedActivity = LeisureActivityKind.Rest;
                return true;
            }

            if (roll < restWeight + educationWeight)
            {
                selectedActivity = LeisureActivityKind.Education;
                return true;
            }

            selectedActivity = LeisureActivityKind.Visual;
            return true;
        }

        private static void LogVanillaDecision(
            Entity patient,
            TileObject vanillaTarget,
            int roll,
            string reason)
        {
            if (!HospitalizedPatientTrace.Enabled)
            {
                return;
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | free-time-target=" + GetObjectId(vanillaTarget) +
                " | source=VANILLA" +
                " | chance=" + HospitalPatientLifeConfig.SameFloorLeisureChancePercent +
                " | roll=" + roll +
                " | reason=" + reason);
        }

        private static string GetObjectId(TileObject target)
        {
            if (target == null ||
                target.m_state == null ||
                target.m_state.m_gameDBObject == null ||
                target.m_state.m_gameDBObject.Entry == null ||
                ID.IsNullOrNoID(target.m_state.m_gameDBObject.Entry.DatabaseID))
            {
                return "<none>";
            }

            return target.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
        }
    }

    [HarmonyPatch]
    internal static class HospitalizedFreeTimeOutdoorBenchSitPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedFreeTime),
                "ChooseSomething",
                Type.EmptyTypes);

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedFreeTime.ChooseSomething() was not found.");
            }

            return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo hasTag = AccessTools.Method(
                typeof(TileObject),
                "HasTag",
                new Type[] { typeof(string) });
            MethodInfo replacement = AccessTools.Method(
                typeof(HospitalizedFreeTimeDestinationPatch),
                "IsRestSeatForFreeTime",
                new Type[]
                {
                    typeof(TileObject),
                    typeof(string)
                });

            if (hasTag == null || replacement == null)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Outdoor bench free-time sit patch could not resolve its helper methods; native behavior is unchanged.");
                }
                return codes;
            }

            int replacements = 0;
            for (int i = 1; i < codes.Count; i++)
            {
                if (codes[i - 1].opcode != OpCodes.Ldstr ||
                    !object.Equals(codes[i - 1].operand, "rest"))
                {
                    continue;
                }

                MethodInfo calledMethod = codes[i].operand as MethodInfo;
                if (calledMethod != hasTag)
                {
                    continue;
                }

                codes[i].opcode = OpCodes.Call;
                codes[i].operand = replacement;
                replacements++;
            }

            if (replacements != 1 && Plugin.Log != null)
            {
                Plugin.Log.LogWarning(
                    "Outdoor bench free-time sit patch expected one rest-tag check but found " +
                    replacements + "; review the game method before relying on outside benches.");
            }

            return codes;
        }
    }
}
