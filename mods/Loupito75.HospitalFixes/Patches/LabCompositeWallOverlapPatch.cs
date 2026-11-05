using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    // The two Vanilla lab composites use one center A part plus a B part that
    // is PlacedToEdge on the wall-facing side. That B part must remain in its
    // real native edge slot so composite ownership, save/load and Undo/Redo
    // stay Vanilla. When a reviewed wall/ceiling object needs the same face,
    // HFX stores only that upper object in another free edge field while
    // preserving its real Orientation. ObjectRenderer uses slot 3 (unused by
    // Vanilla's 0/1/2/4/8/9/10 layout) for the parked upper object's visual.
    internal static class LabCompositeWallOverlapPatch
    {
        private const int ParkedRenderSlot = 3;

        private static Direction s_preferredParkingDirection =
            Direction.None;

        private static readonly Direction[] EdgeDirections =
            new Direction[]
            {
                Direction.NW,
                Direction.NE,
                Direction.SE,
                Direction.SW
            };

        private static readonly FieldInfo ObjectObjectsField =
            AccessTools.Field(
                typeof(ObjectRenderer),
                "m_objectObjects");

        private sealed class ParkingMove
        {
            internal TileObjects Objects;
            internal Direction From;
            internal Direction To;
            internal TileObject TileObject;
        }

        private sealed class RenderCleanup
        {
            internal int FloorIndex;
            internal Vector2i Position;
        }

        private static readonly List<RenderCleanup>
            PendingRenderCleanup =
                new List<RenderCleanup>();

        internal static bool CanPrepareUpperPlacement(
            Floor floor,
            Vector2i tile,
            Direction targetDirection,
            TileObject targetEdge,
            CompositeObject parent,
            GameDBObject upper,
            float requiredHeight)
        {
            if (floor == null ||
                floor.m_tileObjects == null ||
                targetEdge == null ||
                parent == null ||
                !IsCompatibleUpper(
                    upper,
                    requiredHeight))
            {
                return false;
            }

            TileObjects objects =
                floor.m_tileObjects[
                    tile.m_x,
                    tile.m_y];

            if (IsLabEdgePartOfParent(
                    targetEdge,
                    parent))
            {
                if (FindParkedUpperForOrientation(
                        objects,
                        targetDirection) != null)
                {
                    return false;
                }

                Direction parking;
                return TryFindFreeParkingDirection(
                    objects,
                    targetDirection,
                    Direction.None,
                    out parking);
            }

            Direction storedDirection;
            if (!TryGetStoredDirection(
                    objects,
                    targetEdge,
                    out storedDirection) ||
                storedDirection != targetDirection ||
                targetEdge.Orientation ==
                    targetDirection ||
                !IsCompatibleUpper(
                    targetEdge.m_state
                        .m_gameDBObject.Entry,
                    requiredHeight))
            {
                return false;
            }

            TileObject labPart =
                GetEdgeObject(
                    objects,
                    targetEdge.Orientation);
            if (!IsLabEdgePartOfParent(
                    labPart,
                    parent))
            {
                return false;
            }

            Direction replacementParking;
            return TryFindFreeParkingDirection(
                objects,
                targetEdge.Orientation,
                targetDirection,
                out replacementParking);
        }

        internal static bool
            CanMoveExistingUpperForIncomingLabPart(
                Floor floor,
                Vector2i tile,
                Direction targetDirection,
                GameDBObject labPart,
                TileObject existingUpper,
                float requiredHeight)
        {
            if (floor == null ||
                floor.m_tileObjects == null ||
                !WallObjectPlacementConflictsPatch
                    .IsReviewedLabEdgePart(
                        labPart) ||
                !IsCompatibleUpper(
                    existingUpper,
                    requiredHeight))
            {
                return false;
            }

            Direction parking;
            return TryFindFreeParkingDirection(
                floor.m_tileObjects[
                    tile.m_x,
                    tile.m_y],
                targetDirection,
                Direction.None,
                out parking);
        }

        internal static bool TryAssignLoadedEdge(
            TileObjects objects,
            Direction direction,
            TileObject value)
        {
            if (objects == null ||
                value == null)
            {
                return false;
            }

            TileObject existing =
                GetEdgeObject(
                    objects,
                    direction);
            if (existing == null)
            {
                return false;
            }

            // A previously parked upper object can occupy the storage field
            // needed by a later-loaded ordinary edge object. Move the parked
            // object again before assigning the incoming object.
            if (IsUpperStoredAwayFromOrientation(
                    objects,
                    existing,
                    direction))
            {
                Direction replacement;
                if (TryFindFreeParkingDirection(
                        objects,
                        existing.Orientation,
                        direction,
                        out replacement))
                {
                    SetEdgeObject(
                        objects,
                        direction,
                        null);
                    SetEdgeObject(
                        objects,
                        replacement,
                        existing);
                    SetEdgeObject(
                        objects,
                        direction,
                        value);
                    objects.SetDirty(true);
                    return true;
                }

                LogParkingError(
                    "load-repark-no-free-slot",
                    existing,
                    direction);
                return true;
            }

            GameDBObject existingType =
                existing.m_state == null
                    ? null
                    : existing.m_state
                        .m_gameDBObject.Entry;
            GameDBObject valueType =
                value.m_state == null
                    ? null
                    : value.m_state
                        .m_gameDBObject.Entry;

            if (WallObjectPlacementConflictsPatch
                .IsReviewedLabEdgePart(
                    existingType))
            {
                float requiredHeight;
                if (WallObjectPlacementConflictsPatch
                        .TryGetRequiredWallObjectHeight(
                            existingType,
                            out requiredHeight) &&
                    IsCompatibleUpper(
                        value,
                        requiredHeight))
                {
                    Direction parking;
                    if (TryFindFreeParkingDirection(
                            objects,
                            direction,
                            Direction.None,
                            out parking))
                    {
                        SetEdgeObject(
                            objects,
                            parking,
                            value);
                        objects.SetDirty(true);
                        return true;
                    }
                }

                LogParkingError(
                    "load-upper-over-lab-no-free-slot",
                    value,
                    direction);
                // Preserve the composite B part rather than overwrite it.
                return true;
            }

            if (WallObjectPlacementConflictsPatch
                .IsReviewedLabEdgePart(
                    valueType))
            {
                float requiredHeight;
                if (WallObjectPlacementConflictsPatch
                        .TryGetRequiredWallObjectHeight(
                            valueType,
                            out requiredHeight) &&
                    IsCompatibleUpper(
                        existing,
                        requiredHeight))
                {
                    Direction parking;
                    if (TryFindFreeParkingDirection(
                            objects,
                            direction,
                            Direction.None,
                            out parking))
                    {
                        SetEdgeObject(
                            objects,
                            direction,
                            null);
                        SetEdgeObject(
                            objects,
                            parking,
                            existing);
                        SetEdgeObject(
                            objects,
                            direction,
                            value);
                        objects.SetDirty(true);
                        return true;
                    }
                }

                LogParkingError(
                    "load-lab-over-upper-no-free-slot",
                    existing,
                    direction);
                // Composite integrity wins if an invalid/corrupt state is
                // encountered. The displaced upper entity remains loaded but
                // is not allowed to overwrite the lab part.
                SetEdgeObject(
                    objects,
                    direction,
                    value);
                objects.SetDirty(true);
                return true;
            }

            return false;
        }

        internal static Direction GetRenderTileSideDirection(
            TileObject tileObject,
            Floor floor)
        {
            if (IsParkedLabUpper(
                    floor,
                    tileObject))
            {
                return (Direction)ParkedRenderSlot;
            }

            return tileObject == null
                ? Direction.None
                : tileObject.Orientation;
        }

        private static bool IsCompatibleUpper(
            TileObject upper,
            float requiredHeight)
        {
            return upper != null &&
                upper.m_state != null &&
                IsCompatibleUpper(
                    upper.m_state.m_gameDBObject.Entry,
                    requiredHeight);
        }

        private static bool IsCompatibleUpper(
            GameDBObject upper,
            float requiredHeight)
        {
            return WallObjectPlacementConflictsPatch
                    .IsSupportedUpperObject(
                        upper) &&
                WallObjectPlacementConflictsPatch
                    .GetEffectiveUpperHeight(
                        upper) >=
                    requiredHeight;
        }

        private static bool IsLabEdgePartOfParent(
            TileObject tileObject,
            CompositeObject parent)
        {
            return tileObject != null &&
                tileObject.m_state != null &&
                WallObjectPlacementConflictsPatch
                    .IsReviewedLabEdgePart(
                        tileObject.m_state
                            .m_gameDBObject.Entry) &&
                tileObject.m_state
                    .m_compositeParent != null &&
                tileObject.m_state
                    .m_compositeParent.GetEntity() ==
                    parent;
        }

        private static TileObject
            FindParkedUpperForOrientation(
                TileObjects objects,
                Direction orientation)
        {
            if (objects == null)
            {
                return null;
            }

            for (int i = 0;
                 i < EdgeDirections.Length;
                 i++)
            {
                Direction stored =
                    EdgeDirections[i];
                TileObject candidate =
                    GetEdgeObject(
                        objects,
                        stored);
                if (candidate == null ||
                    candidate.Orientation !=
                        orientation ||
                    stored == orientation)
                {
                    continue;
                }

                GameDBObject type =
                    candidate.m_state == null
                        ? null
                        : candidate.m_state
                            .m_gameDBObject.Entry;
                if (WallObjectPlacementConflictsPatch
                    .IsSupportedUpperObject(
                        type))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static bool
            TryFindFreeParkingDirection(
                TileObjects objects,
                Direction trueOrientation,
                Direction blockedStorage,
                out Direction parking)
        {
            parking = Direction.None;
            if (objects == null)
            {
                return false;
            }

            if (IsUsableParkingDirection(
                    objects,
                    s_preferredParkingDirection,
                    trueOrientation,
                    blockedStorage))
            {
                parking =
                    s_preferredParkingDirection;
                return true;
            }

            for (int i = 0;
                 i < EdgeDirections.Length;
                 i++)
            {
                Direction candidate =
                    EdgeDirections[i];
                if (IsUsableParkingDirection(
                        objects,
                        candidate,
                        trueOrientation,
                        blockedStorage))
                {
                    parking = candidate;
                    return true;
                }
            }

            return false;
        }

        private static bool IsUsableParkingDirection(
            TileObjects objects,
            Direction candidate,
            Direction trueOrientation,
            Direction blockedStorage)
        {
            return candidate != Direction.None &&
                candidate != trueOrientation &&
                candidate != blockedStorage &&
                GetEdgeObject(
                    objects,
                    candidate) == null;
        }

        private static bool
            IsUpperStoredAwayFromOrientation(
                TileObjects objects,
                TileObject tileObject,
                Direction storedDirection)
        {
            if (objects == null ||
                tileObject == null ||
                tileObject.m_state == null ||
                storedDirection == Direction.None ||
                storedDirection ==
                    tileObject.Orientation ||
                !WallObjectPlacementConflictsPatch
                    .IsSupportedUpperObject(
                        tileObject.m_state
                            .m_gameDBObject.Entry))
            {
                return false;
            }

            TileObject target =
                GetEdgeObject(
                    objects,
                    tileObject.Orientation);
            return target != null &&
                target.m_state != null &&
                WallObjectPlacementConflictsPatch
                    .IsReviewedLabEdgePart(
                        target.m_state
                            .m_gameDBObject.Entry);
        }

        private static bool IsParkedLabUpper(
            Floor floor,
            TileObject tileObject)
        {
            if (floor == null ||
                floor.m_tileObjects == null ||
                tileObject == null ||
                tileObject.m_state == null)
            {
                return false;
            }

            Vector2i tile =
                tileObject.m_state.m_position;
            if (!Inside(
                    floor,
                    tile))
            {
                return false;
            }

            Direction stored;
            return TryGetStoredDirection(
                    floor.m_tileObjects[
                        tile.m_x,
                        tile.m_y],
                    tileObject,
                    out stored) &&
                IsUpperStoredAwayFromOrientation(
                    floor.m_tileObjects[
                        tile.m_x,
                        tile.m_y],
                    tileObject,
                    stored);
        }

        private static bool TryGetStoredDirection(
            TileObjects objects,
            TileObject tileObject,
            out Direction direction)
        {
            direction = Direction.None;
            if (objects == null ||
                tileObject == null)
            {
                return false;
            }

            for (int i = 0;
                 i < EdgeDirections.Length;
                 i++)
            {
                Direction candidate =
                    EdgeDirections[i];
                if (GetEdgeObject(
                        objects,
                        candidate) ==
                    tileObject)
                {
                    direction = candidate;
                    return true;
                }
            }

            return false;
        }

        private static TileObject GetEdgeObject(
            TileObjects objects,
            Direction direction)
        {
            if (objects == null)
            {
                return null;
            }

            switch (direction)
            {
                case Direction.NW:
                    return objects.m_ObjectNW;
                case Direction.NE:
                    return objects.m_ObjectNE;
                case Direction.SE:
                    return objects.m_ObjectSE;
                case Direction.SW:
                    return objects.m_ObjectSW;
                default:
                    return null;
            }
        }

        private static void SetEdgeObject(
            TileObjects objects,
            Direction direction,
            TileObject value)
        {
            switch (direction)
            {
                case Direction.NW:
                    objects.m_ObjectNW = value;
                    break;
                case Direction.NE:
                    objects.m_ObjectNE = value;
                    break;
                case Direction.SE:
                    objects.m_ObjectSE = value;
                    break;
                case Direction.SW:
                    objects.m_ObjectSW = value;
                    break;
            }
        }

        private static bool ResolvePlacement(
            Floor floor,
            GameDBObject objectType,
            Vector2i position,
            Direction orientation,
            bool noRotation,
            out Vector2i tile,
            out Direction direction)
        {
            tile =
                noRotation
                    ? position
                    : IsometricCameraUtils
                        .GetRotatedPosition(
                            position.m_x,
                            position.m_y);
            direction =
                noRotation
                    ? orientation
                    : IsometricCameraUtils
                        .GetRotatedDirectionInverse(
                            orientation);

            if (!Inside(
                    floor,
                    tile))
            {
                return false;
            }

            Direction forced =
                floor.GetForcedOrientation(
                    position,
                    objectType);
            if (forced != Direction.None)
            {
                direction = forced;
            }

            return direction !=
                Direction.None;
        }

        private static Department ResolveDepartment(
            Floor floor,
            Vector2i tile,
            Department department)
        {
            if (department != null)
            {
                return department;
            }

            Room room =
                floor.m_roomTiles[
                    tile.m_x,
                    tile.m_y];
            if (room != null)
            {
                return room.m_roomPersistentData
                    .m_department.GetEntity();
            }

            return Hospital.Instance
                .m_activeDepartment.GetEntity();
        }

        private static bool TryMoveEdgeObject(
            TileObjects objects,
            Direction from,
            Direction to,
            TileObject tileObject)
        {
            if (objects == null ||
                tileObject == null ||
                from == Direction.None ||
                to == Direction.None ||
                GetEdgeObject(
                    objects,
                    from) !=
                    tileObject ||
                GetEdgeObject(
                    objects,
                    to) != null)
            {
                return false;
            }

            SetEdgeObject(
                objects,
                from,
                null);
            SetEdgeObject(
                objects,
                to,
                tileObject);
            tileObject.SetDirty(true);
            objects.SetDirty(true);
            return true;
        }

        private static void RollBack(
            ParkingMove move)
        {
            if (move == null ||
                move.Objects == null ||
                move.TileObject == null ||
                GetEdgeObject(
                    move.Objects,
                    move.To) !=
                    move.TileObject ||
                GetEdgeObject(
                    move.Objects,
                    move.From) != null)
            {
                return;
            }

            SetEdgeObject(
                move.Objects,
                move.To,
                null);
            SetEdgeObject(
                move.Objects,
                move.From,
                move.TileObject);
            move.TileObject.SetDirty(true);
            move.Objects.SetDirty(true);
        }

        private static float GetActualEdgeZOffset(
            TileObject tileObject)
        {
            GameDBObject type =
                tileObject.m_state
                    .m_gameDBObject.Entry;
            if (type.AttachedToCeiling)
            {
                return -0.26f;
            }

            Direction rotation =
                Hospital.Instance
                    .m_state.m_rotation;
            bool front =
                type.AlwaysFront;

            switch (tileObject.Orientation)
            {
                case Direction.NW:
                    return
                        ((rotation != Direction.NW &&
                          rotation != Direction.NE) ||
                         front)
                            ? -0.49f
                            : -0.015f;
                case Direction.NE:
                    return
                        ((rotation != Direction.NW &&
                          rotation != Direction.SW) ||
                         front)
                            ? -0.49f
                            : -0.015f;
                case Direction.SE:
                    return
                        ((rotation != Direction.SW &&
                          rotation != Direction.SE) ||
                         front)
                            ? -0.49f
                            : -0.015f;
                case Direction.SW:
                    return
                        ((rotation != Direction.NE &&
                          rotation != Direction.SE) ||
                         front)
                            ? -0.49f
                            : -0.015f;
                default:
                    return -0.25f;
            }
        }

        private static void MarkRenderCleanup(
            TileObject tileObject)
        {
            if (tileObject == null ||
                tileObject.m_state == null)
            {
                return;
            }

            int floorIndex =
                tileObject.GetFloorIndex();
            if (floorIndex < 0 ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                floorIndex >=
                    Hospital.Instance.m_floors.Count)
            {
                return;
            }

            MarkRenderCleanup(
                Hospital.Instance
                    .m_floors[floorIndex],
                tileObject.m_state
                    .m_position);
        }

        private static void MarkRenderCleanup(
            Floor floor,
            Vector2i position)
        {
            if (floor == null)
            {
                return;
            }

            int floorIndex =
                floor.m_floorIndex;

            for (int i = 0;
                 i < PendingRenderCleanup.Count;
                 i++)
            {
                RenderCleanup existing =
                    PendingRenderCleanup[i];
                if (existing.FloorIndex ==
                        floorIndex &&
                    existing.Position.m_x ==
                        position.m_x &&
                    existing.Position.m_y ==
                        position.m_y)
                {
                    return;
                }
            }

            PendingRenderCleanup.Add(
                new RenderCleanup
                {
                    FloorIndex =
                        floorIndex,
                    Position =
                        position
                });
        }

        private static void ClearRenderSlot(
            ObjectRenderer renderer,
            Vector2i position)
        {
            if (renderer == null ||
                ObjectObjectsField == null)
            {
                return;
            }

            GameObject[,,] objects =
                ObjectObjectsField.GetValue(
                    renderer)
                    as GameObject[,,];
            if (objects == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >=
                    objects.GetLength(0) ||
                position.m_y >=
                    objects.GetLength(1))
            {
                return;
            }

            GameObject existing =
                objects[
                    position.m_x,
                    position.m_y,
                    ParkedRenderSlot];
            if (existing != null)
            {
                GameObjectManager
                    .DestroyGameObject(
                        existing);
                objects[
                    position.m_x,
                    position.m_y,
                    ParkedRenderSlot] =
                        null;
            }
        }

        private static bool Inside(
            Floor floor,
            Vector2i tile)
        {
            return floor != null &&
                tile.m_x >= 0 &&
                tile.m_y >= 0 &&
                tile.m_x <
                    floor.m_size.m_x &&
                tile.m_y <
                    floor.m_size.m_y;
        }

        private static void LogParkingError(
            string outcome,
            TileObject tileObject,
            Direction direction)
        {
            if (Plugin.Log == null)
            {
                return;
            }

            string id =
                tileObject == null ||
                tileObject.m_state == null ||
                tileObject.m_state
                    .m_gameDBObject.Entry == null ||
                tileObject.m_state
                    .m_gameDBObject.Entry
                    .DatabaseID == null
                    ? "unknown"
                    : tileObject.m_state
                        .m_gameDBObject.Entry
                        .DatabaseID.ToString();

            Plugin.Log.LogError(
                "Lab wall overlap " +
                outcome +
                ": object=" +
                id +
                ", direction=" +
                direction);
        }

        [HarmonyPatch(
            typeof(Floor),
            "AddObject",
            new Type[]
            {
                typeof(GameDBObject),
                typeof(Vector2i),
                typeof(Direction),
                typeof(Color),
                typeof(Department),
                typeof(bool)
            })]
        private static class AddObjectPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                Floor __instance,
                GameDBObject objectType,
                Vector2i position,
                Direction orientation,
                Color materialColor,
                Department department,
                bool noRotation,
                ref TileObject __result,
                ref ParkingMove __state)
            {
                __state = null;

                if (__instance == null ||
                    objectType == null ||
                    !objectType.PlacedToEdge)
                {
                    return true;
                }

                // New player/gameplay placement follows the HFX toggle.
                // Vanilla clone/Undo and move restoration use noRotation and
                // must remain restorable even if the option is later disabled.
                if (!noRotation &&
                    !FixConfig
                        .AllowWallAndCeilingObjectsAboveGroundObjects)
                {
                    return true;
                }

                Vector2i tile;
                Direction direction;
                if (!ResolvePlacement(
                        __instance,
                        objectType,
                        position,
                        orientation,
                        noRotation,
                        out tile,
                        out direction))
                {
                    return true;
                }

                TileObjects objects =
                    __instance.m_tileObjects[
                        tile.m_x,
                        tile.m_y];
                TileObject target =
                    GetEdgeObject(
                        objects,
                        direction);
                if (target == null)
                {
                    return true;
                }

                // Composite B is being inserted under an already-existing
                // compatible wall/ceiling object. Move only that upper object
                // to parking, then let Vanilla create B in its real slot.
                if (WallObjectPlacementConflictsPatch
                    .IsReviewedLabEdgePart(
                        objectType))
                {
                    float requiredHeight;
                    Direction parking;
                    if (!WallObjectPlacementConflictsPatch
                            .TryGetRequiredWallObjectHeight(
                                objectType,
                                out requiredHeight) ||
                        !IsCompatibleUpper(
                            target,
                            requiredHeight) ||
                        !TryFindFreeParkingDirection(
                            objects,
                            direction,
                            Direction.None,
                            out parking))
                    {
                        __result = null;
                        LogParkingError(
                            "add-lab-part-no-free-slot",
                            target,
                            direction);
                        return false;
                    }

                    if (!TryMoveEdgeObject(
                            objects,
                            direction,
                            parking,
                            target))
                    {
                        __result = null;
                        return false;
                    }

                    __state =
                        new ParkingMove
                        {
                            Objects = objects,
                            From = direction,
                            To = parking,
                            TileObject = target
                        };
                    return true;
                }

                if (!WallObjectPlacementConflictsPatch
                    .IsSupportedUpperObject(
                        objectType))
                {
                    return true;
                }

                // Normal reviewed case: the lab B part owns the true target
                // edge. Store the new upper object in another free edge field
                // but keep its Orientation unchanged.
                if (target.m_state != null &&
                    WallObjectPlacementConflictsPatch
                        .IsReviewedLabEdgePart(
                            target.m_state
                                .m_gameDBObject.Entry))
                {
                    float requiredHeight;
                    Direction parking;
                    if (!WallObjectPlacementConflictsPatch
                            .TryGetRequiredWallObjectHeight(
                                target.m_state
                                    .m_gameDBObject.Entry,
                                out requiredHeight) ||
                        !IsCompatibleUpper(
                            objectType,
                            requiredHeight) ||
                        FindParkedUpperForOrientation(
                            objects,
                            direction) != null ||
                        !TryFindFreeParkingDirection(
                            objects,
                            direction,
                            Direction.None,
                            out parking))
                    {
                        __result = null;
                        LogParkingError(
                            "add-upper-over-lab-no-free-slot",
                            target,
                            direction);
                        return false;
                    }

                    Department resolved =
                        ResolveDepartment(
                            __instance,
                            tile,
                            department);
                    if (resolved == null)
                    {
                        __result = null;
                        return false;
                    }

                    objects.SetDirty(true);
                    TileObject upper =
                        new TileObject(
                            tile,
                            resolved,
                            objectType,
                            direction,
                            materialColor,
                            __instance);
                    SetEdgeObject(
                        objects,
                        parking,
                        upper);
                    resolved.AddObject(
                        upper);
                    __instance
                        .ValidateObjectsInARoom(
                            tile);

                    if (objectType.LightAutomatic)
                    {
                        objects.UpdateAllObjects(
                            0,
                            0f);
                    }

                    __result = upper;

                    if (HfxDiagnostics.Enabled)
                    {
                        HfxDiagnostics.Result(
                            "AllowWallAndCeilingObjectsAboveGroundObjects",
                            "Floor.AddObject",
                            "lab-upper-parked",
                            "object=" +
                            (objectType.DatabaseID == null
                                ? "unknown"
                                : objectType.DatabaseID.ToString()) +
                            " tile=" +
                            tile.m_x + "," +
                            tile.m_y +
                            " target=" +
                            direction +
                            " storage=" +
                            parking);
                    }

                    return false;
                }

                // A parked lab-overlap upper object may itself be using the
                // storage field needed by a new wall object on another face.
                Direction storedDirection;
                if (TryGetStoredDirection(
                        objects,
                        target,
                        out storedDirection) &&
                    storedDirection ==
                        direction &&
                    target.Orientation !=
                        direction &&
                    IsUpperStoredAwayFromOrientation(
                        objects,
                        target,
                        storedDirection))
                {
                    Direction replacement;
                    if (!TryFindFreeParkingDirection(
                            objects,
                            target.Orientation,
                            direction,
                            out replacement) ||
                        !TryMoveEdgeObject(
                            objects,
                            direction,
                            replacement,
                            target))
                    {
                        __result = null;
                        LogParkingError(
                            "repark-upper-no-free-slot",
                            target,
                            direction);
                        return false;
                    }

                    __state =
                        new ParkingMove
                        {
                            Objects = objects,
                            From = direction,
                            To = replacement,
                            TileObject = target
                        };
                }

                return true;
            }

            [HarmonyPostfix]
            private static void Postfix(
                TileObject __result,
                ParkingMove __state)
            {
                if (__state != null &&
                    __result == null)
                {
                    RollBack(
                        __state);
                }
            }
        }

        [HarmonyPatch(
            typeof(TileObjects),
            "RemoveObject",
            new Type[]
            {
                typeof(TileObject)
            })]
        private static class RemoveObjectPatch
        {
            private sealed class RemoveState
            {
                internal bool WasParkedUpper;
                internal bool WasLabPart;
                internal Direction LabDirection;
                internal TileObject Removed;
            }

            [HarmonyPrefix]
            private static void Prefix(
                TileObjects __instance,
                TileObject tileObject,
                ref RemoveState __state)
            {
                __state =
                    new RemoveState
                    {
                        Removed = tileObject
                    };

                if (__instance == null ||
                    tileObject == null ||
                    tileObject.m_state == null)
                {
                    return;
                }

                Direction stored;
                __state.WasParkedUpper =
                    TryGetStoredDirection(
                        __instance,
                        tileObject,
                        out stored) &&
                    IsUpperStoredAwayFromOrientation(
                        __instance,
                        tileObject,
                        stored);

                __state.WasLabPart =
                    WallObjectPlacementConflictsPatch
                        .IsReviewedLabEdgePart(
                            tileObject.m_state
                                .m_gameDBObject.Entry);
                if (__state.WasLabPart)
                {
                    __state.LabDirection =
                        tileObject.Orientation;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(
                TileObjects __instance,
                RemoveState __state)
            {
                if (__state == null ||
                    __instance == null)
                {
                    return;
                }

                if (__state.WasParkedUpper)
                {
                    MarkRenderCleanup(
                        __state.Removed);
                }

                if (!__state.WasLabPart ||
                    GetEdgeObject(
                        __instance,
                        __state.LabDirection) !=
                        null)
                {
                    return;
                }

                TileObject parked =
                    FindParkedUpperForOrientation(
                        __instance,
                        __state.LabDirection);
                if (parked == null)
                {
                    return;
                }

                Direction stored;
                if (!TryGetStoredDirection(
                        __instance,
                        parked,
                        out stored) ||
                    !TryMoveEdgeObject(
                        __instance,
                        stored,
                        __state.LabDirection,
                        parked))
                {
                    return;
                }

                MarkRenderCleanup(
                    parked);
            }
        }

        [HarmonyPatch(
            typeof(ObjectRenderer),
            "RenderSingleObject",
            new Type[]
            {
                typeof(TileObject),
                typeof(float),
                typeof(float),
                typeof(Floor),
                typeof(LightMap2DUnity),
                typeof(bool),
                typeof(bool)
            })]
        private static class RenderSingleObjectPatch
        {
            [HarmonyPrefix]
            private static void Prefix(
                ObjectRenderer __instance,
                TileObject tileObject,
                ref float zOffset,
                Floor floor,
                bool ghost)
            {
                if (ghost ||
                    tileObject == null ||
                    tileObject.m_state == null ||
                    tileObject.m_state.m_moving ||
                    !IsParkedLabUpper(
                        floor,
                        tileObject))
                {
                    return;
                }

                ClearRenderSlot(
                    __instance,
                    tileObject.m_state
                        .m_position);
                zOffset =
                    GetActualEdgeZOffset(
                        tileObject);
            }
        }

        [HarmonyPatch(
            typeof(ObjectRenderer),
            "RenderObjects")]
        private static class RenderObjectsPatch
        {
            [HarmonyPrefix]
            private static void Prefix(
                ObjectRenderer __instance,
                Floor floor)
            {
                if (__instance == null ||
                    floor == null ||
                    PendingRenderCleanup.Count == 0)
                {
                    return;
                }

                for (int i =
                         PendingRenderCleanup.Count - 1;
                     i >= 0;
                     i--)
                {
                    RenderCleanup cleanup =
                        PendingRenderCleanup[i];
                    if (cleanup.FloorIndex !=
                        floor.m_floorIndex)
                    {
                        continue;
                    }

                    ClearRenderSlot(
                        __instance,
                        cleanup.Position);
                    PendingRenderCleanup
                        .RemoveAt(i);
                }
            }
        }

        // FloorClone/Undo stores each edge field separately. During redo,
        // pass the snapshot's exact edge field to Floor.AddObject so a parked
        // upper object returns to the same native storage slot whenever it is
        // still available.
        [HarmonyPatch]
        private static class UndoApplyPatch
        {
            private static MethodBase TargetMethod()
            {
                Type type =
                    AccessTools.TypeByName(
                        "Lopital.TileObjectsPersistentData");
                return type == null
                    ? null
                    : AccessTools.Method(
                        type,
                        "ApplySingleTileObject");
            }

            [HarmonyPrefix]
            private static void Prefix(
                object[] __args)
            {
                s_preferredParkingDirection =
                    Direction.None;
                if (__args == null ||
                    __args.Length < 3 ||
                    __args[2] == null)
                {
                    return;
                }

                string value =
                    __args[2].ToString();
                switch (value)
                {
                    case "NW":
                        s_preferredParkingDirection =
                            Direction.NW;
                        break;
                    case "NE":
                        s_preferredParkingDirection =
                            Direction.NE;
                        break;
                    case "SE":
                        s_preferredParkingDirection =
                            Direction.SE;
                        break;
                    case "SW":
                        s_preferredParkingDirection =
                            Direction.SW;
                        break;
                }
            }

            [HarmonyPostfix]
            private static void Postfix(
                object[] __args)
            {
                if (__args != null &&
                    __args.Length >= 5)
                {
                    Floor floor =
                        __args[0] as Floor;
                    TileObjects objects =
                        __args[1] as TileObjects;

                    if (floor != null &&
                        objects != null &&
                        __args[3] is int &&
                        __args[4] is int)
                    {
                        int x =
                            (int)__args[3];
                        int y =
                            (int)__args[4];
                        objects.SetDirty(true);
                        MarkRenderCleanup(
                            floor,
                            new Vector2i(
                                x,
                                y));
                    }
                }

                s_preferredParkingDirection =
                    Direction.None;
            }
        }
    }
}
