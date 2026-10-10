using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    internal static class BathroomAvailabilityAudit
    {
        private static readonly Dictionary<int, string> LastSnapshotByFloor =
            new Dictionary<int, string>();

        internal static void Reset()
        {
            LastSnapshotByFloor.Clear();
        }

        internal static void LogRefusedAvailability(
            Entity character,
            AccessRights accessRights,
            EquipmentListRules equipmentListRules,
            ProcedureSceneAvailability availability)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                character == null ||
                Hospital.Instance == null)
            {
                return;
            }

            WalkComponent walkComponent = character.GetComponent<WalkComponent>();
            if (walkComponent == null)
            {
                return;
            }

            int floorIndex = walkComponent.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return;
            }

            List<string> details = new List<string>();
            int validFree = 0;
            int locked = 0;
            int ghost = 0;

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    AddCandidate(
                        floor,
                        character,
                        floor.m_tileObjects[x, y].m_centerObject,
                        details,
                        ref validFree,
                        ref locked,
                        ref ghost);
                    AddCandidate(
                        floor,
                        character,
                        floor.m_tileObjects[x, y].m_attachmentObject,
                        details,
                        ref validFree,
                        ref locked,
                        ref ghost);
                }
            }

            StringBuilder signatureBuilder = new StringBuilder();
            signatureBuilder.Append(availability).Append('|');
            signatureBuilder.Append(accessRights).Append('|');
            signatureBuilder.Append(equipmentListRules).Append('|');
            signatureBuilder.Append("free=").Append(validFree).Append('|');
            signatureBuilder.Append("locked=").Append(locked).Append('|');
            signatureBuilder.Append("ghost=").Append(ghost);
            for (int i = 0; i < details.Count; i++)
            {
                signatureBuilder.Append('|').Append(details[i]);
            }

            string signature = signatureBuilder.ToString();
            string previous;
            if (LastSnapshotByFloor.TryGetValue(floorIndex, out previous) &&
                previous == signature)
            {
                return;
            }

            LastSnapshotByFloor[floorIndex] = signature;

            Plugin.Log?.LogInfo("[BathroomDebug] wc-availability-refused" +
                " | character=" + BathroomFlowDiagnostics.CharacterName(character) +
                " | floor=" + floorIndex +
                " | availability=" + availability +
                " | accessRights=" + accessRights +
                " | equipmentRules=" + equipmentListRules +
                " | wcCandidates=" + details.Count +
                " | validFree=" + validFree +
                " | locked=" + locked +
                " | ghost=" + ghost);

            for (int i = 0; i < details.Count; i++)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] wc-availability-candidate" +
                    " | floor=" + floorIndex +
                    " | " + details[i]);
            }
        }

        internal static void LogHospitalizedSelectionSnapshot(
            Entity character,
            TileObject selected)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                character == null ||
                !PrivateBathroomManager.IsHospitalizedPatient(character) ||
                Hospital.Instance == null)
            {
                return;
            }

            WalkComponent walkComponent = character.GetComponent<WalkComponent>();
            if (walkComponent == null)
            {
                return;
            }

            int floorIndex = walkComponent.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return;
            }

            List<string> details = new List<string>();
            int validFree = 0;
            int locked = 0;
            int ghost = 0;

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    AddCandidate(
                        floor,
                        character,
                        floor.m_tileObjects[x, y].m_centerObject,
                        details,
                        ref validFree,
                        ref locked,
                        ref ghost);
                    AddCandidate(
                        floor,
                        character,
                        floor.m_tileObjects[x, y].m_attachmentObject,
                        details,
                        ref validFree,
                        ref locked,
                        ref ghost);
                }
            }

            Plugin.Log?.LogInfo("[BathroomDebug] hospitalized-wc-selection-audit" +
                " | character=" + BathroomFlowDiagnostics.CharacterName(character) +
                " | floor=" + floorIndex +
                " | selected=" + BathroomFlowDiagnostics.ObjectName(selected) +
                " | wcCandidates=" + details.Count +
                " | validFree=" + validFree +
                " | locked=" + locked +
                " | ghost=" + ghost);

            for (int i = 0; i < details.Count; i++)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] hospitalized-wc-candidate" +
                    " | character=" + BathroomFlowDiagnostics.CharacterName(character) +
                    " | floor=" + floorIndex +
                    " | " + details[i]);
            }
        }

        private static void AddCandidate(
            Floor floor,
            Entity character,
            TileObject toilet,
            List<string> details,
            ref int validFree,
            ref int locked,
            ref int ghostCount)
        {
            if (toilet == null || !toilet.HasTag("wc"))
            {
                return;
            }

            bool valid = toilet.IsValid();
            bool broken = toilet.IsBroken();
            bool free = toilet.User == null && toilet.Owner == null;
            if (valid && !broken && free)
            {
                validFree++;
            }
            if (!free)
            {
                locked++;
            }

            Entity user = toilet.User;
            UseComponent userUse = user == null ? null : user.GetComponent<UseComponent>();
            WalkComponent userWalk = user == null ? null : user.GetComponent<WalkComponent>();
            TileObject userReserved = BathroomFixtureHandoff.GetReservedObject(userUse);
            TileObject userCurrent = BathroomFixtureHandoff.GetCurrentObject(userUse);
            TileObject userSitting = null;

            if (userWalk != null &&
                userWalk.m_state != null &&
                userWalk.m_state.m_objectToSitOn != null)
            {
                userSitting = userWalk.m_state.m_objectToSitOn.GetEntity();
            }

            bool reservedMatches = user != null && userReserved == toilet;
            bool currentMatches = user != null && userCurrent == toilet;
            bool sittingMatches = user != null && userSitting == toilet;
            bool userGhost =
                user != null &&
                !reservedMatches &&
                !currentMatches &&
                !sittingMatches;

            ProcedureScriptNeedBladder ownerScript = toilet.Owner as ProcedureScriptNeedBladder;
            string ownerState = "<none>";
            Entity ownerMain = null;
            bool ownerReservedMatches = false;
            bool ownerCurrentMatches = false;
            bool ownerGhost = false;

            if (ownerScript != null && ownerScript.m_stateData != null)
            {
                ownerState = string.IsNullOrEmpty(ownerScript.m_stateData.m_state)
                    ? "<empty>"
                    : ownerScript.m_stateData.m_state;

                if (ownerScript.m_stateData.m_procedureScene != null)
                {
                    ownerMain = ownerScript.m_stateData.m_procedureScene.MainCharacter;
                }

                UseComponent ownerUse = ownerMain == null
                    ? null
                    : ownerMain.GetComponent<UseComponent>();
                ownerReservedMatches =
                    BathroomFixtureHandoff.GetReservedObject(ownerUse) == toilet;
                ownerCurrentMatches =
                    BathroomFixtureHandoff.GetCurrentObject(ownerUse) == toilet;

                bool ownerUserMismatch = ownerMain == null || toilet.User != ownerMain;
                if (ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_OBJECT)
                {
                    ownerGhost = ownerUserMismatch || !ownerReservedMatches;
                }
                else if (ownerState == ProcedureScriptNeedBladder.STATE_USING_OBJECT)
                {
                    ownerGhost = ownerUserMismatch || !ownerCurrentMatches;
                }
                else if (TrafficControlConfig.ReleaseToiletOwnerAfterUse &&
                         !SingleToiletBathroomLockManager.IsSingleToiletRoom(toilet) &&
                         (ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_SINK ||
                          ownerState == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                          ownerState == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE ||
                          ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_DRYER ||
                          ownerState == ProcedureScriptNeedBladder.STATE_USING_DRYER ||
                          ownerState == ProcedureScriptNeedBladder.STATE_IDLE))
                {
                    ownerGhost = true;
                }
            }

            bool ghost = userGhost || ownerGhost;
            if (ghost)
            {
                ghostCount++;
            }

            Vector2i position = toilet.m_state.m_position;
            Vector2f usePosition = toilet.GetDefaultUsePosition();
            Vector2i useTile = new Vector2i(
                (int)(usePosition.m_x + 0.5f),
                (int)(usePosition.m_y + 0.5f));

            Room room = IsInsideFloor(floor, position)
                ? floor.m_roomTiles[position.m_x, position.m_y]
                : null;
            GameDBRoomType roomType =
                room == null ||
                room.m_roomPersistentData == null
                    ? null
                    : room.m_roomPersistentData.m_roomType.Entry;

            RoomValidity roomValidity =
                room == null || room.m_roomPersistentData == null
                    ? (RoomValidity)0
                    : room.m_roomPersistentData.m_valid;
            string roomTypeId =
                roomType == null ? "<none>" : roomType.DatabaseID.ToString();
            string roomTypeAccess =
                roomType == null ? "<none>" : roomType.AccessRights.ToString();

            string objectRoomAccess = GetRoomAccess(floor, position);
            string objectLogisticsAccess = GetLogisticsAccess(floor, position);
            string useRoomAccess = GetRoomAccess(floor, useTile);
            string useLogisticsAccess = GetLogisticsAccess(floor, useTile);
            string objectPatientFlood = GetPatientFloodState(floor, position);
            string usePatientFlood = GetPatientFloodState(floor, useTile);

            WalkComponent characterWalk =
                character == null ? null : character.GetComponent<WalkComponent>();
            Vector2i origin =
                characterWalk == null ? position : characterWalk.GetCurrentTile();
            int distancePatient = GetDistance(
                floor,
                origin,
                useTile,
                AccessRights.PATIENT);
            int distanceProcedure = GetDistance(
                floor,
                origin,
                useTile,
                AccessRights.PATIENT_PROCEDURE);

            bool privateBathroom =
                PrivateBathroomManager.IsPrivateHospitalBathroom(toilet);
            bool privateForCharacter =
                character != null &&
                PrivateBathroomManager.IsPrivateHospitalBathroomForPatient(
                    toilet,
                    character);

            AccessRights tileAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];
            bool portraitsEnabled =
                SettingsManager.Instance != null &&
                SettingsManager.Instance.m_viewSettings.m_showObjectReservation.m_value;

            details.Add(
                "wc=" + BathroomFlowDiagnostics.ObjectName(toilet) +
                "{valid=" + valid +
                ",broken=" + broken +
                ",free=" + free +
                ",tileAccess=" + tileAccess +
                ",roomType=" + roomTypeId +
                ",roomValidity=" + roomValidity +
                ",roomTypeAccess=" + roomTypeAccess +
                ",objectRoomAccess=" + objectRoomAccess +
                ",objectLogisticsAccess=" + objectLogisticsAccess +
                ",useTile=" + useTile +
                ",useRoomAccess=" + useRoomAccess +
                ",useLogisticsAccess=" + useLogisticsAccess +
                ",patientFloodObject=" + objectPatientFlood +
                ",patientFloodUse=" + usePatientFlood +
                ",distancePATIENT=" + distancePatient +
                ",distancePATIENT_PROCEDURE=" + distanceProcedure +
                ",private=" + privateBathroom +
                ",privateForCharacter=" + privateForCharacter +
                ",user=" + BathroomFlowDiagnostics.CharacterName(user) +
                ",owner=" + (toilet.Owner == null ? "<none>" : toilet.Owner.GetType().Name) +
                ",ownerState=" + ownerState +
                ",ownerMain=" + BathroomFlowDiagnostics.CharacterName(ownerMain) +
                ",reservedMatches=" + reservedMatches +
                ",currentMatches=" + currentMatches +
                ",sittingMatches=" + sittingMatches +
                ",ownerReservedMatches=" + ownerReservedMatches +
                ",ownerCurrentMatches=" + ownerCurrentMatches +
                ",portraitExpected=" + (portraitsEnabled && reservedMatches) +
                ",ghost=" + ghost + "}");
        }

        private static string GetRoomAccess(Floor floor, Vector2i tile)
        {
            if (!IsInsideFloor(floor, tile))
            {
                return "<out-of-bounds>";
            }

            return floor.m_roomAccessRights[tile.m_x, tile.m_y].ToString();
        }

        private static string GetLogisticsAccess(Floor floor, Vector2i tile)
        {
            if (!IsInsideFloor(floor, tile))
            {
                return "<out-of-bounds>";
            }

            return floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                tile.m_x,
                tile.m_y].ToString();
        }

        private static string GetPatientFloodState(Floor floor, Vector2i tile)
        {
            if (!IsInsideFloor(floor, tile))
            {
                return "<out-of-bounds>";
            }

            byte value = floor.m_accessibilityPatients[tile.m_x, tile.m_y];
            return value == 2
                ? "BLOCKED(" + value + ")"
                : "REACHABLE(" + value + ")";
        }

        private static int GetDistance(
            Floor floor,
            Vector2i origin,
            Vector2i destination,
            AccessRights accessRights)
        {
            if (!IsInsideFloor(floor, origin) ||
                !IsInsideFloor(floor, destination) ||
                GridMap.GetInstance() == null)
            {
                return -1;
            }

            return (int)GridMap.GetInstance().GetDistance(
                floor.m_floorIndex,
                origin,
                floor.m_floorIndex,
                destination,
                accessRights);
        }

        private static bool IsInsideFloor(Floor floor, Vector2i tile)
        {
            return floor != null &&
                   tile.m_x >= 0 &&
                   tile.m_y >= 0 &&
                   tile.m_x < floor.Size.m_x &&
                   tile.m_y < floor.Size.m_y;
        }
    }

    [HarmonyPatch]
    internal static class BathroomAvailabilityDiagnosticsPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureComponent),
                "GetProcedureAvailabilty",
                new Type[]
                {
                    typeof(GameDBProcedure),
                    typeof(Entity),
                    typeof(Department),
                    typeof(AccessRights),
                    typeof(EquipmentListRules)
                });
        }

        private static void Postfix(
            GameDBProcedure procedure,
            Entity patient,
            AccessRights accessRights,
            EquipmentListRules equipmentListRules,
            ProcedureSceneAvailability __result)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                __result == ProcedureSceneAvailability.AVAILABLE ||
                procedure == null ||
                procedure.ProcedureScript != "ProcedureScriptNeedBladder")
            {
                return;
            }

            BathroomAvailabilityAudit.LogRefusedAvailability(
                patient,
                accessRights,
                equipmentListRules,
                __result);
        }
    }

    [HarmonyPatch(typeof(ProcedureManager), "Reset")]
    internal static class BathroomAvailabilityResetPatch
    {
        private static void Prefix()
        {
            BathroomAvailabilityAudit.Reset();
        }
    }
}
