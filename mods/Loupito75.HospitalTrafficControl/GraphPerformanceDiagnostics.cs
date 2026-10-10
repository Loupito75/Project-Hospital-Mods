using System.Diagnostics;
using System.Globalization;
using UnityEngine;

namespace HospitalTrafficControl
{
    // Lightweight, opt-in wall-clock measurements. No gameplay changes.
    internal static class GraphPerformanceDiagnostics
    {
        private const int WindowSeconds = 20;
        private static readonly double MillisecondsPerTick =
            1000d / Stopwatch.Frequency;

        private static long s_windowStart;
        private static int s_sameFloorRoutes;
        private static int s_crossFloorRoutes;
        private static int s_routesOver5Ms;
        private static long s_totalRouteTicks;
        private static long s_peakRouteTicks;
        private static int s_frames;
        private static int s_framesOver33Ms;
        private static int s_framesOver66Ms;
        private static float s_peakFrameMs;

        internal static long BeginSample()
        {
            return TrafficControlConfig.GraphPerformanceDebug
                ? Stopwatch.GetTimestamp()
                : 0L;
        }

        internal static void RecordRoute(long start, bool crossFloor)
        {
            if (start == 0L)
            {
                return;
            }

            long elapsed = Stopwatch.GetTimestamp() - start;
            if (crossFloor)
            {
                s_crossFloorRoutes++;
            }
            else
            {
                s_sameFloorRoutes++;
            }

            s_totalRouteTicks += elapsed;
            if (elapsed > s_peakRouteTicks)
            {
                s_peakRouteTicks = elapsed;
            }
            if (elapsed * MillisecondsPerTick >= 5d)
            {
                s_routesOver5Ms++;
            }
        }

        internal static void RecordRebuild(long start, string mode, int floor, int graphs)
        {
            if (start == 0L)
            {
                return;
            }

            Plugin.Log?.LogInfo(
                "[GraphPerf] REBUILD kind=" + mode +
                " floor=" + floor +
                " graphs=" + graphs +
                " elapsedMs=" + Ms(Stopwatch.GetTimestamp() - start) + ".");
        }

        internal static void UpdateFrame()
        {
            if (!TrafficControlConfig.GraphPerformanceDebug)
            {
                return;
            }

            long now = Stopwatch.GetTimestamp();
            if (s_windowStart == 0L)
            {
                s_windowStart = now;
            }

            float frameMs = Time.unscaledDeltaTime * 1000f;
            s_frames++;
            if (frameMs > s_peakFrameMs)
            {
                s_peakFrameMs = frameMs;
            }
            if (frameMs >= 33f)
            {
                s_framesOver33Ms++;
            }
            if (frameMs >= 66f)
            {
                s_framesOver66Ms++;
            }

            if (now - s_windowStart < Stopwatch.Frequency * WindowSeconds)
            {
                return;
            }

            int routes = s_sameFloorRoutes + s_crossFloorRoutes;
            string average = routes == 0 ? "0.00" : Ms(s_totalRouteTicks / routes);

            Plugin.Log?.LogInfo(
                "[GraphPerf] WINDOW seconds=" + WindowSeconds +
                " frames=" + s_frames +
                " over33ms=" + s_framesOver33Ms +
                " over66ms=" + s_framesOver66Ms +
                " peakFrameMs=" +
                s_peakFrameMs.ToString("F2", CultureInfo.InvariantCulture) +
                " elevatorChecks=" + routes +
                " sameFloor=" + s_sameFloorRoutes +
                " crossFloor=" + s_crossFloorRoutes +
                " avgCheckMs=" + average +
                " peakCheckMs=" + Ms(s_peakRouteTicks) +
                " checksOver5Ms=" + s_routesOver5Ms + ".");

            ResetWindow(now);
        }

        internal static void Reset()
        {
            ResetWindow(0L);
        }

        private static void ResetWindow(long start)
        {
            s_windowStart = start;
            s_sameFloorRoutes = 0;
            s_crossFloorRoutes = 0;
            s_routesOver5Ms = 0;
            s_totalRouteTicks = 0L;
            s_peakRouteTicks = 0L;
            s_frames = 0;
            s_framesOver33Ms = 0;
            s_framesOver66Ms = 0;
            s_peakFrameMs = 0f;
        }

        private static string Ms(long ticks)
        {
            return (ticks * MillisecondsPerTick).ToString(
                "F2", CultureInfo.InvariantCulture);
        }
    }
}
