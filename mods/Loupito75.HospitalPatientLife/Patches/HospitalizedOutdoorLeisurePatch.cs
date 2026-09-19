using System;
using System.Collections.Generic;
using GLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalizedOutdoorLeisure
    {
        private const string OutsideBenchCompositeId = "COMPOSITE_OBJECT_OUTSIDE_BENCH";
        private const string OutsideBenchPartAId = "OBJECT_OUTSIDE_BENCH_A";
        private const string OutsideBenchPartBId = "OBJECT_OUTSIDE_BENCH_B";

        private sealed class OutdoorCandidate
        {
            internal TileObject Target;
            internal float RouteDistance;
            internal int FloorDifference;
        }

        internal static void TryOverride(
            Entity patient,
            Entity owner,
            ref TileObject result)
        {
            if (patient == null || owner == null)
            {
                return;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            WalkComponent walk = patient.GetComponent<WalkComponent>();

            if (hospitalization == null ||
                !hospitalization.IsHospitalized() ||
                !hospitalization.IsAllowedToWalk() ||
                walk == null ||
                walk.Floor == null)
            {
                return;
            }

            int outdoorRoll;
            if (!HospitalPatientLifeConfig.RollOutdoorLeisure(out outdoorRoll))
            {
                LogDecision(
                    patient,
                    "SKIPPED",
                    outdoorRoll,
                    null,
                    0,
                    0);
                return;
            }

            List<OutdoorCandidate> candidates = CollectOutdoorCandidates(
                patient,
                owner);
            if (candidates.Count == 0)
            {
                LogDecision(
                    patient,
                    "NO_TARGET",
                    outdoorRoll,
                    null,
                    0,
                    0);
                return;
            }

            candidates.Sort(delegate(OutdoorCandidate left, OutdoorCandidate right)
            {
                return left.RouteDistance.CompareTo(right.RouteDistance);
            });

            List<OutdoorCandidate> pool = BuildOutdoorPool(
                candidates,
                HospitalPatientLifeConfig.CandidatePoolSize);
            if (pool.Count == 0)
            {
                return;
            }

            OutdoorCandidate selected =
                pool[UnityEngine.Random.Range(0, pool.Count)];
            if (selected == null || selected.Target == null)
            {
                return;
            }

            result = selected.Target;
            LogDecision(
                patient,
                "SELECTED",
                outdoorRoll,
                selected,
                candidates.Count,
                pool.Count);
        }

        private static List<OutdoorCandidate> BuildOutdoorPool(
            List<OutdoorCandidate> candidates,
            int maximumPoolSize)
        {
            List<OutdoorCandidate> pool = new List<OutdoorCandidate>();
            if (candidates == null || candidates.Count == 0 || maximumPoolSize <= 0)
            {
                return pool;
            }

            Dictionary<int, bool> representedFloors = new Dictionary<int, bool>();

            // Give every reachable floor one representative before proximity fills
            // the remaining slots. This keeps distant rooftop benches eligible without
            // making them as common as nearby ground-level benches.
            for (int i = 0; i < candidates.Count && pool.Count < maximumPoolSize; i++)
            {
                OutdoorCandidate candidate = candidates[i];
                if (candidate == null || candidate.Target == null)
                {
                    continue;
                }

                int floor = candidate.Target.GetFloorIndex();
                if (representedFloors.ContainsKey(floor))
                {
                    continue;
                }

                representedFloors[floor] = true;
                pool.Add(candidate);
            }

            for (int i = 0; i < candidates.Count && pool.Count < maximumPoolSize; i++)
            {
                OutdoorCandidate candidate = candidates[i];
                if (candidate == null || candidate.Target == null || pool.Contains(candidate))
                {
                    continue;
                }

                pool.Add(candidate);
            }

            return pool;
        }

        private static List<OutdoorCandidate> CollectOutdoorCandidates(
            Entity patient,
            Entity owner)
        {
            List<OutdoorCandidate> candidates = new List<OutdoorCandidate>();

            if (patient == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null)
            {
                return candidates;
            }

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            GridMap gridMap = GridMap.GetInstance();
            if (walk == null || walk.Floor == null || gridMap == null)
            {
                return candidates;
            }

            int currentFloorIndex = walk.GetFloorIndex();
            Vector2i currentTile = walk.GetCurrentTile();

            for (int floorIndex = 0;
                floorIndex < Hospital.Instance.m_floors.Count;
                floorIndex++)
            {
                Floor floor = Hospital.Instance.m_floors[floorIndex];
                if (floor == null || floor.m_compositeObjects == null)
                {
                    continue;
                }

                for (int i = 0; i < floor.m_compositeObjects.Count; i++)
                {
                    CompositeObject composite = floor.m_compositeObjects[i];
                    if (!IsOutsideBenchComposite(composite))
                    {
                        continue;
                    }

                    for (int partIndex = 0;
                        partIndex < composite.m_compositeObjectPersistentData.m_parts.Count;
                        partIndex++)
                    {
                        TileObject target =
                            composite.m_compositeObjectPersistentData.m_parts[partIndex].GetEntity();
                        if (!IsOutdoorBench(target) ||
                            target.IsBroken() ||
                            !target.IsValid() ||
                            (target.User != null && target.User != patient) ||
                            (target.Owner != null && target.Owner != owner))
                        {
                            continue;
                        }

                        int targetFloorIndex = target.GetFloorIndex();
                        Floor destinationFloor = GetFloor(targetFloorIndex);
                        if (destinationFloor == null)
                        {
                            continue;
                        }

                        Vector2i objectPosition = target.m_state.m_position;
                        Vector2i targetPosition = target.GetDefaultUseTile();
                        if (!IsPositionAllowedForPatient(destinationFloor, objectPosition) ||
                            !IsPositionAllowedForPatient(destinationFloor, targetPosition))
                        {
                            continue;
                        }

                        float routeDistance = gridMap.GetDistance(
                            currentFloorIndex,
                            currentTile,
                            targetFloorIndex,
                            targetPosition,
                            AccessRights.PATIENT);
                        if (routeDistance < 0f)
                        {
                            continue;
                        }

                        candidates.Add(new OutdoorCandidate
                        {
                            Target = target,
                            RouteDistance = routeDistance,
                            FloorDifference = Math.Abs(
                                targetFloorIndex - currentFloorIndex)
                        });
                    }
                }
            }

            return candidates;
        }

        private static bool IsOutsideBenchComposite(CompositeObject composite)
        {
            if (composite == null ||
                composite.m_compositeObjectPersistentData == null ||
                !composite.m_compositeObjectPersistentData.m_compositeObject.IsValid ||
                composite.m_compositeObjectPersistentData.m_compositeObject.Entry == null ||
                composite.m_compositeObjectPersistentData.m_parts == null)
            {
                return false;
            }

            GameDBCompositeObject compositeType =
                composite.m_compositeObjectPersistentData.m_compositeObject.Entry;
            return !ID.IsNullOrNoID(compositeType.DatabaseID) &&
                compositeType.DatabaseID.ToString() == OutsideBenchCompositeId;
        }

        private static bool IsOutdoorBench(TileObject target)
        {
            string objectId = GetObjectId(target);
            return objectId == OutsideBenchPartAId ||
                objectId == OutsideBenchPartBId;
        }

        private static Floor GetFloor(int floorIndex)
        {
            if (Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return null;
            }

            return Hospital.Instance.m_floors[floorIndex];
        }

        private static bool IsPositionAllowedForPatient(
            Floor floor,
            Vector2i position)
        {
            if (floor == null ||
                floor.m_roomAccessRights == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer == null ||
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights == null ||
                position.m_x < 0 ||
                position.m_y < 0 ||
                position.m_x >= floor.m_size.m_x ||
                position.m_y >= floor.m_size.m_y)
            {
                return false;
            }

            AccessRights roomAccess =
                floor.m_roomAccessRights[position.m_x, position.m_y];
            AccessRights logisticsAccess =
                floor.m_mapPersistentData.m_mapLogisticsLayer.m_accessRights[
                    position.m_x,
                    position.m_y];

            return (int)roomAccess <= (int)AccessRights.PATIENT &&
                (int)logisticsAccess <= (int)AccessRights.PATIENT;
        }

        private static void LogDecision(
            Entity patient,
            string action,
            int roll,
            OutdoorCandidate selected,
            int candidateCount,
            int poolSize)
        {
            if (!HospitalizedPatientTrace.Enabled)
            {
                return;
            }

            string details = string.Empty;
            if (selected != null && selected.Target != null)
            {
                details =
                    " | target=" + GetObjectId(selected.Target) +
                    " | floor=" + selected.Target.GetFloorIndex() +
                    " | floorDifference=" + selected.FloorDifference +
                    " | routeDistance=" + selected.RouteDistance.ToString("0.0");
            }

            HospitalizedPatientTrace.Log(
                HospitalizedPatientTrace.GetName(patient) +
                " | free-time-outdoor=" + action +
                " | chance=" + HospitalPatientLifeConfig.OutdoorLeisureChancePercent +
                " | roll=" + roll +
                " | candidates=" + candidateCount +
                " | pool=" + poolSize +
                details);
        }

        private static string GetObjectId(TileObject target)
        {
            if (target == null ||
                target.m_state == null ||
                target.m_state.m_gameDBObject == null ||
                target.m_state.m_gameDBObject.Entry == null ||
                ID.IsNullOrNoID(target.m_state.m_gameDBObject.Entry.DatabaseID))
            {
                return "<none>";
            }

            return target.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
        }
    }
}
