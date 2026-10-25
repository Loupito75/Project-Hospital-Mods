using System;
using System.Reflection;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class OpeningClosingObjectScanPatch
    {
        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            PatchTarget(
                harmony,
                "OnOpenForPatients",
                "OpenPrefix");
            PatchTarget(
                harmony,
                "OnClosedForDayShift",
                "ClosePrefix");

            Plugin.Log.LogInfo(
                "OptimizeOpeningClosingObjectScan installed: floor opening/closing scans use the six native TileObjects slots without GetAllObjects().ToArray().");
        }

        private static void PatchTarget(
            Harmony harmony,
            string methodName,
            string prefixName)
        {
            MethodInfo target = AccessTools.Method(
                typeof(Floor),
                methodName,
                Type.EmptyTypes);
            MethodInfo prefix = AccessTools.Method(
                typeof(OpeningClosingObjectScanPatch),
                prefixName);

            if (target == null)
            {
                throw new MissingMethodException(
                    typeof(Floor).FullName,
                    methodName + "()");
            }
            if (prefix == null)
            {
                throw new MissingMethodException(
                    typeof(OpeningClosingObjectScanPatch).FullName,
                    prefixName);
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix));
        }

        private static bool OpenPrefix(Floor __instance)
        {
            ProcessFloor(__instance, true);
            return false;
        }

        private static bool ClosePrefix(Floor __instance)
        {
            ProcessFloor(__instance, false);
            return false;
        }

        private static void ProcessFloor(
            Floor floor,
            bool opening)
        {
            bool deferLights =
                OptimizerConfig.OptimizeOpeningClosingLightRefresh;

            if (deferLights)
            {
                OpeningClosingLightRefreshPatch.BeginFloorScan(floor);
            }

            try
            {
                for (int x = 0; x < floor.m_size.m_x; x++)
                {
                    for (int y = 0; y < floor.m_size.m_y; y++)
                    {
                        TileObjects tileObjects =
                            floor.m_tileObjects[x, y];

                        ProcessObject(tileObjects.m_centerObject, opening);
                        ProcessObject(tileObjects.m_attachmentObject, opening);
                        ProcessObject(tileObjects.m_ObjectNW, opening);
                        ProcessObject(tileObjects.m_ObjectNE, opening);
                        ProcessObject(tileObjects.m_ObjectSE, opening);
                        ProcessObject(tileObjects.m_ObjectSW, opening);
                    }
                }

                floor.UpdateRoomLights();
            }
            finally
            {
                if (deferLights)
                {
                    OpeningClosingLightRefreshPatch.EndFloorScan(floor);
                }
            }
        }

        private static void ProcessObject(
            TileObject tileObject,
            bool opening)
        {
            if (tileObject == null)
            {
                return;
            }

            if (opening)
            {
                if (!tileObject.HasTag("queue_next"))
                {
                    return;
                }

                tileObject
                    .GetComponent<AnimatedObjectComponent>()
                    .ForceFrame(1);
                tileObject.SetLightEnabled(true);
                return;
            }

            if (!tileObject.HasTag("tv"))
            {
                return;
            }

            tileObject
                .GetComponent<AnimatedObjectComponent>()
                .ForceFrame(0);
            tileObject.SetLightEnabled(false);
        }
    }
}
