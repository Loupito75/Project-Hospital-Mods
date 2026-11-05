using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    // Recovery for outpatients whose Vanilla waiting-room lookup fails.
    // The patient is held only when Vanilla's own visit-completion rules say
    // they still need to remain in the hospital. Waiting-room selection keeps
    // the exact clinic vs diagnostic/radiology rules used by the base game.
    [HarmonyPatch]
    internal static class OutpatientWaitingRoomRecoveryPatch
    {
        private const float RetryIntervalSeconds = 5f;
        private const float StandingRetrySeconds = 0.5f;
        private const float TopologyRefreshIntervalSeconds = 5f;

        private static readonly Dictionary<BehaviorPatient, float> NextAttempt =
            new Dictionary<BehaviorPatient, float>();
        private static readonly HashSet<BehaviorPatient> MissingRoomReported =
            new HashSet<BehaviorPatient>();
        private static readonly HashSet<BehaviorPatient> NativeReportInProgress =
            new HashSet<BehaviorPatient>();
        private static readonly Dictionary<Floor, float> NextTopologyRefresh =
            new Dictionary<Floor, float>();

        private static readonly MethodInfo ReportMissingWaitingRoomMethod =
            AccessTools.Method(typeof(BehaviorPatient), "ReportMissingWaitingRoom");

        [HarmonyPatch(typeof(BehaviorPatient), "GoToWaitingRoom")]
        [HarmonyPrefix]
        private static bool WaitingRoomPrefix(BehaviorPatient __instance)
        {
            if (!FixConfig.PreventPrematureOutpatientExit || __instance == null ||
                __instance.m_state == null)
            {
                return true;
            }

            bool needsStandingTransition;
            if (TryEnsureVanillaWaitingRoom(__instance, out needsStandingTransition))
            {
                if (needsStandingTransition)
                {
                    HoldForStandingTransition(__instance);
                    return false;
                }

                Clear(
                    __instance,
                    "waiting-room-ready");
                return true;
            }

            if (ShouldAllowVanillaMissingRoomExit(__instance))
            {
                StopRetry(
                    __instance,
                    "visit-complete-or-explicit-exit");
                return true;
            }

            HoldForMissingWaitingRoom(__instance);
            return false;
        }

        // Vanilla has a second independent missing-waiting-room Leave() path
        // before the queue machine has been used. Apply the same guard there.
        [HarmonyPatch(typeof(BehaviorPatient), "UpdateStateFindQueueMachine")]
        [HarmonyPrefix]
        private static bool FindQueueMachinePrefix(BehaviorPatient __instance)
        {
            if (!FixConfig.PreventPrematureOutpatientExit || __instance == null ||
                __instance.m_state == null)
            {
                return true;
            }

            BehaviorPatientStateData state = __instance.m_state;
            if (state.m_department == null || state.m_department.GetEntity() == null ||
                !state.m_department.GetEntity().AcceptsOutpatients())
            {
                return true; // Preserve Vanilla EnsureDepartment / department-not-working handling.
            }

            bool needsStandingTransition;
            if (TryEnsureVanillaWaitingRoom(__instance, out needsStandingTransition))
            {
                if (needsStandingTransition)
                {
                    HoldForStandingTransition(__instance);
                    return false;
                }

                Clear(
                    __instance,
                    "waiting-room-ready");
                return true;
            }

            if (ShouldAllowVanillaMissingRoomExit(__instance))
            {
                StopRetry(
                    __instance,
                    "visit-complete-or-explicit-exit");
                return true;
            }

            HoldForMissingWaitingRoom(__instance);
            return false;
        }

        // While a patient is deliberately held in Idle, prevent the normal
        // Idle -> GoToWaitingRoom / FindQueueMachine loop from firing every frame.
        [HarmonyPatch(typeof(BehaviorPatient), "UpdateStateIdle")]
        [HarmonyPrefix]
        private static bool IdlePrefix(BehaviorPatient __instance)
        {
            if (!FixConfig.PreventPrematureOutpatientExit || __instance == null ||
                __instance.m_state == null || !NextAttempt.ContainsKey(__instance))
            {
                return true;
            }

            BehaviorPatientStateData state = __instance.m_state;

            // Explicit/native terminal transitions and hospitalization requests
            // take precedence over waiting-room recovery. In particular,
            // UpdateStateIdle() must remain free to call CheckHospitalization().
            if (state.m_deathTriggered || state.m_sentHome || state.m_sentAway ||
                state.m_requestedHospitalizationTreatment != null)
            {
                StopRetry(
                    __instance,
                    "terminal-or-hospitalization-transition");
                return true;
            }

            // The patient's clinical state can change while waiting. As soon as
            // Vanilla would consider the visit complete, release the hold.
            if (ShouldAllowVanillaMissingRoomExit(__instance))
            {
                StopRetry(
                    __instance,
                    "visit-complete");
                return true;
            }

            if (Time.realtimeSinceStartup < NextAttempt[__instance])
            {
                return false;
            }

            NextAttempt[__instance] = Time.realtimeSinceStartup + RetryIntervalSeconds;
            if (state.m_usedQueueMachine)
            {
                __instance.GoToWaitingRoom();
            }
            else
            {
                __instance.UpdateStateFindQueueMachine();
            }

            return false;
        }

        // Our first missing-room report uses the native method. Suppress only
        // duplicate reports from the same unresolved recovery episode.
        [HarmonyPatch(typeof(BehaviorPatient), "ReportMissingWaitingRoom")]
        [HarmonyPrefix]
        private static bool MissingWaitingRoomReportPrefix(BehaviorPatient __instance)
        {
            if (!FixConfig.PreventPrematureOutpatientExit || __instance == null)
            {
                return true;
            }

            return NativeReportInProgress.Contains(__instance) ||
                !MissingRoomReported.Contains(__instance);
        }

        [HarmonyPatch(typeof(BehaviorPatient), "SwitchState")]
        [HarmonyPostfix]
        private static void StateChanged(BehaviorPatient __instance, PatientState state)
        {
            if (state != PatientState.Idle)
            {
                Clear(
                    __instance,
                    "state-changed");
            }
        }

        [HarmonyPatch(typeof(InGameMenuController), "Destroy")]
        [HarmonyPostfix]
        private static void ClearOnUnload()
        {
            NextAttempt.Clear();
            MissingRoomReported.Clear();
            NativeReportInProgress.Clear();
            NextTopologyRefresh.Clear();
        }

        private static bool TryEnsureVanillaWaitingRoom(
            BehaviorPatient patient,
            out bool needsStandingTransition)
        {
            needsStandingTransition = false;

            if (MapScriptInterface.Instance == null || Database.Instance == null ||
                patient == null || patient.m_state == null)
            {
                return false;
            }

            BehaviorPatientStateData state = patient.m_state;
            if (state.m_department == null || state.m_department.GetEntity() == null)
            {
                return false;
            }

            patient.CheckStaff();

            Room currentRoom = null;
            if (state.m_waitingRoom != null && state.m_waitingRoom.CheckEntity())
            {
                currentRoom = state.m_waitingRoom.GetEntity();
            }

            if (currentRoom != null)
            {
                if (IsCurrentWaitingRoomEligible(patient, currentRoom))
                {
                    ClearChairOutsideWaitingRoom(patient, currentRoom);
                    if (PreparePhysicalWaitingRoomTransition(patient, currentRoom))
                    {
                        needsStandingTransition = true;
                    }
                    return true;
                }

                // A waiting room can be physically valid while the cached
                // nearby-room topology is stale. Rebuild Vanilla's own data once
                // before discarding an otherwise usable current assignment.
                if (IsBasicWaitingRoomValid(patient, currentRoom))
                {
                    bool currentRoomRefreshPerformed =
                        RefreshNativeWaitingRoomData(
                            patient,
                            currentRoom.GetFloorIndex());

                    if (IsCurrentWaitingRoomEligible(
                            patient,
                            currentRoom))
                    {
                        if (HfxDiagnostics.Enabled)
                        {
                            HfxDiagnostics.Intervention(
                                "PreventPrematureOutpatientExit",
                                "BehaviorPatient waiting-room validation",
                                "current-room-restored-after-topology-check",
                                "patient=" +
                                HfxDiagnostics.EntityLabel(
                                    patient.m_entity) +
                                " floor=" +
                                currentRoom.GetFloorIndex() +
                                " refreshPerformed=" +
                                currentRoomRefreshPerformed);
                        }

                        ClearChairOutsideWaitingRoom(
                            patient,
                            currentRoom);
                        if (PreparePhysicalWaitingRoomTransition(
                                patient,
                                currentRoom))
                        {
                            needsStandingTransition = true;
                        }
                        return true;
                    }
                }
            }

            ClearWaitingRoomAssignment(patient, currentRoom);

            Room replacement =
                FindVanillaWaitingRoom(patient);
            bool foundBeforeRefresh =
                replacement != null;
            bool refreshPerformed = false;
            if (replacement == null)
            {
                // Important distinction: a failed selector is not proof that no
                // waiting room exists. Rebuild the native waiting-room topology
                // for the relevant department/floor(s), then retry the exact
                // Vanilla selector before reporting a missing room.
                refreshPerformed =
                    RefreshNativeWaitingRoomData(
                        patient,
                        -1);
                replacement =
                    FindVanillaWaitingRoom(patient);
            }

            if (replacement == null)
            {
                return false;
            }

            state.m_waitingRoom = replacement;
            replacement.EnqueueCharacter(patient.m_entity);
            ClearChairOutsideWaitingRoom(
                patient,
                replacement);

            if (HfxDiagnostics.Enabled)
            {
                string patientLabel =
                    HfxDiagnostics.EntityLabel(
                        patient.m_entity);

                if (foundBeforeRefresh)
                {
                    HfxDiagnostics.ObservedRateLimited(
                        "PreventPrematureOutpatientExit",
                        "BehaviorPatient waiting-room selector",
                        "normal-assignment",
                        "patient=" + patientLabel +
                        " floor=" +
                        replacement.GetFloorIndex() +
                        " selector=vanilla-equivalent",
                        "PreventPrematureOutpatientExit|normal|" +
                        patientLabel,
                        5000);
                }
                else
                {
                    HfxDiagnostics.Intervention(
                        "PreventPrematureOutpatientExit",
                        "Floor.UpdateWaitingRoomData + native selector",
                        "replacement-found-after-topology-refresh",
                        "patient=" + patientLabel +
                        " floor=" +
                        replacement.GetFloorIndex() +
                        " refreshPerformed=" +
                        refreshPerformed);
                }
            }

            if (PreparePhysicalWaitingRoomTransition(patient, replacement))
            {
                needsStandingTransition = true;
            }

            return true;
        }

        private static bool IsBasicWaitingRoomValid(
            BehaviorPatient patient,
            Room room)
        {
            if (patient == null || patient.m_state == null || room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry !=
                    Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING"))
            {
                return false;
            }

            RoomValidity invalid =
                RoomValidity.INACCESSIBLE |
                RoomValidity.INACCESSIBLE_PATIENTS |
                RoomValidity.OUTDOORS;

            if ((room.m_roomPersistentData.m_valid & invalid) != (RoomValidity)0)
            {
                return false;
            }

            BehaviorPatientStateData state = patient.m_state;
            if (state.m_labSpecialist != null && state.m_labSpecialist.CheckEntity())
            {
                EmployeeComponent employee =
                    state.m_labSpecialist.GetEntity().GetComponent<EmployeeComponent>();
                return employee != null &&
                    employee.m_state != null &&
                    employee.m_state.m_department != null &&
                    employee.m_state.m_department.GetEntity() != null &&
                    room.m_roomPersistentData.m_department != null &&
                    room.m_roomPersistentData.m_department.GetEntity() ==
                        employee.m_state.m_department.GetEntity();
            }

            return state.m_department != null &&
                state.m_department.GetEntity() != null &&
                room.m_roomPersistentData.m_department != null &&
                room.m_roomPersistentData.m_department.GetEntity() ==
                    state.m_department.GetEntity();
        }

        private static bool RefreshNativeWaitingRoomData(
            BehaviorPatient patient,
            int preferredFloorIndex)
        {
            if (patient == null || patient.m_state == null ||
                Hospital.Instance == null)
            {
                return false;
            }

            bool refreshed = false;
            HashSet<Floor> floors = new HashSet<Floor>();

            if (preferredFloorIndex >= 0 &&
                preferredFloorIndex < Hospital.Instance.m_floors.Count)
            {
                floors.Add(Hospital.Instance.m_floors[preferredFloorIndex]);
            }

            BehaviorPatientStateData state = patient.m_state;
            if (state.m_labSpecialist != null && state.m_labSpecialist.CheckEntity())
            {
                EmployeeComponent employee =
                    state.m_labSpecialist.GetEntity().GetComponent<EmployeeComponent>();
                if (employee != null && employee.m_state != null &&
                    employee.m_state.m_homeRoom != null &&
                    employee.m_state.m_homeRoom.GetEntity() != null)
                {
                    int floorIndex =
                        employee.m_state.m_homeRoom.GetEntity().GetFloorIndex();
                    if (floorIndex >= 0 &&
                        floorIndex < Hospital.Instance.m_floors.Count)
                    {
                        floors.Add(Hospital.Instance.m_floors[floorIndex]);
                    }
                }
            }
            else if (state.m_department != null &&
                state.m_department.GetEntity() != null)
            {
                Department department = state.m_department.GetEntity();
                foreach (EntityIDPointer<Room> roomPointer in
                    department.m_departmentPersistentData.m_rooms)
                {
                    if (roomPointer == null || !roomPointer.CheckEntity())
                    {
                        continue;
                    }

                    int floorIndex = roomPointer.GetEntity().GetFloorIndex();
                    if (floorIndex >= 0 &&
                        floorIndex < Hospital.Instance.m_floors.Count)
                    {
                        floors.Add(Hospital.Instance.m_floors[floorIndex]);
                    }
                }
            }

            float now = Time.realtimeSinceStartup;
            foreach (Floor floor in floors)
            {
                if (floor == null)
                {
                    continue;
                }

                float nextRefresh;
                if (NextTopologyRefresh.TryGetValue(floor, out nextRefresh) &&
                    now < nextRefresh)
                {
                    continue;
                }

                floor.UpdateWaitingRoomData();
                NextTopologyRefresh[floor] =
                    now + TopologyRefreshIntervalSeconds;
                refreshed = true;
            }

            return refreshed;
        }

        private static Room FindVanillaWaitingRoom(BehaviorPatient patient)
        {
            BehaviorPatientStateData state = patient.m_state;
            GameDBRoomType waitingRoomType =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING");

            if (state.m_labSpecialist != null && state.m_labSpecialist.CheckEntity())
            {
                EmployeeComponent employee =
                    state.m_labSpecialist.GetEntity().GetComponent<EmployeeComponent>();
                if (employee == null || employee.m_state == null ||
                    employee.m_state.m_department == null ||
                    employee.m_state.m_department.GetEntity() == null ||
                    employee.m_state.m_homeRoom == null ||
                    employee.m_state.m_homeRoom.GetEntity() == null)
                {
                    return null;
                }

                return MapScriptInterface.Instance.FindRoomClosestToOtherRoom(
                    waitingRoomType,
                    employee.m_state.m_department.GetEntity(),
                    employee.m_state.m_homeRoom.GetEntity());
            }

            return MapScriptInterface.Instance.FindLeastFullRoomAroundOutpatientOffice(
                waitingRoomType,
                state.m_department.GetEntity());
        }

        private static bool IsCurrentWaitingRoomEligible(
            BehaviorPatient patient,
            Room room)
        {
            if (room == null || room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry !=
                    Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING"))
            {
                return false;
            }

            RoomValidity invalidCommon =
                RoomValidity.INACCESSIBLE |
                RoomValidity.INACCESSIBLE_PATIENTS |
                RoomValidity.OUTDOORS;

            if ((room.m_roomPersistentData.m_valid & invalidCommon) != (RoomValidity)0)
            {
                return false;
            }

            BehaviorPatientStateData state = patient.m_state;
            if (state.m_labSpecialist != null && state.m_labSpecialist.CheckEntity())
            {
                EmployeeComponent employee =
                    state.m_labSpecialist.GetEntity().GetComponent<EmployeeComponent>();
                if (employee == null || employee.m_state == null ||
                    employee.m_state.m_department == null ||
                    employee.m_state.m_department.GetEntity() == null ||
                    employee.m_state.m_homeRoom == null ||
                    employee.m_state.m_homeRoom.GetEntity() == null)
                {
                    return false;
                }

                if ((room.m_roomPersistentData.m_valid & RoomValidity.MISSING_EQUIPMENT) !=
                    (RoomValidity)0)
                {
                    return false;
                }

                if (room.m_roomPersistentData.m_department == null ||
                    room.m_roomPersistentData.m_department.GetEntity() !=
                        employee.m_state.m_department.GetEntity())
                {
                    return false;
                }

                return room.GetFloorIndex() ==
                    employee.m_state.m_homeRoom.GetEntity().GetFloorIndex();
            }

            if (room.m_roomPersistentData.m_department == null ||
                room.m_roomPersistentData.m_department.GetEntity() !=
                    state.m_department.GetEntity())
            {
                return false;
            }

            // Match FindLeastFullRoomAroundOutpatientOffice(): a clinic waiting
            // room must be near at least one outpatient-capable room.
            GameDBRoomType[] roomTypes = Database.Instance.GetEntries<GameDBRoomType>();
            for (int i = 0; i < roomTypes.Length; i++)
            {
                GameDBRoomType roomType = roomTypes[i];
                if (roomType.AcceptsOutpatients &&
                    room.m_roomPersistentData.m_nearbyRoomTypeCount.ContainsKey(roomType) &&
                    room.m_roomPersistentData.m_nearbyRoomTypeCount[roomType] > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static void ClearChairOutsideWaitingRoom(
            BehaviorPatient patient,
            Room waitingRoom)
        {
            BehaviorPatientStateData state = patient.m_state;
            if (state.m_chair == null || !state.m_chair.CheckEntity())
            {
                return;
            }

            Room chairRoom = MapScriptInterface.Instance.GetRoomAt(state.m_chair.GetEntity());
            if (chairRoom != waitingRoom)
            {
                state.m_chair = null;
            }
        }

        private static bool PreparePhysicalWaitingRoomTransition(
            BehaviorPatient patient,
            Room waitingRoom)
        {
            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk == null || !walk.IsSitting() ||
                walk.m_state.m_objectSittingOn == null ||
                !walk.m_state.m_objectSittingOn.CheckEntity())
            {
                return false;
            }

            Room sittingRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    walk.m_state.m_objectSittingOn.GetEntity());
            if (sittingRoom == waitingRoom)
            {
                return false;
            }

            // DequeueCharacter is idempotent in Vanilla and removes the entity
            // from all three room queue collections. This prevents a ghost queue
            // entry when the administrative waiting room and physical chair diverge.
            if (sittingRoom != null)
            {
                sittingRoom.DequeueCharacter(patient.m_entity);
            }

            return true;
        }

        private static bool ShouldAllowVanillaMissingRoomExit(BehaviorPatient patient)
        {
            BehaviorPatientStateData state = patient.m_state;

            // Explicit player/game decisions to end the visit are authoritative.
            if (state.m_sentHome || state.m_sentAway)
            {
                return true;
            }

            // Death and hospitalization transitions have their own native paths.
            if (state.m_deathTriggered || state.m_requestedHospitalizationTreatment != null)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            if (hospitalization != null && hospitalization.IsHospitalized())
            {
                return false;
            }

            ProcedureComponent procedures = patient.GetComponent<ProcedureComponent>();
            if (procedures == null || procedures.m_state == null ||
                procedures.m_state.m_procedureQueue == null || procedures.IsBusy())
            {
                return false;
            }

            // Player-controlled patients do not auto-finish in SelectNextProcedure;
            // they leave only after an explicit SendHome(), handled above.
            if (patient.GetControlMode() != PatientControlMode.AI)
            {
                return false;
            }

            ProcedureQueue queue = procedures.m_state.m_procedureQueue;
            if (queue.m_plannedExaminationStates.Count > 0 ||
                queue.m_plannedTreatmentStates.Count > 0)
            {
                return false;
            }

            // This is the only lab state SelectNextProcedure() requires the
            // outpatient to handle before considering visit completion.
            // A persistent LabProcedure by itself may legitimately survive until
            // a later visit and therefore must not keep the patient in hospital.
            if (queue.GetFirstLabSampleToDeliver() != null)
            {
                return false;
            }

            if (state.m_medicalCondition == null ||
                state.m_medicalCondition.m_diagnosedMedicalCondition == null)
            {
                return false;
            }

            return patient.HasBeenCorrectlyTreated();
        }

        private static void ClearWaitingRoomAssignment(
            BehaviorPatient patient,
            Room oldRoom)
        {
            BehaviorPatientStateData state = patient.m_state;

            if (oldRoom != null)
            {
                oldRoom.DequeueCharacter(patient.m_entity);
            }

            if (state.m_reservedWaitingRoomTile != Vector2i.ZERO_VECTOR &&
                MapScriptInterface.Instance != null)
            {
                WalkComponent walk = patient.GetComponent<WalkComponent>();
                int floorIndex = oldRoom != null
                    ? oldRoom.GetFloorIndex()
                    : (walk != null ? walk.GetFloorIndex() : 0);

                MapScriptInterface.Instance.FreeTile(
                    state.m_reservedWaitingRoomTile,
                    floorIndex);
                state.m_reservedWaitingRoomTile = Vector2i.ZERO_VECTOR;
            }

            state.m_waitingRoom = null;
            state.m_chair = null;
        }

        private static void HoldForStandingTransition(BehaviorPatient patient)
        {
            WalkComponent walk = patient.GetComponent<WalkComponent>();
            bool targetNeutralized = false;
            string previousTargetDetails = null;

            if (walk != null && walk.IsSitting())
            {
                bool alreadyStandingUp =
                    walk.m_state.m_walkState == WalkState.StandingUp;

                if (HfxDiagnostics.Enabled)
                {
                    previousTargetDetails =
                        " previousDestination=" +
                        walk.m_state.m_destination.m_x + "," +
                        walk.m_state.m_destination.m_y +
                        " previousDestinationFloor=" +
                        walk.m_state.m_destinationFloor;
                }

                NeutralizePendingWalkTarget(
                    walk,
                    alreadyStandingUp);
                targetNeutralized = true;

                if (!alreadyStandingUp)
                {
                    if (walk.m_state.m_objectSittingOn != null &&
                        walk.m_state.m_objectSittingOn.CheckEntity())
                    {
                        // Keep Vanilla's native stand-up animation and release
                        // timing, but never let an older clinical destination
                        // resume after the HFX waiting-room hold.
                        walk.StandUp();
                    }
                    else
                    {
                        walk.Interrupt();
                        patient.GetComponent<AnimModelComponent>()
                            .PlayAnimation("stand_idle");
                    }
                }
            }

            patient.SwitchState(PatientState.Idle);
            NextAttempt[patient] =
                Time.realtimeSinceStartup + StandingRetrySeconds;

            if (HfxDiagnostics.Enabled)
            {
                string patientLabel =
                    HfxDiagnostics.EntityLabel(patient.m_entity);
                HfxDiagnostics.InterventionRateLimited(
                    "PreventPrematureOutpatientExit",
                    "BehaviorPatient.GoToWaitingRoom",
                    "physical-standing-transition-held",
                    "patient=" + patientLabel +
                    " retrySeconds=" + StandingRetrySeconds +
                    " staleTargetNeutralized=" +
                    targetNeutralized +
                    (previousTargetDetails ?? string.Empty),
                    "PreventPrematureOutpatientExit|standing|" + patientLabel,
                    3000);
            }
        }

        private static void HoldForMissingWaitingRoom(BehaviorPatient patient)
        {
            ReportMissingWaitingRoomOnce(patient);

            // If the administrative pointer was already lost, clean any stale
            // physical waiting-room queue entry before leaving the chair.
            PreparePhysicalWaitingRoomTransition(patient, null);

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            bool targetNeutralized = false;
            if (walk != null)
            {
                if (walk.IsSitting())
                {
                    bool alreadyStandingUp =
                        walk.m_state.m_walkState == WalkState.StandingUp;

                    NeutralizePendingWalkTarget(
                        walk,
                        alreadyStandingUp);
                    targetNeutralized = true;

                    if (!alreadyStandingUp)
                    {
                        if (walk.m_state.m_objectSittingOn != null &&
                            walk.m_state.m_objectSittingOn.CheckEntity())
                        {
                            walk.StandUp();
                        }
                        else
                        {
                            walk.Interrupt();
                            patient.GetComponent<AnimModelComponent>()
                                .PlayAnimation("stand_idle");
                        }
                    }
                }
                else if (walk.IsBusy())
                {
                    NeutralizePendingWalkTarget(
                        walk,
                        keepStandingUpState: false);
                    targetNeutralized = true;
                    patient.GetComponent<AnimModelComponent>()
                        .PlayAnimation("stand_idle");
                }
                else
                {
                    patient.GetComponent<AnimModelComponent>()
                        .PlayAnimation("stand_idle");
                }
            }

            patient.SwitchState(PatientState.Idle);
            NextAttempt[patient] =
                Time.realtimeSinceStartup + RetryIntervalSeconds;

            if (HfxDiagnostics.Enabled)
            {
                string patientLabel =
                    HfxDiagnostics.EntityLabel(patient.m_entity);
                HfxDiagnostics.InterventionRateLimited(
                    "PreventPrematureOutpatientExit",
                    "BehaviorPatient.GoToWaitingRoom / UpdateStateFindQueueMachine",
                    "premature-exit-prevented",
                    "patient=" + patientLabel +
                    " state=" + patient.m_state.m_patientState +
                    " usedQueueMachine=" +
                    patient.m_state.m_usedQueueMachine +
                    " selectorEmptyAfterRefresh=true" +
                    " staleTargetNeutralized=" +
                    targetNeutralized +
                    " retrySeconds=" + RetryIntervalSeconds,
                    "PreventPrematureOutpatientExit|missing|" + patientLabel,
                    5000);
            }
        }

        private static void NeutralizePendingWalkTarget(
            WalkComponent walk,
            bool keepStandingUpState)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            if (keepStandingUpState)
            {
                // A normal WalkComponent stand-up resumes m_route when one
                // exists, otherwise it switches back to DestinationSet. Keep
                // the animation/state intact while replacing the stale target
                // with the current position.
                walk.m_state.m_destination =
                    walk.m_state.m_currentPosition;
                walk.m_state.m_destinationFloor =
                    walk.GetFloorIndex();
            }
            else
            {
                // Stop() aborts an active PathfinderJob and resets the
                // destination to the current position without clearing the
                // seated object. That lets us call Vanilla StandUp() next.
                walk.Stop();
            }

            // Stop() does not clear an already-computed route or elevator
            // midpoints. They must not survive this HFX hold, otherwise
            // UpdateStandingUp() can resume the previous clinical route.
            walk.m_route = null;
            walk.m_state.m_walkMidpoint1 = null;
            walk.m_state.m_walkMidpoint2 = null;
        }

        private static void ReportMissingWaitingRoomOnce(BehaviorPatient patient)
        {
            if (MissingRoomReported.Contains(patient))
            {
                return;
            }

            MissingRoomReported.Add(patient);
            if (ReportMissingWaitingRoomMethod == null)
            {
                MissingRoomReported.Remove(patient);
                Plugin.Log.LogWarning(
                    "Could not access BehaviorPatient.ReportMissingWaitingRoom; " +
                    "waiting-room recovery continues without the popup.");
                return;
            }

            NativeReportInProgress.Add(patient);
            try
            {
                ReportMissingWaitingRoomMethod.Invoke(patient, null);
            }
            catch (System.Exception exception)
            {
                MissingRoomReported.Remove(patient);
                Plugin.Log.LogWarning(
                    "Could not show the native missing waiting-room popup: " + exception);
            }
            finally
            {
                NativeReportInProgress.Remove(patient);
            }
        }

        private static void StopRetry(
            BehaviorPatient patient,
            string reason)
        {
            bool wasHeld =
                patient != null &&
                NextAttempt.Remove(patient);

            if (wasHeld &&
                HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "PreventPrematureOutpatientExit",
                    "BehaviorPatient waiting-room recovery",
                    "hold-ended-with-vanilla-control",
                    "patient=" +
                    HfxDiagnostics.EntityLabel(
                        patient.m_entity) +
                    " reason=" + reason +
                    " state=" +
                    patient.m_state.m_patientState);
            }
        }

        private static void Clear(
            BehaviorPatient patient,
            string reason)
        {
            bool wasHeld =
                patient != null &&
                NextAttempt.Remove(patient);

            if (patient != null)
            {
                MissingRoomReported.Remove(patient);
                NativeReportInProgress.Remove(patient);
            }

            if (wasHeld &&
                HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "PreventPrematureOutpatientExit",
                    "BehaviorPatient waiting-room recovery",
                    "waiting-room-recovered-after-hold",
                    "patient=" +
                    HfxDiagnostics.EntityLabel(
                        patient.m_entity) +
                    " reason=" + reason +
                    " state=" +
                    patient.m_state.m_patientState +
                    " waitingRoomValid=" +
                    (patient.m_state.m_waitingRoom != null &&
                     patient.m_state.m_waitingRoom.CheckEntity()));
            }
        }
    }
}
