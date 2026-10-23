using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    internal enum ObservationEquipmentState
    {
        Free,
        Busy,
        MissingOrUnusable
    }

    internal sealed class TraumaCenterRetryState
    {
        internal GameDBExamination Examination;
        internal Room ObservationRoom;
        internal int RetryCount;
        internal float NextRetryGameMinute;
        internal bool ReleasedToVanilla;
        internal bool NotifyOnFallbackSuccess;
        internal List<GameDBObject> BusyEquipmentTypes =
            new List<GameDBObject>();
    }

    internal sealed class TraumaCenterReservationCallState
    {
        internal Entity Patient;
        internal GameDBExamination Examination;
        internal Room ObservationRoom;
        internal bool ForcedObservationRoom;
        internal bool SuppressedUntilRetry;
        internal bool VanillaFallbackReleased;
    }

    internal static class TraumaCenterProtectionRuntime
    {
        private static readonly Dictionary<Entity, List<TraumaCenterRetryState>>
            RetryStates =
                new Dictionary<Entity, List<TraumaCenterRetryState>>();

        private static readonly HashSet<Room>
            NotifiedObservationRooms =
                new HashSet<Room>();

        internal static bool PrepareReservation(
            GameDBExamination examination,
            Entity patient,
            Department department,
            ref Room room,
            ref ProcedureSceneAvailability result,
            out TraumaCenterReservationCallState callState)
        {
            callState = null;

            Room observationRoom;
            if (!TryGetProtectedObservationRoom(
                    examination,
                    patient,
                    department,
                    room,
                    out observationRoom))
            {
                ClearIfContextChanged(patient, examination);
                return true;
            }

            TraumaCenterReservationCallState state =
                new TraumaCenterReservationCallState();
            state.Patient = patient;
            state.Examination = examination;
            state.ObservationRoom = observationRoom;
            callState = state;

            ClearOtherObservationContexts(
                patient,
                examination,
                observationRoom);

            TraumaCenterRetryState retry =
                FindRetryState(
                    patient,
                    examination,
                    observationRoom);

            if (retry != null && retry.ReleasedToVanilla)
            {
                room = null;
                state.VanillaFallbackReleased = true;
                Log(
                    patient,
                    examination,
                    observationRoom,
                    "native fallback remains released");
                return true;
            }

            List<string> busyTags;
            List<GameDBObject> busyEquipmentTypes;
            ObservationEquipmentState equipmentState =
                GetObservationEquipmentState(
                    examination.Procedure,
                    patient,
                    observationRoom,
                    out busyTags,
                    out busyEquipmentTypes);

            if (equipmentState ==
                ObservationEquipmentState.MissingOrUnusable)
            {
                ReleaseToVanilla(
                    patient,
                    examination,
                    observationRoom,
                    retry,
                    busyEquipmentTypes,
                    notify: false,
                    reason:
                        "Observation room lacks usable required equipment");
                room = null;
                state.VanillaFallbackReleased = true;
                return true;
            }

            if (retry != null &&
                equipmentState == ObservationEquipmentState.Busy &&
                GetGameMinuteStamp() < retry.NextRetryGameMinute)
            {
                state.SuppressedUntilRetry = true;
                result =
                    ProcedureSceneAvailability.EQUIPMENT_UNAVAILABLE;

                if (FlowOptimizerConfig.TraumaCenterDiagnosticLogging)
                {
                    Log(
                        patient,
                        examination,
                        observationRoom,
                        "waiting before equipment retry; retry=" +
                        retry.RetryCount + "/" +
                        FlowOptimizerConfig
                            .TraumaCenterProtectionFallbackRetries +
                        "; busyTags=" +
                        JoinTags(busyTags));
                }
                return false;
            }

            room = observationRoom;
            state.ForcedObservationRoom = true;

            if (FlowOptimizerConfig.TraumaCenterDiagnosticLogging)
            {
                Log(
                    patient,
                    examination,
                    observationRoom,
                    equipmentState == ObservationEquipmentState.Busy
                        ? "retrying in Observation; busyTags=" +
                          JoinTags(busyTags)
                        : "preferring Observation room");
            }

            return true;
        }

        internal static void CompleteReservation(
            ProcedureComponent procedureComponent,
            ProcedureSceneAvailability result,
            TraumaCenterReservationCallState callState)
        {
            if (callState == null ||
                callState.SuppressedUntilRetry)
            {
                return;
            }

            Entity patient = callState.Patient;
            GameDBExamination examination =
                callState.Examination;
            Room observationRoom =
                callState.ObservationRoom;

            if (callState.VanillaFallbackReleased)
            {
                if (result == ProcedureSceneAvailability.AVAILABLE)
                {
                    Room selectedRoom =
                        GetReservedProcedureRoom(
                            procedureComponent);

                    LogVanillaFallbackSelection(
                        procedureComponent,
                        patient,
                        examination,
                        observationRoom);

                    TraumaCenterRetryState retryState =
                        FindRetryState(
                            patient,
                            examination,
                            observationRoom);
                    if (retryState != null &&
                        retryState.NotifyOnFallbackSuccess)
                    {
                        NotifyCapacity(
                            examination,
                            observationRoom,
                            selectedRoom,
                            retryState.BusyEquipmentTypes);
                    }

                    RemoveRetryState(
                        patient,
                        examination,
                        observationRoom);
                }
                return;
            }

            if (!callState.ForcedObservationRoom)
            {
                return;
            }

            if (result == ProcedureSceneAvailability.AVAILABLE)
            {
                RemoveRetryState(
                    patient,
                    examination,
                    observationRoom);
                NotifiedObservationRooms.Remove(observationRoom);

                Log(
                    patient,
                    examination,
                    observationRoom,
                    "Observation reservation succeeded");
                return;
            }

            List<string> busyTags;
            List<GameDBObject> busyEquipmentTypes;
            ObservationEquipmentState equipmentState =
                GetObservationEquipmentState(
                    examination.Procedure,
                    patient,
                    observationRoom,
                    out busyTags,
                    out busyEquipmentTypes);

            if (equipmentState ==
                ObservationEquipmentState.MissingOrUnusable)
            {
                ReleaseToVanilla(
                    patient,
                    examination,
                    observationRoom,
                    null,
                    busyEquipmentTypes,
                    notify: false,
                    reason:
                        "required equipment became missing or unusable");
                return;
            }

            if (equipmentState != ObservationEquipmentState.Busy ||
                HasStaffBlocker(result))
            {
                RemoveRetryState(
                    patient,
                    examination,
                    observationRoom);

                if (FlowOptimizerConfig.TraumaCenterDiagnosticLogging)
                {
                    Log(
                        patient,
                        examination,
                        observationRoom,
                        "Observation attempt failed for a non-capacity reason; " +
                        "availability=" + result);
                }
                return;
            }

            int maximumRetries =
                FlowOptimizerConfig
                    .TraumaCenterProtectionFallbackRetries;
            if (maximumRetries <= 0)
            {
                ReleaseToVanilla(
                    patient,
                    examination,
                    observationRoom,
                    null,
                    busyEquipmentTypes,
                    notify: true,
                    reason:
                        "equipment busy and retries disabled");
                return;
            }

            TraumaCenterRetryState retry =
                FindRetryState(
                    patient,
                    examination,
                    observationRoom);
            if (retry == null)
            {
                retry = new TraumaCenterRetryState();
                retry.Examination = examination;
                retry.ObservationRoom = observationRoom;
                retry.RetryCount = 0;
                retry.NextRetryGameMinute =
                    GetGameMinuteStamp() +
                    FlowOptimizerConfig
                        .TraumaCenterProtectionRetryMinutes;
                retry.BusyEquipmentTypes =
                    new List<GameDBObject>(busyEquipmentTypes);
                StoreRetryState(patient, retry);

                if (FlowOptimizerConfig.TraumaCenterDiagnosticLogging)
                {
                    Log(
                        patient,
                        examination,
                        observationRoom,
                        "equipment capacity wait started; retry=0/" +
                        maximumRetries +
                        "; nextRetryMinutes=" +
                        FlowOptimizerConfig
                            .TraumaCenterProtectionRetryMinutes +
                        "; busyTags=" +
                        JoinTags(busyTags));
                }
                return;
            }

            retry.BusyEquipmentTypes =
                new List<GameDBObject>(busyEquipmentTypes);
            retry.RetryCount++;
            if (retry.RetryCount >= maximumRetries)
            {
                ReleaseToVanilla(
                    patient,
                    examination,
                    observationRoom,
                    retry,
                    busyEquipmentTypes,
                    notify: true,
                    reason:
                        "equipment remained busy after configured retries");
                return;
            }

            retry.NextRetryGameMinute =
                GetGameMinuteStamp() +
                FlowOptimizerConfig
                    .TraumaCenterProtectionRetryMinutes;

            if (FlowOptimizerConfig.TraumaCenterDiagnosticLogging)
            {
                Log(
                    patient,
                    examination,
                    observationRoom,
                    "equipment retry failed; retry=" +
                    retry.RetryCount + "/" +
                    maximumRetries +
                    "; busyTags=" +
                    JoinTags(busyTags));
            }
        }

        internal static Room GetRoomForScheduledExaminationRoomCheck(
            MapScriptInterface map,
            WalkComponent walkComponent,
            HospitalizationComponent hospitalization)
        {
            Room currentRoom =
                map == null || walkComponent == null
                    ? null
                    : map.GetRoomAt(walkComponent);

            if (!FlowOptimizerConfig
                    .ProtectTraumaCenterForObservationExams ||
                currentRoom == null ||
                hospitalization == null ||
                hospitalization.m_entity == null)
            {
                return currentRoom;
            }

            Entity patient = hospitalization.m_entity;
            ProcedureComponent procedureComponent =
                patient.GetComponent<ProcedureComponent>();
            if (procedureComponent == null ||
                procedureComponent.m_state == null ||
                procedureComponent.m_state
                    .m_reservedProcedureScript == null ||
                procedureComponent.m_state
                    .m_reservedProcedureScript.GetEntity() == null)
            {
                return currentRoom;
            }

            ProcedureScript procedureScript =
                procedureComponent.m_state
                    .m_reservedProcedureScript.GetEntity();
            if (procedureScript.m_stateData == null ||
                procedureScript.m_stateData.m_examination == null ||
                procedureScript.m_stateData.m_examination.Entry == null)
            {
                return currentRoom;
            }

            GameDBExamination examination =
                procedureScript.m_stateData.m_examination.Entry;

            BehaviorPatient behavior =
                patient.GetComponent<BehaviorPatient>();
            Department department =
                behavior == null || behavior.m_state == null
                    ? null
                    : behavior.m_state.m_department.GetEntity();

            Room observationRoom;
            if (!TryGetProtectedObservationRoom(
                    examination,
                    patient,
                    department,
                    currentRoom,
                    out observationRoom) ||
                observationRoom != currentRoom)
            {
                return currentRoom;
            }

            Room reservedRoom =
                GetReservedProcedureRoom(procedureComponent);
            if (reservedRoom == null ||
                reservedRoom == currentRoom)
            {
                return currentRoom;
            }

            if (FlowOptimizerConfig.TraumaCenterDiagnosticLogging)
            {
                string selectedId =
                    reservedRoom.m_roomPersistentData == null ||
                    reservedRoom.m_roomPersistentData.m_roomType == null
                        ? "<none>"
                        : reservedRoom
                            .m_roomPersistentData
                            .m_roomType
                            .Entry
                            .DatabaseID
                            .ToString();

                Log(
                    patient,
                    examination,
                    observationRoom,
                    "reserved fallback scene is outside Observation; " +
                    "preserving native patient transport; selectedRoom=" +
                    selectedId);
            }

            // TryToStartScheduledExamination compares its cached requested room
            // with the result of GetRoomAt(WalkComponent). Returning the actual
            // reserved scene room only for this protected fallback makes that
            // comparison fail, so vanilla continues into GetTransportedToProcedure().
            return reservedRoom;
        }

        internal static void Clear()
        {
            RetryStates.Clear();
            NotifiedObservationRooms.Clear();
        }

        private static bool TryGetProtectedObservationRoom(
            GameDBExamination examination,
            Entity patient,
            Department department,
            Room requestedRoom,
            out Room observationRoom)
        {
            observationRoom = null;

            if (!FlowOptimizerConfig
                    .ProtectTraumaCenterForObservationExams ||
                examination == null ||
                examination.Procedure == null ||
                patient == null ||
                department == null ||
                MapScriptInterface.Instance == null ||
                Database.Instance == null)
            {
                return false;
            }

            GameDBDepartment emergency =
                Database.Instance.GetEntry<GameDBDepartment>(
                    "DPT_EMERGENCY");
            if (emergency == null ||
                department.GetDepartmentType() != emergency)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            BehaviorPatient behavior =
                patient.GetComponent<BehaviorPatient>();
            if (hospitalization == null ||
                hospitalization.m_state == null ||
                behavior == null ||
                behavior.m_state == null ||
                !hospitalization.IsHospitalized())
            {
                return false;
            }

            GameDBTreatment observationTreatment =
                Database.Instance.GetEntry<GameDBTreatment>(
                    "TRT_HOSPITALIZATION_OBSERVATION");
            if (observationTreatment == null ||
                hospitalization.GetHospitalizationTreatment() !=
                    observationTreatment)
            {
                return false;
            }

            if (behavior.GetWorstKnownHazard() ==
                    SymptomHazard.High ||
                (behavior.m_state.m_medicalCondition != null &&
                 behavior.m_state.m_medicalCondition
                     .HasCriticalHiddenSymptom()))
            {
                return false;
            }

            if (hospitalization.m_state.m_bed == null ||
                hospitalization.m_state.m_bed.GetEntity() == null)
            {
                return false;
            }

            TileObject bed =
                hospitalization.m_state.m_bed.GetEntity();
            observationRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    bed.m_state.m_position,
                    bed.GetFloorIndex());

            GameDBRoomType observationType =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_ER_OBSERVATION_ROOM");
            GameDBRoomType traumaType =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_TRAUMA_CENTER");

            if (observationRoom == null ||
                observationType == null ||
                traumaType == null ||
                observationRoom.m_roomPersistentData == null ||
                observationRoom.m_roomPersistentData.m_roomType == null ||
                observationRoom.m_roomPersistentData.m_roomType.Entry !=
                    observationType ||
                observationRoom.m_roomPersistentData.m_valid !=
                    RoomValidity.OK ||
                !observationRoom.AllowsProcedure(
                    examination.Procedure) ||
                examination.Procedure.RequiredRoomTags == null ||
                !traumaType.HasAnyTag(
                    examination.Procedure.RequiredRoomTags))
            {
                observationRoom = null;
                return false;
            }

            if (requestedRoom != null &&
                requestedRoom != observationRoom &&
                requestedRoom.m_roomPersistentData != null &&
                requestedRoom.m_roomPersistentData.m_roomType != null &&
                requestedRoom.m_roomPersistentData.m_roomType.Entry !=
                    traumaType)
            {
                observationRoom = null;
                return false;
            }

            return true;
        }

        private static ObservationEquipmentState
            GetObservationEquipmentState(
                GameDBProcedure procedure,
                Entity patient,
                Room observationRoom,
                out List<string> busyTags,
                out List<GameDBObject> busyEquipmentTypes)
        {
            busyTags = new List<string>();
            busyEquipmentTypes =
                new List<GameDBObject>();

            if (procedure == null ||
                patient == null ||
                observationRoom == null ||
                MapScriptInterface.Instance == null)
            {
                return ObservationEquipmentState.MissingOrUnusable;
            }

            RequiredEquipment[] required =
                procedure.RequiredEquipment;
            if (required == null || required.Length == 0)
            {
                return ObservationEquipmentState.Free;
            }

            WalkComponent walk =
                patient.GetComponent<WalkComponent>();
            if (walk == null)
            {
                return ObservationEquipmentState.MissingOrUnusable;
            }

            for (int i = 0; i < required.Length; i++)
            {
                RequiredEquipment item = required[i];
                if (item == null ||
                    string.IsNullOrEmpty(item.Tag))
                {
                    continue;
                }

                string[] tags =
                    new string[] { item.Tag };

                TileObject free =
                    MapScriptInterface.Instance
                        .FindClosestFreeObjectWithTags(
                            patient,
                            patient,
                            walk.GetCurrentTile(),
                            observationRoom,
                            tags,
                            AccessRights.PATIENT_PROCEDURE,
                            true,
                            procedure.RequiredRoomTypes,
                            procedure.AllowedOutsideOfRoom);

                if (free != null)
                {
                    continue;
                }

                bool foundUsable = false;
                bool foundBusy = false;
                GameDBObject busyEquipmentType = null;
                FindMatchingEquipmentState(
                    observationRoom,
                    tags,
                    out foundUsable,
                    out foundBusy,
                    out busyEquipmentType);

                if (!foundUsable || !foundBusy)
                {
                    return
                        ObservationEquipmentState
                            .MissingOrUnusable;
                }

                busyTags.Add(item.Tag);
                if (busyEquipmentType != null &&
                    !busyEquipmentTypes.Contains(
                        busyEquipmentType))
                {
                    busyEquipmentTypes.Add(
                        busyEquipmentType);
                }
            }

            return busyTags.Count > 0
                ? ObservationEquipmentState.Busy
                : ObservationEquipmentState.Free;
        }

        private static void FindMatchingEquipmentState(
            Room room,
            string[] tags,
            out bool foundUsable,
            out bool foundBusy,
            out GameDBObject busyEquipmentType)
        {
            foundUsable = false;
            foundBusy = false;
            busyEquipmentType = null;

            if (room == null ||
                room.m_roomPersistentData == null ||
                Hospital.Instance == null)
            {
                return;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];

            for (int x =
                     room.m_roomPersistentData.m_positionBottom.m_x;
                 x <=
                     room.m_roomPersistentData.m_positionTop.m_x;
                 x++)
            {
                for (int y =
                         room.m_roomPersistentData.m_positionBottom.m_y;
                     y <=
                         room.m_roomPersistentData.m_positionTop.m_y;
                     y++)
                {
                    Vector2i position =
                        new Vector2i(x, y);
                    if (!room.IsPositionInRoom(position))
                    {
                        continue;
                    }

                    foreach (TileObject equipment in
                        floor.m_tileObjects[x, y].GetAllObjects())
                    {
                        if (equipment == null ||
                            !equipment.HasAllTags(tags) ||
                            !equipment.IsValid() ||
                            equipment.IsBroken())
                        {
                            continue;
                        }

                        foundUsable = true;
                        if (equipment.User != null ||
                            equipment.Owner != null)
                        {
                            foundBusy = true;
                            if (busyEquipmentType == null &&
                                equipment.m_state != null &&
                                equipment.m_state.m_gameDBObject != null)
                            {
                                busyEquipmentType =
                                    equipment.m_state
                                        .m_gameDBObject.Entry;
                            }
                        }
                    }
                }
            }
        }

        private static bool HasStaffBlocker(
            ProcedureSceneAvailability availability)
        {
            ProcedureSceneAvailability blockers =
                ProcedureSceneAvailability.STAFF_BUSY |
                ProcedureSceneAvailability.STAFF_NOT_QUALIFIED |
                ProcedureSceneAvailability.STAFF_UNAVAILABLE |
                ProcedureSceneAvailability.STAFF_UNAVAILABLE_LAB |
                ProcedureSceneAvailability.STAFF_UNAVAILABLE_NIGHT |
                ProcedureSceneAvailability
                    .STAFF_UNAVAILABLE_LAB_ROLES |
                ProcedureSceneAvailability.DOCTOR_CAN_NOT_PRESCRIBE;

            return (availability & blockers) !=
                   (ProcedureSceneAvailability)0;
        }

        private static void ReleaseToVanilla(
            Entity patient,
            GameDBExamination examination,
            Room observationRoom,
            TraumaCenterRetryState retry,
            List<GameDBObject> busyEquipmentTypes,
            bool notify,
            string reason)
        {
            if (patient == null)
            {
                return;
            }

            if (retry == null)
            {
                retry = new TraumaCenterRetryState();
                retry.Examination = examination;
                retry.ObservationRoom = observationRoom;
                StoreRetryState(patient, retry);
            }

            retry.ReleasedToVanilla = true;

            if (busyEquipmentTypes != null &&
                busyEquipmentTypes.Count > 0)
            {
                retry.BusyEquipmentTypes =
                    new List<GameDBObject>(
                        busyEquipmentTypes);
            }

            if (notify)
            {
                retry.NotifyOnFallbackSuccess = true;
            }

            Log(
                patient,
                examination,
                observationRoom,
                "protection released to vanilla; reason=" +
                reason);
        }

        private static void NotifyCapacity(
            GameDBExamination examination,
            Room observationRoom,
            Room selectedRoom,
            List<GameDBObject> busyEquipmentTypes)
        {
            if (examination == null ||
                observationRoom == null ||
                selectedRoom == null ||
                selectedRoom == observationRoom ||
                NotificationManager.GetInstance() == null ||
                Database.Instance == null ||
                NotifiedObservationRooms.Contains(
                    observationRoom))
            {
                return;
            }

            if (Database.Instance.GetEntry<GameDBNotification>(
                    HfoDatabaseIds
                        .ObservationEquipmentNotification) == null)
            {
                return;
            }

            StringTable strings =
                StringTable.GetInstance();

            string examinationName =
                strings == null
                    ? examination.DatabaseID.ToString()
                    : strings.GetLocalizedText(
                        examination);

            string equipmentName =
                GetLocalizedEquipmentNames(
                    busyEquipmentTypes,
                    strings);

            string roomName =
                GetLocalizedRoomName(
                    selectedRoom,
                    strings);

            if (string.IsNullOrEmpty(equipmentName) ||
                string.IsNullOrEmpty(roomName))
            {
                return;
            }

            NotifiedObservationRooms.Add(
                observationRoom);

            GameDBRoomType observationType =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_ER_OBSERVATION_ROOM");
            int icon =
                observationType == null
                    ? 0
                    : observationType.IconIndex;

            NotificationManager.GetInstance().AddMessage(
                null,
                HfoDatabaseIds
                    .ObservationEquipmentNotification,
                equipmentName,
                roomName,
                examinationName,
                icon,
                0,
                0,
                0,
                null);
        }

        private static string GetLocalizedEquipmentNames(
            List<GameDBObject> equipmentTypes,
            StringTable strings)
        {
            if (equipmentTypes == null ||
                equipmentTypes.Count == 0)
            {
                return string.Empty;
            }

            List<string> names =
                new List<string>();

            for (int i = 0;
                 i < equipmentTypes.Count;
                 i++)
            {
                GameDBObject equipment =
                    equipmentTypes[i];
                if (equipment == null)
                {
                    continue;
                }

                string name =
                    strings == null
                        ? equipment.DatabaseID.ToString()
                        : strings.GetLocalizedText(
                            equipment);

                if (!string.IsNullOrEmpty(name) &&
                    !names.Contains(name))
                {
                    names.Add(name);
                }
            }

            return names.Count == 0
                ? string.Empty
                : string.Join(", ", names.ToArray());
        }

        private static string GetLocalizedRoomName(
            Room room,
            StringTable strings)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return string.Empty;
            }

            GameDBRoomType roomType =
                room.m_roomPersistentData
                    .m_roomType.Entry;

            return strings == null
                ? roomType.DatabaseID.ToString()
                : strings.GetLocalizedText(roomType);
        }

        private static Room GetReservedProcedureRoom(
            ProcedureComponent procedureComponent)
        {
            if (procedureComponent == null ||
                procedureComponent.m_state == null ||
                procedureComponent.m_state
                    .m_reservedProcedureScript == null ||
                procedureComponent.m_state
                    .m_reservedProcedureScript.GetEntity() == null)
            {
                return null;
            }

            ProcedureScript script =
                procedureComponent.m_state
                    .m_reservedProcedureScript
                    .GetEntity();
            ProcedureScene scene =
                script.m_stateData == null
                    ? null
                    : script.m_stateData.m_procedureScene;

            return scene == null ||
                   scene.m_room == null
                ? null
                : scene.m_room.GetEntity();
        }

        private static void LogVanillaFallbackSelection(
            ProcedureComponent procedureComponent,
            Entity patient,
            GameDBExamination examination,
            Room observationRoom)
        {
            Room selectedRoom =
                GetReservedProcedureRoom(
                    procedureComponent);

            if (FlowOptimizerConfig.TraumaCenterDiagnosticLogging)
            {
                string selectedId =
                    selectedRoom == null ||
                    selectedRoom.m_roomPersistentData == null ||
                    selectedRoom.m_roomPersistentData.m_roomType == null
                        ? "<none>"
                        : selectedRoom
                            .m_roomPersistentData
                            .m_roomType
                            .Entry
                            .DatabaseID
                            .ToString();

                Log(
                    patient,
                    examination,
                    observationRoom,
                    "native fallback reservation succeeded; selectedRoom=" +
                    selectedId);
            }
        }

        private static TraumaCenterRetryState FindRetryState(
            Entity patient,
            GameDBExamination examination,
            Room observationRoom)
        {
            if (patient == null)
            {
                return null;
            }

            List<TraumaCenterRetryState> states;
            if (!RetryStates.TryGetValue(patient, out states) ||
                states == null)
            {
                return null;
            }

            for (int i = 0; i < states.Count; i++)
            {
                TraumaCenterRetryState retry = states[i];
                if (retry != null &&
                    retry.Examination == examination &&
                    retry.ObservationRoom == observationRoom)
                {
                    return retry;
                }
            }

            return null;
        }

        private static void StoreRetryState(
            Entity patient,
            TraumaCenterRetryState retry)
        {
            if (patient == null || retry == null)
            {
                return;
            }

            List<TraumaCenterRetryState> states;
            if (!RetryStates.TryGetValue(patient, out states) ||
                states == null)
            {
                states = new List<TraumaCenterRetryState>();
                RetryStates[patient] = states;
            }

            if (!states.Contains(retry))
            {
                states.Add(retry);
            }
        }

        private static void RemoveRetryState(
            Entity patient,
            GameDBExamination examination,
            Room observationRoom)
        {
            if (patient == null)
            {
                return;
            }

            List<TraumaCenterRetryState> states;
            if (!RetryStates.TryGetValue(patient, out states) ||
                states == null)
            {
                return;
            }

            for (int i = states.Count - 1; i >= 0; i--)
            {
                TraumaCenterRetryState retry = states[i];
                if (retry == null ||
                    (retry.Examination == examination &&
                     retry.ObservationRoom == observationRoom))
                {
                    states.RemoveAt(i);
                }
            }

            if (states.Count == 0)
            {
                RetryStates.Remove(patient);
            }
        }

        private static void RemoveRetryStatesForExamination(
            Entity patient,
            GameDBExamination examination)
        {
            if (patient == null)
            {
                return;
            }

            List<TraumaCenterRetryState> states;
            if (!RetryStates.TryGetValue(patient, out states) ||
                states == null)
            {
                return;
            }

            for (int i = states.Count - 1; i >= 0; i--)
            {
                TraumaCenterRetryState retry = states[i];
                if (retry == null ||
                    retry.Examination == examination)
                {
                    states.RemoveAt(i);
                }
            }

            if (states.Count == 0)
            {
                RetryStates.Remove(patient);
            }
        }

        private static void ClearOtherObservationContexts(
            Entity patient,
            GameDBExamination examination,
            Room observationRoom)
        {
            if (patient == null)
            {
                return;
            }

            List<TraumaCenterRetryState> states;
            if (!RetryStates.TryGetValue(patient, out states) ||
                states == null)
            {
                return;
            }

            for (int i = states.Count - 1; i >= 0; i--)
            {
                TraumaCenterRetryState retry = states[i];
                if (retry == null ||
                    (retry.Examination == examination &&
                     retry.ObservationRoom != observationRoom))
                {
                    states.RemoveAt(i);
                }
            }

            if (states.Count == 0)
            {
                RetryStates.Remove(patient);
            }
        }

        private static void ClearIfContextChanged(
            Entity patient,
            GameDBExamination examination)
        {
            RemoveRetryStatesForExamination(
                patient,
                examination);
        }

        private static float GetGameMinuteStamp()
        {
            if (DayTime.Instance == null)
            {
                return 0f;
            }

            return
                DayTime.Instance.GetDay() * 24f * 60f +
                DayTime.Instance.GetDayTimeHours() * 60f;
        }

        private static string JoinTags(
            List<string> tags)
        {
            return tags == null || tags.Count == 0
                ? "<none>"
                : string.Join(",", tags.ToArray());
        }

        private static void Log(
            Entity patient,
            GameDBExamination examination,
            Room observationRoom,
            string message)
        {
            if (!FlowOptimizerConfig
                    .TraumaCenterDiagnosticLogging ||
                Plugin.Log == null)
            {
                return;
            }

            string examinationId =
                examination == null
                    ? "<none>"
                    : examination.DatabaseID.ToString();
            string roomId =
                observationRoom == null ||
                observationRoom.m_roomPersistentData == null ||
                observationRoom.m_roomPersistentData.m_roomType == null
                    ? "<none>"
                    : observationRoom
                        .m_roomPersistentData
                        .m_roomType
                        .Entry
                        .DatabaseID
                        .ToString();

            Plugin.Log.LogInfo(
                "[TraumaCenterProtection] patient=" +
                HfoDiagnostics.EntityName(patient) +
                "; examination=" + examinationId +
                "; observationRoom=" + roomId +
                "; " + message);
        }
    }

    [HarmonyPatch(
        typeof(ProcedureComponent),
        nameof(ProcedureComponent.ReserveExamination),
        new Type[]
        {
            typeof(GameDBExamination),
            typeof(Entity),
            typeof(Department),
            typeof(Room),
            typeof(AccessRights)
        })]
    internal static class TraumaCenterProtectionReserveExaminationPatch
    {
        [HarmonyPriority(Priority.First)]
        private static bool Prefix(
            GameDBExamination examination,
            Entity patient,
            Department department,
            ref Room room,
            ref ProcedureSceneAvailability __result,
            out TraumaCenterReservationCallState __state)
        {
            return TraumaCenterProtectionRuntime
                .PrepareReservation(
                    examination,
                    patient,
                    department,
                    ref room,
                    ref __result,
                    out __state);
        }

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            ProcedureComponent __instance,
            ProcedureSceneAvailability __result,
            TraumaCenterReservationCallState __state)
        {
            TraumaCenterProtectionRuntime
                .CompleteReservation(
                    __instance,
                    __result,
                    __state);
        }
    }
    [HarmonyPatch(
        typeof(HospitalizationComponent),
        nameof(HospitalizationComponent.TryToStartScheduledExamination),
        new Type[] { typeof(float) })]
    internal static class TraumaCenterProtectionScheduledExaminationPatch
    {
        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo nativeGetRoomAt = AccessTools.Method(
                typeof(MapScriptInterface),
                "GetRoomAt",
                new Type[] { typeof(WalkComponent) });
            MethodInfo replacement = AccessTools.Method(
                typeof(TraumaCenterProtectionRuntime),
                "GetRoomForScheduledExaminationRoomCheck",
                new Type[]
                {
                    typeof(MapScriptInterface),
                    typeof(WalkComponent),
                    typeof(HospitalizationComponent)
                });

            if (nativeGetRoomAt == null ||
                replacement == null)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Trauma Center Protection could not resolve the native scheduled-examination room check; vanilla behavior is unchanged.");
                }
                return codes;
            }

            int matchIndex = -1;
            int matchCount = 0;
            for (int i = 0; i < codes.Count; i++)
            {
                MethodInfo called =
                    codes[i].operand as MethodInfo;
                if (called == nativeGetRoomAt)
                {
                    matchIndex = i;
                    matchCount++;
                }
            }

            if (matchCount != 1 ||
                matchIndex < 0)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Trauma Center Protection expected one native GetRoomAt(WalkComponent) scheduled-examination room check but found " +
                        matchCount +
                        "; vanilla behavior is unchanged.");
                }
                return codes;
            }

            CodeInstruction loadHospitalization =
                new CodeInstruction(OpCodes.Ldarg_0);
            loadHospitalization.labels.AddRange(
                codes[matchIndex].labels);
            codes[matchIndex].labels.Clear();

            codes.Insert(
                matchIndex,
                loadHospitalization);
            codes[matchIndex + 1].opcode =
                OpCodes.Call;
            codes[matchIndex + 1].operand =
                replacement;

            return codes;
        }
    }

}
