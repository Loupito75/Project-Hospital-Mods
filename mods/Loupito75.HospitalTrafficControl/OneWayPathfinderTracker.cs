using System;
using System.Collections.Generic;
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

        private const int MaxPendingJobs = 64;

        private sealed class PendingJobInfo
        {
            internal WeakReference Job;
            internal OneWayFailureInfo Failure;
        }

        private static readonly object PendingJobsLock = new object();
        private static readonly List<PendingJobInfo> FailedJobs =
            new List<PendingJobInfo>();
        private static readonly List<PendingJobInfo> SuccessfulDetourJobs =
            new List<PendingJobInfo>();

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
                        StorePendingJob(FailedJobs, job, info);
                    }
                    else if (TrafficControlConfig.PathfindingDebug)
                    {
                        // Worker-thread invariant: do not inspect entities, Unity state,
                        // floors or route tiles here. Only queue primitive path metadata.
                        StorePendingJob(SuccessfulDetourJobs, job, info);
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
            return TryTakePendingJob(FailedJobs, job, out failure);
        }

        internal static bool ConsumeSuccessfulDetour(
            PathfinderJob job,
            out OneWayFailureInfo failure)
        {
            return TryTakePendingJob(
                SuccessfulDetourJobs,
                job,
                out failure);
        }

        internal static void Forget(PathfinderJob job)
        {
            if (job == null)
            {
                return;
            }

            lock (PendingJobsLock)
            {
                RemovePendingJobNoLock(FailedJobs, job);
                RemovePendingJobNoLock(SuccessfulDetourJobs, job);
            }
        }

        internal static void Reset()
        {
            s_generation++;

            lock (PendingJobsLock)
            {
                FailedJobs.Clear();
                SuccessfulDetourJobs.Clear();
            }
        }

        private static void StorePendingJob(
            List<PendingJobInfo> pending,
            PathfinderJob job,
            OneWayFailureInfo failure)
        {
            if (job == null || failure == null)
            {
                return;
            }

            lock (PendingJobsLock)
            {
                RemovePendingJobNoLock(pending, job);
                RemoveCollectedJobsNoLock(pending);

                while (pending.Count >= MaxPendingJobs)
                {
                    pending.RemoveAt(0);
                }

                pending.Add(new PendingJobInfo
                {
                    Job = new WeakReference(job),
                    Failure = failure
                });
            }
        }

        private static bool TryTakePendingJob(
            List<PendingJobInfo> pending,
            PathfinderJob job,
            out OneWayFailureInfo failure)
        {
            failure = null;
            if (job == null)
            {
                return false;
            }

            lock (PendingJobsLock)
            {
                for (int i = pending.Count - 1; i >= 0; i--)
                {
                    PendingJobInfo record = pending[i];
                    PathfinderJob recordedJob =
                        record.Job == null ? null : record.Job.Target as PathfinderJob;

                    if (recordedJob == null)
                    {
                        pending.RemoveAt(i);
                        continue;
                    }

                    if (ReferenceEquals(recordedJob, job))
                    {
                        failure = record.Failure;
                        pending.RemoveAt(i);
                        return failure != null;
                    }
                }
            }

            return false;
        }

        private static void RemovePendingJobNoLock(
            List<PendingJobInfo> pending,
            PathfinderJob job)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingJobInfo record = pending[i];
                PathfinderJob recordedJob =
                    record.Job == null ? null : record.Job.Target as PathfinderJob;

                if (recordedJob == null || ReferenceEquals(recordedJob, job))
                {
                    pending.RemoveAt(i);
                }
            }
        }

        private static void RemoveCollectedJobsNoLock(
            List<PendingJobInfo> pending)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                PendingJobInfo record = pending[i];
                if (record.Job == null ||
                    !record.Job.IsAlive ||
                    record.Job.Target == null)
                {
                    pending.RemoveAt(i);
                }
            }
        }
    }
}
