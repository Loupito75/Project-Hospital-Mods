using System;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(
        typeof(MapScriptInterface),
        "FindClosestFreeObjectWithTags",
        new Type[]
        {
            typeof(Entity),
            typeof(Entity),
            typeof(Vector2i),
            typeof(Room),
            typeof(string[]),
            typeof(AccessRights),
            typeof(bool),
            typeof(DatabaseEntryRef<GameDBRoomType>[]),
            typeof(bool)
        })]
    internal static class HospitalizedProcedureEquipmentSelectionPatch
    {
        private struct CandidateScore
        {
            internal float Distance;
            internal bool UsesTransportTarget;
            internal int FromFloor;
            internal Vector2i From;
            internal int ToFloor;
            internal Vector2i To;
        }

        private struct SelectionState
        {
            internal TileObject Reachable;
            internal float ReachableDistance;
            internal CandidateScore ReachableScore;
            internal TileObject NegativeFallback;
            internal CandidateScore NegativeFallbackScore;
            internal bool SawReachable;
            internal bool SawNegative;
        }

        private static bool Prefix(
            Entity character,
            Entity owner,
            Vector2i position,
            Room room,
            string[] tags,
            AccessRights accessRights,
            bool allowObjectsWithAttachments,
            DatabaseEntryRef<GameDBRoomType>[] roomTypes,
            bool allowedOutsideOfRoom,
            ref TileObject __result)
        {
            if (!ShouldUseCorrectedSelector(
                    character,
                    owner,
                    room,
                    tags,
                    accessRights,
                    allowObjectsWithAttachments))
            {
                return true;
            }

            __result =
                FindClosestFreeObjectWithCorrectedScoring(
                    character,
                    owner,
                    room,
                    tags,
                    accessRights,
                    allowObjectsWithAttachments,
                    roomTypes,
                    allowedOutsideOfRoom);

            return false;
        }

        private static bool ShouldUseCorrectedSelector(
            Entity character,
            Entity owner,
            Room room,
            string[] tags,
            AccessRights accessRights,
            bool allowObjectsWithAttachments)
        {
            if (!FixConfig.FixHospitalizedProcedureEquipmentSelection ||
                character == null ||
                !object.ReferenceEquals(character, owner) ||
                room == null ||
                tags == null ||
                tags.Length == 0 ||
                accessRights != AccessRights.PATIENT_PROCEDURE ||
                !allowObjectsWithAttachments ||
                character.GetComponent<WalkComponent>() == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                character.GetComponent<HospitalizationComponent>();

            return hospitalization != null &&
                hospitalization.IsHospitalized();
        }

        private static TileObject FindClosestFreeObjectWithCorrectedScoring(
            Entity character,
            Entity owner,
            Room room,
            string[] tags,
            AccessRights accessRights,
            bool allowObjectsWithAttachments,
            DatabaseEntryRef<GameDBRoomType>[] roomTypes,
            bool allowedOutsideOfRoom)
        {
            SelectionState selection = new SelectionState();
            selection.ReachableDistance = float.MaxValue;

            Floor floor =
                Hospital.Instance.m_floors[room.GetFloorIndex()];

            for (int i = room.m_roomPersistentData.m_positionBottom.m_x;
                i <= room.m_roomPersistentData.m_positionTop.m_x;
                i++)
            {
                for (int j = room.m_roomPersistentData.m_positionBottom.m_y;
                    j <= room.m_roomPersistentData.m_positionTop.m_y;
                    j++)
                {
                    Vector2i tile = new Vector2i(i, j);

                    if (!room.IsPositionInRoom(tile) ||
                        (floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[i, j] > accessRights &&
                        (floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[i, j] != AccessRights.BIOHAZARD ||
                            accessRights != AccessRights.PATIENT_PROCEDURE) &&
                        (accessRights != AccessRights.PATIENT ||
                            floor.m_accessibilityPatients[i, j] == 2)))
                    {
                        continue;
                    }

                    TileObjects tileObjects = floor.m_tileObjects[i, j];
                    Room roomAtTile = floor.m_roomTiles[i, j];

                    bool roomUsable =
                        roomAtTile == null ||
                        roomAtTile.m_roomPersistentData.m_valid == RoomValidity.OK ||
                        roomAtTile.m_roomPersistentData.m_valid == RoomValidity.MISSING_STAFF ||
                        roomAtTile.m_roomPersistentData.m_valid == RoomValidity.DEPARTMENT_CLOSED ||
                        (accessRights == AccessRights.STAFF &&
                            roomAtTile.m_roomPersistentData.m_valid == RoomValidity.INACCESSIBLE_PATIENTS);

                    if (roomAtTile != null &&
                        roomAtTile.m_roomPersistentData.m_roomType.Entry.AcceptsOutpatients)
                    {
                        roomUsable = false;
                    }

                    bool allowedRoomType =
                        allowedOutsideOfRoom &&
                        roomAtTile == null;

                    if (roomTypes != null &&
                        roomAtTile != null)
                    {
                        foreach (DatabaseEntryRef<GameDBRoomType> roomType in roomTypes)
                        {
                            if (roomType.Entry ==
                                roomAtTile.m_roomPersistentData.m_roomType.Entry)
                            {
                                allowedRoomType = true;
                            }
                        }
                    }

                    if (roomTypes != null &&
                        !allowedRoomType)
                    {
                        continue;
                    }

                    TileObject center = tileObjects.m_centerObject;

                    if (center != null &&
                        center.HasAllTags(tags) &&
                        (center.User == null || center.User == character) &&
                        (center.Owner == null || center.Owner == owner) &&
                        !center.IsBroken() &&
                        center.IsValid() &&
                        roomUsable &&
                        (allowObjectsWithAttachments ||
                            tileObjects.m_attachmentObject == null))
                    {
                        if (center.m_state.m_compositeParent.GetEntity() != null &&
                            center.m_state.m_compositeParent.GetEntity().HasUnusablePart())
                        {
                            continue;
                        }

                        ConsiderCandidate(
                            character,
                            center,
                            tile,
                            room.GetFloorIndex(),
                            accessRights,
                            ref selection);
                    }

                    foreach (TileObject nonCenter in tileObjects.GetAllNonCenterObjects())
                    {
                        if (nonCenter == null ||
                            !nonCenter.HasAllTags(tags) ||
                            (nonCenter.User != null && nonCenter.User != character) ||
                            (nonCenter.Owner != null && nonCenter.Owner != owner) ||
                            nonCenter.IsBroken() ||
                            !nonCenter.IsValid() ||
                            !roomUsable)
                        {
                            continue;
                        }

                        ConsiderCandidate(
                            character,
                            nonCenter,
                            tile,
                            room.GetFloorIndex(),
                            accessRights,
                            ref selection);
                    }
                }
            }

            TileObject result =
                selection.Reachable != null
                    ? selection.Reachable
                    : selection.NegativeFallback;

            CandidateScore selectedScore =
                selection.Reachable != null
                    ? selection.ReachableScore
                    : selection.NegativeFallbackScore;

            HfxDiagnostics.ReportHospitalizedProcedureEquipmentSelection(
                character,
                tags,
                result,
                selectedScore.Distance,
                selectedScore.From,
                selectedScore.FromFloor,
                selectedScore.To,
                selectedScore.ToFloor,
                selectedScore.UsesTransportTarget,
                selection.SawNegative,
                selection.SawReachable);

            return result;
        }

        private static void ConsiderCandidate(
            Entity character,
            TileObject candidate,
            Vector2i vanillaTarget,
            int vanillaTargetFloor,
            AccessRights accessRights,
            ref SelectionState selection)
        {
            CandidateScore score =
                GetCandidateScore(
                    character,
                    candidate,
                    vanillaTarget,
                    vanillaTargetFloor,
                    accessRights);

            if (score.Distance >= 0f)
            {
                selection.SawReachable = true;

                if (selection.Reachable == null ||
                    score.Distance < selection.ReachableDistance)
                {
                    selection.Reachable = candidate;
                    selection.ReachableDistance = score.Distance;
                    selection.ReachableScore = score;
                }

                return;
            }

            selection.SawNegative = true;

            // Preserve Vanilla's all-negative behavior as a fallback.
            if (selection.NegativeFallback == null)
            {
                selection.NegativeFallback = candidate;
                selection.NegativeFallbackScore = score;
            }
        }

        private static CandidateScore GetCandidateScore(
            Entity character,
            TileObject candidate,
            Vector2i vanillaTarget,
            int vanillaTargetFloor,
            AccessRights accessRights)
        {
            CandidateScore score = new CandidateScore();
            WalkComponent walk = character.GetComponent<WalkComponent>();

            score.FromFloor = walk.GetFloorIndex();
            score.From = walk.GetCurrentTile();
            score.ToFloor = vanillaTargetFloor;
            score.To = vanillaTarget;

            TileObject transportPoint =
                TryGetPartWithTag(candidate, "transport_point");

            if (transportPoint != null &&
                transportPoint.m_state != null)
            {
                score.UsesTransportTarget = true;

                TileObject sittingObject =
                    TryGetTileObject(
                        walk.m_state != null
                            ? walk.m_state.m_objectSittingOn
                            : null);

                if (sittingObject != null &&
                    sittingObject.m_state != null)
                {
                    score.FromFloor = sittingObject.m_state.m_floorIndex;
                    score.From = sittingObject.m_state.m_position;
                }

                score.ToFloor = transportPoint.m_state.m_floorIndex;
                score.To = transportPoint.GetDefaultStretcherTile();
            }

            score.Distance =
                GridMap.GetInstance().GetDistance(
                    score.FromFloor,
                    score.From,
                    score.ToFloor,
                    score.To,
                    accessRights);

            return score;
        }

        private static TileObject TryGetPartWithTag(
            TileObject tileObject,
            string tag)
        {
            if (tileObject == null ||
                tileObject.m_state == null)
            {
                return null;
            }

            try
            {
                return tileObject.GetPartWithTag(tag);
            }
            catch
            {
                return null;
            }
        }

        private static TileObject TryGetTileObject(
            EntityIDPointer<TileObject> pointer)
        {
            if (pointer == null)
            {
                return null;
            }

            try
            {
                return pointer.GetEntity();
            }
            catch
            {
                return null;
            }
        }

    }
}
