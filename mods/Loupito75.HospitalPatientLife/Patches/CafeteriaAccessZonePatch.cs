using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPatientLife.Patches
{
    internal sealed class CafeteriaAccessTileSnapshot
    {
        internal int X;
        internal int Y;
        internal AccessRights AccessRights;
    }

    internal sealed class CafeteriaAccessPaintState
    {
        internal Floor Floor;
        internal readonly List<CafeteriaAccessTileSnapshot> Tiles =
            new List<CafeteriaAccessTileSnapshot>();
    }

    internal static class CafeteriaAccessChangeTracker
    {
        private static readonly HashSet<int> PendingFloors = new HashSet<int>();

        internal static void MarkChanged(int floorIndex)
        {
            PendingFloors.Add(floorIndex);
        }

        internal static bool ConsumeChanged(int floorIndex)
        {
            return PendingFloors.Remove(floorIndex);
        }
    }

    internal static class CafeteriaRouteRefresh
    {
        private static readonly FieldInfo PathfinderJobField =
            AccessTools.Field(typeof(WalkComponent), "m_pathfinderJob");

        internal static void RefreshFloor(Floor floor)
        {
            if (floor == null || Hospital.Instance == null ||
                Hospital.Instance.m_characters == null)
            {
                return;
            }

            int repathed = 0;
            int cancelled = 0;
            List<Entity> characters = new List<Entity>(Hospital.Instance.m_characters);

            foreach (Entity entity in characters)
            {
                if (entity == null || entity.GetComponent<BehaviorPatient>() == null)
                {
                    continue;
                }

                WalkComponent walk = entity.GetComponent<WalkComponent>();
                ProcedureComponent procedures = entity.GetComponent<ProcedureComponent>();
                if (walk == null || walk.m_state == null || procedures == null ||
                    procedures.m_state == null ||
                    procedures.m_state.m_currentProcedureScript == null ||
                    procedures.m_state.m_currentProcedureScript.GetEntity() == null)
                {
                    continue;
                }

                ProcedureScript script =
                    procedures.m_state.m_currentProcedureScript.GetEntity();
                if (script.IsIdle() ||
                    (!(script is ProcedureScriptNeedHunger) &&
                     !(script is ProcedureScriptStaffLunch)) ||
                    script.m_stateData == null ||
                    script.m_stateData.m_procedureScene == null ||
                    script.m_stateData.m_procedureScene.m_equipment == null ||
                    script.m_stateData.m_procedureScene.m_equipment.Length == 0 ||
                    script.m_stateData.m_procedureScene.m_equipment[0] == null ||
                    script.m_stateData.m_procedureScene.m_equipment[0].GetEntity() == null)
                {
                    continue;
                }

                TileObject target =
                    script.m_stateData.m_procedureScene.m_equipment[0].GetEntity();
                if (target.GetFloorIndex() != floor.m_floorIndex ||
                    !IsCafeteriaTarget(floor, target))
                {
                    continue;
                }

                BehaviorPatient behavior = entity.GetComponent<BehaviorPatient>();
                if (CanAccessTarget(floor, target, behavior))
                {
                    if (IsPathState(walk.m_state.m_walkState) && ForceRepath(walk))
                    {
                        repathed++;
                        Trace(entity, script, target, behavior, "REPATH");
                    }
                    continue;
                }

                if (CanFinishWithoutAnotherCafeteriaRoute(script))
                {
                    continue;
                }

                bool scheduledMeal =
                    ScheduledCafeteriaMealState.IsScheduledCafeteriaMealInProgress(entity);
                if (scheduledMeal)
                {
                    ScheduledCafeteriaMealState.ClearPending(entity);
                    ScheduledCafeteriaMealState.ForceTrayFallback(entity);
                }

                CancelCafeteriaProcedure(entity, walk, procedures, script);
                cancelled++;
                Trace(
                    entity,
                    script,
                    target,
                    behavior,
                    scheduledMeal ? "CANCEL_TO_TRAY" : "CANCEL_RESELECT");
            }

            if (HospitalPatientLifeConfig.DebugLogging && Plugin.Log != null &&
                (repathed > 0 || cancelled > 0))
            {
                Plugin.Log.LogInfo(
                    "cafeteria-route-refresh | floor=" + floor.m_floorIndex +
                    " | repathed=" + repathed +
                    " | cancelled=" + cancelled);
            }
        }

        private static bool IsCafeteriaTarget(Floor floor, TileObject target)
        {
            if (floor == null || target == null || target.m_state == null)
            {
                return false;
            }

            Vector2i position = target.m_state.m_position;
            if (position.m_x < 0 || position.m_y < 0 ||
                position.m_x >= floor.m_size.m_x || position.m_y >= floor.m_size.m_y)
            {
                return false;
            }

            return PatientCafeteriaRules.IsCafeteriaRoom(
                floor.m_roomTiles[position.m_x, position.m_y]);
        }

        private static bool CanAccessTarget(
            Floor floor,
            TileObject target,
            BehaviorPatient behavior)
        {
            if (floor == null || target == null || target.m_state == null || behavior == null)
            {
                return false;
            }

            Vector2i position = target.m_state.m_position;
            if (position.m_x < 0 || position.m_y < 0 ||
                position.m_x >= floor.m_size.m_x || position.m_y >= floor.m_size.m_y)
            {
                return false;
            }

            int accessLevel = (int)behavior.GetAccessRights();
            AccessRights roomAccess = floor.m_roomAccessRights[position.m_x, position.m_y];
            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            // Matches Floor.IsAccessible(): room access may not exceed the character's
            // current access level; logistics BIOHAZARD is the vanilla exception.
            return (int)roomAccess <= accessLevel &&
                   (logisticsAccess == AccessRights.BIOHAZARD ||
                    (int)logisticsAccess <= accessLevel);
        }

        private static bool CanFinishWithoutAnotherCafeteriaRoute(ProcedureScript script)
        {
            if (script == null || script.m_stateData == null)
            {
                return false;
            }

            string state = script.m_stateData.m_state;

            // A snack already being consumed has no further cafeteria route.
            if (script is ProcedureScriptNeedHunger)
            {
                return state == ProcedureScriptNeedHunger.STATE_USING_OBJECT;
            }

            // StaffLunch still tries another cafeteria object after USING_OBJECT and
            // USING_OWEN. Once actually eating, no further cafeteria route is needed.
            if (script is ProcedureScriptStaffLunch)
            {
                return state == ProcedureScriptStaffLunch.STATE_EATING ||
                       state == ProcedureScriptStaffLunch.STATE_EATING_FINISHED;
            }

            return false;
        }

        private static bool IsPathState(WalkState state)
        {
            return state == WalkState.Walking ||
                   state == WalkState.DestinationSet ||
                   state == WalkState.LookingForPath ||
                   state == WalkState.LookingForPathFallback ||
                   state == WalkState.LookingForPathFallback2 ||
                   state == WalkState.NoPath;
        }

        private static bool ForceRepath(WalkComponent walk)
        {
            if (walk == null || walk.m_state == null)
            {
                return false;
            }

            bool wasWalking = walk.m_state.m_walkState == WalkState.Walking;
            AbortCurrentJob(walk);
            walk.m_route = null;
            walk.m_blockedCount = 0;
            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;

            if (wasWalking && !walk.m_state.m_lying)
            {
                Entity entity = walk.m_entity;
                entity?.GetComponent<AnimModelComponent>()?.PlayAnimation("stand_idle");
            }

            walk.SwitchState(WalkState.DestinationSet);
            return true;
        }

        private static void CancelCafeteriaProcedure(
            Entity entity,
            WalkComponent walk,
            ProcedureComponent procedures,
            ProcedureScript script)
        {
            UseComponent use = entity.GetComponent<UseComponent>();
            if (use != null)
            {
                use.Interrupt();
            }

            AbortCurrentJob(walk);
            walk.m_route = null;
            walk.m_blockedCount = 0;
            walk.m_state.m_nextPosition = walk.m_state.m_currentPosition;
            walk.SwitchState(WalkState.Idle);

            // This is the same generic-procedure cleanup performed by
            // ProcedureComponent.Update() once a script becomes IDLE. Doing it here
            // prevents StaffLunch completion/payment Postfixes from treating a route
            // cancelled by an access change as a completed meal.
            script.SwitchState("IDLE");
            script.ResetOwnerOfEquipment();
            script.ResetPatientReservationInfo();

            ProcedureManager manager = ProcedureManager.GetInstance();
            if (manager != null)
            {
                manager.RemoveScript(script);
            }

            script.Destroy();
            procedures.m_state.m_currentProcedureScript = null;
        }

        private static void AbortCurrentJob(WalkComponent walk)
        {
            try
            {
                PathfinderJob job =
                    PathfinderJobField == null
                        ? null
                        : PathfinderJobField.GetValue(walk) as PathfinderJob;
                if (job != null)
                {
                    job.Abort();
                    PathfinderJobField.SetValue(walk, null);
                }
            }
            catch (Exception exception)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Failed to abort a stale cafeteria pathfinding job: " +
                        exception.Message);
                }

                if (PathfinderJobField != null)
                {
                    PathfinderJobField.SetValue(walk, null);
                }
            }
        }

        private static void Trace(
            Entity entity,
            ProcedureScript script,
            TileObject target,
            BehaviorPatient behavior,
            string action)
        {
            if (!HospitalPatientLifeConfig.DebugLogging || Plugin.Log == null)
            {
                return;
            }

            int access = behavior == null ? -1 : (int)behavior.GetAccessRights();
            string name = entity == null ? "<none>" : (entity.Name ?? string.Empty).Trim();
            string scriptName = script == null ? "<none>" : script.GetType().Name;
            string scriptState =
                script == null || script.m_stateData == null
                    ? "<none>"
                    : script.m_stateData.m_state;

            Plugin.Log.LogInfo(
                "cafeteria-route-change | character=" + name +
                " | action=" + action +
                " | script=" + scriptName +
                " | scriptState=" + scriptState +
                " | movementAccess=" + access +
                " | targetFloor=" + (target == null ? -1 : target.GetFloorIndex()));
        }
    }

    [HarmonyPatch(typeof(MapEditorController), "FillAccessRights")]
    internal static class CafeteriaAccessPaintingPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Prefix(
            MapEditorController __instance,
            out CafeteriaAccessPaintState __state)
        {
            __state = null;

            if (__instance == null || Hospital.Instance == null ||
                MapEditorUIController.Instance == null)
            {
                return;
            }

            Floor floor = Hospital.Instance.GetCurrentFloor();
            if (floor == null)
            {
                return;
            }

            Vector2i selectionStart = IsometricCameraUtils.GetRotatedPosition(
                MapEditorUIController.Instance.m_selectionStart.m_x,
                MapEditorUIController.Instance.m_selectionStart.m_y);
            Vector2i selectionEnd = IsometricCameraUtils.GetRotatedPosition(
                MapEditorUIController.Instance.m_selectionEnd.m_x,
                MapEditorUIController.Instance.m_selectionEnd.m_y);
            Range2i range = __instance.GetSafeRange(selectionStart, selectionEnd);

            CafeteriaAccessPaintState state = new CafeteriaAccessPaintState();
            state.Floor = floor;

            for (int x = range.m_start.m_x; x <= range.m_end.m_x; x++)
            {
                for (int y = range.m_start.m_y; y <= range.m_end.m_y; y++)
                {
                    Room room = floor.m_roomTiles[x, y];
                    if (!PatientCafeteriaRules.IsCafeteriaRoom(room))
                    {
                        continue;
                    }

                    state.Tiles.Add(
                        new CafeteriaAccessTileSnapshot
                        {
                            X = x,
                            Y = y,
                            AccessRights =
                                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[x, y]
                        });
                }
            }

            if (state.Tiles.Count > 0)
            {
                __state = state;
            }
        }

        [HarmonyPriority(Priority.First)]
        private static void Postfix(
            MapEditorController __instance,
            CafeteriaAccessPaintState __state)
        {
            if (__instance == null || Hospital.Instance == null ||
                MapEditorUIController.Instance == null ||
                __state == null || __state.Floor == null)
            {
                return;
            }

            AccessRights selectedRights = (AccessRights)__instance.m_currentEntityType;

            if (selectedRights == AccessRights.BIOHAZARD)
            {
                // Orange/BIOHAZARD has no useful cafeteria meaning. Let vanilla paint
                // the rest of a mixed selection, then restore every cafeteria tile to
                // the exact access value/color it had before the action.
                foreach (CafeteriaAccessTileSnapshot snapshot in __state.Tiles)
                {
                    __state.Floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                        snapshot.X,
                        snapshot.Y] = snapshot.AccessRights;

                    Room room = __state.Floor.m_roomTiles[snapshot.X, snapshot.Y];
                    if (room != null)
                    {
                        room.SetDirty(dirty: true);
                    }
                }
            }
            else if (selectedRights == AccessRights.PEDESTRIAN)
            {
                foreach (CafeteriaAccessTileSnapshot snapshot in __state.Tiles)
                {
                    // PEDESTRIAN is the map-wide default and cannot tell us whether the
                    // player explicitly opened this cafeteria. Store PATIENT instead:
                    // clinic patients, hospitalized patients, visitors and staff can
                    // enter, while decorative BehaviorPedestrian characters cannot.
                    __state.Floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                        snapshot.X,
                        snapshot.Y] = AccessRights.PATIENT;

                    Room room = __state.Floor.m_roomTiles[snapshot.X, snapshot.Y];
                    if (room != null)
                    {
                        room.SetDirty(dirty: true);
                    }
                }
            }

            bool changed = false;
            foreach (CafeteriaAccessTileSnapshot snapshot in __state.Tiles)
            {
                if (__state.Floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                        snapshot.X,
                        snapshot.Y] != snapshot.AccessRights)
                {
                    changed = true;
                    break;
                }
            }

            if (changed)
            {
                CafeteriaAccessChangeTracker.MarkChanged(__state.Floor.m_floorIndex);
            }
        }
    }

    [HarmonyPatch(typeof(Floor), "UpdateStaticNavigationData")]
    internal static class CafeteriaRoomAccessPatch
    {
        [HarmonyPriority(Priority.First)]
        private static void Postfix(Floor __instance)
        {
            if (__instance == null || __instance.m_roomTiles == null ||
                __instance.m_roomAccessRights == null ||
                __instance.m_mapPersistentData == null ||
                __instance.m_mapPersistentData.m_mapLogisticsLayer == null)
            {
                return;
            }

            int cafeteriaTileCount = 0;
            int correctedTileCount = 0;
            int patientTileCount = 0;

            for (int x = 0; x < __instance.m_size.m_x; x++)
            {
                for (int y = 0; y < __instance.m_size.m_y; y++)
                {
                    Room room = __instance.m_roomTiles[x, y];
                    if (!PatientCafeteriaRules.IsCafeteriaRoom(room))
                    {
                        continue;
                    }

                    cafeteriaTileCount++;

                    AccessRights paintedRights =
                        __instance.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[x, y];
                    AccessRights desiredRoomAccess =
                        paintedRights == AccessRights.PEDESTRIAN
                            ? AccessRights.STAFF
                            : paintedRights;

                    if (__instance.m_roomAccessRights[x, y] != desiredRoomAccess)
                    {
                        __instance.m_roomAccessRights[x, y] = desiredRoomAccess;
                        correctedTileCount++;
                    }

                    if (desiredRoomAccess == AccessRights.PATIENT)
                    {
                        patientTileCount++;
                    }
                }
            }

            bool recalculated = false;
            if (correctedTileCount > 0)
            {
                GridMap gridMap = GridMap.GetInstance();
                if (gridMap != null)
                {
                    // Vanilla has already initialized GridMap and completed its first
                    // rebuild before this Postfix runs. Recalculate once more only when
                    // cafeteria room access actually differs from the native room type.
                    gridMap.Recalculate(
                        __instance.m_floorIndex,
                        __instance.m_mapPersistentData.m_tileWalls,
                        __instance.m_mapPersistentData.m_tiles,
                        __instance.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights,
                        __instance.m_roomAccessRights,
                        __instance.m_elevators);
                    recalculated = true;
                }
            }

            if (CafeteriaAccessChangeTracker.ConsumeChanged(__instance.m_floorIndex))
            {
                CafeteriaRouteRefresh.RefreshFloor(__instance);
            }

            if (HospitalPatientLifeConfig.DebugLogging &&
                Plugin.Log != null && cafeteriaTileCount > 0)
            {
                Plugin.Log.LogInfo(
                    "cafeteria-nav-access-postfix | floor=" + __instance.m_floorIndex +
                    " | cafeteriaTiles=" + cafeteriaTileCount +
                    " | correctedTiles=" + correctedTileCount +
                    " | patientTiles=" + patientTileCount +
                    " | recalculated=" + recalculated);
            }
        }
    }

    [HarmonyPatch]
    internal static class CafeteriaLogisticsColorPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(RoomRenderer),
                "GetRoomColor",
                new Type[] { typeof(Room), typeof(Floor), typeof(Vector2i) });
        }

        private static bool Prefix(Room room, Floor floor, Vector2i position, ref Color __result)
        {
            if (!PatientCafeteriaRules.IsCafeteriaRoom(room) || floor == null)
            {
                return true;
            }

            if (SettingsManager.Instance.m_viewSettings.m_transparentRooms.m_value)
            {
                __result = new Color(1f, 1f, 1f, 0f);
                return false;
            }

            if (!(room.m_roomPersistentData.m_department == Hospital.Instance.m_activeDepartment))
            {
                __result = UISettings.Instance.LOGISTICS_COLOR_OTHER_DEPARTMENT;
                return false;
            }

            AccessRights paintedRights =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];
            if (paintedRights == AccessRights.PEDESTRIAN)
            {
                paintedRights = AccessRights.STAFF;
            }

            switch (paintedRights)
            {
                case AccessRights.PATIENT:
                    __result = UISettings.Instance.LOGISTICS_COLOR_ALL;
                    break;
                case AccessRights.PATIENT_PROCEDURE:
                    __result = UISettings.Instance.LOGISTICS_COLOR_STAFF;
                    break;
                case AccessRights.BIOHAZARD:
                    // Kept only for backwards compatibility with older saves that may
                    // already contain an orange cafeteria. New orange paint is ignored.
                    __result = UISettings.Instance.LOGISTICS_COLOR_QUARANTINE;
                    break;
                case AccessRights.STAFF:
                case AccessRights.STAFF_ONLY:
                    __result = UISettings.Instance.LOGISTICS_COLOR_CLOSED;
                    break;
                default:
                    __result = UISettings.Instance.LOGISTICS_COLOR_CLOSED;
                    break;
            }

            return false;
        }
    }
}
