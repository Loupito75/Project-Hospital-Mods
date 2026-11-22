using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    internal sealed class WaitingRoomTopologyObservation
    {
        internal string TopologyKey;
        internal float FirstObservedGameMinute;
        internal float LastObservedGameMinute;
        internal Room ExampleWorkspace;
        internal int MaximumQueueCount;
    }

    internal static class WaitingRoomTopologyDiagnosticRuntime
    {
        private const float MaximumObservationGapMinutes = 10f;

        private static readonly Dictionary<Room, WaitingRoomTopologyObservation>
            Observations = new Dictionary<Room, WaitingRoomTopologyObservation>();

        private static readonly HashSet<string>
            NotifiedToday = new HashSet<string>(StringComparer.Ordinal);

        private static int CurrentDay = int.MinValue;

        internal static void ObserveSelectionSnapshot(
            BehaviorPatient patient,
            Department department,
            Room waitingRoom,
            bool hasLocalDoctor,
            bool hasFreeLocalDoctor,
            Room availableOutsideWorkspace)
        {
            if (!FlowOptimizerConfig.WaitingRoomTopologyWarning ||
                patient == null ||
                patient.m_entity == null ||
                patient.m_state == null ||
                department == null ||
                waitingRoom == null ||
                DayTime.Instance == null ||
                Database.Instance == null)
            {
                return;
            }

            EnsureCurrentDay();

            HospitalizationComponent hospitalization =
                patient.m_entity.GetComponent<HospitalizationComponent>();
            if ((hospitalization != null && hospitalization.IsHospitalized()) ||
                patient.GetControlMode() != PatientControlMode.AI)
            {
                return;
            }

            if (waitingRoom.m_roomPersistentData == null ||
                waitingRoom.m_roomPersistentData.m_characterQueue == null ||
                waitingRoom.m_roomPersistentData.m_valid != RoomValidity.OK)
            {
                Observations.Remove(waitingRoom);
                return;
            }

            int queueCount =
                waitingRoom.m_roomPersistentData.m_characterQueue.Count;

            if (queueCount <
                    FlowOptimizerConfig.WaitingRoomTopologyQueueThreshold ||
                !waitingRoom.m_roomPersistentData
                    .m_characterQueue.Contains(patient.m_entity) ||
                !hasLocalDoctor ||
                hasFreeLocalDoctor ||
                availableOutsideWorkspace == null)
            {
                Observations.Remove(waitingRoom);
                return;
            }

            float now =
                GetGameMinuteStamp();
            string topologyKey =
                BuildTopologyKey(
                    department,
                    waitingRoom);

            WaitingRoomTopologyObservation observation;
            if (!Observations.TryGetValue(
                    waitingRoom,
                    out observation) ||
                observation == null ||
                observation.TopologyKey != topologyKey ||
                now < observation.LastObservedGameMinute ||
                now - observation.LastObservedGameMinute >
                    MaximumObservationGapMinutes)
            {
                observation =
                    new WaitingRoomTopologyObservation();
                observation.TopologyKey =
                    topologyKey;
                observation.FirstObservedGameMinute =
                    now;
                observation.LastObservedGameMinute =
                    now;
                observation.ExampleWorkspace =
                    availableOutsideWorkspace;
                observation.MaximumQueueCount =
                    queueCount;
                Observations[waitingRoom] =
                    observation;
                return;
            }

            observation.LastObservedGameMinute =
                now;
            observation.ExampleWorkspace =
                availableOutsideWorkspace;
            if (queueCount >
                observation.MaximumQueueCount)
            {
                observation.MaximumQueueCount =
                    queueCount;
            }

            if (now -
                    observation.FirstObservedGameMinute <
                FlowOptimizerConfig
                    .WaitingRoomTopologyPersistenceMinutes ||
                NotifiedToday.Contains(topologyKey))
            {
                return;
            }

            Notify(
                department,
                observation.ExampleWorkspace,
                Math.Max(
                    queueCount,
                    observation.MaximumQueueCount));

            NotifiedToday.Add(topologyKey);

            if (FlowOptimizerConfig.DoctorDiagnosticLogging &&
                Plugin.Log != null)
            {
                Plugin.Log.LogInfo(
                    "[Diagnostic][WaitingRoomTopology] " +
                    "department=" +
                    HfoDiagnostics.DepartmentId(department) +
                    "; waitingRoom=" +
                    waitingRoom.GetEntityID() +
                    "@F" +
                    waitingRoom.GetFloorIndex() +
                    "; queue=" +
                    queueCount +
                    "; workspace=" +
                    observation.ExampleWorkspace.GetEntityID() +
                    "@F" +
                    observation.ExampleWorkspace.GetFloorIndex() +
                    "; topologyKey=" +
                    topologyKey +
                    ".");
            }
        }

        internal static void InvalidateTopology()
        {
            Observations.Clear();
        }

        internal static void Clear()
        {
            Observations.Clear();
            NotifiedToday.Clear();
            CurrentDay = int.MinValue;
        }

        private static void EnsureCurrentDay()
        {
            int day = DayTime.Instance.GetDay();
            if (day == CurrentDay)
            {
                return;
            }

            CurrentDay = day;
            Observations.Clear();
            NotifiedToday.Clear();
        }

        private static float GetGameMinuteStamp()
        {
            return
                DayTime.Instance.GetDay() * 24f * 60f +
                DayTime.Instance.GetDayTimeHours() * 60f;
        }

        private static string BuildTopologyKey(
            Department department,
            Room waitingRoom)
        {
            List<uint> nearbyRoomIds = new List<uint>();

            if (waitingRoom.m_roomPersistentData.m_nearbyRooms != null)
            {
                foreach (EntityIDPointer<Room> pointer in
                    waitingRoom.m_roomPersistentData.m_nearbyRooms)
                {
                    Room room = pointer == null ? null : pointer.GetEntity();
                    if (room != null)
                    {
                        nearbyRoomIds.Add(room.GetEntityID());
                    }
                }
            }

            nearbyRoomIds.Sort();

            StringBuilder builder = new StringBuilder();
            builder.Append(department.GetEntityID());
            builder.Append(':');
            builder.Append(waitingRoom.GetEntityID());
            builder.Append(':');

            for (int i = 0; i < nearbyRoomIds.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }
                builder.Append(nearbyRoomIds[i]);
            }

            return builder.ToString();
        }

        private static void Notify(
            Department department,
            Room workspace,
            int queueCount)
        {
            if (department == null ||
                workspace == null ||
                workspace.m_roomPersistentData == null ||
                workspace.m_roomPersistentData.m_roomType == null ||
                workspace.m_roomPersistentData.m_roomType.Entry == null ||
                NotificationManager.GetInstance() == null ||
                StringTable.GetInstance() == null)
            {
                return;
            }

            string departmentName =
                StringTable.GetInstance().GetLocalizedText(department.GetDepartmentType());
            string workspaceName =
                StringTable.GetInstance().GetLocalizedText(
                    workspace.m_roomPersistentData.m_roomType.Entry);
            Vector2i focus = GetRoomFocusTile(workspace);
            int icon = workspace.m_roomPersistentData.m_roomType.Entry.IconIndex;

            NotificationManager.GetInstance().AddMessage(
                null,
                HfoDatabaseIds.WaitingRoomTopologyNotification,
                departmentName,
                queueCount.ToString(CultureInfo.InvariantCulture),
                workspaceName,
                icon,
                focus.m_x,
                focus.m_y,
                workspace.GetFloorIndex());
        }

        private static Vector2i GetRoomFocusTile(Room room)
        {
            Vector2i bottom = room.m_roomPersistentData.m_positionBottom;
            Vector2i top = room.m_roomPersistentData.m_positionTop;
            int centerX = (bottom.m_x + top.m_x) / 2;
            int centerY = (bottom.m_y + top.m_y) / 2;

            Vector2i best = bottom;
            int bestDistance = int.MaxValue;
            bool found = false;

            for (int x = bottom.m_x; x <= top.m_x; x++)
            {
                for (int y = bottom.m_y; y <= top.m_y; y++)
                {
                    Vector2i tile = new Vector2i(x, y);
                    if (!room.IsPositionInRoom(tile))
                    {
                        continue;
                    }

                    int distance = Math.Abs(x - centerX) + Math.Abs(y - centerY);
                    if (!found ||
                        distance < bestDistance ||
                        (distance == bestDistance &&
                         best == Vector2i.ZERO_VECTOR &&
                         tile != Vector2i.ZERO_VECTOR))
                    {
                        best = tile;
                        bestDistance = distance;
                        found = true;
                    }
                }
            }

            return best;
        }
    }

    [HarmonyPatch(typeof(Floor), nameof(Floor.UpdateWaitingRoomData))]
    internal static class WaitingRoomTopologyRefreshPatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            WaitingRoomTopologyDiagnosticRuntime.InvalidateTopology();
        }
    }
}
