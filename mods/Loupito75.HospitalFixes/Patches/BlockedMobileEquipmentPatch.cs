using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    // Replace only the calls made by the native return/disembark state handlers.
    // Do not intercept global destruction APIs or alter pathfinding.
    internal static class BlockedMobileEquipmentPatch
    {
        private const int SearchRadius = 12;

        internal static IEnumerable<CodeInstruction> NurseTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo removeObject = AccessTools.Method(typeof(MapScriptInterface), "RemoveMovingObject");
            MethodInfo removeComposite = AccessTools.Method(typeof(MapScriptInterface), "RemoveMovingCompositeObject");
            MethodInfo rescueObject = AccessTools.Method(typeof(BlockedMobileEquipmentPatch), "RescueWheelchairOrRemove");
            MethodInfo rescueComposite = AccessTools.Method(typeof(BlockedMobileEquipmentPatch), "RescueStretcherOrRemove");

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(removeObject))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = rescueObject;
                }
                else if (instruction.Calls(removeComposite))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = rescueComposite;
                }
                yield return instruction;
            }
        }

        internal static IEnumerable<CodeInstruction> JanitorTranspiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo original = AccessTools.Method(typeof(MapScriptInterface), "MoveObject",
                new Type[] { typeof(TileObject), typeof(Vector2i) });
            MethodInfo replacement = AccessTools.Method(typeof(BlockedMobileEquipmentPatch), "TryReturnCart");

            foreach (CodeInstruction instruction in instructions)
            {
                if (instruction.Calls(original))
                {
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = replacement;
                }
                yield return instruction;
            }
        }

        private static bool TryReturnCart(MapScriptInterface map, TileObject cart, Vector2i position)
        {
            if (map == null || cart == null || !FixConfig.PreserveBlockedMobileEquipment)
            {
                return map != null && map.MoveObject(cart, position);
            }

            // Always keep the exact Vanilla success path first. Recovery only on failure.
            if (map.MoveObject(cart, position))
            {
                return true;
            }

            Vector2i alternate;
            if (FindAlternate(map, new TileObject[] { cart }, new Vector2i[] { position }, out alternate) &&
                map.MoveObject(cart, alternate))
            {
                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Result(
                        "PreserveBlockedMobileEquipment",
                        "BehaviorJanitor return-cart MoveObject",
                        "alternate-placement-confirmed",
                        "type=janitor-cart originalTarget=" +
                        position.m_x + "," + position.m_y +
                        " alternateTarget=" +
                        alternate.m_x + "," + alternate.m_y);
                }
                return true;
            }
            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Fallback(
                    "PreserveBlockedMobileEquipment",
                    "BehaviorJanitor return-cart MoveObject",
                    "alternate-placement-failed",
                    "type=janitor-cart originalTarget=" +
                    position.m_x + "," + position.m_y +
                    " vanillaFailureBranch=true");
            }
            return false; // Let the native caller execute its original failure branch.
        }

        private static void RescueWheelchairOrRemove(
            MapScriptInterface map,
            TileObject chair)
        {
            if (map == null || chair == null)
            {
                return;
            }

            // PickUpObject() deliberately leaves m_position at the object's
            // original storage tile while m_movePosition follows the nurse.
            // Recovery must therefore search around the original tile, not
            // around the transient pushed position.
            Vector2i originalPosition =
                chair.m_state.m_position;
            Vector2i alternate;
            if (FixConfig.PreserveBlockedMobileEquipment &&
                IsDetachedAndUnowned(chair) &&
                FindAlternate(
                    map,
                    new TileObject[] { chair },
                    new Vector2i[] { originalPosition },
                    out alternate) &&
                map.MoveObject(chair, alternate))
            {
                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Result(
                        "PreserveBlockedMobileEquipment",
                        "BehaviorNurse.UpdateOverridenReturningStretcher",
                        "alternate-placement-confirmed",
                        "type=wheelchair originalTarget=" +
                        originalPosition.m_x + "," +
                        originalPosition.m_y +
                        " alternateTarget=" +
                        alternate.m_x + "," + alternate.m_y);
                }
                return;
            }

            // If the wheelchair still carries ownership/user state, do not
            // relocate that inconsistent transport state. Keep Vanilla's
            // cleanup behavior instead.
            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Fallback(
                    "PreserveBlockedMobileEquipment",
                    "BehaviorNurse.UpdateOverridenReturningStretcher",
                    "native-cleanup-used",
                    "type=wheelchair recoveryFailedOrUnsafe=true" +
                    " originalTarget=" +
                    originalPosition.m_x + "," +
                    originalPosition.m_y);
            }
            map.RemoveMovingObject(chair);
        }

        private static void RescueStretcherOrRemove(MapScriptInterface map, CompositeObject stretcher)
        {
            if (map == null || stretcher == null)
            {
                return;
            }

            List<TileObject> parts = new List<TileObject>();
            List<Vector2i> positions = new List<Vector2i>();
            foreach (EntityIDPointer<TileObject> pointer in stretcher.m_compositeObjectPersistentData.m_parts)
            {
                TileObject part = pointer.GetEntity();
                if (part == null)
                {
                    if (HfxDiagnostics.Enabled)
                    {
                        HfxDiagnostics.Fallback(
                            "PreserveBlockedMobileEquipment",
                            "BehaviorNurse.UpdateOverridenReturningStretcher",
                            "native-cleanup-used",
                            "type=stretcher reason=missing-composite-part");
                    }
                    map.RemoveMovingCompositeObject(stretcher);
                    return;
                }
                parts.Add(part);
                // As with wheelchairs, m_position remains the original
                // storage footprint while m_movePosition follows the nurse.
                positions.Add(part.m_state.m_position);
            }

            Vector2i alternate;
            if (FixConfig.PreserveBlockedMobileEquipment && parts.Count == 2 &&
                FindAlternate(map, parts.ToArray(), positions.ToArray(), out alternate))
            {
                Vector2i offset = new Vector2i(alternate.m_x - positions[0].m_x,
                    alternate.m_y - positions[0].m_y);
                Vector2i second = new Vector2i(positions[1].m_x + offset.m_x,
                    positions[1].m_y + offset.m_y);
                // Both tiles were checked before the first part was installed.
                if (map.MoveObject(parts[0], alternate))
                {
                    if (map.MoveObject(parts[1], second))
                    {
                        if (HfxDiagnostics.Enabled)
                        {
                            HfxDiagnostics.Result(
                                "PreserveBlockedMobileEquipment",
                                "BehaviorNurse.UpdateOverridenReturningStretcher",
                                "alternate-placement-confirmed",
                                "type=stretcher originalFirst=" +
                                positions[0].m_x + "," +
                                positions[0].m_y +
                                " originalSecond=" +
                                positions[1].m_x + "," +
                                positions[1].m_y +
                                " alternateFirst=" +
                                alternate.m_x + "," + alternate.m_y +
                                " alternateSecond=" +
                                second.m_x + "," + second.m_y);
                        }
                        return;
                    }
                    // No half-placed composite if the second native move unexpectedly fails.
                    map.PickUpObject(parts[0]);
                }
            }
            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Fallback(
                    "PreserveBlockedMobileEquipment",
                    "BehaviorNurse.UpdateOverridenReturningStretcher",
                    "native-cleanup-used",
                    "type=stretcher recoveryFailedOrUnsafe=true" +
                    " parts=" + parts.Count);
            }
            map.RemoveMovingCompositeObject(stretcher);
        }

        private static bool IsDetachedAndUnowned(
            TileObject tile)
        {
            return tile != null &&
                tile.m_state != null &&
                tile.m_state.m_moving &&
                !tile.m_state.m_attachedToCharacter &&
                tile.User == null &&
                tile.Owner == null;
        }

        private static bool FindAlternate(MapScriptInterface map, TileObject[] parts,
            Vector2i[] positions, out Vector2i result)
        {
            result = positions[0];
            if (parts.Length == 0 || parts.Length != positions.Length ||
                Hospital.Instance == null || map == null)
            {
                return false;
            }

            int floorIndex = parts[0].GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            Room[] originalRooms = new Room[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                if (parts[i].GetFloorIndex() != floorIndex ||
                    !Inside(floor, positions[i]))
                {
                    return false;
                }
                originalRooms[i] = map.GetRoomAt(positions[i], floorIndex);
            }

            // Search nearest first and keep every part in the same original room.
            for (int radius = 1; radius <= SearchRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int dyAbs = radius - Math.Abs(dx);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        if (dyAbs == 0 && sign == 1)
                        {
                            continue;
                        }
                        int dy = dyAbs * sign;
                        bool clear = true;
                        Room fallbackRoom = null;
                        for (int i = 0; i < parts.Length; i++)
                        {
                            Vector2i candidate =
                                new Vector2i(
                                    positions[i].m_x + dx,
                                    positions[i].m_y + dy);
                            Room candidateRoom =
                                map.GetRoomAt(
                                    candidate,
                                    floorIndex);

                            if (!Inside(floor, candidate) ||
                                floor.IsOutdoors(candidate) ||
                                candidateRoom == null ||
                                (originalRooms[i] != null &&
                                    candidateRoom != originalRooms[i]) ||
                                (fallbackRoom != null &&
                                    candidateRoom != fallbackRoom) ||
                                !CanPlace(
                                    map,
                                    parts[i],
                                    candidate))
                            {
                                clear = false;
                                break;
                            }

                            if (fallbackRoom == null)
                            {
                                fallbackRoom = candidateRoom;
                            }
                        }
                        if (clear)
                        {
                            result =
                                new Vector2i(
                                    positions[0].m_x + dx,
                                    positions[0].m_y + dy);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

        private static bool Inside(Floor floor, Vector2i position)
        {
            return position.m_x >= 1 && position.m_y >= 1 &&
                position.m_x < floor.m_size.m_x - 1 &&
                position.m_y < floor.m_size.m_y - 1;
        }

        private static bool CanPlace(
            MapScriptInterface map,
            TileObject tile,
            Vector2i position)
        {
            if (tile == null ||
                tile.m_state == null ||
                tile.m_state.m_gameDBObject.Entry == null ||
                Hospital.Instance == null)
            {
                return false;
            }

            int floorIndex = tile.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];
            if (!Inside(floor, position) ||
                floor.IsOutdoors(position) ||
                map.GetRoomAt(position, floorIndex) == null ||
                (floor.m_tileObjects[
                    position.m_x,
                    position.m_y].m_centerObject != null &&
                 floor.m_tileObjects[
                    position.m_x,
                    position.m_y].m_centerObject != tile) ||
                floor.m_mapPersistentData.m_tiles[
                    position.m_x,
                    position.m_y].m_user != null ||
                floor.IsAnyReservedObjectAt(position))
            {
                return false;
            }

            Direction objectOrientation =
                tile.m_state.m_moving
                    ? tile.m_state.m_moveOrientation
                    : tile.Orientation;
            Vector2i placementPosition =
                IsometricCameraUtils.GetRotatedPositionInverse(
                    position.m_x,
                    position.m_y);
            Direction placementOrientation =
                IsometricCameraUtils.GetRotatedDirection(
                    objectOrientation);

            PlacementValidationResult validity =
                floor.CanPlaceObject(
                    tile.m_state.m_gameDBObject.Entry,
                    placementPosition,
                    placementOrientation);

            return validity == PlacementValidationResult.ALLOWED &&
                map.CanMoveObject(tile, position);
        }
    }
    [HarmonyPatch(typeof(BehaviorNurse), "UpdateOverridenReturningStretcher")]
    internal static class NurseEquipmentReturnPatch
    {
        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return BlockedMobileEquipmentPatch.NurseTranspiler(instructions);
        }
    }

    [HarmonyPatch]
    internal static class JanitorEquipmentReturnPatch
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            yield return AccessTools.Method(typeof(BehaviorJanitor), "UpdateStateReturningCart");
            yield return AccessTools.Method(typeof(BehaviorJanitor), "UpdateStateWalkingToNextRoom");
        }

        [HarmonyTranspiler]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            return BlockedMobileEquipmentPatch.JanitorTranspiler(instructions);
        }
    }

}
