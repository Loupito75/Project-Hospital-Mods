using System;
using System.Collections.Generic;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    internal static class LabStaleGuardRuntime
    {
        private const int MaximumRememberedStaleProcedures = 1024;

        private static readonly HashSet<LabProcedure>
            ReportedStaleProcedures =
                new HashSet<LabProcedure>();

        internal static bool ShouldIgnoreForHfo(
            LabProcedure procedure)
        {
            if (!IsNonActionableOutpatientSamplingFinished(
                    procedure))
            {
                return false;
            }

            LogOnce(procedure);
            return true;
        }

        private static bool IsNonActionableOutpatientSamplingFinished(
            LabProcedure procedure)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished ||
                procedure.m_state.m_statLab == null ||
                procedure.m_state.m_statLab.GetEntity() == null ||
                procedure.m_state.m_patient == null)
            {
                return false;
            }

            Entity patient =
                procedure.m_state.m_patient.GetEntity();
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();

            // Vanilla BehaviorLabSpecialist only executes SamplingFinished
            // work for hospitalized patients. Vanilla outpatient delivery
            // also keeps m_statLab null until SwitchLabProceduresToBeingDelivered
            // changes the state away from SamplingFinished. Therefore this
            // combination is not actionable by the lab worker and must not
            // distort HFO ordering/queue load. HFO deliberately does not
            // repair the underlying vanilla procedure state here.
            return hospitalization != null &&
                !hospitalization.IsHospitalized();
        }

        internal static void Clear()
        {
            ReportedStaleProcedures.Clear();
        }

        private static void LogOnce(
            LabProcedure procedure)
        {
            if (!FlowOptimizerConfig.LabDiagnosticLogging ||
                Plugin.Log == null ||
                procedure == null ||
                procedure.m_state == null ||
                ReportedStaleProcedures.Contains(procedure))
            {
                return;
            }

            if (ReportedStaleProcedures.Count >=
                MaximumRememberedStaleProcedures)
            {
                ReportedStaleProcedures.Clear();
            }

            ReportedStaleProcedures.Add(procedure);

            Entity patient =
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            Room statLab =
                procedure.m_state.m_statLab == null
                    ? null
                    : procedure.m_state.m_statLab.GetEntity();
            GameDBExamination examination =
                procedure.m_state.m_examination == null
                    ? null
                    : procedure.m_state.m_examination.Entry;

            float waitMinutes = 0f;
            if (DayTime.Instance != null)
            {
                waitMinutes =
                    DayTime.Instance.RealTimeSecondsToIngameTimeHours(
                        procedure.m_state.m_timeInState) * 60f;
            }

            Plugin.Log.LogInfo(
                "[Diagnostic][LabStaleGuard]" +
                " action=ignored" +
                "; reason=outpatient-sampling-finished-with-assigned-stat-lab" +
                "; examination=" +
                (examination == null
                    ? "unknown"
                    : examination.DatabaseID.ToString()) +
                "; procedureId=" +
                procedure.GetEntityID() +
                "; patient=" +
                (patient == null
                    ? "none"
                    : patient.GetEntityID().ToString()) +
                "; statLab=" +
                (statLab == null
                    ? "none"
                    : statLab.GetEntityID() + "@F" +
                      statLab.GetFloorIndex()) +
                "; waitMinutes=" +
                waitMinutes.ToString("0.0",
                    System.Globalization.CultureInfo.InvariantCulture) +
                "; note=state-left-unchanged-for-HFX.");
        }
    }

    [HarmonyPatch(typeof(LabProcedure), "FindRoom")]
    internal static class LabQueueBalancingPatch
    {
        [HarmonyPrefix]
        private static void FindRoomPrefix(
            LabProcedure __instance,
            out bool __state)
        {
            __state = __instance != null &&
                __instance.m_state != null &&
                __instance.m_state.m_statLab != null;
        }

        [HarmonyPostfix]
        private static void FindRoomPostfix(
            LabProcedure __instance,
            bool onlyFree,
            bool __state)
        {
            if (__state ||
                !FlowOptimizerConfig.LabQueueBalancing ||
                __instance == null ||
                __instance.m_state == null ||
                __instance.m_state.m_statLab == null)
                return;

            Room vanillaRoom = __instance.m_state.m_statLab.GetEntity();
            Entity patient = __instance.m_state.m_patient.GetEntity();
            if (vanillaRoom == null || patient == null)
                return;

            BehaviorPatient patientBehavior = patient.GetComponent<BehaviorPatient>();
            HospitalizationComponent hospitalization = patient.GetComponent<HospitalizationComponent>();
            if (patientBehavior == null || hospitalization == null)
                return;

            GameDBExamination labExamination =
                __instance.m_state.m_examination.Entry.LabTestingExaminationRef.Entry;
            if (labExamination == null || labExamination.Procedure == null ||
                labExamination.Procedure.RequiredRoomTags == null ||
                labExamination.Procedure.RequiredStatLabQualificationRef == null)
                return;

            GameDBEmployeeRole role = hospitalization.IsHospitalized()
                ? Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_LAB_FOR_HOSPITALIZED")
                : Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_LAB");

            List<Room> candidates = new List<Room>();
            Department vanillaDepartment =
                vanillaRoom.m_roomPersistentData.m_department.GetEntity();
            bool hospitalized = hospitalization.IsHospitalized();

            AddCandidateRooms(
                candidates,
                vanillaDepartment,
                labExamination,
                role,
                patient,
                requireFree: true);

            if (candidates.Count == 0 &&
                (hospitalized || !onlyFree))
            {
                AddCandidateRooms(
                    candidates,
                    vanillaDepartment,
                    labExamination,
                    role,
                    patient,
                    requireFree: false);
            }

            if (candidates.Count == 0)
                return;

            Dictionary<Room, int> queueSnapshot =
                OutpatientLabRoutingRuntime.BuildQueueSnapshot();

            Room best = vanillaRoom;
            int bestQueue =
                OutpatientLabRoutingRuntime.GetQueueCount(
                    vanillaRoom,
                    queueSnapshot);
            int bestUsage = GetCurrentUsage(vanillaRoom);

            foreach (Room candidate in candidates)
            {
                int queue =
                    OutpatientLabRoutingRuntime.GetQueueCount(
                        candidate,
                        queueSnapshot);
                int usage = GetCurrentUsage(candidate);
                if (queue < bestQueue || (queue == bestQueue && usage < bestUsage))
                {
                    best = candidate;
                    bestQueue = queue;
                    bestUsage = usage;
                }
            }

            if (FlowOptimizerConfig.LabDiagnosticLogging)
            {
                Plugin.Log.LogInfo("[Diagnostic][LabBalance] exam=" +
                    labExamination.DatabaseID +
                    "; procedureId=" + __instance.GetEntityID() +
                    "; vanillaLab=" + DescribeRoom(vanillaRoom) +
                    "; vanillaQueue=" +
                    OutpatientLabRoutingRuntime.GetQueueCount(
                        vanillaRoom,
                        queueSnapshot) +
                    "; selectedLab=" + DescribeRoom(best) + "; selectedQueue=" + bestQueue +
                    "; candidates=" + candidates.Count);
            }

            if (best != null && best != vanillaRoom)
                __instance.m_state.m_statLab = best;
        }

        private static void AddCandidateRooms(
            List<Room> rooms,
            Department department,
            GameDBExamination examination,
            GameDBEmployeeRole role,
            Entity patient,
            bool requireFree)
        {
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_labSpecialists == null ||
                examination == null ||
                examination.Procedure == null ||
                DayTime.Instance == null)
            {
                return;
            }

            GameDBProcedure procedure = examination.Procedure;
            GameDBSkill skill = procedure.RequiredStatLabQualificationRef.Entry;
            Shift shift = DayTime.Instance.GetShift();

            foreach (GLib.EntityIDPointer<Entity> specialistPointer
                in department.m_departmentPersistentData.m_labSpecialists)
            {
                Entity specialist =
                    specialistPointer == null
                        ? null
                        : specialistPointer.GetEntity();
                if (specialist == null)
                    continue;

                EmployeeComponent employee =
                    specialist.GetComponent<EmployeeComponent>();
                BehaviorLabSpecialist behavior =
                    specialist.GetComponent<BehaviorLabSpecialist>();

                if (employee == null ||
                    employee.m_state == null ||
                    behavior == null ||
                    !employee.HasSkill(skill) ||
                    (role != null && !employee.HasRole(role)) ||
                    employee.IsFired() ||
                    employee.m_state.m_homeRoom == null ||
                    employee.m_state.m_shift != shift ||
                    (requireFree && !behavior.IsFree(patient)))
                {
                    continue;
                }

                Room room = employee.m_state.m_homeRoom.GetEntity();
                if (room == null ||
                    !HasRequiredRoomTag(
                        room,
                        procedure.RequiredRoomTags))
                {
                    continue;
                }

                if (!rooms.Contains(room))
                    rooms.Add(room);
            }
        }

        private static bool HasRequiredRoomTag(Room room, string[] requiredTags)
        {
            if (room == null || requiredTags == null ||
                room.m_roomPersistentData.m_roomType == null)
                return false;

            GameDBRoomType roomType = room.m_roomPersistentData.m_roomType.Entry;
            if (roomType == null)
                return false;

            foreach (string tag in requiredTags)
                if (roomType.HasTag(tag))
                    return true;

            return false;
        }

        private static int GetCurrentUsage(Room room)
        {
            return DayTime.Instance.GetShift() == Shift.DAY
                ? room.GetWorkloadDayPercent()
                : room.GetWorkloadNightPercent();
        }

        private static string DescribeRoom(Room room)
        {
            return room == null ? "none" :
                room.GetEntityID() + "@F" + room.GetFloorIndex();
        }
    }

    internal sealed class OutpatientLabRoutingTarget
    {
        internal Room Room;
        internal int CandidateCount;
        internal int QueueAtSelection;
        internal int UsageAtSelection;
    }

    internal static class OutpatientLabRoutingRuntime
    {
        private const int MaximumRememberedDiagnosticStates = 4096;

        private static readonly Dictionary<LabProcedure, OutpatientLabRoutingTarget>
            Targets = new Dictionary<LabProcedure, OutpatientLabRoutingTarget>();

        private static readonly Dictionary<LabProcedure, string> LastDiagnosticStates =
            new Dictionary<LabProcedure, string>();

        internal static bool TryGetOrSelectTarget(
            BehaviorPatient patientBehavior,
            GameDBExamination examination,
            GameDBEmployeeRole role,
            bool hasToBeFree,
            out LabProcedure procedure,
            out OutpatientLabRoutingTarget target)
        {
            procedure = null;
            target = null;

            if (!FlowOptimizerConfig.LabQueueBalancing ||
                patientBehavior == null ||
                patientBehavior.m_state == null ||
                examination == null ||
                examination.Procedure == null)
                return false;

            ProcedureComponent procedureComponent =
                patientBehavior.GetComponent<ProcedureComponent>();
            if (procedureComponent == null ||
                procedureComponent.m_state == null ||
                procedureComponent.m_state.m_procedureQueue == null)
                return false;

            procedure =
                procedureComponent.m_state.m_procedureQueue.GetFirstLabSampleToDeliver();
            if (!IsEligibleOutpatientProcedure(procedure))
            {
                procedure = null;
                return false;
            }

            GameDBExamination labExamination = GetLabExamination(procedure);
            if (labExamination == null || labExamination != examination ||
                labExamination.Procedure == null ||
                labExamination.Procedure.RequiredRoomTags == null ||
                labExamination.Procedure.RequiredRoomTags.Length == 0 ||
                labExamination.Procedure.RequiredStatLabQualificationRef == null ||
                labExamination.Procedure.RequiredStatLabQualificationRef.Entry == null)
                return false;

            GameDBEmployeeRole effectiveRole = role ??
                Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_LAB");
            Entity patient =
                procedure.m_state.m_patient == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            if (patient == null)
                return false;

            PruneTargets();

            OutpatientLabRoutingTarget existing;
            if (Targets.TryGetValue(procedure, out existing) &&
                IsTargetValid(
                    existing,
                    labExamination,
                    effectiveRole,
                    patient,
                    hasToBeFree))
            {
                target = existing;
                return true;
            }

            if (existing != null)
                Targets.Remove(procedure);

            Department primaryDepartment =
                patientBehavior.m_state.m_department == null
                    ? null
                    : patientBehavior.m_state.m_department.GetEntity();

            List<Room> candidates = new List<Room>();
            AddCandidateRooms(
                candidates,
                primaryDepartment,
                labExamination,
                effectiveRole,
                patient,
                hasToBeFree);

            if (candidates.Count == 0 &&
                labExamination.Procedure.FallbackLabDepartmentRef != null)
            {
                Department fallbackDepartment =
                    MapScriptInterface.Instance.GetDepartmentOfType(
                        labExamination.Procedure.FallbackLabDepartmentRef.Entry);

                if (fallbackDepartment != primaryDepartment)
                {
                    AddCandidateRooms(
                        candidates,
                        fallbackDepartment,
                        labExamination,
                        effectiveRole,
                        patient,
                        hasToBeFree);
                }
            }

            int selectedQueue;
            Room selectedRoom =
                SelectBestRoom(
                    candidates,
                    out selectedQueue);
            if (selectedRoom == null)
                return false;

            target = new OutpatientLabRoutingTarget();
            target.Room = selectedRoom;
            target.CandidateCount = candidates.Count;
            target.QueueAtSelection = selectedQueue;
            target.UsageAtSelection = GetCurrentUsage(selectedRoom);
            Targets[procedure] = target;
            return true;
        }

        internal static Entity FindSpecialist(
            BehaviorPatient patientBehavior,
            GameDBExamination examination,
            GameDBEmployeeRole role,
            bool hasToBeFree,
            OutpatientLabRoutingTarget target)
        {
            if (patientBehavior == null || target == null ||
                target.Room == null || examination == null ||
                examination.Procedure == null)
                return null;

            GameDBEmployeeRole effectiveRole = role ??
                Database.Instance.GetEntry<GameDBEmployeeRole>("EMPL_ROLE_LAB");

            Entity patient = procedurePatient(patientBehavior);
            Entity currentSpecialist =
                patientBehavior.m_state.m_labSpecialist == null
                    ? null
                    : patientBehavior.m_state.m_labSpecialist.GetEntity();

            if (IsCompatibleSpecialistForRoom(
                    currentSpecialist,
                    target.Room,
                    examination,
                    effectiveRole) &&
                (!hasToBeFree ||
                    currentSpecialist.GetComponent<BehaviorLabSpecialist>()
                        .IsFree(patient)))
            {
                return currentSpecialist;
            }

            if (target.Room.m_roomPersistentData == null ||
                target.Room.m_roomPersistentData.m_department == null)
            {
                return null;
            }

            Department department =
                target.Room.m_roomPersistentData.m_department.GetEntity();
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_labSpecialists == null)
            {
                return null;
            }

            foreach (EntityIDPointer<Entity> specialistPointer
                in department.m_departmentPersistentData.m_labSpecialists)
            {
                Entity specialist =
                    specialistPointer == null
                        ? null
                        : specialistPointer.GetEntity();
                if (!IsCompatibleSpecialistForRoom(
                        specialist,
                        target.Room,
                        examination,
                        effectiveRole))
                    continue;

                BehaviorLabSpecialist behavior =
                    specialist.GetComponent<BehaviorLabSpecialist>();
                if (!hasToBeFree || behavior.IsFree(patient))
                    return specialist;
            }

            return null;
        }

        internal static void TryRerouteWaitingRoom(
            BehaviorPatient patientBehavior,
            string source)
        {
            if (!FlowOptimizerConfig.LabQueueBalancing ||
                patientBehavior == null ||
                patientBehavior.m_state == null)
                return;

            ProcedureComponent procedureComponent =
                patientBehavior.GetComponent<ProcedureComponent>();
            if (procedureComponent == null ||
                procedureComponent.m_state == null ||
                procedureComponent.m_state.m_procedureQueue == null)
                return;

            LabProcedure procedure =
                procedureComponent.m_state.m_procedureQueue.GetFirstLabSampleToDeliver();
            if (!IsEligibleOutpatientProcedure(procedure))
                return;

            PruneTargets();

            OutpatientLabRoutingTarget target;
            if (!Targets.TryGetValue(procedure, out target) ||
                target == null || target.Room == null)
                return;

            if (patientBehavior.m_state.m_waitingRoom != null &&
                !patientBehavior.m_state.m_waitingRoom.CheckEntity())
            {
                patientBehavior.m_state.m_waitingRoom = null;
                patientBehavior.m_state.m_chair = null;
                patientBehavior.m_state.m_reservedWaitingRoomTile =
                    Vector2i.ZERO_VECTOR;
            }

            Room waitingBefore =
                patientBehavior.m_state.m_waitingRoom == null
                    ? null
                    : patientBehavior.m_state.m_waitingRoom.GetEntity();

            Department labDepartment =
                target.Room.m_roomPersistentData.m_department.GetEntity();
            GameDBRoomType waitingType =
                Database.Instance.GetEntry<GameDBRoomType>("ROOM_TYPE_WAITING");
            Room waitingTarget =
                labDepartment == null
                    ? null
                    : MapScriptInterface.Instance.FindRoomClosestToOtherRoom(
                        waitingType,
                        labDepartment,
                        target.Room);

            int rerouteRoll =
                GetStableRerouteRoll(procedure, target.Room);
            string rerouteResult = "not-needed";

            if (waitingBefore == null)
            {
                rerouteResult = "initial-target";
            }
            else if (waitingTarget == null)
            {
                rerouteResult = "kept-no-target-waiting-room";
            }
            else if (waitingBefore != waitingTarget)
            {
                if (!CanReroute(patientBehavior.m_state.m_patientState))
                {
                    rerouteResult =
                        "kept-state-" +
                        patientBehavior.m_state.m_patientState;
                }
                else if (rerouteRoll >=
                    FlowOptimizerConfig.LabWaitingRoomRerouteChance)
                {
                    rerouteResult = "kept-random";
                }
                else
                {
                    Entity patient =
                        procedure.m_state.m_patient.GetEntity();

                    if (patientBehavior.m_state.m_patientState ==
                            PatientState.WaitingSitting)
                    {
                        rerouteResult =
                            TryRerouteSeatedWaitingRoom(
                                patientBehavior,
                                patient,
                                waitingBefore,
                                waitingTarget)
                                ? "rerouted-seated"
                                : "kept-no-reroute-position";
                    }
                    else
                    {
                        RerouteWaitingRoom(
                            patientBehavior,
                            patient,
                            waitingBefore);
                        patientBehavior.GoToWaitingRoom();
                        rerouteResult = "rerouted";
                    }
                }
            }

            LogDiagnosticState(
                procedure,
                "exam=" + GetLabExamination(procedure).DatabaseID +
                "; procedureId=" + procedure.GetEntityID() +
                "; patient=" + procedure.m_state.m_patient.GetEntity().GetEntityID() +
                "; source=" + source +
                "; lab=" + DescribeRoom(target.Room) +
                "; queueAtSelection=" + target.QueueAtSelection +
                "; queueNow=" + GetQueueCount(target.Room) +
                "; usageAtSelection=" + target.UsageAtSelection +
                "; candidates=" + target.CandidateCount +
                "; waitingBefore=" + DescribeRoom(waitingBefore) +
                "; waitingTarget=" + DescribeRoom(waitingTarget) +
                "; rerouteChance=" +
                    FlowOptimizerConfig.LabWaitingRoomRerouteChance +
                "; rerouteRoll=" + rerouteRoll +
                "; reroute=" + rerouteResult);
        }

        internal static int GetIncomingTargetCount(Room room)
        {
            if (room == null)
                return 0;

            PruneTargets();

            int count = 0;
            foreach (KeyValuePair<LabProcedure, OutpatientLabRoutingTarget> item
                in Targets)
            {
                LabProcedure procedure = item.Key;
                OutpatientLabRoutingTarget target = item.Value;

                if (procedure != null &&
                    procedure.m_state != null &&
                    procedure.m_state.m_statLab == null &&
                    procedure.m_state.m_labProcedureState ==
                        LabProcedureState.SamplingFinished &&
                    target != null &&
                    target.Room == room)
                {
                    count++;
                }
            }

            return count;
        }

        internal static void Clear()
        {
            Targets.Clear();
            LastDiagnosticStates.Clear();
        }

        private static Entity procedurePatient(BehaviorPatient patientBehavior)
        {
            ProcedureComponent procedureComponent =
                patientBehavior.GetComponent<ProcedureComponent>();
            LabProcedure procedure =
                procedureComponent.m_state.m_procedureQueue.GetFirstLabSampleToDeliver();

            return procedure == null || procedure.m_state == null
                ? null
                : procedure.m_state.m_patient.GetEntity();
        }

        private static bool IsEligibleOutpatientProcedure(
            LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished ||
                procedure.m_state.m_statLab != null)
                return false;

            Entity patient = procedure.m_state.m_patient.GetEntity();
            if (patient == null)
                return false;

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            return hospitalization != null &&
                !hospitalization.IsHospitalized();
        }

        private static GameDBExamination GetLabExamination(
            LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null)
                return null;

            GameDBExamination source =
                procedure.m_state.m_examination.Entry;
            return source == null ||
                source.LabTestingExaminationRef == null
                    ? null
                    : source.LabTestingExaminationRef.Entry;
        }

        private static void AddCandidateRooms(
            List<Room> rooms,
            Department department,
            GameDBExamination examination,
            GameDBEmployeeRole role,
            Entity patient,
            bool requireFree)
        {
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_labSpecialists == null ||
                examination == null ||
                examination.Procedure == null)
            {
                return;
            }

            foreach (EntityIDPointer<Entity> specialistPointer
                in department.m_departmentPersistentData.m_labSpecialists)
            {
                Entity specialist =
                    specialistPointer == null
                        ? null
                        : specialistPointer.GetEntity();
                if (specialist == null)
                    continue;

                EmployeeComponent employee =
                    specialist.GetComponent<EmployeeComponent>();
                BehaviorLabSpecialist behavior =
                    specialist.GetComponent<BehaviorLabSpecialist>();
                if (employee == null ||
                    employee.m_state == null ||
                    employee.m_state.m_homeRoom == null ||
                    behavior == null)
                {
                    continue;
                }

                Room room = employee.m_state.m_homeRoom.GetEntity();
                if (!IsCompatibleSpecialistForRoom(
                        specialist,
                        room,
                        examination,
                        role) ||
                    (requireFree && !behavior.IsFree(patient)))
                {
                    continue;
                }

                if (!rooms.Contains(room))
                    rooms.Add(room);
            }
        }

        private static Room SelectBestRoom(
            List<Room> candidates,
            out int selectedQueue)
        {
            Dictionary<Room, int> queueSnapshot =
                BuildQueueSnapshot();

            Room best = null;
            int bestQueue = int.MaxValue;
            int bestUsage = int.MaxValue;

            foreach (Room candidate in candidates)
            {
                int queue =
                    GetQueueCount(
                        candidate,
                        queueSnapshot);
                int usage = GetCurrentUsage(candidate);

                if (best == null ||
                    queue < bestQueue ||
                    (queue == bestQueue && usage < bestUsage))
                {
                    best = candidate;
                    bestQueue = queue;
                    bestUsage = usage;
                }
            }

            selectedQueue = bestQueue;
            return best;
        }

        private static bool IsTargetValid(
            OutpatientLabRoutingTarget target,
            GameDBExamination examination,
            GameDBEmployeeRole role,
            Entity patient,
            bool requireFree)
        {
            if (target == null ||
                target.Room == null ||
                target.Room.m_roomPersistentData == null ||
                target.Room.m_roomPersistentData.m_department == null)
            {
                return false;
            }

            Department department =
                target.Room.m_roomPersistentData.m_department.GetEntity();
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_labSpecialists == null)
            {
                return false;
            }

            foreach (EntityIDPointer<Entity> specialistPointer
                in department.m_departmentPersistentData.m_labSpecialists)
            {
                Entity specialist =
                    specialistPointer == null
                        ? null
                        : specialistPointer.GetEntity();

                if (!IsCompatibleSpecialistForRoom(
                        specialist,
                        target.Room,
                        examination,
                        role))
                {
                    continue;
                }

                BehaviorLabSpecialist behavior =
                    specialist.GetComponent<BehaviorLabSpecialist>();
                if (!requireFree ||
                    (behavior != null && behavior.IsFree(patient)))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCompatibleSpecialistForRoom(
            Entity specialist,
            Room room,
            GameDBExamination examination,
            GameDBEmployeeRole role)
        {
            if (specialist == null || room == null ||
                examination == null ||
                examination.Procedure == null)
                return false;

            EmployeeComponent employee =
                specialist.GetComponent<EmployeeComponent>();
            BehaviorLabSpecialist behavior =
                specialist.GetComponent<BehaviorLabSpecialist>();
            GameDBSkill skill =
                examination.Procedure.RequiredStatLabQualificationRef.Entry;

            return employee != null &&
                behavior != null &&
                employee.HasSkill(skill) &&
                (role == null || employee.HasRole(role)) &&
                !employee.IsFired() &&
                employee.m_state.m_homeRoom != null &&
                employee.m_state.m_homeRoom.GetEntity() == room &&
                employee.m_state.m_shift == DayTime.Instance.GetShift() &&
                HasRequiredRoomTag(
                    room,
                    examination.Procedure.RequiredRoomTags);
        }

        private static bool HasRequiredRoomTag(
            Room room,
            string[] requiredTags)
        {
            if (room == null || requiredTags == null ||
                room.m_roomPersistentData.m_roomType == null)
                return false;

            GameDBRoomType roomType =
                room.m_roomPersistentData.m_roomType.Entry;
            if (roomType == null)
                return false;

            foreach (string tag in requiredTags)
            {
                if (roomType.HasTag(tag))
                    return true;
            }

            return false;
        }

        internal static Dictionary<Room, int> BuildQueueSnapshot()
        {
            PruneTargets();

            Dictionary<Room, int> counts =
                new Dictionary<Room, int>();

            if (LabProcedureManager.Instance != null &&
                LabProcedureManager.Instance.m_labProcedures != null)
            {
                foreach (LabProcedure procedure
                    in LabProcedureManager.Instance.m_labProcedures)
                {
                    if (procedure == null ||
                        procedure.m_state == null ||
                        procedure.m_state.m_statLab == null ||
                        procedure.m_state.m_labProcedureState ==
                            LabProcedureState.Finished ||
                        LabStaleGuardRuntime.ShouldIgnoreForHfo(
                            procedure))
                    {
                        continue;
                    }

                    Room room =
                        procedure.m_state.m_statLab.GetEntity();
                    IncrementQueueCount(
                        counts,
                        room);
                }
            }

            foreach (KeyValuePair<LabProcedure, OutpatientLabRoutingTarget> item
                in Targets)
            {
                LabProcedure procedure = item.Key;
                OutpatientLabRoutingTarget target = item.Value;

                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_statLab != null ||
                    procedure.m_state.m_labProcedureState !=
                        LabProcedureState.SamplingFinished ||
                    target == null ||
                    target.Room == null)
                {
                    continue;
                }

                IncrementQueueCount(
                    counts,
                    target.Room);
            }

            return counts;
        }

        internal static int GetQueueCount(
            Room room,
            Dictionary<Room, int> queueSnapshot)
        {
            if (room == null)
            {
                return int.MaxValue;
            }

            int count;
            return queueSnapshot != null &&
                queueSnapshot.TryGetValue(
                    room,
                    out count)
                    ? count
                    : 0;
        }

        private static int GetQueueCount(Room room)
        {
            return GetQueueCount(
                room,
                BuildQueueSnapshot());
        }

        private static void IncrementQueueCount(
            Dictionary<Room, int> counts,
            Room room)
        {
            if (counts == null || room == null)
            {
                return;
            }

            int current;
            counts.TryGetValue(
                room,
                out current);
            counts[room] = current + 1;
        }

        private static int GetCurrentUsage(Room room)
        {
            if (room == null)
                return int.MaxValue;

            return DayTime.Instance.GetShift() == Shift.DAY
                ? room.GetWorkloadDayPercent()
                : room.GetWorkloadNightPercent();
        }

        private static bool CanReroute(PatientState state)
        {
            switch (state)
            {
                case PatientState.Idle:
                case PatientState.Wandering:
                case PatientState.GoingToWaitingRoom:
                case PatientState.WaitingGoingToChair:
                case PatientState.WaitingSitting:
                case PatientState.WaitingStandingIdle:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryRerouteSeatedWaitingRoom(
            BehaviorPatient patientBehavior,
            Entity patient,
            Room waitingBefore,
            Room waitingTarget)
        {
            if (patientBehavior == null ||
                patientBehavior.m_state == null ||
                patient == null ||
                waitingBefore == null ||
                waitingTarget == null ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            WalkComponent walk =
                patient.GetComponent<WalkComponent>();
            if (walk == null ||
                !walk.IsSitting() ||
                walk.m_state.m_objectSittingOn == null ||
                walk.m_state.m_objectSittingOn.GetEntity() == null)
            {
                return false;
            }

            AccessRights accessRights =
                patientBehavior.GetAccessRights();

            TileObject chair =
                MapScriptInterface.Instance
                    .FindClosestFreeObjectWithTag(
                        patient,
                        null,
                        walk.GetCurrentTile(),
                        waitingTarget,
                        "sitting",
                        accessRights);

            Vector2i standingPosition =
                Vector2i.ZERO_VECTOR;
            if (chair == null)
            {
                standingPosition =
                    MapScriptInterface.Instance
                        .GetRandomFreePosition(
                            waitingTarget,
                            accessRights);

                if (standingPosition ==
                    Vector2i.ZERO_VECTOR)
                {
                    standingPosition =
                        MapScriptInterface.Instance
                            .FindClosestTileWithFree3x3Area(
                                waitingTarget.GetCenter()
                                    .ToVector2i(),
                                waitingTarget.GetFloorIndex());
                }

                if (standingPosition ==
                    Vector2i.ZERO_VECTOR)
                {
                    return false;
                }
            }

            if (patientBehavior.m_state.m_reservedWaitingRoomTile !=
                    Vector2i.ZERO_VECTOR)
            {
                MapScriptInterface.Instance.FreeTile(
                    patientBehavior.m_state.m_reservedWaitingRoomTile,
                    waitingBefore.GetFloorIndex());
                patientBehavior.m_state.m_reservedWaitingRoomTile =
                    Vector2i.ZERO_VECTOR;
            }

            waitingBefore.DequeueCharacter(patient);
            patientBehavior.m_state.m_waitingRoom =
                waitingTarget;
            waitingTarget.EnqueueCharacter(patient);

            if (chair != null)
            {
                // Keep WalkComponent.m_objectSittingOn intact here.
                // Vanilla pathfinding starts from the current chair,
                // calls StandUp() when the route is ready, releases
                // that chair after the stand-up animation, then walks
                // to and sits on the new chair.
                patientBehavior.m_state.m_chair = chair;
                walk.GoSit(chair);
                patientBehavior.SwitchState(
                    PatientState.WaitingGoingToChair);
                return true;
            }

            patientBehavior.m_state.m_chair = null;
            MapScriptInterface.Instance.ReserveTile(
                standingPosition,
                patient,
                waitingTarget.GetFloorIndex());
            patientBehavior.m_state.m_reservedWaitingRoomTile =
                standingPosition;
            walk.SetDestination(
                standingPosition,
                waitingTarget.GetFloorIndex());
            patientBehavior.SwitchState(
                PatientState.GoingToWaitingRoom);
            return true;
        }

        private static void RerouteWaitingRoom(
            BehaviorPatient patientBehavior,
            Entity patient,
            Room waitingBefore)
        {
            if (patient == null)
                return;

            WalkComponent walk = patient.GetComponent<WalkComponent>();
            if (walk != null)
            {
                if (walk.m_state.m_objectSittingOn != null)
                    walk.MoveTo(null, false);

                walk.Interrupt();
            }

            UseComponent use = patient.GetComponent<UseComponent>();
            if (use != null && use.IsUsing())
                use.Interrupt();

            if (patientBehavior.m_state.m_reservedWaitingRoomTile !=
                    Vector2i.ZERO_VECTOR &&
                waitingBefore != null)
            {
                MapScriptInterface.Instance.FreeTile(
                    patientBehavior.m_state.m_reservedWaitingRoomTile,
                    waitingBefore.GetFloorIndex());
                patientBehavior.m_state.m_reservedWaitingRoomTile =
                    Vector2i.ZERO_VECTOR;
            }

            patientBehavior.FreeWaitingRoom();
            patientBehavior.m_state.m_chair = null;
        }

        private static int GetStableRerouteRoll(
            LabProcedure procedure,
            Room room)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 +
                    procedure.GetEntityID().GetHashCode();
                hash = hash * 31 +
                    room.GetEntityID().GetHashCode();
                hash ^= hash >> 16;
                return (hash & int.MaxValue) % 100;
            }
        }

        private static string DescribeRoom(Room room)
        {
            return room == null
                ? "none"
                : room.GetEntityID() + "@F" +
                    room.GetFloorIndex();
        }

        private static void LogDiagnosticState(
            LabProcedure procedure,
            string message)
        {
            if (!FlowOptimizerConfig.LabDiagnosticLogging ||
                Plugin.Log == null ||
                procedure == null)
                return;

            if (LastDiagnosticStates.Count >=
                MaximumRememberedDiagnosticStates)
            {
                LastDiagnosticStates.Clear();
            }

            string previous;
            if (LastDiagnosticStates.TryGetValue(
                    procedure,
                    out previous) &&
                previous == message)
                return;

            LastDiagnosticStates[procedure] = message;
            Plugin.Log.LogInfo(
                "[Diagnostic][LabRoute] " + message);
        }

        private static void PruneTargets()
        {
            if (Targets.Count == 0)
                return;

            List<LabProcedure> stale =
                new List<LabProcedure>();

            foreach (KeyValuePair<LabProcedure, OutpatientLabRoutingTarget> item
                in Targets)
            {
                if (!IsEligibleOutpatientProcedure(item.Key) ||
                    item.Value == null ||
                    item.Value.Room == null)
                {
                    stale.Add(item.Key);
                }
            }

            foreach (LabProcedure procedure in stale)
            {
                Targets.Remove(procedure);
                LastDiagnosticStates.Remove(procedure);
            }
        }
    }

    [HarmonyPatch(
        typeof(BehaviorPatient),
        "FindLabSpecialist",
        new Type[]
        {
            typeof(GameDBExamination),
            typeof(bool),
            typeof(GameDBEmployeeRole)
        })]
    internal static class OutpatientLabSpecialistRoutingPatch
    {
        [HarmonyPrefix]
        private static bool FindLabSpecialistPrefix(
            BehaviorPatient __instance,
            GameDBExamination examination,
            bool hasToBeFree,
            GameDBEmployeeRole role,
            ref Entity __result)
        {
            LabProcedure procedure;
            OutpatientLabRoutingTarget target;
            if (!OutpatientLabRoutingRuntime.TryGetOrSelectTarget(
                    __instance,
                    examination,
                    role,
                    hasToBeFree,
                    out procedure,
                    out target))
            {
                return true;
            }

            __result = OutpatientLabRoutingRuntime.FindSpecialist(
                __instance,
                examination,
                role,
                hasToBeFree,
                target);

            return false;
        }
    }

    [HarmonyPatch(typeof(BehaviorPatient), "FindDoctorOrLabSpecialist")]
    internal static class OutpatientLabWaitingRoomRefreshPatch
    {
        [HarmonyPostfix]
        private static void FindDoctorOrLabSpecialistPostfix(
            BehaviorPatient __instance,
            bool onlyAssignSpecialist)
        {
            // TryToDeliverSample() calls FindDoctorOrLabSpecialist(true)
            // and then GoToWaitingRoom() itself. Let that vanilla call
            // finish first; the TryToDeliverSample postfix below applies
            // any late HFO reroute once, without a second synchronous
            // GoToWaitingRoom() fighting the seated transition.
            if (onlyAssignSpecialist)
                return;

            OutpatientLabRoutingRuntime.TryRerouteWaitingRoom(
                __instance,
                "staff-selection");
        }
    }

    [HarmonyPatch(typeof(BehaviorPatient), "TryToDeliverSample")]
    internal static class OutpatientLabRoutePatch
    {
        [HarmonyPostfix]
        private static void TryToDeliverSamplePostfix(
            BehaviorPatient __instance)
        {
            OutpatientLabRoutingRuntime.TryRerouteWaitingRoom(
                __instance,
                "deliver-sample");
        }
    }

    [HarmonyPatch(typeof(BehaviorLabSpecialist), "SelectNextLabProcedureStep")]
    internal static class LabTaskPickedDiagnosticPatch
    {
        [HarmonyPrefix]
        private static void SelectNextLabProcedureStepPrefix(
            LabProcedure procedure,
            out LabProcedureState __state)
        {
            __state = procedure == null || procedure.m_state == null
                ? LabProcedureState.Finished
                : procedure.m_state.m_labProcedureState;
        }

        [HarmonyPostfix]
        private static void SelectNextLabProcedureStepPostfix(
            BehaviorLabSpecialist __instance,
            LabProcedure procedure,
            LabProcedureState __state,
            bool __result)
        {
            if (!FlowOptimizerConfig.SmartLabTaskAssignment ||
                !FlowOptimizerConfig.LabDiagnosticLogging ||
                !__result || __instance == null ||
                procedure == null || procedure.m_state == null ||
                Plugin.Log == null)
                return;

            EmployeeComponent employee =
                __instance.GetComponent<EmployeeComponent>();
            Room homeLab = employee == null ||
                employee.m_state == null ||
                employee.m_state.m_homeRoom == null
                ? null
                : employee.m_state.m_homeRoom.GetEntity();

            Entity technologist = employee == null
                ? null
                : employee.m_entity;

            Plugin.Log.LogInfo(
                "[Diagnostic][LabTaskPicked] technologist=" +
                (technologist == null ? "unknown" : technologist.Name) +
                "; technologistId=" +
                (technologist == null
                    ? "none"
                    : technologist.GetEntityID().ToString()) +
                "; homeLab=" +
                (homeLab == null
                    ? "none"
                    : homeLab.GetEntityID() + "@F" +
                        homeLab.GetFloorIndex()) +
                "; homeLabType=" +
                SmartLabTaskAssignmentPatch.DescribeRoomType(
                    homeLab) +
                "; originalState=" + __state +
                "; task=" +
                SmartLabTaskAssignmentPatch.DescribeProcedure(
                    procedure,
                    homeLab));
        }
    }

    [HarmonyPatch(typeof(LabProcedureManager), "GetIdleLabProcedures")]
    internal static class SmartLabTaskAssignmentPatch
    {
        private const int MaximumRememberedOrderStates = 512;

        private static readonly Dictionary<Room, string>
            LastOrderDiagnosticStates =
                new Dictionary<Room, string>();

        [HarmonyPostfix]
        private static void GetIdleLabProceduresPostfix(Room statLab,
            ref List<LabProcedure> __result)
        {
            if (!FlowOptimizerConfig.SmartLabTaskAssignment ||
                statLab == null ||
                __result == null ||
                __result.Count == 0)
            {
                return;
            }

            int ignoredStaleCount = 0;
            for (int i = __result.Count - 1; i >= 0; i--)
            {
                if (LabStaleGuardRuntime.ShouldIgnoreForHfo(
                        __result[i]))
                {
                    __result.RemoveAt(i);
                    ignoredStaleCount++;
                }
            }

            if (__result.Count == 0)
            {
                return;
            }

            Dictionary<LabProcedure, int> vanillaOrder =
                new Dictionary<LabProcedure, int>();
            for (int i = 0; i < __result.Count; i++)
            {
                vanillaOrder[__result[i]] = i;
            }

            LabProcedure vanillaFirst =
                FindByVanillaOrder(
                    __result,
                    vanillaOrder,
                    0);

            if (__result.Count > 1)
            {
                __result.Sort(delegate(LabProcedure left, LabProcedure right)
                {
                    return Compare(left, right, statLab, vanillaOrder);
                });
            }

            if (FlowOptimizerConfig.LabDiagnosticLogging &&
                Plugin.Log != null)
            {
                int localCount =
                    CountLocalProcedures(__result, statLab);
                int fallbackCount =
                    __result.Count - localCount;
                string diagnosticState =
                    BuildOrderDiagnosticState(
                        __result,
                        statLab,
                        vanillaFirst,
                        ignoredStaleCount,
                        localCount,
                        fallbackCount);

                string previousState;
                bool changed =
                    !LastOrderDiagnosticStates.TryGetValue(
                        statLab,
                        out previousState) ||
                    previousState != diagnosticState;

                if (changed)
                {
                    if (LastOrderDiagnosticStates.Count >=
                        MaximumRememberedOrderStates)
                    {
                        LastOrderDiagnosticStates.Clear();
                    }

                    LastOrderDiagnosticStates[statLab] =
                        diagnosticState;

                    Plugin.Log.LogInfo(
                        "[Diagnostic][LabTaskOrder] lab=" +
                        statLab.GetEntityID() + "@F" +
                        statLab.GetFloorIndex() +
                        "; count=" + __result.Count +
                        "; ignoredStale=" +
                        ignoredStaleCount +
                        "; localCount=" + localCount +
                        "; fallbackCount=" +
                        fallbackCount +
                        "; vanillaFirst=" +
                        DescribeProcedure(
                            vanillaFirst,
                            statLab) +
                        "; selectedFirst=" +
                        DescribeProcedure(
                            __result[0],
                            statLab));
                }
            }
        }

        private static string BuildOrderDiagnosticState(
            List<LabProcedure> procedures,
            Room statLab,
            LabProcedure vanillaFirst,
            int ignoredStaleCount,
            int localCount,
            int fallbackCount)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append(procedures.Count);
            builder.Append('|');
            builder.Append(ignoredStaleCount);
            builder.Append('|');
            builder.Append(localCount);
            builder.Append('|');
            builder.Append(fallbackCount);
            builder.Append("|V:");
            AppendProcedureDiagnosticKey(
                builder,
                vanillaFirst,
                statLab);
            builder.Append("|S:");
            AppendProcedureDiagnosticKey(
                builder,
                procedures.Count == 0
                    ? null
                    : procedures[0],
                statLab);
            builder.Append("|O:");

            for (int i = 0; i < procedures.Count; i++)
            {
                if (i > 0)
                {
                    builder.Append(',');
                }

                AppendProcedureDiagnosticKey(
                    builder,
                    procedures[i],
                    statLab);
            }

            return builder.ToString();
        }

        private static void AppendProcedureDiagnosticKey(
            StringBuilder builder,
            LabProcedure procedure,
            Room statLab)
        {
            if (procedure == null ||
                procedure.m_state == null)
            {
                builder.Append("none");
                return;
            }

            builder.Append(procedure.GetEntityID());
            builder.Append(':');
            builder.Append(
                procedure.m_state.m_labProcedureState);
            builder.Append(':');
            builder.Append(
                IsLocalToLab(procedure, statLab)
                    ? 'L'
                    : 'F');
            builder.Append(':');

            Room equipmentRoom =
                GetEquipmentRoom(procedure);
            builder.Append(
                equipmentRoom == null
                    ? "none"
                    : equipmentRoom.GetEntityID().ToString());
        }

        internal static void ClearDiagnosticState()
        {
            LastOrderDiagnosticStates.Clear();
        }

        private static int Compare(LabProcedure left, LabProcedure right,
            Room statLab, Dictionary<LabProcedure, int> vanillaOrder)
        {
            bool leftLocal = IsLocalToLab(left, statLab);
            bool rightLocal = IsLocalToLab(right, statLab);
            if (leftLocal != rightLocal)
                return leftLocal ? -1 : 1;

            float leftScore = GetEffectivePriority(left);
            float rightScore = GetEffectivePriority(right);
            int scoreComparison = rightScore.CompareTo(leftScore);
            if (scoreComparison != 0)
                return scoreComparison;

            float leftAge = GetWaitingMinutes(left);
            float rightAge = GetWaitingMinutes(right);
            int ageComparison = rightAge.CompareTo(leftAge);
            if (ageComparison != 0)
                return ageComparison;

            return vanillaOrder[left].CompareTo(vanillaOrder[right]);
        }

        internal static bool IsLocalToLab(LabProcedure procedure, Room statLab)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_statLab == null || statLab == null ||
                procedure.m_state.m_statLab.GetEntity() != statLab)
            {
                return false;
            }

            if (procedure.m_state.m_labProcedureState !=
                LabProcedureState.ProcessingResults)
            {
                return true;
            }

            TileObject equipment =
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            if (equipment == null || MapScriptInterface.Instance == null)
            {
                return false;
            }

            Room equipmentRoom =
                MapScriptInterface.Instance.GetRoomAt(
                    equipment.m_state.m_position,
                    equipment.GetFloorIndex());

            return equipmentRoom == statLab;
        }

        private static int CountLocalProcedures(List<LabProcedure> procedures,
            Room statLab)
        {
            int count = 0;
            foreach (LabProcedure procedure in procedures)
                if (IsLocalToLab(procedure, statLab))
                    count++;
            return count;
        }

        private static float GetEffectivePriority(LabProcedure procedure)
        {
            return GetWaitingMinutes(procedure) + GetWorkflowBonusMinutes(procedure);
        }

        private static float GetWaitingMinutes(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null || DayTime.Instance == null)
                return 0f;

            return DayTime.Instance.RealTimeSecondsToIngameTimeHours(
                procedure.m_state.m_timeInState) * 60f;
        }

        private static float GetWorkflowBonusMinutes(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null)
                return 0f;

            float stateFactor;
            switch (procedure.m_state.m_labProcedureState)
            {
                case LabProcedureState.DeliveredToStatLab:
                case LabProcedureState.ProcessingResults:
                    stateFactor = 1.0f;
                    break;
                case LabProcedureState.SamplingFinished:
                    stateFactor = 0.67f;
                    break;
                case LabProcedureState.ReadyForTesting:
                    stateFactor = 0.33f;
                    break;
                default:
                    return 0f;
            }

            float baseMinutes =
                FlowOptimizerConfig.LabTaskPriorityTimeMinutes * stateFactor;
            int variationMinutes =
                FlowOptimizerConfig.LabTaskPriorityRandomnessMinutes;
            if (variationMinutes <= 0)
                return baseMinutes;

            int hash = GetStableProcedureHash(procedure);
            int range = variationMinutes * 2 + 1;
            int variation = (hash % range) - variationMinutes;
            float result = baseMinutes + variation;
            return result < 0f ? 0f : result;
        }

        private static int GetStableProcedureHash(LabProcedure procedure)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + procedure.GetEntityID().GetHashCode();
                hash = hash * 31 + (int)procedure.m_state.m_labProcedureState;

                GameDBExamination examination = procedure.m_state.m_examination.Entry;
                if (examination != null)
                    hash = hash * 31 + examination.DatabaseID.GetHashCode();

                return hash & int.MaxValue;
            }
        }

        private static LabProcedure FindByVanillaOrder(List<LabProcedure> procedures,
            Dictionary<LabProcedure, int> vanillaOrder, int order)
        {
            foreach (LabProcedure procedure in procedures)
                if (vanillaOrder[procedure] == order)
                    return procedure;
            return null;
        }

        internal static string DescribeProcedure(LabProcedure procedure, Room statLab)
        {
            if (procedure == null || procedure.m_state == null)
                return "none";

            GameDBExamination examinationEntry =
                procedure.m_state.m_examination == null
                    ? null
                    : procedure.m_state.m_examination.Entry;
            string examination = examinationEntry == null
                ? "unknown"
                : examinationEntry.DatabaseID.ToString();

            Entity patient = procedure.m_state.m_patient == null
                ? null
                : procedure.m_state.m_patient.GetEntity();
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();
            bool hospitalized =
                hospitalization != null &&
                hospitalization.IsHospitalized();

            Room procedureLab = procedure.m_state.m_statLab == null
                ? null
                : procedure.m_state.m_statLab.GetEntity();
            string procedureLabDescription =
                DescribeRoom(procedureLab);

            TileObject equipment = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            string equipmentDescription = equipment == null
                ? "none"
                : "F" + equipment.GetFloorIndex() + ":" +
                    equipment.m_state.m_position;
            string equipmentId = equipment == null
                ? "none"
                : equipment.GetEntityID().ToString();

            Room equipmentRoom =
                GetEquipmentRoom(procedure);
            string equipmentRoomRelation;
            if (equipmentRoom == null)
            {
                equipmentRoomRelation = "NONE";
            }
            else if (procedureLab == null)
            {
                equipmentRoomRelation = "NO_STAT_LAB";
            }
            else
            {
                equipmentRoomRelation =
                    equipmentRoom == procedureLab
                        ? "SAME"
                        : "DIFFERENT";
            }

            return examination +
                ":procedureId=" + procedure.GetEntityID() +
                ":" + procedure.m_state.m_labProcedureState +
                ":age=" + ((int)procedure.m_state.m_timeInState) +
                ":waitMin=" + ((int)GetWaitingMinutes(procedure)) +
                ":score=" + ((int)GetEffectivePriority(procedure)) +
                ":scope=" + (IsLocalToLab(procedure, statLab)
                    ? "LOCAL"
                    : "FALLBACK") +
                ":patient=" + (patient == null
                    ? "none"
                    : patient.GetEntityID().ToString()) +
                ":hospitalized=" + hospitalized +
                ":requiredLabTags=" +
                    GetRequiredLabTags(examinationEntry) +
                ":statLab=" + procedureLabDescription +
                ":statLabType=" +
                    DescribeRoomType(procedureLab) +
                ":equipmentId=" + equipmentId +
                ":equipment=" + equipmentDescription +
                ":equipmentRoom=" +
                    DescribeRoom(equipmentRoom) +
                ":equipmentRoomType=" +
                    DescribeRoomType(equipmentRoom) +
                ":equipmentRoomVsStatLab=" +
                    equipmentRoomRelation +
                ":equipmentUser=" + (equipment != null && equipment.User != null) +
                ":equipmentOwner=" + (equipment != null && equipment.Owner != null);
        }

        internal static string DescribeRoomType(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType == null ||
                room.m_roomPersistentData.m_roomType.Entry == null)
            {
                return "none";
            }

            return room.m_roomPersistentData
                .m_roomType.Entry.DatabaseID.ToString();
        }

        private static string DescribeRoom(Room room)
        {
            return room == null
                ? "none"
                : room.GetEntityID() + "@F" +
                    room.GetFloorIndex();
        }

        private static Room GetEquipmentRoom(
            LabProcedure procedure)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_usedEquipment == null ||
                MapScriptInterface.Instance == null)
            {
                return null;
            }

            TileObject equipment =
                procedure.m_state.m_usedEquipment.GetEntity();
            if (equipment == null)
            {
                return null;
            }

            return MapScriptInterface.Instance.GetRoomAt(
                equipment.m_state.m_position,
                equipment.GetFloorIndex());
        }

        private static string GetRequiredLabTags(
            GameDBExamination samplingExamination)
        {
            if (samplingExamination == null ||
                samplingExamination.LabTestingExaminationRef == null ||
                samplingExamination.LabTestingExaminationRef.Entry == null ||
                samplingExamination.LabTestingExaminationRef.Entry.Procedure == null ||
                samplingExamination.LabTestingExaminationRef.Entry
                    .Procedure.RequiredRoomTags == null ||
                samplingExamination.LabTestingExaminationRef.Entry
                    .Procedure.RequiredRoomTags.Length == 0)
            {
                return "none";
            }

            return string.Join(
                ",",
                samplingExamination.LabTestingExaminationRef.Entry
                    .Procedure.RequiredRoomTags);
        }
    }
}
