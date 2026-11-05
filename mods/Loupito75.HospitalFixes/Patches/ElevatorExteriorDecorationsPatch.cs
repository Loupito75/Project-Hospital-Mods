using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    // Elevator call buttons occupy the same native edge slots that normal
    // wall decorations use. Keep the prefab button in its edge slot and use
    // the otherwise-unused native attachment slot for one user wall visual.
    // The TileObject itself remains a normal Vanilla entity and is therefore
    // saved/rendered/refunded through the game's existing systems.
    internal static class ElevatorExteriorDecorationsPatch
    {
        private static readonly FieldInfo EdgeNWField =
            AccessTools.Field(typeof(TileObjects), "m_ObjectNW");
        private static readonly FieldInfo EdgeNEField =
            AccessTools.Field(typeof(TileObjects), "m_ObjectNE");
        private static readonly FieldInfo EdgeSEField =
            AccessTools.Field(typeof(TileObjects), "m_ObjectSE");
        private static readonly FieldInfo EdgeSWField =
            AccessTools.Field(typeof(TileObjects), "m_ObjectSW");

        private static readonly MethodInfo AssignLoadedNWMethod =
            AccessTools.Method(
                typeof(ElevatorExteriorDecorationsPatch),
                "AssignLoadedNW");
        private static readonly MethodInfo AssignLoadedNEMethod =
            AccessTools.Method(
                typeof(ElevatorExteriorDecorationsPatch),
                "AssignLoadedNE");
        private static readonly MethodInfo AssignLoadedSEMethod =
            AccessTools.Method(
                typeof(ElevatorExteriorDecorationsPatch),
                "AssignLoadedSE");
        private static readonly MethodInfo AssignLoadedSWMethod =
            AccessTools.Method(
                typeof(ElevatorExteriorDecorationsPatch),
                "AssignLoadedSW");

        private static readonly MethodInfo PlacedToEdgeGetter =
            AccessTools.PropertyGetter(
                typeof(GameDBObject),
                "PlacedToEdge");

        private static readonly MethodInfo RenderPlacedToEdgeMethod =
            AccessTools.Method(
                typeof(ElevatorExteriorDecorationsPatch),
                "RenderAsPlacedToEdge");

        private static readonly MethodInfo TileObjectOrientationGetter =
            AccessTools.PropertyGetter(
                typeof(TileObject),
                "Orientation");

        private static readonly MethodInfo RenderTileSideDirectionMethod =
            AccessTools.Method(
                typeof(LabCompositeWallOverlapPatch),
                "GetRenderTileSideDirection");
        internal static bool AllowsOnExteriorWall(
            GameDBObject existing,
            GameDBObject placing,
            Floor floor,
            Vector2i position,
            Direction orientation,
            bool ignoreWalls)
        {
            if (!IsSupportedDecoration(
                    placing,
                    floor,
                    ignoreWalls) ||
                existing == null)
            {
                return false;
            }

            Vector2i tile =
                IsometricCameraUtils.GetRotatedPosition(
                    position.m_x,
                    position.m_y);
            if (!Inside(floor, tile))
            {
                return false;
            }

            TileObjects objects =
                floor.m_tileObjects[tile.m_x, tile.m_y];
            if (objects == null ||
                objects.m_centerObject == null ||
                objects.m_centerObject.m_state == null ||
                objects.m_centerObject.m_state
                    .m_gameDBObject.Entry != existing ||
                !IsElevatorPrefabObject(
                    objects.m_centerObject))
            {
                return false;
            }

            Direction direction =
                IsometricCameraUtils.GetRotatedDirectionInverse(
                    orientation);
            bool allowed =
                IsTargetFaceFree(objects, direction) &&
                HasElevatorExteriorWall(
                    floor,
                    tile,
                    direction);

            if (allowed && HfxDiagnostics.Enabled)
            {
                string placingId =
                    placing.DatabaseID == null
                        ? "unknown"
                        : placing.DatabaseID.ToString();
                HfxDiagnostics.InterventionRateLimited(
                    "AllowElevatorExteriorWallDecorations",
                    "Floor.CanPlaceObject",
                    "placement-allowed-on-elevator-exterior",
                    "object=" + placingId +
                    " tile=" + tile.m_x + "," + tile.m_y +
                    " direction=" + direction,
                    "AllowElevatorExteriorWallDecorations|" +
                    placingId + "|" + tile.m_x + "," + tile.m_y,
                    3000);
            }

            return allowed;
        }

        internal static bool CanBypassOccupiedPrefabTile(
            GameDBObject placing,
            Floor floor,
            Vector2i position,
            Direction orientation,
            bool ignoreWalls)
        {
            if (!IsSupportedDecoration(
                    placing,
                    floor,
                    ignoreWalls))
            {
                return false;
            }

            Vector2i tile =
                IsometricCameraUtils.GetRotatedPosition(
                    position.m_x,
                    position.m_y);
            if (!Inside(floor, tile))
            {
                return false;
            }

            TileObjects objects =
                floor.m_tileObjects[tile.m_x, tile.m_y];
            if (objects == null ||
                !ContainsOnlyElevatorPrefabObjects(objects))
            {
                return false;
            }

            Direction direction =
                IsometricCameraUtils.GetRotatedDirectionInverse(
                    orientation);

            // The two narrow panels beside the doors contain the elevator
            // call buttons themselves. They occupy the target edge slot, but
            // the physical wall is valid and the native attachment slot on
            // these corridor tiles is unused. Treat this one exact pairing as
            // non-blocking; AddObjectPatch stores the user decoration in that
            // secondary native slot.
            if (objects.m_attachmentObject == null &&
                IsElevatorCallButton(
                    GetTargetFaceObject(
                        objects,
                        direction)) &&
                HasPhysicalWallWithoutDoor(
                    floor,
                    tile,
                    direction))
            {
                return true;
            }

            // Other elevator exterior faces keep the conservative free-slot
            // path used by earlier HFX builds.
            return IsTargetFaceFree(objects, direction) &&
                HasElevatorExteriorWall(
                    floor,
                    tile,
                    direction);
        }

        internal static bool CanUseCallButtonOverlay(
            GameDBObject placing,
            Floor floor,
            Vector2i position,
            Direction orientation,
            bool noRotation)
        {
            // noRotation is used by Vanilla clone/Undo restoration. Existing
            // overlays must remain restorable even if the XML option is later
            // disabled; only new player placement depends on the option.
            if (noRotation)
            {
                if (placing == null ||
                    floor == null ||
                    floor.m_tileObjects == null ||
                    !IsSupportedWallVisual(placing))
                {
                    return false;
                }
            }
            else if (!IsSupportedDecoration(
                         placing,
                         floor,
                         false))
            {
                return false;
            }

            Vector2i tile =
                noRotation
                    ? position
                    : IsometricCameraUtils.GetRotatedPosition(
                        position.m_x,
                        position.m_y);
            Direction direction =
                noRotation
                    ? orientation
                    : IsometricCameraUtils
                        .GetRotatedDirectionInverse(
                            orientation);

            if (!Inside(floor, tile))
            {
                return false;
            }

            TileObjects objects =
                floor.m_tileObjects[tile.m_x, tile.m_y];

            return objects != null &&
                objects.m_attachmentObject == null &&
                IsElevatorCallButton(
                    GetTargetFaceObject(
                        objects,
                        direction)) &&
                HasPhysicalWallWithoutDoor(
                    floor,
                    tile,
                    direction);
        }

        private static bool IsSupportedWallVisual(
            GameDBObject placing)
        {
            if (placing == null ||
                !placing.AttachedToWall ||
                !placing.PlacedToEdge ||
                placing.AttachedToObject)
            {
                return false;
            }

            return placing.HasTag("ui_decals") ||
                placing.HasTag("ui_posters") ||
                (placing.HasTag("decoration") &&
                    placing.HasTag("ui_decorations"));
        }

        private static bool IsSupportedDecoration(
            GameDBObject placing,
            Floor floor,
            bool ignoreWalls)
        {
            if (!FixConfig.AllowElevatorExteriorWallDecorations ||
                placing == null ||
                floor == null ||
                floor.m_tileObjects == null ||
                Database.Instance == null ||
                ignoreWalls ||
                !IsSupportedWallVisual(placing))
            {
                return false;
            }

            return true;
        }

        private static bool ContainsOnlyElevatorPrefabObjects(
            TileObjects objects)
        {
            if (objects == null)
            {
                return false;
            }

            bool foundElevatorPart = false;

            if (!ValidateElevatorPrefabSlot(
                    objects.m_centerObject,
                    ref foundElevatorPart) ||
                !ValidateElevatorPrefabSlot(
                    objects.m_attachmentObject,
                    ref foundElevatorPart) ||
                !ValidateElevatorPrefabSlot(
                    objects.m_ObjectNW,
                    ref foundElevatorPart) ||
                !ValidateElevatorPrefabSlot(
                    objects.m_ObjectNE,
                    ref foundElevatorPart) ||
                !ValidateElevatorPrefabSlot(
                    objects.m_ObjectSE,
                    ref foundElevatorPart) ||
                !ValidateElevatorPrefabSlot(
                    objects.m_ObjectSW,
                    ref foundElevatorPart))
            {
                return false;
            }

            return foundElevatorPart;
        }

        private static bool ValidateElevatorPrefabSlot(
            TileObject tileObject,
            ref bool foundElevatorPart)
        {
            if (tileObject == null)
            {
                return true;
            }

            if (!IsElevatorPrefabObject(tileObject))
            {
                return false;
            }

            foundElevatorPart = true;
            return true;
        }

        private static bool IsElevatorPrefabObject(
            TileObject tileObject)
        {
            if (tileObject == null ||
                tileObject.m_state == null)
            {
                return false;
            }

            PrefabInstance parent =
                tileObject.m_state.m_prefabParent == null
                    ? null
                    : tileObject.m_state.m_prefabParent
                        .GetEntity();
            if (parent != null &&
                parent.m_persistentData != null &&
                parent.m_persistentData.m_prefabObject != null &&
                parent.m_persistentData.m_prefabObject.Entry != null &&
                parent.m_persistentData.m_prefabObject.Entry
                    .HasTag("elevators_prefab"))
            {
                return true;
            }

            // Defensive fallback for old/incomplete prefab-parent data.
            return IsElevatorPart(tileObject);
        }

        private static bool IsElevatorPart(
            TileObject tile)
        {
            if (tile == null ||
                tile.m_state == null ||
                tile.m_state.m_gameDBObject.Entry == null ||
                ID.IsNullOrNoID(
                    tile.m_state.m_gameDBObject.Entry.DatabaseID))
            {
                return false;
            }

            string id =
                tile.m_state.m_gameDBObject.Entry
                    .DatabaseID.ToString();
            switch (id)
            {
                case "OBJECT_ELEVATOR_1":
                case "OBJECT_ELEVATOR_2":
                case "OBJECT_ELEVATOR_3":
                case "OBJECT_ELEVATOR_4":
                case "OBJECT_ELEVATOR_5":
                case "OBJECT_ELEVATOR_6":
                case "OBJECT_ELEVATOR_7":
                case "OBJECT_ELEVATOR_8":
                case "OBJECT_ELEVATOR_9":
                case "OBJECT_ELEVATOR_10":
                case "OBJECT_ELEVATOR_11":
                    return true;
                default:
                    return false;
            }
        }

        private static bool TileHasElevatorPrefabPart(
            Floor floor,
            Vector2i tile)
        {
            if (!Inside(floor, tile))
            {
                return false;
            }

            TileObjects objects =
                floor.m_tileObjects[tile.m_x, tile.m_y];
            if (objects == null)
            {
                return false;
            }

            return IsElevatorPrefabObject(objects.m_centerObject) ||
                IsElevatorPrefabObject(objects.m_attachmentObject) ||
                IsElevatorPrefabObject(objects.m_ObjectNW) ||
                IsElevatorPrefabObject(objects.m_ObjectNE) ||
                IsElevatorPrefabObject(objects.m_ObjectSE) ||
                IsElevatorPrefabObject(objects.m_ObjectSW);
        }

        private static TileObject GetTargetFaceObject(
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

        private static void SetTargetFaceObject(
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

        private static bool IsElevatorCallButton(
            TileObject tileObject)
        {
            if (tileObject == null ||
                tileObject.m_state == null ||
                tileObject.m_state.m_gameDBObject.Entry == null ||
                ID.IsNullOrNoID(
                    tileObject.m_state.m_gameDBObject
                        .Entry.DatabaseID))
            {
                return false;
            }

            return tileObject.m_state.m_gameDBObject
                .Entry.DatabaseID.ToString() ==
                    "OBJECT_ELEVATOR_1";
        }

        private static bool IsTargetFaceFree(
            TileObjects objects,
            Direction direction)
        {
            switch (direction)
            {
                case Direction.NW:
                    return objects.m_ObjectNW == null;
                case Direction.NE:
                    return objects.m_ObjectNE == null;
                case Direction.SE:
                    return objects.m_ObjectSE == null;
                case Direction.SW:
                    return objects.m_ObjectSW == null;
                default:
                    return false;
            }
        }

        private static bool HasPhysicalWallWithoutDoor(
            Floor floor,
            Vector2i tile,
            Direction direction)
        {
            TileWalls wall;

            switch (direction)
            {
                case Direction.NW:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x + 1,
                            tile.m_y);
                    return wall != null &&
                        wall.m_wallSE != null &&
                        wall.m_doorSE == null;

                case Direction.NE:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x,
                            tile.m_y + 1);
                    return wall != null &&
                        wall.m_wallSW != null &&
                        wall.m_doorSW == null;

                case Direction.SE:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x,
                            tile.m_y);
                    return wall != null &&
                        wall.m_wallSE != null &&
                        wall.m_doorSE == null;

                case Direction.SW:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x,
                            tile.m_y);
                    return wall != null &&
                        wall.m_wallSW != null &&
                        wall.m_doorSW == null;

                default:
                    return false;
            }
        }

        private static bool HasElevatorExteriorWall(
            Floor floor,
            Vector2i tile,
            Direction direction)
        {
            TileWalls wall;
            Vector2i other;

            switch (direction)
            {
                case Direction.NW:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x + 1,
                            tile.m_y);
                    other =
                        new Vector2i(
                            tile.m_x + 1,
                            tile.m_y);
                    if (wall == null ||
                        wall.m_wallSE == null ||
                        wall.m_doorSE != null)
                    {
                        return false;
                    }
                    break;

                case Direction.NE:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x,
                            tile.m_y + 1);
                    other =
                        new Vector2i(
                            tile.m_x,
                            tile.m_y + 1);
                    if (wall == null ||
                        wall.m_wallSW == null ||
                        wall.m_doorSW != null)
                    {
                        return false;
                    }
                    break;

                case Direction.SE:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x,
                            tile.m_y);
                    other =
                        new Vector2i(
                            tile.m_x - 1,
                            tile.m_y);
                    if (wall == null ||
                        wall.m_wallSE == null ||
                        wall.m_doorSE != null)
                    {
                        return false;
                    }
                    break;

                case Direction.SW:
                    wall =
                        floor.GetWallsSafe(
                            tile.m_x,
                            tile.m_y);
                    other =
                        new Vector2i(
                            tile.m_x,
                            tile.m_y - 1);
                    if (wall == null ||
                        wall.m_wallSW == null ||
                        wall.m_doorSW != null)
                    {
                        return false;
                    }
                    break;

                default:
                    return false;
            }

            if (!Inside(floor, other))
            {
                return false;
            }

            // A real elevator exterior wall separates one elevator-prefab
            // tile from one ordinary tile. This works from either side of the
            // wall, including the narrow wall panels beside elevator doors.
            bool thisSideElevator =
                TileHasElevatorPrefabPart(
                    floor,
                    tile);
            bool otherSideElevator =
                TileHasElevatorPrefabPart(
                    floor,
                    other);

            return thisSideElevator != otherSideElevator;
        }

        [HarmonyPatch(
            typeof(ObjectRenderer),
            "RenderSingleObject",
            new System.Type[]
            {
                typeof(TileObject),
                typeof(float),
                typeof(float),
                typeof(Floor),
                typeof(LightMap2DUnity),
                typeof(bool),
                typeof(bool)
            })]
        private static class ObjectRendererPatch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                bool replacedTileSideCheck = false;
                bool replaceTileSideOrientation = false;

                foreach (CodeInstruction instruction in instructions)
                {
                    if (!replacedTileSideCheck &&
                        instruction.Calls(
                            PlacedToEdgeGetter))
                    {
                        // Stack already contains the GameDBObject receiver.
                        // Add TileObject and Floor, then decide only the
                        // renderer slot. The actual object definition remains
                        // PlacedToEdge everywhere else.
                        yield return
                            new CodeInstruction(
                                OpCodes.Ldarg_1);
                        yield return
                            new CodeInstruction(
                                OpCodes.Ldarg_S,
                                (byte)4);
                        instruction.opcode =
                            OpCodes.Call;
                        instruction.operand =
                            RenderPlacedToEdgeMethod;
                        replacedTileSideCheck = true;
                        replaceTileSideOrientation = true;
                        yield return instruction;
                        continue;
                    }

                    if (replaceTileSideOrientation &&
                        instruction.Calls(
                            TileObjectOrientationGetter))
                    {
                        // This is the Orientation read used only to assign
                        // ObjectData.m_tileSide. A lab-overlap wall keeps its
                        // real Orientation for texture/use logic but renders
                        // into HFX's otherwise-unused slot 3.
                        yield return
                            new CodeInstruction(
                                OpCodes.Ldarg_S,
                                (byte)4);
                        instruction.opcode =
                            OpCodes.Call;
                        instruction.operand =
                            RenderTileSideDirectionMethod;
                        replaceTileSideOrientation = false;
                    }

                    yield return instruction;
                }
            }
        }

        private static bool RenderAsPlacedToEdge(
            GameDBObject objectType,
            TileObject tileObject,
            Floor floor)
        {
            if (objectType == null)
            {
                return false;
            }

            if (IsStoredCallButtonOverlay(
                    tileObject,
                    floor))
            {
                // Returning false only for the first renderer-side check makes
                // ObjectRenderer use visual slot 0 instead of the occupied
                // edge slot. Orientation, texture choice and wall rendering
                // remain those of the real PlacedToEdge object.
                return false;
            }

            return objectType.PlacedToEdge;
        }

        private static bool IsStoredCallButtonOverlay(
            TileObject tileObject,
            Floor floor)
        {
            if (tileObject == null ||
                tileObject.m_state == null ||
                floor == null ||
                !IsSupportedWallVisual(
                    tileObject.m_state.m_gameDBObject.Entry))
            {
                return false;
            }

            Vector2i tile =
                tileObject.m_state.m_position;
            if (!Inside(floor, tile))
            {
                return false;
            }

            TileObjects objects =
                floor.m_tileObjects[
                    tile.m_x,
                    tile.m_y];

            return objects != null &&
                objects.m_attachmentObject == tileObject &&
                HasCallButtonEdge(objects);
        }

        [HarmonyPatch(
            typeof(Floor),
            "AddObject",
            new System.Type[]
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
                ref TileObject __result)
            {
                if (!CanUseCallButtonOverlay(
                        objectType,
                        __instance,
                        position,
                        orientation,
                        noRotation))
                {
                    return true;
                }

                Vector2i tile =
                    noRotation
                        ? position
                        : IsometricCameraUtils
                            .GetRotatedPosition(
                                position.m_x,
                                position.m_y);
                Direction direction =
                    noRotation
                        ? orientation
                        : IsometricCameraUtils
                            .GetRotatedDirectionInverse(
                                orientation);

                TileObjects objects =
                    __instance.m_tileObjects[
                        tile.m_x,
                        tile.m_y];
                if (objects == null ||
                    objects.m_attachmentObject != null)
                {
                    return true;
                }

                if (department == null)
                {
                    Room room =
                        __instance.m_roomTiles[
                            tile.m_x,
                            tile.m_y];
                    department =
                        room != null
                            ? room.m_roomPersistentData
                                .m_department.GetEntity()
                            : Hospital.Instance
                                .m_activeDepartment
                                .GetEntity();
                }

                if (department == null)
                {
                    return true;
                }

                objects.SetDirty(true);

                TileObject decoration =
                    new TileObject(
                        tile,
                        department,
                        objectType,
                        direction,
                        materialColor,
                        __instance);

                // Deliberately do not set attachmentParent. m_attachmentObject
                // is used only as the native second storage/render/save slot;
                // the wall visual keeps its own PlacedToEdge semantics.
                objects.m_attachmentObject =
                    decoration;
                department.AddObject(decoration);
                __instance.ValidateObjectsInARoom(
                    tile);

                if (objectType.LightAutomatic)
                {
                    objects.UpdateAllObjects(
                        0,
                        0f);
                }

                __result = decoration;

                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Result(
                        "AllowElevatorExteriorWallDecorations",
                        "Floor.AddObject",
                        "overlay-placement-confirmed",
                        "object=" +
                        (objectType.DatabaseID == null
                            ? "unknown"
                            : objectType.DatabaseID.ToString()) +
                        " tile=" + tile.m_x + "," + tile.m_y +
                        " direction=" + direction +
                        " storage=m_attachmentObject");
                }

                return false;
            }
        }

        [HarmonyPatch(typeof(Floor), "LoadEntities")]
        private static class LoadEntitiesPatch
        {
            [HarmonyTranspiler]
            private static IEnumerable<CodeInstruction> Transpiler(
                IEnumerable<CodeInstruction> instructions)
            {
                foreach (CodeInstruction instruction in instructions)
                {
                    if (instruction.opcode == OpCodes.Stfld)
                    {
                        if (object.Equals(
                                instruction.operand,
                                EdgeNWField))
                        {
                            instruction.opcode =
                                OpCodes.Call;
                            instruction.operand =
                                AssignLoadedNWMethod;
                        }
                        else if (object.Equals(
                                     instruction.operand,
                                     EdgeNEField))
                        {
                            instruction.opcode =
                                OpCodes.Call;
                            instruction.operand =
                                AssignLoadedNEMethod;
                        }
                        else if (object.Equals(
                                     instruction.operand,
                                     EdgeSEField))
                        {
                            instruction.opcode =
                                OpCodes.Call;
                            instruction.operand =
                                AssignLoadedSEMethod;
                        }
                        else if (object.Equals(
                                     instruction.operand,
                                     EdgeSWField))
                        {
                            instruction.opcode =
                                OpCodes.Call;
                            instruction.operand =
                                AssignLoadedSWMethod;
                        }
                    }

                    yield return instruction;
                }
            }
        }

        [HarmonyPatch(
            typeof(TileObjects),
            "GetTopObject",
            new System.Type[]
            {
                typeof(
                    MapEditorUIController
                        .ObjectPickerModifier),
                typeof(bool),
                typeof(bool)
            })]
        private static class GetTopObjectPatch
        {
            [HarmonyPrefix]
            private static bool Prefix(
                TileObjects __instance,
                MapEditorUIController
                    .ObjectPickerModifier modifier,
                bool selectForDeletion,
                bool selectForCloning,
                ref TileObject __result)
            {
                if (__instance == null ||
                    __instance.m_attachmentObject == null ||
                    !IsSupportedWallVisual(
                        __instance.m_attachmentObject
                            .m_state.m_gameDBObject.Entry) ||
                    !HasCallButtonEdge(__instance))
                {
                    return true;
                }

                bool buildingMode =
                    ViewModeController.Instance != null &&
                    (ViewModeController.Instance
                        .m_currentMode == ViewModes.BUILDING ||
                     ViewModeController.Instance
                        .m_currentMode == ViewModes.BUILDING_FLOORS);

                // Treat the overlay as the top wall visual only while editing
                // the building. Gameplay object searches keep Vanilla's
                // original ordering and still see the elevator prefab part.
                if (buildingMode &&
                    (modifier ==
                        MapEditorUIController
                            .ObjectPickerModifier.None ||
                     selectForDeletion ||
                     selectForCloning))
                {
                    __result =
                        __instance.m_attachmentObject;
                    return false;
                }

                return true;
            }
        }

        [HarmonyPatch(typeof(PrefabInstance), "Delete")]
        private static class PrefabDeletePatch
        {
            [HarmonyPrefix]
            private static void Prefix(
                PrefabInstance __instance)
            {
                if (__instance == null ||
                    __instance.m_persistentData == null ||
                    __instance.m_persistentData
                        .m_prefabObject == null ||
                    __instance.m_persistentData
                        .m_prefabObject.Entry == null ||
                    !__instance.m_persistentData
                        .m_prefabObject.Entry
                        .HasTag("elevators_prefab"))
                {
                    return;
                }

                foreach (
                    EntityIDPointer<TileObject> pointer
                    in __instance.m_persistentData
                        .m_tileObjects)
                {
                    TileObject prefabPart =
                        pointer.GetEntity();
                    if (prefabPart == null)
                    {
                        continue;
                    }

                    Floor floor =
                        Hospital.Instance.m_floors[
                            prefabPart.GetFloorIndex()];
                    Vector2i tile =
                        prefabPart.m_state.m_position;
                    TileObjects objects =
                        floor.m_tileObjects[
                            tile.m_x,
                            tile.m_y];

                    if (objects == null ||
                        objects.m_attachmentObject == null ||
                        !IsSupportedWallVisual(
                            objects.m_attachmentObject
                                .m_state.m_gameDBObject
                                .Entry) ||
                        !HasCallButtonEdge(objects))
                    {
                        continue;
                    }

                    floor.RemoveObject(
                        tile,
                        false,
                        true,
                        objects.m_attachmentObject);
                }
            }
        }

        private static bool HasCallButtonEdge(
            TileObjects objects)
        {
            return objects != null &&
                (IsElevatorCallButton(
                    objects.m_ObjectNW) ||
                 IsElevatorCallButton(
                    objects.m_ObjectNE) ||
                 IsElevatorCallButton(
                    objects.m_ObjectSE) ||
                 IsElevatorCallButton(
                    objects.m_ObjectSW));
        }

        private static void AssignLoadedNW(
            TileObjects objects,
            TileObject value)
        {
            AssignLoadedEdge(
                objects,
                Direction.NW,
                value);
        }

        private static void AssignLoadedNE(
            TileObjects objects,
            TileObject value)
        {
            AssignLoadedEdge(
                objects,
                Direction.NE,
                value);
        }

        private static void AssignLoadedSE(
            TileObjects objects,
            TileObject value)
        {
            AssignLoadedEdge(
                objects,
                Direction.SE,
                value);
        }

        private static void AssignLoadedSW(
            TileObjects objects,
            TileObject value)
        {
            AssignLoadedEdge(
                objects,
                Direction.SW,
                value);
        }

        private static void AssignLoadedEdge(
            TileObjects objects,
            Direction direction,
            TileObject value)
        {
            if (objects == null)
            {
                return;
            }

            if (LabCompositeWallOverlapPatch
                .TryAssignLoadedEdge(
                    objects,
                    direction,
                    value))
            {
                return;
            }

            TileObject existing =
                GetTargetFaceObject(
                    objects,
                    direction);

            if (existing != null &&
                value != null)
            {
                bool existingIsButton =
                    IsElevatorCallButton(
                        existing);
                bool valueIsButton =
                    IsElevatorCallButton(
                        value);
                bool existingIsOverlay =
                    IsSupportedWallVisual(
                        existing.m_state
                            .m_gameDBObject.Entry);
                bool valueIsOverlay =
                    IsSupportedWallVisual(
                        value.m_state
                            .m_gameDBObject.Entry);

                if (existingIsButton &&
                    valueIsOverlay &&
                    objects.m_attachmentObject == null)
                {
                    objects.m_attachmentObject =
                        value;
                    return;
                }

                if (existingIsOverlay &&
                    valueIsButton &&
                    objects.m_attachmentObject == null)
                {
                    objects.m_attachmentObject =
                        existing;
                    SetTargetFaceObject(
                        objects,
                        direction,
                        value);
                    return;
                }
            }

            SetTargetFaceObject(
                objects,
                direction,
                value);
        }

        private static bool Inside(
            Floor floor,
            Vector2i tile)
        {
            return floor != null &&
                tile.m_x >= 0 &&
                tile.m_y >= 0 &&
                tile.m_x < floor.m_size.m_x &&
                tile.m_y < floor.m_size.m_y;
        }
    }
}
