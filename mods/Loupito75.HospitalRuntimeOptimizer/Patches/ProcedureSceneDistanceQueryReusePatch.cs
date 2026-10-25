using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using GLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class ProcedureSceneDistanceQueryReusePatch
    {
        [ThreadStatic]
        private static Dictionary<DistanceKey, float> s_distanceCache;

        [ThreadStatic]
        private static int s_scopeDepth;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo updateAll = AccessTools.Method(
                typeof(ProcedureComponent),
                "UpdateAllExaminationsForMedicalCondition",
                new Type[]
                {
                    typeof(MedicalCondition),
                    typeof(int),
                    typeof(bool)
                });

            MethodInfo findClosest = AccessTools.Method(
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
                });

            MethodInfo scopePrefix = AccessTools.Method(
                typeof(ProcedureSceneDistanceQueryReusePatch),
                "ScopePrefix");
            MethodInfo scopeFinalizer = AccessTools.Method(
                typeof(ProcedureSceneDistanceQueryReusePatch),
                "ScopeFinalizer");
            MethodInfo transpiler = AccessTools.Method(
                typeof(ProcedureSceneDistanceQueryReusePatch),
                "Transpiler");

            if (updateAll == null)
            {
                throw new MissingMethodException(
                    typeof(ProcedureComponent).FullName,
                    "UpdateAllExaminationsForMedicalCondition(MedicalCondition, Int32, Boolean)");
            }
            if (findClosest == null)
            {
                throw new MissingMethodException(
                    typeof(MapScriptInterface).FullName,
                    "FindClosestFreeObjectWithTags(Entity, Entity, Vector2i, Room, String[], AccessRights, Boolean, DatabaseEntryRef<GameDBRoomType>[], Boolean)");
            }
            if (scopePrefix == null || scopeFinalizer == null || transpiler == null)
            {
                throw new MissingMethodException(
                    typeof(ProcedureSceneDistanceQueryReusePatch).FullName,
                    "HRO procedure distance patch method");
            }

            harmony.Patch(
                updateAll,
                prefix: new HarmonyMethod(scopePrefix),
                finalizer: new HarmonyMethod(scopeFinalizer));

            harmony.Patch(
                findClosest,
                transpiler: new HarmonyMethod(transpiler));

            Plugin.Log.LogInfo(
                "OptimizeProcedureSceneDistanceQueries installed: identical GridMap.GetDistance results are reused only within one UpdateAllExaminationsForMedicalCondition pass.");
        }

        internal static void Reset()
        {
            s_scopeDepth = 0;
            if (s_distanceCache != null)
            {
                s_distanceCache.Clear();
            }
        }

        private static void ScopePrefix(out bool __state)
        {
            __state = true;

            if (s_scopeDepth == 0)
            {
                if (s_distanceCache == null)
                {
                    s_distanceCache = new Dictionary<DistanceKey, float>();
                }
                else
                {
                    s_distanceCache.Clear();
                }
            }

            s_scopeDepth++;
        }

        private static Exception ScopeFinalizer(Exception __exception, bool __state)
        {
            if (__state && s_scopeDepth > 0)
            {
                s_scopeDepth--;
                if (s_scopeDepth == 0 && s_distanceCache != null)
                {
                    s_distanceCache.Clear();
                }
            }

            return __exception;
        }

        private static float GetDistanceCached(
            GridMap gridMap,
            int floorFrom,
            Vector2i from,
            int floorTo,
            Vector2i to,
            AccessRights accessRights)
        {
            if (s_scopeDepth <= 0)
            {
                return gridMap.GetDistance(
                    floorFrom,
                    from,
                    floorTo,
                    to,
                    accessRights);
            }

            if (s_distanceCache == null)
            {
                s_distanceCache = new Dictionary<DistanceKey, float>();
            }

            DistanceKey key = new DistanceKey(
                gridMap,
                floorFrom,
                from,
                floorTo,
                to,
                accessRights);

            float result;
            if (s_distanceCache.TryGetValue(key, out result))
            {
                return result;
            }

            result = gridMap.GetDistance(
                floorFrom,
                from,
                floorTo,
                to,
                accessRights);

            s_distanceCache.Add(key, result);
            return result;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo getDistance = AccessTools.Method(
                typeof(GridMap),
                "GetDistance",
                new Type[]
                {
                    typeof(int),
                    typeof(Vector2i),
                    typeof(int),
                    typeof(Vector2i),
                    typeof(AccessRights)
                });
            MethodInfo cachedDistance = AccessTools.Method(
                typeof(ProcedureSceneDistanceQueryReusePatch),
                "GetDistanceCached");

            if (getDistance == null)
            {
                throw new MissingMethodException(
                    typeof(GridMap).FullName,
                    "GetDistance(Int32, Vector2i, Int32, Vector2i, AccessRights)");
            }
            if (cachedDistance == null)
            {
                throw new MissingMethodException(
                    typeof(ProcedureSceneDistanceQueryReusePatch).FullName,
                    "GetDistanceCached");
            }

            int matches = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(getDistance))
                {
                    continue;
                }

                // The original instance (GridMap) is already on the stack.
                // Replacing callvirt with this static helper preserves the exact
                // argument stack while allowing reuse only when the HRO scope is active.
                codes[i].opcode = OpCodes.Call;
                codes[i].operand = cachedDistance;
                matches++;
            }

            if (matches != 2)
            {
                throw new InvalidOperationException(
                    "Expected exactly two GridMap.GetDistance calls in MapScriptInterface.FindClosestFreeObjectWithTags(Entity,...), found " +
                    matches + ".");
            }

            return codes;
        }

        private struct DistanceKey : IEquatable<DistanceKey>
        {
            private readonly GridMap _gridMap;
            private readonly int _floorFrom;
            private readonly int _fromX;
            private readonly int _fromY;
            private readonly int _floorTo;
            private readonly int _toX;
            private readonly int _toY;
            private readonly AccessRights _accessRights;

            internal DistanceKey(
                GridMap gridMap,
                int floorFrom,
                Vector2i from,
                int floorTo,
                Vector2i to,
                AccessRights accessRights)
            {
                _gridMap = gridMap;
                _floorFrom = floorFrom;
                _fromX = from.m_x;
                _fromY = from.m_y;
                _floorTo = floorTo;
                _toX = to.m_x;
                _toY = to.m_y;
                _accessRights = accessRights;
            }

            public bool Equals(DistanceKey other)
            {
                return ReferenceEquals(_gridMap, other._gridMap) &&
                    _floorFrom == other._floorFrom &&
                    _fromX == other._fromX &&
                    _fromY == other._fromY &&
                    _floorTo == other._floorTo &&
                    _toX == other._toX &&
                    _toY == other._toY &&
                    _accessRights == other._accessRights;
            }

            public override bool Equals(object obj)
            {
                return obj is DistanceKey && Equals((DistanceKey)obj);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = _gridMap == null ? 0 : _gridMap.GetHashCode();
                    hash = (hash * 397) ^ _floorFrom;
                    hash = (hash * 397) ^ _fromX;
                    hash = (hash * 397) ^ _fromY;
                    hash = (hash * 397) ^ _floorTo;
                    hash = (hash * 397) ^ _toX;
                    hash = (hash * 397) ^ _toY;
                    hash = (hash * 397) ^ (int)_accessRights;
                    return hash;
                }
            }
        }
    }
}
