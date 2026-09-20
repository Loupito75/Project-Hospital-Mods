using System;
using System.Collections;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl
{
    internal sealed class OneWayFailureInfo
    {
        internal int FloorIndex;
        internal Vector2i From;
        internal Vector2i To;
        internal int DeniedChecks;
    }

    internal static class OneWayPathfinderTracker
    {
        private static readonly FieldInfo PathfinderJobField =
            AccessTools.Field(typeof(WalkComponent), "m_pathfinderJob");

        private static readonly Hashtable FailedJobs =
            Hashtable.Synchronized(new Hashtable());

        private static readonly Hashtable SuccessfulDetourJobs =
            Hashtable.Synchronized(new Hashtable());

        private static volatile int s_generation;

        [ThreadStatic]
        private static PathfinderJob s_currentJob;

        [ThreadStatic]
        private static bool s_oneWayDenied;

        [ThreadStatic]
        private static int s_oneWayDeniedCount;

        [ThreadStatic]
        private static bool s_hasFirstDeniedEdge;

        [ThreadStatic]
        private static int s_firstDeniedFloor;

        [ThreadStatic]
        private static Vector2i s_firstDeniedFrom;

        [ThreadStatic]
        private static Vector2i s_firstDeniedTo;

        [ThreadStatic]
        private static int s_jobGeneration;

        internal static PathfinderJob CurrentJob
        {
            get { return s_currentJob; }
        }

        internal static void Begin(PathfinderJob job)
        {
            s_currentJob = job;
            s_oneWayDenied = false;
            s_oneWayDeniedCount = 0;
            s_hasFirstDeniedEdge = false;
            s_firstDeniedFloor = -1;
            s_firstDeniedFrom = Vector2i.ZERO_VECTOR;
            s_firstDeniedTo = Vector2i.ZERO_VECTOR;
            s_jobGeneration = s_generation;
        }

        internal static void RecordDenied(
            Floor floor,
            Vector2i actualFrom,
            Vector2i actualTo)
        {
            if (ReferenceEquals(s_currentJob, null))
            {
                return;
            }

            s_oneWayDenied = true;
            s_oneWayDeniedCount++;

            if (!s_hasFirstDeniedEdge)
            {
                s_hasFirstDeniedEdge = true;
                s_firstDeniedFloor = floor == null ? -1 : floor.m_floorIndex;
                s_firstDeniedFrom = actualFrom;
                s_firstDeniedTo = actualTo;
            }
        }

        internal static void End(PathfinderJob job)
        {
            try
            {
                if (!ReferenceEquals(s_currentJob, job) ||
                    s_jobGeneration != s_generation)
                {
                    return;
                }

                PathfinderResult result = job == null ? null : job.m_result;
                bool noRoute = result == null || result.m_route == null;

                if (s_oneWayDenied)
                {
                    OneWayFailureInfo info = new OneWayFailureInfo
                    {
                        FloorIndex = s_firstDeniedFloor,
                        From = s_firstDeniedFrom,
                        To = s_firstDeniedTo,
                        DeniedChecks = s_oneWayDeniedCount
                    };

                    if (noRoute)
                    {
                        FailedJobs[job] = info;
                    }
                    else if (TrafficControlConfig.PathfindingDebug)
                    {
                        // Worker-thread invariant: do not inspect entities, Unity state,
                        // floors or route tiles here. Only queue primitive path metadata.
                        SuccessfulDetourJobs[job] = info;
                    }
                }
            }
            finally
            {
                s_currentJob = null;
                s_oneWayDenied = false;
                s_oneWayDeniedCount = 0;
                s_hasFirstDeniedEdge = false;
                s_firstDeniedFloor = -1;
                s_firstDeniedFrom = Vector2i.ZERO_VECTOR;
                s_firstDeniedTo = Vector2i.ZERO_VECTOR;
                s_jobGeneration = 0;
            }
        }

        internal static bool ConsumeDeniedNoPath(
            WalkComponent walk,
            out OneWayFailureInfo failure)
        {
            failure = null;

            if (walk == null || ReferenceEquals(PathfinderJobField, null))
            {
                return false;
            }

            PathfinderJob job = PathfinderJobField.GetValue(walk) as PathfinderJob;
            if (job == null || !FailedJobs.ContainsKey(job))
            {
                return false;
            }

            failure = FailedJobs[job] as OneWayFailureInfo;
            FailedJobs.Remove(job);
            return failure != null;
        }

        internal static bool ConsumeSuccessfulDetour(
            PathfinderJob job,
            out OneWayFailureInfo failure)
        {
            failure = null;

            if (job == null || !SuccessfulDetourJobs.ContainsKey(job))
            {
                return false;
            }

            failure = SuccessfulDetourJobs[job] as OneWayFailureInfo;
            SuccessfulDetourJobs.Remove(job);
            return failure != null;
        }

        internal static void Forget(PathfinderJob job)
        {
            if (job != null)
            {
                FailedJobs.Remove(job);
                SuccessfulDetourJobs.Remove(job);
            }
        }

        internal static void Reset()
        {
            s_generation++;
            FailedJobs.Clear();
            SuccessfulDetourJobs.Clear();
        }
    }
}
