using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    // A selective, non-mutating replacement for Vanilla's property-read rule.
    // Internal reviewed ground IDs grant eligibility, not exemption from safety checks.
    [HarmonyPatch(typeof(Floor), "CanPlaceObject")]
    internal static class WallObjectPlacementConflictsPatch
    {
        // The lowest reviewed upper-object threshold is 0.5. Per-ground
        // clearances are HFX-owned GameDBTweakableFloat entries loaded from
        // Database/Loupito75.HospitalFixes.xml.
        private const float MinimumSupportedUpperHeight = 0.5f;

        private static readonly MethodInfo NativeWallAboveRule =
            AccessTools.PropertyGetter(typeof(GameDBObject), "AllowsObjectsOnWallsAbove");

        private static readonly MethodInfo SafeWallAboveRule =
            AccessTools.Method(typeof(WallObjectPlacementConflictsPatch),
                "AllowsWallObjectAboveCenter");

        private static readonly MethodInfo NativeAnyObjectRule =
            AccessTools.Method(
                typeof(Floor),
                "IsAnyObjectAt",
                new System.Type[] { typeof(Vector2i) });

        private static readonly MethodInfo SafeAnyObjectRule =
            AccessTools.Method(
                typeof(WallObjectPlacementConflictsPatch),
                "IsAnyObjectBlockingPlacement");

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(NativeWallAboveRule))
                {
                    // Original receiver remains on stack. Add the Floor, object
                    // being placed, tile, orientation, and the ignoreWalls flag.
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_S, (byte)4);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = SafeWallAboveRule;
                }

                yield return instruction;

                if (instruction.Calls(NativeAnyObjectRule))
                {
                    // Preserve Vanilla's result except for the narrow elevator
                    // exterior-wall case and the two reviewed laboratory
                    // composites. The latter bypasses only the generic
                    // "some object occupies this tile" branch; target edge,
                    // wall, door, access and room checks remain active.
                    yield return new CodeInstruction(OpCodes.Ldarg_0);
                    yield return new CodeInstruction(OpCodes.Ldarg_1);
                    yield return new CodeInstruction(OpCodes.Ldarg_2);
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Ldarg_S, (byte)4);
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        SafeAnyObjectRule);
                }
            }
        }

        private static bool IsAnyObjectBlockingPlacement(
            bool nativeResult,
            Floor floor,
            GameDBObject placing,
            Vector2i position,
            Direction orientation,
            bool ignoreWalls)
        {
            if (!nativeResult)
            {
                return false;
            }

            if (ElevatorExteriorDecorationsPatch
                .CanBypassOccupiedPrefabTile(
                    placing,
                    floor,
                    position,
                    orientation,
                    ignoreWalls))
            {
                return false;
            }

            if (CanBypassReviewedLabComposite(
                floor,
                placing,
                position,
                orientation,
                ignoreWalls))
            {
                return false;
            }

            if (CanBypassCompatibleUpperObjectsForGroundPlacement(
                floor,
                placing,
                position,
                orientation,
                ignoreWalls))
            {
                return false;
            }

            return true;
        }

        // Symmetric path for build order: when the reviewed ground object is
        // placed after compatible wall/ceiling objects, Vanilla sees the edge
        // object through IsAnyObjectAt() and rejects the ground object before
        // reaching any of the normal access/room checks. Ignore that occupancy
        // only when every existing edge object is one HFX would have allowed
        // above this same ground object.
        private static bool CanBypassCompatibleUpperObjectsForGroundPlacement(
            Floor floor,
            GameDBObject placing,
            Vector2i position,
            Direction orientation,
            bool ignoreWalls)
        {
            if (!FixConfig.AllowWallAndCeilingObjectsAboveGroundObjects ||
                floor == null || floor.m_tileObjects == null ||
                placing == null || ignoreWalls)
            {
                return false;
            }

            bool reviewedGround =
                IsSupportedGroundObject(placing);
            bool reviewedLabEdgePart =
                IsReviewedLabEdgePart(placing);
            if (!reviewedGround &&
                !reviewedLabEdgePart)
            {
                return false;
            }

            Vector2i tile = IsometricCameraUtils.GetRotatedPosition(
                position.m_x,
                position.m_y);
            if (!IsTileInPlacementBounds(floor, tile) ||
                floor.IsAnyPrefabObjectAt(tile))
            {
                return false;
            }

            TileObjects occupants =
                floor.m_tileObjects[tile.m_x, tile.m_y];

            // Never hide a real center object or an attachment. This bypass is
            // only for the inverse order of the already-supported wall/ground
            // pairing.
            if (occupants.m_centerObject != null ||
                occupants.m_attachmentObject != null)
            {
                return false;
            }

            float requiredHeight;
            if (!TryGetRequiredWallObjectHeight(
                    placing,
                    out requiredHeight))
            {
                return false;
            }

            if (reviewedLabEdgePart)
            {
                // The B part keeps its real Vanilla edge slot. If a compatible
                // wall/ceiling object already occupies that face, HFX can move
                // only that upper object to a secondary native edge-storage
                // slot while preserving its real Orientation.
                Direction direction =
                    IsometricCameraUtils.GetRotatedDirectionInverse(
                        orientation);
                TileObject targetEdge =
                    GetEdgeObject(
                        floor,
                        tile,
                        direction);
                if (targetEdge != null &&
                    !LabCompositeWallOverlapPatch
                        .CanMoveExistingUpperForIncomingLabPart(
                            floor,
                            tile,
                            direction,
                            placing,
                            targetEdge,
                            requiredHeight))
                {
                    return false;
                }
            }

            bool foundCompatibleUpper = false;
            if (!IsCompatibleExistingUpper(
                    occupants.m_ObjectNW,
                    requiredHeight,
                    ref foundCompatibleUpper) ||
                !IsCompatibleExistingUpper(
                    occupants.m_ObjectNE,
                    requiredHeight,
                    ref foundCompatibleUpper) ||
                !IsCompatibleExistingUpper(
                    occupants.m_ObjectSE,
                    requiredHeight,
                    ref foundCompatibleUpper) ||
                !IsCompatibleExistingUpper(
                    occupants.m_ObjectSW,
                    requiredHeight,
                    ref foundCompatibleUpper) ||
                !foundCompatibleUpper)
            {
                return false;
            }

            LogGroundUnderExistingUpperAllowance(
                placing,
                requiredHeight,
                reviewedLabEdgePart);
            return true;
        }

        private static bool IsCompatibleExistingUpper(
            TileObject existing,
            float requiredHeight,
            ref bool foundCompatibleUpper)
        {
            if (existing == null)
            {
                return true;
            }

            if (existing.m_state == null ||
                existing.m_state.m_gameDBObject.Entry == null ||
                existing.m_state.m_compositeParent != null)
            {
                return false;
            }

            GameDBObject upper =
                existing.m_state.m_gameDBObject.Entry;
            if (!IsSupportedUpperObject(upper) ||
                GetEffectiveUpperHeight(upper) < requiredHeight)
            {
                return false;
            }

            foundCompatibleUpper = true;
            return true;
        }

        private static bool AllowsWallObjectAboveCenter(
            GameDBObject existing,
            Floor floor,
            GameDBObject placing,
            Vector2i position,
            Direction orientation,
            bool ignoreWalls)
        {
            if (existing == null)
            {
                return false;
            }

            if (existing.AllowsObjectsOnWallsAbove || placing == null)
            {
                return existing.AllowsObjectsOnWallsAbove;
            }

            if (ElevatorExteriorDecorationsPatch.AllowsOnExteriorWall(
                existing, placing, floor, position, orientation, ignoreWalls))
            {
                return true;
            }

            if (!FixConfig.AllowWallAndCeilingObjectsAboveGroundObjects ||
                floor == null || floor.m_tileObjects == null || ignoreWalls ||
                !IsSupportedUpperObject(placing) ||
                !IsSupportedGroundObject(existing))
            {
                return false;
            }

            float requiredHeight;
            if (!TryGetRequiredWallObjectHeight(
                    existing,
                    out requiredHeight) ||
                GetEffectiveUpperHeight(placing) < requiredHeight)
            {
                return false;
            }

            Vector2i tile = IsometricCameraUtils.GetRotatedPosition(
                position.m_x, position.m_y);
            if (!IsTileInPlacementBounds(floor, tile) ||
                !floor.IsOnlyCenterObjectAt(tile) ||
                floor.IsAnyPrefabObjectAt(tile))
            {
                return false;
            }

            TileObject center =
                floor.m_tileObjects[tile.m_x, tile.m_y].m_centerObject;
            if (center == null || center.m_state == null ||
                center.m_state.m_gameDBObject.Entry != existing)
            {
                return false;
            }

            LogGroundAllowance(existing, placing, "placement-allowed");
            return true;
            // The game's remaining wall/door, orientation, access, room and
            // neighbor checks execute normally after this narrow rule.
        }

        internal static bool IsSupportedUpperObject(GameDBObject placing)
        {
            return placing != null &&
                placing.PlacedToEdge &&
                !placing.AttachedToObject &&
                (placing.AttachedToWall || placing.AttachedToCeiling) &&
                !placing.HasTag("ui_hidden") &&
                GetEffectiveUpperHeight(placing) >=
                    MinimumSupportedUpperHeight;
        }

        private static bool IsSupportedGroundObject(GameDBObject existing)
        {
            if (existing == null ||
                existing.AttachedToObject ||
                existing.AttachedToWall ||
                existing.AttachedToCeiling ||
                existing.PlacedToEdge ||
                existing.HasTag("ui_hidden"))
            {
                return false;
            }

            float requiredHeight;
            return TryGetRequiredWallObjectHeight(
                existing,
                out requiredHeight);
        }

        private static bool CanBypassReviewedLabComposite(
            Floor floor,
            GameDBObject placing,
            Vector2i position,
            Direction orientation,
            bool ignoreWalls)
        {
            if (!FixConfig.AllowWallAndCeilingObjectsAboveGroundObjects ||
                floor == null || floor.m_tileObjects == null || ignoreWalls ||
                !IsSupportedUpperObject(placing))
            {
                return false;
            }

            Vector2i tile = IsometricCameraUtils.GetRotatedPosition(
                position.m_x, position.m_y);
            if (!IsTileInPlacementBounds(floor, tile) ||
                floor.IsAnyPrefabObjectAt(tile))
            {
                return false;
            }

            TileObject center =
                floor.m_tileObjects[tile.m_x, tile.m_y].m_centerObject;
            if (center == null || center.m_state == null ||
                center.m_state.m_gameDBObject.Entry == null ||
                !IsReviewedLabGround(
                    center.m_state.m_gameDBObject.Entry) ||
                !IsSupportedGroundObject(
                    center.m_state.m_gameDBObject.Entry) ||
                center.m_state.m_compositeParent == null ||
                floor.m_tileObjects[tile.m_x, tile.m_y].m_attachmentObject != null)
            {
                return false;
            }

            float requiredHeight;
            if (!TryGetRequiredWallObjectHeight(
                    center.m_state.m_gameDBObject.Entry,
                    out requiredHeight) ||
                GetEffectiveUpperHeight(placing) < requiredHeight)
            {
                return false;
            }

            CompositeObject parent =
                center.m_state.m_compositeParent.GetEntity();
            if (parent == null)
            {
                return false;
            }

            Direction direction =
                IsometricCameraUtils.GetRotatedDirectionInverse(orientation);
            TileObject targetEdge =
                GetEdgeObject(floor, tile, direction);
            if (targetEdge != null &&
                !LabCompositeWallOverlapPatch
                    .CanPrepareUpperPlacement(
                        floor,
                        tile,
                        direction,
                        targetEdge,
                        parent,
                        placing,
                        requiredHeight))
            {
                return false;
            }

            if (!IsCompatibleLabTileOccupant(
                    floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectNW,
                    parent,
                    requiredHeight) ||
                !IsCompatibleLabTileOccupant(
                    floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectNE,
                    parent,
                    requiredHeight) ||
                !IsCompatibleLabTileOccupant(
                    floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectSE,
                    parent,
                    requiredHeight) ||
                !IsCompatibleLabTileOccupant(
                    floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectSW,
                    parent,
                    requiredHeight))
            {
                return false;
            }

            LogGroundAllowance(
                center.m_state.m_gameDBObject.Entry,
                placing,
                "placement-allowed-lab-composite");
            return true;
        }

        private static bool IsApprovedAboveGroundPlacement(
            Floor floor,
            GameDBObject placing,
            Vector2i position,
            Direction orientation,
            bool ignoreWalls)
        {
            if (!FixConfig.AllowWallAndCeilingObjectsAboveGroundObjects ||
                floor == null || floor.m_tileObjects == null || ignoreWalls ||
                !IsSupportedUpperObject(placing))
            {
                return false;
            }

            Vector2i tile = IsometricCameraUtils.GetRotatedPosition(
                position.m_x, position.m_y);
            if (!IsTileInPlacementBounds(floor, tile))
            {
                return false;
            }

            TileObject center =
                floor.m_tileObjects[tile.m_x, tile.m_y].m_centerObject;
            if (center == null || center.m_state == null ||
                center.m_state.m_gameDBObject.Entry == null ||
                !IsSupportedGroundObject(
                    center.m_state.m_gameDBObject.Entry))
            {
                return false;
            }

            float requiredHeight;
            return TryGetRequiredWallObjectHeight(
                    center.m_state.m_gameDBObject.Entry,
                    out requiredHeight) &&
                GetEffectiveUpperHeight(placing) >= requiredHeight;
        }

        internal static bool ShouldWarnWhenOverGround(
            GameDBObject placing)
        {
            return placing != null &&
                (placing.BlockedAboveOtherObjects ||
                 HasNativeUseInteraction(placing));
        }

        internal static bool HasNativeUseInteraction(GameDBObject placing)
        {
            if (placing == null)
            {
                return false;
            }

            if (!string.IsNullOrEmpty(placing.CallbackMessageName) ||
                placing.AccessPositions != null &&
                placing.AccessPositions.Length > 0)
            {
                return true;
            }

            return HasEntries(placing.DefaultUseStartAnimations) ||
                HasEntries(placing.DefaultUseAnimations) ||
                HasEntries(placing.DefaultUseEndAnimations);
        }

        private static bool HasEntries(string[] values)
        {
            return values != null && values.Length > 0;
        }

        internal static bool IsApprovedGroundBelow(
            Floor floor,
            TileObject wallObject)
        {
            if (floor == null || floor.m_tileObjects == null ||
                wallObject == null || wallObject.m_state == null ||
                wallObject.m_state.m_gameDBObject.Entry == null ||
                !IsSupportedUpperObject(
                    wallObject.m_state.m_gameDBObject.Entry))
            {
                return false;
            }

            Vector2i tile = wallObject.m_state.m_position;
            if (!IsTileInPlacementBounds(floor, tile))
            {
                return false;
            }

            TileObject center =
                floor.m_tileObjects[tile.m_x, tile.m_y].m_centerObject;
            if (center == null || center.m_state == null ||
                center.m_state.m_gameDBObject.Entry == null ||
                !IsSupportedGroundObject(
                    center.m_state.m_gameDBObject.Entry))
            {
                return false;
            }

            float requiredHeight;
            return TryGetRequiredWallObjectHeight(
                    center.m_state.m_gameDBObject.Entry,
                    out requiredHeight) &&
                GetEffectiveUpperHeight(
                    wallObject.m_state.m_gameDBObject.Entry) >=
                    requiredHeight;
        }

        internal static bool TryGetRequiredWallObjectHeight(
            GameDBObject ground,
            out float requiredHeight)
        {
            requiredHeight = 0f;

            if (ground == null ||
                ground.DatabaseID == null)
            {
                return false;
            }

            float clearance;
            if (!HfxDatabase.TryGetGroundClearance(
                    ground.DatabaseID,
                    out clearance) ||
                clearance < MinimumSupportedUpperHeight)
            {
                return false;
            }

            requiredHeight = clearance;
            return true;
        }

        internal static float GetEffectiveUpperHeight(
            GameDBObject upper)
        {
            if (upper == null)
            {
                return 0f;
            }

            if (upper.HeightOffset > 0f)
            {
                return upper.HeightOffset;
            }

            if (upper.DatabaseID == null)
            {
                return 0f;
            }

            float overrideHeight;
            return HfxDatabase.TryGetUpperHeight(
                    upper.DatabaseID,
                    out overrideHeight)
                ? overrideHeight
                : 0f;
        }

        internal static bool IsReviewedLabGround(GameDBObject ground)
        {
            string id = GetDatabaseId(ground);
            return id == "OBJECT_LAB_HALF_DESK_A" ||
                id == "OBJECT_LAB_CABINET_A";
        }

        internal static bool IsReviewedLabEdgePart(GameDBObject part)
        {
            string id = GetDatabaseId(part);
            return id == "OBJECT_LAB_HALF_DESK_B" ||
                id == "OBJECT_LAB_CABINET_B";
        }

        private static bool IsTileInPlacementBounds(
            Floor floor,
            Vector2i tile)
        {
            return tile.m_x >= 1 && tile.m_y >= 1 &&
                tile.m_x < floor.m_size.m_x - 1 &&
                tile.m_y < floor.m_size.m_y - 1;
        }

        private static TileObject GetEdgeObject(
            Floor floor,
            Vector2i tile,
            Direction direction)
        {
            switch (direction)
            {
                case Direction.NW:
                    return floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectNW;
                case Direction.NE:
                    return floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectNE;
                case Direction.SE:
                    return floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectSE;
                case Direction.SW:
                    return floor.m_tileObjects[tile.m_x, tile.m_y].m_ObjectSW;
                default:
                    return null;
            }
        }

        private static bool IsCompatibleLabTileOccupant(
            TileObject tileObject,
            CompositeObject parent,
            float requiredHeight)
        {
            if (tileObject == null)
            {
                return true;
            }

            if (tileObject.m_state == null ||
                tileObject.m_state.m_gameDBObject.Entry == null)
            {
                return false;
            }

            if (tileObject.m_state.m_compositeParent != null)
            {
                return tileObject.m_state.m_compositeParent
                    .GetEntity() == parent;
            }

            GameDBObject upper =
                tileObject.m_state.m_gameDBObject.Entry;
            return IsSupportedUpperObject(upper) &&
                GetEffectiveUpperHeight(upper) >=
                    requiredHeight;
        }

        private static string GetDatabaseId(GameDBObject entry)
        {
            return entry != null && entry.DatabaseID != null
                ? entry.DatabaseID.ToString()
                : null;
        }

        private static void LogGroundUnderExistingUpperAllowance(
            GameDBObject placing,
            float requiredHeight,
            bool labEdgePart)
        {
            if (!HfxDiagnostics.Enabled)
            {
                return;
            }

            string placingId =
                GetDatabaseId(placing) ?? "unknown";
            HfxDiagnostics.InterventionRateLimited(
                "AllowWallAndCeilingObjectsAboveGroundObjects",
                "Floor.CanPlaceObject",
                "ground-placement-under-existing-wall-allowed",
                "placing=" + placingId +
                " requiredUpperHeight=" + requiredHeight +
                " labEdgePart=" + labEdgePart,
                "AllowWallAndCeilingObjectsAboveGroundObjects|ground-under-wall|" +
                placingId,
                3000);
        }

        private static void LogGroundAllowance(
            GameDBObject existing,
            GameDBObject placing,
            string outcome)
        {
            if (!HfxDiagnostics.Enabled)
            {
                return;
            }

            string existingId = GetDatabaseId(existing) ?? "unknown";
            string placingId = GetDatabaseId(placing) ?? "unknown";
            HfxDiagnostics.InterventionRateLimited(
                "AllowWallAndCeilingObjectsAboveGroundObjects",
                "Floor.CanPlaceObject",
                outcome,
                "existing=" + existingId +
                " placing=" + placingId,
                "AllowWallAndCeilingObjectsAboveGroundObjects|" +
                outcome + "|" + existingId + "|" + placingId,
                3000);
        }
    }

    [HarmonyPatch(
        typeof(Floor),
        "ValidateObjectsInARoom",
        new System.Type[] { typeof(Room), typeof(List<TileObject>) })]
    internal static class WallObjectAccessibilityValidationPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Floor __instance,
            List<TileObject> allObjects)
        {
            if (!FixConfig.AllowWallAndCeilingObjectsAboveGroundObjects ||
                __instance == null || allObjects == null)
            {
                return;
            }

            foreach (TileObject tileObject in allObjects)
            {
                if (tileObject == null || tileObject.m_state == null ||
                    tileObject.m_state.m_gameDBObject.Entry == null ||
                    tileObject.m_state.m_placementValidity !=
                        PlacementValidationResult.ALLOWED ||
                    !WallObjectPlacementConflictsPatch
                        .ShouldWarnWhenOverGround(
                            tileObject.m_state.m_gameDBObject.Entry) ||
                    !WallObjectPlacementConflictsPatch
                        .IsApprovedGroundBelow(__instance, tileObject))
                {
                    continue;
                }

                tileObject.m_state.m_placementValidity =
                    PlacementValidationResult.ALLOWED_NOT_ACCESSIBLE;
                tileObject.SetDirty(dirty: true);
            }
        }
    }
}
