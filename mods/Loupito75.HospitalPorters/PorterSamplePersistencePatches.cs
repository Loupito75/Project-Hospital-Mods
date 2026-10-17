using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal static class PorterSamplePersistence
    {
        private const string ScriptName = "GlobalScript";
        private const string StateHeader =
            "HPO_PORTER_SAMPLE_TRANSPORT_V2";
        private const string NativeLabStateHeader =
            "HPO_NATIVE_LAB_SAMPLE_BUNDLES_V1";
        private const string RoomWaitRetryStateHeader =
            "HPO_SAMPLE_ROOM_WAIT_RETRY_V1";
        private const string RoomWaitRetryCooldownStateHeader =
            "HPO_SAMPLE_ROOM_WAIT_COOLDOWN_V1";
        private const string PreBlockedRoomWaitChoiceHeader =
            "HPO_SAMPLE_PREBLOCK_WAIT_CHOICE_V1";
        private const string SampleFallbackStateHeader =
            "HPO_SAMPLE_FALLBACK_STATE_V1";
        private const string SamplePorterCapacityHistoryHeader =
            "HPO_SAMPLE_PORTER_CAPACITY_HISTORY_V1";
        private const string SamplePorterCapacityReportedHeader =
            "HPO_SAMPLE_PORTER_CAPACITY_REPORTED_V1";
        private const string PatientFallbackStateHeader =
            "HPO_PATIENT_FALLBACK_STATE_V1";
        private const string PatientVehicleContinuationHeader =
            "HPO_PATIENT_VEHICLE_CONTINUATION_V1";
        private const string PatientStationReturnHeader =
            "HPO_PATIENT_STATION_RETURN_V2";
        private const string PatientStationReturnLegacyHeader =
            "HPO_PATIENT_STATION_RETURN_V1";
        private const string PatientPorterCapacityHistoryHeader =
            "HPO_PATIENT_PORTER_CAPACITY_HISTORY_V1";
        private const string PatientPorterCapacityReportedHeader =
            "HPO_PATIENT_PORTER_CAPACITY_REPORTED_V1";

        internal static void AppendToSave(GameSave save)
        {
            if (save == null)
            {
                return;
            }

            List<KeyValuePair<Entity, PorterSampleTransportJob>>
                jobs =
                    PorterSampleTransportRuntime
                        .GetActiveJobsSnapshot();
            List<KeyValuePair<Entity, LabProcedure>>
                nativeLabBundles =
                    PorterSampleTransportRuntime
                        .GetNativeLabSampleBundlesSnapshot();
            List<KeyValuePair<LabProcedure, int>>
                roomWaitRetries =
                    PorterSampleTransportRuntime
                        .GetRoomWaitRetrySnapshot();
            List<KeyValuePair<LabProcedure, int>>
                roomWaitRetryCooldowns =
                    PorterSampleTransportRuntime
                        .GetRoomWaitRetryCooldownSnapshot();

            List<KeyValuePair<LabProcedure, bool>>
                preBlockedChoices =
                    PorterSampleTransportRuntime
                        .GetPreBlockedRoomWaitDecisionsSnapshot();
            List<PorterSampleFallbackSnapshot>
                fallbackStates =
                    PorterSampleTransportRuntime
                        .GetFallbackStateSnapshot();
            List<KeyValuePair<Department, int>>
                capacityFallbackHistory =
                    PorterSampleTransportRuntime
                        .GetSamplePorterCapacityFallbackHistorySnapshot();
            List<Department>
                capacityNotifications =
                    PorterSampleTransportRuntime
                        .GetSamplePorterCapacityNotificationsSnapshot();
            List<PorterPatientStationReturnSnapshot>
                patientStationReturns =
                    PorterPatientContinuationRuntime
                        .GetStationReturnSnapshot();
            List<PorterPatientFallbackSnapshot>
                patientFallbackStates =
                    StretcherDestinationPatch
                        .GetPatientFallbackStateSnapshot();
            List<PorterPatientVehicleContinuationSnapshot>
                patientVehicleContinuations =
                    PorterPatientContinuationRuntime
                        .GetActiveVehicleContinuationSnapshot();
            List<KeyValuePair<Department, int>>
                patientCapacityFallbackHistory =
                    StretcherDestinationPatch
                        .GetPatientPorterCapacityFallbackHistorySnapshot();
            List<Department>
                patientCapacityNotifications =
                    StretcherDestinationPatch
                        .GetPatientPorterCapacityNotificationsSnapshot();

            bool hasPreBlockedChoices =
                preBlockedChoices != null &&
                preBlockedChoices.Count > 0;
            bool hasFallbackStates =
                fallbackStates != null &&
                fallbackStates.Count > 0;
            bool hasCapacityFallbackHistory =
                capacityFallbackHistory != null &&
                capacityFallbackHistory.Count > 0;
            bool hasCapacityNotifications =
                capacityNotifications != null &&
                capacityNotifications.Count > 0;
            bool hasPatientStationReturns =
                patientStationReturns != null &&
                patientStationReturns.Count > 0;
            bool hasPatientFallbackStates =
                patientFallbackStates != null &&
                patientFallbackStates.Count > 0;
            bool hasPatientVehicleContinuations =
                patientVehicleContinuations != null &&
                patientVehicleContinuations.Count > 0;
            bool hasPatientCapacityFallbackHistory =
                patientCapacityFallbackHistory != null &&
                patientCapacityFallbackHistory.Count > 0;
            bool hasPatientCapacityNotifications =
                patientCapacityNotifications != null &&
                patientCapacityNotifications.Count > 0;
            bool hasJobs =
                jobs != null &&
                jobs.Count > 0;
            bool hasNativeLabBundles =
                nativeLabBundles != null &&
                nativeLabBundles.Count > 0;
            bool hasRoomWaitRetries =
                roomWaitRetries != null &&
                roomWaitRetries.Count > 0;
            bool hasRoomWaitRetryCooldowns =
                roomWaitRetryCooldowns != null &&
                roomWaitRetryCooldowns.Count > 0;
            if (!hasJobs &&
                !hasNativeLabBundles &&
                !hasRoomWaitRetries &&
                !hasRoomWaitRetryCooldowns &&
                !hasPreBlockedChoices &&
                !hasFallbackStates &&
                !hasCapacityFallbackHistory &&
                !hasCapacityNotifications &&
                !hasPatientFallbackStates &&
                !hasPatientVehicleContinuations &&
                !hasPatientStationReturns &&
                !hasPatientCapacityFallbackHistory &&
                !hasPatientCapacityNotifications)
            {
                return;
            }

            if (save.m_entityListEvents == null)
            {
                save.m_entityListEvents =
                    new EntityListSave();
            }

            if (hasPatientStationReturns)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializePatientStationReturns(patientStationReturns);
                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));
            }

            if (hasPatientVehicleContinuations)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializePatientVehicleContinuations(
                        patientVehicleContinuations);
                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));

                PorterDiagnostics.Log(
                    "save: activePatientVehicleContinuations=" +
                    patientVehicleContinuations.Count +
                    ".");
            }

            if (hasJobs)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state = Serialize(jobs);

                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));

                PorterDiagnostics.Log(
                    "save: activeSampleJobs=" +
                    jobs.Count +
                    ".");
            }

            if (hasNativeLabBundles)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializeNativeLabBundles(
                        nativeLabBundles);

                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));

                PorterDiagnostics.Log(
                    "save: nativeLabSampleBundles=" +
                    nativeLabBundles.Count +
                    ".");
            }

            if (hasRoomWaitRetries)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializeRoomWaitRetries(
                        roomWaitRetries);

                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));

                PorterDiagnostics.Log(
                    "save: sampleRoomWaitRetries=" +
                    roomWaitRetries.Count +
                    ".");
            }

            if (hasRoomWaitRetryCooldowns)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializeRoomWaitRetryCooldowns(
                        roomWaitRetryCooldowns);

                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));

                PorterDiagnostics.Log(
                    "save: sampleRoomWaitRetryCooldowns=" +
                    roomWaitRetryCooldowns.Count +
                    ".");
            }
            if (hasPreBlockedChoices)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializePreBlockedRoomWaitChoices(preBlockedChoices);
                save.m_entityListEvents.m_entities.Add(new EntitySave(data));
                PorterDiagnostics.Log("save: samplePreBlockedWaitChoices=" +
                    preBlockedChoices.Count + ".");
            }
            if (hasFallbackStates)
            {
                GlobalScriptPersistentData data = new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state = SerializeSampleFallbackStates(fallbackStates);
                save.m_entityListEvents.m_entities.Add(new EntitySave(data));
                PorterDiagnostics.Log("save: sampleFallbackStates=" +
                    fallbackStates.Count + ".");
            }
            if (hasCapacityFallbackHistory)
            {
                GlobalScriptPersistentData data = new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state = SerializeSamplePorterCapacityHistory(capacityFallbackHistory);
                save.m_entityListEvents.m_entities.Add(new EntitySave(data));
                PorterDiagnostics.Log("save: samplePorterCapacityHistory=" +
                    capacityFallbackHistory.Count + ".");
            }

            if (hasCapacityNotifications)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializeSamplePorterCapacityNotifications(
                        capacityNotifications);
                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));
                PorterDiagnostics.Log(
                    "save: samplePorterCapacityReported=" +
                    capacityNotifications.Count + ".");
            }

            if (hasPatientFallbackStates)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializePatientFallbackStates(
                        patientFallbackStates);
                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));
                PorterDiagnostics.Log(
                    "save: patientFallbackStates=" +
                    patientFallbackStates.Count + ".");
            }

            if (hasPatientCapacityFallbackHistory)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializePatientPorterCapacityHistory(
                        patientCapacityFallbackHistory);
                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));
                PorterDiagnostics.Log(
                    "save: patientPorterCapacityHistory=" +
                    patientCapacityFallbackHistory.Count + ".");
            }

            if (hasPatientCapacityNotifications)
            {
                GlobalScriptPersistentData data =
                    new GlobalScriptPersistentData();
                data.m_scriptName = ScriptName;
                data.m_state =
                    SerializePatientPorterCapacityNotifications(
                        patientCapacityNotifications);
                save.m_entityListEvents.m_entities.Add(
                    new EntitySave(data));
                PorterDiagnostics.Log(
                    "save: patientPorterCapacityReported=" +
                    patientCapacityNotifications.Count + ".");
            }
        }

        internal static bool TryRestoreMarker(
            EntitySave entitySave)
        {
            GlobalScriptPersistentData data =
                entitySave == null
                    ? null
                    : entitySave.m_persistentData
                        as GlobalScriptPersistentData;
            if (data == null ||
                data.m_scriptName != ScriptName ||
                string.IsNullOrEmpty(data.m_state))
            {
                return false;
            }

            if (data.m_state.StartsWith(
                    StateHeader,
                    StringComparison.Ordinal))
            {
                Restore(data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    NativeLabStateHeader,
                    StringComparison.Ordinal))
            {
                RestoreNativeLabBundles(
                    data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    RoomWaitRetryStateHeader,
                    StringComparison.Ordinal))
            {
                RestoreRoomWaitRetries(
                    data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    RoomWaitRetryCooldownStateHeader,
                    StringComparison.Ordinal))
            {
                RestoreRoomWaitRetryCooldowns(
                    data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    PreBlockedRoomWaitChoiceHeader,
                    StringComparison.Ordinal))
            {
                RestorePreBlockedRoomWaitChoices(data.m_state);
                return true;
            }
            if (data.m_state.StartsWith(
                    SampleFallbackStateHeader,
                    StringComparison.Ordinal))
            {
                RestoreSampleFallbackStates(data.m_state);
                return true;
            }
            if (data.m_state.StartsWith(
                    SamplePorterCapacityHistoryHeader,
                    StringComparison.Ordinal))
            {
                RestoreSamplePorterCapacityHistory(data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    SamplePorterCapacityReportedHeader,
                    StringComparison.Ordinal))
            {
                RestoreSamplePorterCapacityNotifications(
                    data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    PatientStationReturnHeader,
                    StringComparison.Ordinal) ||
                data.m_state.StartsWith(
                    PatientStationReturnLegacyHeader,
                    StringComparison.Ordinal))
            {
                RestorePatientStationReturns(data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    PatientVehicleContinuationHeader,
                    StringComparison.Ordinal))
            {
                RestorePatientVehicleContinuations(
                    data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    PatientFallbackStateHeader,
                    StringComparison.Ordinal))
            {
                RestorePatientFallbackStates(
                    data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    PatientPorterCapacityHistoryHeader,
                    StringComparison.Ordinal))
            {
                RestorePatientPorterCapacityHistory(
                    data.m_state);
                return true;
            }

            if (data.m_state.StartsWith(
                    PatientPorterCapacityReportedHeader,
                    StringComparison.Ordinal))
            {
                RestorePatientPorterCapacityNotifications(
                    data.m_state);
                return true;
            }

            return false;
        }

        private static string SerializeSampleFallbackStates(
            List<PorterSampleFallbackSnapshot> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(SampleFallbackStateHeader);
            builder.Append('\n');
            for (int i = 0; i < entries.Count; i++)
            {
                PorterSampleFallbackSnapshot entry = entries[i];
                if (entry == null || entry.Procedure == null) continue;
                builder.Append(entry.Procedure.GetEntityID());
                builder.Append('|');
                builder.Append(entry.RetryCount);
                builder.Append('|');
                builder.Append(entry.NextRetryGameMinute);
                builder.Append('|');
                builder.Append(entry.Released ? 1 : 0);
                builder.Append('\n');
            }
            return builder.ToString();
        }

        private static void RestoreSampleFallbackStates(string serialized)
        {
            string[] lines = serialized.Split(new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || lines[0] != SampleFallbackStateHeader) return;
            EntityManager manager = EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(new char[] { '|' }, StringSplitOptions.None);
                uint procedureId; int retryCount; int nextRetryGameMinute; int released;
                if (parts.Length != 4 ||
                    !uint.TryParse(parts[0], out procedureId) ||
                    !int.TryParse(parts[1], out retryCount) ||
                    !int.TryParse(parts[2], out nextRetryGameMinute) ||
                    !int.TryParse(parts[3], out released) ||
                    (released != 0 && released != 1)) continue;
                LabProcedure procedure = manager.GetEntity(procedureId) as LabProcedure;
                if (PorterSampleTransportRuntime.RestoreFallbackState(
                    procedure, retryCount, nextRetryGameMinute, released == 1)) restored++;
            }
            PorterDiagnostics.Log("load: restoredSampleFallbackStates=" + restored + ".");
        }

        private static string SerializeSamplePorterCapacityHistory(
            List<KeyValuePair<Department, int>> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(SamplePorterCapacityHistoryHeader);
            builder.Append('\n');
            for (int i = 0; i < entries.Count; i++)
            {
                Department department = entries[i].Key;
                if (department == null) continue;
                builder.Append(department.GetEntityID());
                builder.Append('|');
                builder.Append(entries[i].Value);
                builder.Append('\n');
            }
            return builder.ToString();
        }

        private static void RestoreSamplePorterCapacityHistory(string serialized)
        {
            string[] lines = serialized.Split(new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 || lines[0] != SamplePorterCapacityHistoryHeader) return;
            EntityManager manager = EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(new char[] { '|' }, StringSplitOptions.None);
                uint departmentId; int gameMinute;
                if (parts.Length != 2 ||
                    !uint.TryParse(parts[0], out departmentId) ||
                    !int.TryParse(parts[1], out gameMinute)) continue;
                Department department = manager.GetEntity(departmentId) as Department;
                if (PorterSampleTransportRuntime.RestoreSamplePorterCapacityFallbackEvent(
                    department, gameMinute)) restored++;
            }
            PorterDiagnostics.Log("load: restoredSamplePorterCapacityHistory=" + restored + ".");
        }

        private static string SerializeSamplePorterCapacityNotifications(
            List<Department> departments)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(SamplePorterCapacityReportedHeader);
            builder.Append('\n');

            for (int i = 0; i < departments.Count; i++)
            {
                Department department = departments[i];
                if (department == null)
                {
                    continue;
                }

                builder.Append(department.GetEntityID());
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestoreSamplePorterCapacityNotifications(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] !=
                    SamplePorterCapacityReportedHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                uint departmentId;
                if (!uint.TryParse(
                        lines[i],
                        out departmentId))
                {
                    continue;
                }

                Department department =
                    manager.GetEntity(departmentId)
                        as Department;
                if (PorterSampleTransportRuntime
                    .RestoreSamplePorterCapacityNotification(
                        department))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredSamplePorterCapacityReported=" +
                restored + ".");
        }

        private static string SerializePatientStationReturns(
            List<PorterPatientStationReturnSnapshot> returns)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(PatientStationReturnHeader);
            builder.Append('\n');
            for (int i = 0; i < returns.Count; i++)
            {
                PorterPatientStationReturnSnapshot snapshot =
                    returns[i];
                if (snapshot == null || snapshot.Porter == null)
                {
                    continue;
                }
                builder.Append(snapshot.Porter.GetEntityID());
                builder.Append('|');
                builder.Append(snapshot.ContinuationDeclined ? 1 : 0);
                builder.Append('\n');
            }
            return builder.ToString();
        }

        private static void RestorePatientStationReturns(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                (lines[0] != PatientStationReturnHeader &&
                 lines[0] != PatientStationReturnLegacyHeader))
            {
                return;
            }

            EntityManager manager = EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
                uint porterId;
                int declined = 0;
                if ((parts.Length != 1 && parts.Length != 2) ||
                    !uint.TryParse(parts[0], out porterId) ||
                    (parts.Length == 2 &&
                     (!int.TryParse(parts[1], out declined) ||
                      (declined != 0 && declined != 1))))
                {
                    continue;
                }

                if (PorterPatientContinuationRuntime.RestoreStationReturn(
                        manager.GetEntity(porterId),
                        declined == 1))
                {
                    restored++;
                }
            }
            PorterDiagnostics.Log(
                "load: restoredPatientStationReturns=" +
                restored + ".");
        }

        private static string SerializePatientVehicleContinuations(
            List<PorterPatientVehicleContinuationSnapshot> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(PatientVehicleContinuationHeader);
            builder.Append('\n');

            for (int i = 0; i < entries.Count; i++)
            {
                PorterPatientVehicleContinuationSnapshot entry =
                    entries[i];
                if (entry == null ||
                    entry.Porter == null ||
                    entry.Patient == null ||
                    entry.Destination == null)
                {
                    continue;
                }

                builder.Append(entry.Porter.GetEntityID());
                builder.Append('|');
                builder.Append(entry.Patient.GetEntityID());
                builder.Append('|');
                builder.Append(entry.Destination.GetEntityID());
                builder.Append('|');
                builder.Append((int)entry.LyingState);
                builder.Append('|');
                builder.Append((int)entry.StretcherUse);
                builder.Append('|');
                builder.Append((int)entry.BedReservation);
                builder.Append('|');
                builder.Append((int)entry.MovementType);
                builder.Append('|');
                builder.Append((int)entry.RequestedVehicle);
                builder.Append('|');
                builder.Append(entry.AwaitingNativeReplay ? 1 : 0);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestorePatientVehicleContinuations(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] != PatientVehicleContinuationHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
                uint porterId;
                uint patientId;
                uint destinationId;
                int lyingState;
                int stretcherUse;
                int bedReservation;
                int movementType;
                int requestedVehicle;
                int awaitingNativeReplay;
                if (parts.Length != 9 ||
                    !uint.TryParse(parts[0], out porterId) ||
                    !uint.TryParse(parts[1], out patientId) ||
                    !uint.TryParse(parts[2], out destinationId) ||
                    !int.TryParse(parts[3], out lyingState) ||
                    !int.TryParse(parts[4], out stretcherUse) ||
                    !int.TryParse(parts[5], out bedReservation) ||
                    !int.TryParse(parts[6], out movementType) ||
                    !int.TryParse(parts[7], out requestedVehicle) ||
                    !int.TryParse(parts[8], out awaitingNativeReplay) ||
                    (awaitingNativeReplay != 0 &&
                     awaitingNativeReplay != 1) ||
                    !Enum.IsDefined(
                        typeof(LyingState), lyingState) ||
                    !Enum.IsDefined(
                        typeof(StretcherUse), stretcherUse) ||
                    !Enum.IsDefined(
                        typeof(BedReservation), bedReservation) ||
                    !Enum.IsDefined(
                        typeof(MovementType), movementType) ||
                    !Enum.IsDefined(
                        typeof(Vehicle), requestedVehicle))
                {
                    continue;
                }

                Entity porter = manager.GetEntity(porterId);
                Entity patient = manager.GetEntity(patientId);
                TileObject destination =
                    manager.GetEntity(destinationId)
                        as TileObject;
                if (PorterPatientContinuationRuntime
                    .RestoreActiveVehicleContinuation(
                        porter,
                        patient,
                        destination,
                        (LyingState)lyingState,
                        (StretcherUse)stretcherUse,
                        (BedReservation)bedReservation,
                        (MovementType)movementType,
                        (Vehicle)requestedVehicle,
                        awaitingNativeReplay == 1))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredPatientVehicleContinuations=" +
                restored + ".");
        }

        private static string SerializePatientFallbackStates(
            List<PorterPatientFallbackSnapshot> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(PatientFallbackStateHeader);
            builder.Append('\n');

            for (int i = 0; i < entries.Count; i++)
            {
                PorterPatientFallbackSnapshot entry =
                    entries[i];
                if (entry == null ||
                    entry.Patient == null ||
                    entry.Destination == null)
                {
                    continue;
                }

                builder.Append(entry.Patient.GetEntityID());
                builder.Append('|');
                builder.Append(entry.Destination.GetEntityID());
                builder.Append('|');
                builder.Append(entry.RetryCount);
                builder.Append('|');
                builder.Append(entry.NextRetryGameMinute);
                builder.Append('|');
                builder.Append(entry.Released ? 1 : 0);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestorePatientFallbackStates(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] != PatientFallbackStateHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
                uint patientId;
                uint destinationId;
                int retryCount;
                int nextRetryGameMinute;
                int released;
                if (parts.Length != 5 ||
                    !uint.TryParse(parts[0], out patientId) ||
                    !uint.TryParse(parts[1], out destinationId) ||
                    !int.TryParse(parts[2], out retryCount) ||
                    !int.TryParse(parts[3], out nextRetryGameMinute) ||
                    !int.TryParse(parts[4], out released) ||
                    (released != 0 && released != 1))
                {
                    continue;
                }

                Entity patient =
                    manager.GetEntity(patientId);
                TileObject destination =
                    manager.GetEntity(destinationId)
                        as TileObject;
                if (StretcherDestinationPatch
                    .RestorePatientFallbackState(
                        patient,
                        destination,
                        retryCount,
                        nextRetryGameMinute,
                        released == 1))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredPatientFallbackStates=" +
                restored + ".");
        }

        private static string SerializePatientPorterCapacityHistory(
            List<KeyValuePair<Department, int>> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(
                PatientPorterCapacityHistoryHeader);
            builder.Append('\n');

            for (int i = 0; i < entries.Count; i++)
            {
                Department department = entries[i].Key;
                if (department == null)
                {
                    continue;
                }

                builder.Append(department.GetEntityID());
                builder.Append('|');
                builder.Append(entries[i].Value);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestorePatientPorterCapacityHistory(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] !=
                    PatientPorterCapacityHistoryHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
                uint departmentId;
                int gameMinute;
                if (parts.Length != 2 ||
                    !uint.TryParse(
                        parts[0],
                        out departmentId) ||
                    !int.TryParse(
                        parts[1],
                        out gameMinute))
                {
                    continue;
                }

                Department department =
                    manager.GetEntity(departmentId)
                        as Department;
                if (StretcherDestinationPatch
                    .RestorePatientPorterCapacityFallbackEvent(
                        department,
                        gameMinute))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredPatientPorterCapacityHistory=" +
                restored + ".");
        }

        private static string SerializePatientPorterCapacityNotifications(
            List<Department> departments)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(
                PatientPorterCapacityReportedHeader);
            builder.Append('\n');

            for (int i = 0; i < departments.Count; i++)
            {
                Department department = departments[i];
                if (department == null)
                {
                    continue;
                }

                builder.Append(department.GetEntityID());
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestorePatientPorterCapacityNotifications(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] !=
                    PatientPorterCapacityReportedHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                uint departmentId;
                if (!uint.TryParse(
                        lines[i],
                        out departmentId))
                {
                    continue;
                }

                Department department =
                    manager.GetEntity(departmentId)
                        as Department;
                if (StretcherDestinationPatch
                    .RestorePatientPorterCapacityNotification(
                        department))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredPatientPorterCapacityReported=" +
                restored + ".");
        }

        private static string SerializePreBlockedRoomWaitChoices(
            List<KeyValuePair<LabProcedure, bool>> entries)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(PreBlockedRoomWaitChoiceHeader);
            builder.Append('\n');
            for (int i = 0; i < entries.Count; i++)
            {
                LabProcedure procedure = entries[i].Key;
                if (procedure == null)
                {
                    continue;
                }
                builder.Append(procedure.GetEntityID());
                builder.Append('|');
                builder.Append(entries[i].Value ? 1 : 0);
                builder.Append('\n');
            }
            return builder.ToString();
        }

        private static void RestorePreBlockedRoomWaitChoices(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] != PreBlockedRoomWaitChoiceHeader)
            {
                return;
            }
            EntityManager manager = EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(
                    new char[] { '|' }, StringSplitOptions.None);
                if (parts.Length != 2 ||
                    (parts[1] != "0" && parts[1] != "1"))
                {
                    continue;
                }
                uint procedureId;
                if (!uint.TryParse(parts[0], out procedureId))
                {
                    continue;
                }
                LabProcedure procedure =
                    manager.GetEntity(procedureId) as LabProcedure;
                if (PorterSampleTransportRuntime
                    .RestorePreBlockedRoomWaitDecision(
                        procedure, parts[1] == "1"))
                {
                    restored++;
                }
            }
            PorterDiagnostics.Log(
                "load: restoredSamplePreBlockedWaitChoices=" +
                restored + ".");
        }

        private static string SerializeRoomWaitRetryCooldowns(
            List<KeyValuePair<LabProcedure, int>> entries)
        {
            StringBuilder builder =
                new StringBuilder();
            builder.Append(
                RoomWaitRetryCooldownStateHeader);
            builder.Append('\n');

            for (int i = 0; i < entries.Count; i++)
            {
                LabProcedure procedure = entries[i].Key;
                int availableMinute = entries[i].Value;
                if (procedure == null ||
                    availableMinute <= 0)
                {
                    continue;
                }

                builder.Append(procedure.GetEntityID());
                builder.Append('|');
                builder.Append(availableMinute);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestoreRoomWaitRetryCooldowns(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] !=
                    RoomWaitRetryCooldownStateHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    continue;
                }

                uint procedureId;
                int availableMinute;
                if (!uint.TryParse(parts[0], out procedureId) ||
                    !int.TryParse(parts[1], out availableMinute))
                {
                    continue;
                }

                LabProcedure procedure =
                    manager.GetEntity(procedureId)
                        as LabProcedure;
                if (PorterSampleTransportRuntime
                    .RestoreRoomWaitRetryCooldown(
                        procedure,
                        availableMinute))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredSampleRoomWaitRetryCooldowns=" +
                restored +
                ".");
        }

        private static string SerializeRoomWaitRetries(
            List<KeyValuePair<LabProcedure, int>> entries)
        {
            StringBuilder builder =
                new StringBuilder();
            builder.Append(RoomWaitRetryStateHeader);
            builder.Append('\n');

            for (int i = 0; i < entries.Count; i++)
            {
                LabProcedure procedure = entries[i].Key;
                int stage = entries[i].Value;
                if (procedure == null ||
                    (stage != 1 && stage != 2))
                {
                    continue;
                }

                builder.Append(procedure.GetEntityID());
                builder.Append('|');
                builder.Append(stage);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestoreRoomWaitRetries(
            string serialized)
        {
            string[] lines = serialized.Split(
                new char[] { '\n' },
                StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] != RoomWaitRetryStateHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;
            for (int i = 1; i < lines.Length; i++)
            {
                string[] parts = lines[i].Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    continue;
                }

                uint procedureId;
                int stage;
                if (!uint.TryParse(parts[0], out procedureId) ||
                    !int.TryParse(parts[1], out stage))
                {
                    continue;
                }

                LabProcedure procedure =
                    manager.GetEntity(procedureId) as LabProcedure;
                if (PorterSampleTransportRuntime
                    .RestoreRoomWaitRetry(procedure, stage))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredSampleRoomWaitRetries=" +
                restored +
                ".");
        }

        private static string Serialize(
            List<KeyValuePair<Entity, PorterSampleTransportJob>>
                jobs)
        {
            StringBuilder builder =
                new StringBuilder();
            builder.Append(StateHeader);
            builder.Append('\n');

            for (int i = 0; i < jobs.Count; i++)
            {
                Entity porter = jobs[i].Key;
                PorterSampleTransportJob job =
                    jobs[i].Value;
                if (porter == null ||
                    job == null ||
                    job.StatLab == null)
                {
                    continue;
                }

                builder.Append(
                    porter.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.Cart == null
                        ? 0u
                        : job.Cart.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.Procedure == null
                        ? 0u
                        : job.Procedure.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.StatLab.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.CartHomeRoom == null
                        ? 0u
                        : job.CartHomeRoom.GetEntityID());
                builder.Append('|');
                builder.Append((int)job.Phase);
                builder.Append('|');
                builder.Append(
                    job.CartHomeTile.m_x);
                builder.Append('|');
                builder.Append(
                    job.CartHomeTile.m_y);
                builder.Append('|');
                builder.Append(
                    job.CartHomeFloorIndex);
                builder.Append('|');
                builder.Append(
                    (int)job.CartHomeOrientation);
                builder.Append('|');
                builder.Append(
                    job.NativeTimeout ? 1 : 0);
                builder.Append('|');

                bool firstCollected = true;
                for (int procedureIndex = 0;
                    procedureIndex <
                        job.CollectedProcedures.Count;
                    procedureIndex++)
                {
                    LabProcedure procedure =
                        job.CollectedProcedures[
                            procedureIndex];
                    if (procedure == null)
                    {
                        continue;
                    }

                    if (!firstCollected)
                    {
                        builder.Append(',');
                    }
                    builder.Append(
                        procedure.GetEntityID());
                    firstCollected = false;
                }

                builder.Append('|');
                builder.Append(
                    job.SampleRoomWaitDeadlineMinute);
                builder.Append('|');
                builder.Append(
                    job.SampleRoomStagingDeadlineMinute);
                builder.Append('|');
                builder.Append(
                    job.PostDeliveryReturn ? 1 : 0);
                builder.Append('|');
                builder.Append(
                    job.ContinuationDeclined ? 1 : 0);
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static string SerializeNativeLabBundles(
            List<KeyValuePair<Entity, LabProcedure>>
                bundles)
        {
            StringBuilder builder =
                new StringBuilder();
            builder.Append(
                NativeLabStateHeader);
            builder.Append('\n');

            for (int i = 0;
                i < bundles.Count;
                i++)
            {
                Entity lab =
                    bundles[i].Key;
                LabProcedure procedure =
                    bundles[i].Value;
                if (lab == null ||
                    procedure == null)
                {
                    continue;
                }

                builder.Append(
                    lab.GetEntityID());
                builder.Append('|');
                builder.Append(
                    procedure.GetEntityID());
                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void RestoreNativeLabBundles(
            string serialized)
        {
            string[] lines =
                serialized.Split(
                    new char[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] != NativeLabStateHeader)
            {
                return;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            int restored = 0;

            for (int i = 1;
                i < lines.Length;
                i++)
            {
                string[] parts =
                    lines[i].Split(
                        new char[] { '|' },
                        StringSplitOptions.None);
                if (parts.Length != 2)
                {
                    continue;
                }

                uint labId;
                uint procedureId;
                if (!uint.TryParse(
                        parts[0],
                        out labId) ||
                    !uint.TryParse(
                        parts[1],
                        out procedureId))
                {
                    continue;
                }

                Entity lab =
                    manager.GetEntity(
                        labId);
                LabProcedure procedure =
                    manager.GetEntity(
                        procedureId)
                        as LabProcedure;
                if (PorterSampleTransportRuntime
                        .RestoreNativeLabSampleBundle(
                            lab,
                            procedure))
                {
                    restored++;
                }
            }

            PorterDiagnostics.Log(
                "load: restoredNativeLabSampleBundles=" +
                restored +
                ".");
        }

        private static void Restore(string serialized)
        {
            string[] lines =
                serialized.Split(
                    new char[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] != StateHeader)
            {
                return;
            }

            HashSet<int> homeFloors =
                new HashSet<int>();
            int restored = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                PorterSampleTransportJob job;
                Entity porter;
                if (!TryParseJob(
                        lines[i],
                        out porter,
                        out job))
                {
                    Plugin.Log?.LogWarning(
                        "Skipped invalid persisted Porter sample transport entry.");
                    continue;
                }

                if (!PorterSampleTransportRuntime
                        .RestorePersistedJob(
                            porter,
                            job))
                {
                    Plugin.Log?.LogWarning(
                        "Could not restore persisted Porter sample transport for entity " +
                        (porter == null
                            ? "UNKNOWN"
                            : porter.GetEntityID().ToString()) +
                        ".");
                    continue;
                }

                if (job.Cart != null &&
                    job.CartHomeFloorIndex >= 0)
                {
                    homeFloors.Add(
                        job.CartHomeFloorIndex);
                }
                restored++;
            }

            if (Hospital.Instance != null &&
                Hospital.Instance.m_floors != null)
            {
                foreach (int floorIndex in homeFloors)
                {
                    if (floorIndex >= 0 &&
                        floorIndex <
                            Hospital.Instance.m_floors.Count)
                    {
                        Hospital.Instance
                            .m_floors[floorIndex]
                            .ValidateRooms();
                    }
                }
            }

            PorterDiagnostics.Log(
                "load: restoredSampleJobs=" +
                restored +
                ".");
        }

        private static bool TryParseJob(
            string line,
            out Entity porter,
            out PorterSampleTransportJob job)
        {
            porter = null;
            job = null;

            string[] parts =
                line.Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
            if (parts.Length != 12 &&
                parts.Length != 13 &&
                parts.Length != 14 &&
                parts.Length != 15 &&
                parts.Length != 16)
            {
                return false;
            }

            uint porterId;
            uint cartId;
            uint procedureId;
            uint statLabId;
            uint homeRoomId;
            int phaseValue;
            int homeX;
            int homeY;
            int homeFloor;
            int homeOrientation;
            int nativeTimeout;
            int sampleRoomWaitDeadlineMinute = 0;
            int sampleRoomStagingDeadlineMinute = 0;
            int postDeliveryReturn = 0;
            int continuationDeclined = 0;

            if (!uint.TryParse(parts[0], out porterId) ||
                !uint.TryParse(parts[1], out cartId) ||
                !uint.TryParse(parts[2], out procedureId) ||
                !uint.TryParse(parts[3], out statLabId) ||
                !uint.TryParse(parts[4], out homeRoomId) ||
                !int.TryParse(parts[5], out phaseValue) ||
                !int.TryParse(parts[6], out homeX) ||
                !int.TryParse(parts[7], out homeY) ||
                !int.TryParse(parts[8], out homeFloor) ||
                !int.TryParse(parts[9], out homeOrientation) ||
                !int.TryParse(parts[10], out nativeTimeout) ||
                phaseValue <
                    (int)PorterSampleTransportPhase.GoingToCart ||
                phaseValue >
                    (int)PorterSampleTransportPhase.ReturningToStation)
            {
                return false;
            }

            if (parts.Length >= 13 &&
                (!int.TryParse(
                    parts[12],
                    out sampleRoomWaitDeadlineMinute) ||
                 sampleRoomWaitDeadlineMinute < 0))
            {
                return false;
            }

            if (parts.Length >= 14 &&
                (!int.TryParse(
                    parts[13],
                    out sampleRoomStagingDeadlineMinute) ||
                 sampleRoomStagingDeadlineMinute < 0))
            {
                return false;
            }

            if (parts.Length >= 15 &&
                (!int.TryParse(
                    parts[14],
                    out postDeliveryReturn) ||
                 (postDeliveryReturn != 0 &&
                  postDeliveryReturn != 1)))
            {
                return false;
            }

            if (parts.Length == 16 &&
                (!int.TryParse(
                    parts[15],
                    out continuationDeclined) ||
                 (continuationDeclined != 0 &&
                  continuationDeclined != 1)))
            {
                return false;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            porter =
                manager.GetEntity(porterId);
            TileObject cart =
                cartId == 0u
                    ? null
                    : manager.GetEntity(cartId)
                        as TileObject;
            LabProcedure procedure =
                procedureId == 0u
                    ? null
                    : manager.GetEntity(procedureId)
                        as LabProcedure;
            Room statLab =
                manager.GetEntity(statLabId)
                    as Room;
            Room homeRoom =
                homeRoomId == 0u
                    ? null
                    : manager.GetEntity(homeRoomId)
                        as Room;

            if (porter == null ||
                statLab == null ||
                (cart != null && homeRoom == null) ||
                (cart == null && homeRoom != null))
            {
                return false;
            }

            job =
                new PorterSampleTransportJob
                {
                    Procedure = procedure,
                    StatLab = statLab,
                    Cart = cart,
                    CartHomeRoom = homeRoom,
                    CartHomeTile =
                        new Vector2i(homeX, homeY),
                    CartHomeFloorIndex =
                        homeFloor,
                    CartHomeOrientation =
                        (Direction)homeOrientation,
                    NativeTimeout =
                        nativeTimeout != 0,
                    SampleRoomWaitDeadlineMinute =
                        sampleRoomWaitDeadlineMinute,
                    SampleRoomStagingDeadlineMinute =
                        sampleRoomStagingDeadlineMinute,
                    PostDeliveryReturn =
                        postDeliveryReturn != 0,
                    ContinuationDeclined =
                        continuationDeclined != 0,
                    Phase =
                        (PorterSampleTransportPhase)
                            phaseValue
                };

            if (!string.IsNullOrEmpty(parts[11]))
            {
                string[] collectedIds =
                    parts[11].Split(
                        new char[] { ',' },
                        StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0;
                    i < collectedIds.Length;
                    i++)
                {
                    uint collectedId;
                    if (!uint.TryParse(
                            collectedIds[i],
                            out collectedId))
                    {
                        continue;
                    }

                    LabProcedure collected =
                        manager.GetEntity(
                            collectedId)
                            as LabProcedure;
                    if (collected != null &&
                        !job.CollectedProcedures.Contains(
                            collected))
                    {
                        job.CollectedProcedures.Add(
                            collected);
                    }
                }
            }

            return true;
        }
    }

    [HarmonyPatch(
        typeof(InGameMenuController),
        nameof(InGameMenuController.Load),
        new Type[]
        {
            typeof(string),
            typeof(string)
        })]
    internal static class PorterRuntimeLoadResetPatch
    {
        private static void Prefix()
        {
            PorterSampleTransportRuntime.Reset();
            PorterIdleWorkstationVisuals.Reset();
            StretcherDestinationPatch.ResetFallbackTracking();
        }
    }

    [HarmonyPatch(
        typeof(InGameMenuController),
        nameof(InGameMenuController.CheckRoomObjectDepartments),
        new Type[] { })]
    internal static class PorterSamplePersistenceDepartmentRestorePatch
    {
        private static void Postfix()
        {
            PorterSampleTransportRuntime
                .RestoreActiveCartDepartments();
        }
    }

    [HarmonyPatch]
    internal static class PorterSamplePersistenceSavePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Constructor(
                typeof(GameSave),
                new Type[] { typeof(Hospital) });
        }

        private static void Postfix(GameSave __instance)
        {
            PorterSamplePersistence.AppendToSave(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(LopitalEntityFactory),
        nameof(LopitalEntityFactory.LoadEntity),
        new Type[]
        {
            typeof(EntitySave),
            typeof(Floor)
        })]
    internal static class PorterSamplePersistenceLoadPatch
    {
        private static bool Prefix(
            EntitySave entitySave,
            ref Entity __result)
        {
            if (!PorterSamplePersistence.TryRestoreMarker(
                    entitySave))
            {
                return true;
            }

            __result = null;
            return false;
        }

        private static void Postfix(Entity __result)
        {
            if (PorterIdentity.IsPorter(__result))
            {
                PorterIdentity.RefreshPorterName(__result);
            }
        }
    }
}
