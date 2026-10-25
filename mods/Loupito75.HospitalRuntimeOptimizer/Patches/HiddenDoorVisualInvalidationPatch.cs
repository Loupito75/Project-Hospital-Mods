using System;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class HiddenDoorVisualInvalidationPatch
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo update = AccessTools.Method(
                typeof(Door),
                "Update",
                new Type[]
                {
                    typeof(TileWalls),
                    typeof(int),
                    typeof(float)
                });
            MethodInfo postfix = AccessTools.Method(
                typeof(HiddenDoorVisualInvalidationPatch),
                "Postfix");

            if (update == null)
            {
                throw new MissingMethodException(
                    typeof(Door).FullName,
                    "Update(TileWalls, Int32, Single)");
            }
            if (postfix == null)
            {
                throw new MissingMethodException(
                    typeof(HiddenDoorVisualInvalidationPatch).FullName,
                    "Postfix");
            }

            harmony.Patch(
                update,
                postfix: new HarmonyMethod(postfix));

            Plugin.Log.LogInfo(
                "OptimizeHiddenDoorVisualInvalidation installed: Door.Update remains Vanilla; wall redraw dirties are suppressed only for doors that cannot currently be rendered.");
        }

        private static void Postfix(
            Door __instance,
            ref bool __result)
        {
            if (!__result ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.m_parent == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return;
            }

            int floorIndex = __instance.m_state.m_floorIndex;
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            int currentFloorIndex =
                Hospital.Instance.m_currentFloorIndex;

            if (floorIndex == currentFloorIndex)
            {
                return;
            }

            if (floorIndex > currentFloorIndex)
            {
                // ABOVE_HIDDEN floors are completely hidden. The logical door
                // state still advances in Door.Update; only the redraw signal
                // consumed by Floor.UpdateEntities is suppressed.
                __result = false;
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null ||
                !IsDefinitelyInterior(__instance, floor))
            {
                // BELOW floors still render outdoor-facing walls and doors.
                // Any uncertain/exterior case stays fully Vanilla.
                return;
            }

            __result = false;
        }

        private static bool IsDefinitelyInterior(
            Door door,
            Floor floor)
        {
            TileWalls parent = door.m_parent;
            if (parent == null)
            {
                return false;
            }

            Vector2i sideA = parent.m_position;
            Vector2i sideB;

            if (object.ReferenceEquals(parent.m_doorSE, door))
            {
                sideB = new Vector2i(
                    sideA.m_x - 1,
                    sideA.m_y);
            }
            else if (object.ReferenceEquals(parent.m_doorSW, door))
            {
                sideB = new Vector2i(
                    sideA.m_x,
                    sideA.m_y - 1);
            }
            else
            {
                return false;
            }

            if (!IsInBounds(floor, sideA) ||
                !IsInBounds(floor, sideB))
            {
                return false;
            }

            return !floor.IsOutdoors(sideA) &&
                !floor.IsOutdoors(sideB);
        }

        private static bool IsInBounds(
            Floor floor,
            Vector2i position)
        {
            return position.m_x >= 0 &&
                position.m_y >= 0 &&
                position.m_x < floor.m_size.m_x &&
                position.m_y < floor.m_size.m_y;
        }
    }
}
