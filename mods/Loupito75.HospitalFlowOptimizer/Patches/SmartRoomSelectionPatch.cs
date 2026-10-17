using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFlowOptimizer.Patches
{
    internal sealed class SmartRoomSelectionCandidate
    {
        internal Room Room;
        internal ProcedureSceneAvailability Availability;
        internal Entity FreeSpecialist;
        internal bool HasEligibleSpecialist;
        internal float RouteDistance;
        internal int Workload;
        internal int QueueCount;
        internal TileObject AnchorEquipment;
        internal TileObject TransportPoint;
        internal float Score;

        internal bool IsImmediatelyAvailable
        {
            get
            {
                return Availability == ProcedureSceneAvailability.AVAILABLE &&
                    FreeSpecialist != null;
            }
        }

        internal bool IsScorable
        {
            get
            {
                return IsImmediatelyAvailable && RouteDistance >= 0f;
            }
        }
    }

    internal sealed class SmartRoomSelectionDecisionState
    {
        internal Entity Patient;
        internal GameDBExamination Examination;
        internal Department Department;
        internal List<SmartRoomSelectionCandidate> Candidates;
        internal Room VanillaRoom;
        internal SmartRoomSelectionCandidate VanillaCandidate;
        internal SmartRoomSelectionCandidate HfoCandidate;
        internal int ImmediateCandidateCount;
        internal int ScorableCandidateCount;
        internal bool VanillaCaptured;
        internal bool OverrideRequested;
        internal bool KeptVanillaNearTie;
    }

    internal static class SmartRoomSelectionRuntime
    {
        private const int MaximumRememberedStates = 4096;
        private const int MaximumLoggedCandidates = 8;
        private const float DistanceNormalizationTiles = 20f;
        private const float ScoreTieTolerance = 0.03f;

        private static readonly Dictionary<string, string> LastMessages =
            new Dictionary<string, string>();

        [ThreadStatic]
        private static SmartRoomSelectionDecisionState ActiveDecision;

        internal static SmartRoomSelectionDecisionState Prepare(
            GameDBExamination examination,
            Entity patient,
            Department department,
            Room requestedRoom,
            AccessRights accessRights)
        {
            if (!FlowOptimizerConfig.SmartRoomSelection ||
                examination == null ||
                examination.Procedure == null ||
                patient == null ||
                department == null ||
                requestedRoom != null ||
                MapScriptInterface.Instance == null ||
                DayTime.Instance == null ||
                Database.Instance == null)
            {
                return null;
            }

            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();
            WalkComponent walk = patient.GetComponent<WalkComponent>();
            GameDBProcedure procedure = examination.Procedure;

            if (hospitalization == null ||
                !hospitalization.IsHospitalized() ||
                walk == null ||
                walk.Floor == null ||
                procedure.RequiredStatLabQualificationRef == null ||
                procedure.RequiredRoomTags == null ||
                procedure.RequiredRoomTags.Length == 0 ||
                procedure.RequiredEquipment == null ||
                procedure.RequiredEquipment.Length == 0 ||
                procedure.EquipmentFromAnyDepartment ||
                department.IsClosed() ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_rooms == null)
            {
                return null;
            }

            List<Room> rooms =
                GetStructurallyCompatibleRooms(
                    department,
                    procedure.RequiredRoomTags);

            if (rooms.Count < 2)
            {
                return null;
            }

            SmartRoomSelectionDecisionState state =
                new SmartRoomSelectionDecisionState();
            state.Patient = patient;
            state.Examination = examination;
            state.Department = department;
            state.Candidates =
                new List<SmartRoomSelectionCandidate>();

            GameDBEmployeeRole role =
                procedure.EmployeeRoleRef == null
                    ? Database.Instance.GetEntry<GameDBEmployeeRole>(
                        "EMPL_ROLE_LAB_FOR_HOSPITALIZED")
                    : procedure.EmployeeRoleRef.Entry;

            foreach (Room room in rooms)
            {
                SmartRoomSelectionCandidate candidate =
                    EvaluateCandidate(
                        procedure,
                        patient,
                        department,
                        room,
                        role,
                        accessRights);

                state.Candidates.Add(candidate);

                if (candidate.IsImmediatelyAvailable)
                {
                    state.ImmediateCandidateCount++;
                }

                if (candidate.IsScorable)
                {
                    state.ScorableCandidateCount++;

                    if (state.HfoCandidate == null ||
                        candidate.Score <
                            state.HfoCandidate.Score)
                    {
                        state.HfoCandidate = candidate;
                    }
                }
            }

            return state;
        }

        internal static void BeginEvaluation(
            SmartRoomSelectionDecisionState state)
        {
            if (state == null ||
                state.ScorableCandidateCount < 2 ||
                state.HfoCandidate == null ||
                state.HfoCandidate.FreeSpecialist == null)
            {
                return;
            }

            ActiveDecision = state;
        }

        internal static void EndEvaluation(
            SmartRoomSelectionDecisionState state)
        {
            if (ActiveDecision == state)
            {
                ActiveDecision = null;
            }
        }

        internal static void CompareAndMaybeOverrideLabSpecialist(
            Entity patient,
            Department department,
            string tag,
            GameDBSkill skill,
            GameDBEmployeeRole role,
            bool hasToBeFree,
            Shift shift,
            ref Entity vanillaResult)
        {
            SmartRoomSelectionDecisionState state =
                ActiveDecision;

            if (state == null ||
                state.HfoCandidate == null ||
                state.HfoCandidate.Room == null ||
                state.HfoCandidate.FreeSpecialist == null ||
                patient != state.Patient ||
                department != state.Department ||
                !hasToBeFree)
            {
                return;
            }

            if (!state.VanillaCaptured &&
                vanillaResult != null)
            {
                Room vanillaRoom =
                    GetSpecialistHomeRoom(vanillaResult);

                state.VanillaCaptured = true;
                state.VanillaRoom = vanillaRoom;
                state.VanillaCandidate =
                    FindCandidate(
                        state.Candidates,
                        vanillaRoom);

                if (state.VanillaCandidate == null ||
                    !state.VanillaCandidate.IsScorable)
                {
                    return;
                }

                if (state.VanillaCandidate.Room ==
                    state.HfoCandidate.Room)
                {
                    return;
                }

                if (state.VanillaCandidate.Score <=
                    state.HfoCandidate.Score +
                        ScoreTieTolerance)
                {
                    state.KeptVanillaNearTie = true;
                    return;
                }

                state.OverrideRequested = true;
            }

            if (!state.OverrideRequested)
            {
                return;
            }

            Entity selected =
                state.HfoCandidate.FreeSpecialist;

            if (!IsSpecialistStillValid(
                    selected,
                    patient,
                    state.HfoCandidate.Room,
                    skill,
                    role,
                    shift))
            {
                state.OverrideRequested = false;
                return;
            }

            GameDBRoomType roomType =
                state.HfoCandidate.Room.m_roomPersistentData == null ||
                state.HfoCandidate.Room.m_roomPersistentData.m_roomType == null
                    ? null
                    : state.HfoCandidate.Room
                        .m_roomPersistentData
                        .m_roomType
                        .Entry;

            // If vanilla found a specialist under an earlier compatible
            // room tag but HFO selected a room belonging to a later tag,
            // return null for this iteration so ProcedureSceneFactory keeps
            // walking its native RequiredRoomTags loop.
            if (roomType == null ||
                string.IsNullOrEmpty(tag) ||
                !roomType.HasTag(tag))
            {
                vanillaResult = null;
                return;
            }

            vanillaResult = selected;
        }

        internal static void Complete(
            ProcedureComponent procedureComponent,
            ProcedureSceneAvailability result,
            SmartRoomSelectionDecisionState state)
        {
            if (state == null ||
                state.Candidates == null)
            {
                return;
            }

            Room actualRoom =
                result == ProcedureSceneAvailability.AVAILABLE
                    ? GetReservedRoom(procedureComponent)
                    : null;

            if (!state.VanillaCaptured &&
                !state.OverrideRequested &&
                actualRoom != null)
            {
                state.VanillaRoom = actualRoom;
                state.VanillaCandidate =
                    FindCandidate(
                        state.Candidates,
                        actualRoom);
            }

            if (Plugin.Log == null ||
                !FlowOptimizerConfig.TransportDiagnosticLogging)
            {
                return;
            }

            string decision;
            if (result != ProcedureSceneAvailability.AVAILABLE)
            {
                decision = "reservation-failed";
            }
            else if (state.ScorableCandidateCount < 2)
            {
                decision = "no-multi-scorable-choice";
            }
            else if (state.OverrideRequested &&
                state.HfoCandidate != null &&
                actualRoom == state.HfoCandidate.Room)
            {
                decision = "hfo-override-applied";
            }
            else if (state.OverrideRequested)
            {
                decision = "hfo-override-not-applied";
            }
            else if (state.KeptVanillaNearTie)
            {
                decision = "kept-vanilla-near-tie";
            }
            else
            {
                decision = "kept-vanilla";
            }

            string message =
                "patient=" + DescribePatient(state.Patient) +
                "; exam=" + DescribeExamination(state.Examination) +
                "; department=" + DescribeDepartment(state.Department) +
                "; result=" + result +
                "; decision=" + decision +
                "; vanillaRoom=" +
                    DescribeRoom(state.VanillaRoom) +
                "; hfoRoom=" +
                    DescribeRoom(
                        state.HfoCandidate == null
                            ? null
                            : state.HfoCandidate.Room) +
                "; actualRoom=" +
                    DescribeRoom(actualRoom) +
                "; vanillaScore=" +
                    DescribeScore(state.VanillaCandidate) +
                "; hfoScore=" +
                    DescribeScore(state.HfoCandidate) +
                "; workloadWeight=" +
                    FlowOptimizerConfig.SmartRoomWorkloadWeight
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                "; distanceWeight=" +
                    FlowOptimizerConfig.SmartRoomDistanceWeight
                        .ToString(
                            "0.###",
                            CultureInfo.InvariantCulture) +
                "; structuralCandidates=" +
                    state.Candidates.Count.ToString(
                        CultureInfo.InvariantCulture) +
                "; immediateCandidates=" +
                    state.ImmediateCandidateCount.ToString(
                        CultureInfo.InvariantCulture) +
                "; scorableCandidates=" +
                    state.ScorableCandidateCount.ToString(
                        CultureInfo.InvariantCulture) +
                "; candidates=[" +
                    BuildCandidateSummary(state.Candidates) +
                "]";

            string key =
                (state.Patient == null
                    ? "none"
                    : state.Patient.GetEntityID().ToString()) +
                ":" + DescribeExamination(state.Examination);

            string previous;
            if (LastMessages.TryGetValue(key, out previous) &&
                previous == message)
            {
                return;
            }

            if (LastMessages.Count >= MaximumRememberedStates)
            {
                LastMessages.Clear();
            }

            LastMessages[key] = message;
            Plugin.Log.LogInfo(
                "[Diagnostic][SmartRoomSelection] " + message);
        }

        internal static void Clear()
        {
            LastMessages.Clear();
            ActiveDecision = null;
        }

        private static List<Room> GetStructurallyCompatibleRooms(
            Department department,
            string[] requiredRoomTags)
        {
            List<Room> result = new List<Room>();

            foreach (EntityIDPointer<Room> pointer in
                department.m_departmentPersistentData.m_rooms)
            {
                Room room =
                    pointer == null
                        ? null
                        : pointer.GetEntity();

                if (room == null ||
                    room.m_roomPersistentData == null ||
                    room.m_roomPersistentData.m_roomType == null ||
                    room.m_roomPersistentData.m_valid !=
                        RoomValidity.OK)
                {
                    continue;
                }

                GameDBRoomType roomType =
                    room.m_roomPersistentData.m_roomType.Entry;

                if (roomType == null ||
                    !roomType.HasAnyTag(requiredRoomTags) ||
                    result.Contains(room))
                {
                    continue;
                }

                result.Add(room);
            }

            return result;
        }

        private static SmartRoomSelectionCandidate
            EvaluateCandidate(
                GameDBProcedure procedure,
                Entity patient,
                Department department,
                Room room,
                GameDBEmployeeRole role,
                AccessRights accessRights)
        {
            SmartRoomSelectionCandidate result =
                new SmartRoomSelectionCandidate();

            result.Room = room;
            result.Availability =
                ProcedureSceneAvailability.STAFF_UNAVAILABLE_LAB;
            result.RouteDistance = -1f;
            result.Workload = GetWorkload(room);
            result.QueueCount = GetQueueCount(room);
            result.Score = float.MaxValue;

            Entity anySpecialist;
            Entity freeSpecialist;

            FindEligibleSpecialists(
                procedure,
                patient,
                department,
                room,
                role,
                out anySpecialist,
                out freeSpecialist);

            result.HasEligibleSpecialist =
                anySpecialist != null;
            result.FreeSpecialist =
                freeSpecialist;

            if (freeSpecialist == null)
            {
                if (anySpecialist != null)
                {
                    result.Availability =
                        ProcedureSceneAvailability.STAFF_BUSY;
                }

                return result;
            }

            ProcedureScene scene =
                ProcedureSceneFactory.CreateProcedureScene(
                    procedure,
                    patient,
                    null,
                    null,
                    freeSpecialist,
                    accessRights,
                    room,
                    ProcedureSceneType.QUERY,
                    EquipmentListRules.ONLY_FREE);

            if (scene == null)
            {
                result.Availability =
                    ProcedureSceneAvailability.UNKNOWN;
                return result;
            }

            result.Availability =
                scene.m_availability;

            result.AnchorEquipment =
                GetFirstEquipment(scene);

            result.TransportPoint =
                result.AnchorEquipment == null
                    ? null
                    : result.AnchorEquipment.GetPartWithTag(
                        "transport_point");

            if (result.Availability ==
                    ProcedureSceneAvailability.AVAILABLE)
            {
                result.RouteDistance =
                    GetRouteDistance(
                        patient,
                        result.TransportPoint ??
                        result.AnchorEquipment);

                if (result.RouteDistance >= 0f)
                {
                    result.Score =
                        GetScore(
                            result.RouteDistance,
                            result.Workload);
                }
            }

            return result;
        }

        private static float GetScore(
            float routeDistance,
            int workload)
        {
            float normalizedDistance =
                routeDistance /
                DistanceNormalizationTiles;

            float normalizedWorkload =
                Clamp(
                    (float)workload / 100f,
                    0f,
                    1f);

            return
                normalizedWorkload *
                    FlowOptimizerConfig
                        .SmartRoomWorkloadWeight +
                normalizedDistance *
                    FlowOptimizerConfig
                        .SmartRoomDistanceWeight;
        }

        private static void FindEligibleSpecialists(
            GameDBProcedure procedure,
            Entity patient,
            Department department,
            Room room,
            GameDBEmployeeRole role,
            out Entity anySpecialist,
            out Entity freeSpecialist)
        {
            anySpecialist = null;
            freeSpecialist = null;

            if (procedure == null ||
                procedure.RequiredStatLabQualificationRef == null ||
                department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData
                    .m_labSpecialists == null)
            {
                return;
            }

            GameDBSkill skill =
                procedure.RequiredStatLabQualificationRef.Entry;

            Shift shift =
                DayTime.Instance.GetShift();

            foreach (EntityIDPointer<Entity> pointer in
                department.m_departmentPersistentData
                    .m_labSpecialists)
            {
                Entity specialist =
                    pointer == null
                        ? null
                        : pointer.GetEntity();

                if (specialist == null)
                {
                    continue;
                }

                EmployeeComponent employee =
                    specialist.GetComponent<EmployeeComponent>();

                BehaviorLabSpecialist behavior =
                    specialist
                        .GetComponent<BehaviorLabSpecialist>();

                if (employee == null ||
                    employee.m_state == null ||
                    behavior == null ||
                    !employee.HasSkill(skill) ||
                    (role != null &&
                        !employee.HasRole(role)) ||
                    employee.IsFired() ||
                    employee.m_state.m_shift != shift ||
                    employee.m_state.m_homeRoom == null ||
                    employee.m_state.m_homeRoom.GetEntity() !=
                        room)
                {
                    continue;
                }

                if (anySpecialist == null)
                {
                    anySpecialist = specialist;
                }

                if (behavior.IsFree(patient))
                {
                    freeSpecialist = specialist;
                    return;
                }
            }
        }

        private static bool IsSpecialistStillValid(
            Entity specialist,
            Entity patient,
            Room room,
            GameDBSkill skill,
            GameDBEmployeeRole role,
            Shift shift)
        {
            if (specialist == null ||
                patient == null ||
                room == null)
            {
                return false;
            }

            EmployeeComponent employee =
                specialist.GetComponent<EmployeeComponent>();

            BehaviorLabSpecialist behavior =
                specialist.GetComponent<BehaviorLabSpecialist>();

            return
                employee != null &&
                employee.m_state != null &&
                behavior != null &&
                employee.m_state.m_homeRoom != null &&
                employee.m_state.m_homeRoom.GetEntity() == room &&
                !employee.IsFired() &&
                (skill == null ||
                    employee.HasSkill(skill)) &&
                (role == null ||
                    employee.HasRole(role)) &&
                (shift == Shift.ANY ||
                    employee.m_state.m_shift == shift) &&
                behavior.IsFree(patient);
        }

        private static Room GetSpecialistHomeRoom(
            Entity specialist)
        {
            EmployeeComponent employee =
                specialist == null
                    ? null
                    : specialist
                        .GetComponent<EmployeeComponent>();

            return employee == null ||
                employee.m_state == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state
                        .m_homeRoom
                        .GetEntity();
        }

        private static SmartRoomSelectionCandidate
            FindCandidate(
                List<SmartRoomSelectionCandidate> candidates,
                Room room)
        {
            if (candidates == null || room == null)
            {
                return null;
            }

            foreach (SmartRoomSelectionCandidate candidate in
                candidates)
            {
                if (candidate != null &&
                    candidate.Room == room)
                {
                    return candidate;
                }
            }

            return null;
        }

        private static TileObject GetFirstEquipment(
            ProcedureScene scene)
        {
            if (scene == null ||
                scene.m_equipment == null ||
                scene.m_equipment.Length == 0 ||
                scene.m_equipment[0] == null)
            {
                return null;
            }

            return scene.m_equipment[0].GetEntity();
        }

        private static float GetRouteDistance(
            Entity patient,
            TileObject target)
        {
            if (patient == null ||
                target == null)
            {
                return -1f;
            }

            WalkComponent walk =
                patient.GetComponent<WalkComponent>();

            GridMap gridMap =
                GridMap.GetInstance();

            if (walk == null ||
                walk.Floor == null ||
                gridMap == null)
            {
                return -1f;
            }

            Vector2i targetTile =
                target.GetDefaultUseTile();

            return gridMap.GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                target.GetFloorIndex(),
                targetTile,
                AccessRights.PATIENT_PROCEDURE);
        }

        private static int GetWorkload(Room room)
        {
            if (room == null ||
                DayTime.Instance == null)
            {
                return int.MaxValue;
            }

            return
                DayTime.Instance.GetShift() ==
                    Shift.DAY
                    ? room.GetWorkloadDayPercent()
                    : room.GetWorkloadNightPercent();
        }

        private static int GetQueueCount(Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null)
            {
                return int.MaxValue;
            }

            int count = 0;

            if (room.m_roomPersistentData
                    .m_characterQueue != null)
            {
                count +=
                    room.m_roomPersistentData
                        .m_characterQueue.Count;
            }

            if (room.m_roomPersistentData
                    .m_characterQueueLab != null)
            {
                count +=
                    room.m_roomPersistentData
                        .m_characterQueueLab.Count;
            }

            if (room.m_roomPersistentData
                    .m_characterQueueUnits != null)
            {
                count +=
                    room.m_roomPersistentData
                        .m_characterQueueUnits.Count;
            }

            return count;
        }

        private static Room GetReservedRoom(
            ProcedureComponent procedureComponent)
        {
            if (procedureComponent == null ||
                procedureComponent.m_state == null ||
                procedureComponent.m_state
                    .m_reservedProcedureScript == null ||
                procedureComponent.m_state
                    .m_reservedProcedureScript
                    .GetEntity() == null)
            {
                return null;
            }

            ProcedureScript script =
                procedureComponent.m_state
                    .m_reservedProcedureScript
                    .GetEntity();

            if (script.m_stateData == null ||
                script.m_stateData
                    .m_procedureScene == null ||
                script.m_stateData
                    .m_procedureScene
                    .m_room == null)
            {
                return null;
            }

            return script.m_stateData
                .m_procedureScene
                .m_room
                .GetEntity();
        }

        private static string BuildCandidateSummary(
            List<SmartRoomSelectionCandidate> candidates)
        {
            StringBuilder builder =
                new StringBuilder();

            int count =
                Math.Min(
                    candidates.Count,
                    MaximumLoggedCandidates);

            for (int i = 0; i < count; i++)
            {
                if (i > 0)
                {
                    builder.Append(", ");
                }

                SmartRoomSelectionCandidate candidate =
                    candidates[i];

                builder.Append(
                    DescribeRoom(candidate.Room));
                builder.Append("{availability=");
                builder.Append(candidate.Availability);
                builder.Append("; staff=");
                builder.Append(
                    candidate.FreeSpecialist != null
                        ? "free"
                        : candidate.HasEligibleSpecialist
                            ? "busy"
                            : "unavailable");
                builder.Append("; route=");
                builder.Append(
                    candidate.RouteDistance < 0f
                        ? "-"
                        : candidate.RouteDistance
                            .ToString(
                                "0.0",
                                CultureInfo.InvariantCulture));
                builder.Append("; workload=");
                builder.Append(
                    candidate.Workload.ToString(
                        CultureInfo.InvariantCulture));
                builder.Append("; queue=");
                builder.Append(
                    candidate.QueueCount.ToString(
                        CultureInfo.InvariantCulture));
                builder.Append("; score=");
                builder.Append(
                    DescribeScore(candidate));
                builder.Append("}");
            }

            if (candidates.Count >
                MaximumLoggedCandidates)
            {
                builder.Append(", ...+");
                builder.Append(
                    (candidates.Count -
                        MaximumLoggedCandidates)
                    .ToString(
                        CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static string DescribeScore(
            SmartRoomSelectionCandidate candidate)
        {
            return candidate == null ||
                !candidate.IsScorable ||
                candidate.Score == float.MaxValue
                    ? "-"
                    : candidate.Score.ToString(
                        "0.000",
                        CultureInfo.InvariantCulture);
        }

        private static string DescribePatient(
            Entity patient)
        {
            if (patient == null)
            {
                return "<null>";
            }

            string name = patient.Name;
            if (string.IsNullOrEmpty(name))
            {
                name = "<unnamed>";
            }

            return
                name.Trim() +
                "#" +
                patient.GetEntityID();
        }

        private static string DescribeExamination(
            GameDBExamination examination)
        {
            return examination == null
                ? "<null>"
                : examination.DatabaseID.ToString();
        }

        private static string DescribeDepartment(
            Department department)
        {
            GameDBDepartment type =
                department == null
                    ? null
                    : department
                        .GetDepartmentType();

            return type == null
                ? "<null>"
                : type.DatabaseID.ToString();
        }

        private static string DescribeRoom(
            Room room)
        {
            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData
                    .m_roomType == null ||
                room.m_roomPersistentData
                    .m_roomType.Entry == null)
            {
                return "<none>";
            }

            return
                room.m_roomPersistentData
                    .m_roomType.Entry.DatabaseID +
                "#" +
                room.GetEntityID() +
                "@F" +
                room.GetFloorIndex().ToString(
                    CultureInfo.InvariantCulture);
        }

        private static float Clamp(
            float value,
            float minimum,
            float maximum)
        {
            if (value < minimum)
            {
                return minimum;
            }

            if (value > maximum)
            {
                return maximum;
            }

            return value;
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
    internal static class SmartRoomSelectionPatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(
            GameDBExamination examination,
            Entity patient,
            Department department,
            Room room,
            AccessRights accessRights,
            out SmartRoomSelectionDecisionState __state)
        {
            __state =
                SmartRoomSelectionRuntime.Prepare(
                    examination,
                    patient,
                    department,
                    room,
                    accessRights);

            SmartRoomSelectionRuntime
                .BeginEvaluation(__state);
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.First)]
        private static void Postfix(
            ProcedureComponent __instance,
            ProcedureSceneAvailability __result,
            SmartRoomSelectionDecisionState __state)
        {
            SmartRoomSelectionRuntime
                .EndEvaluation(__state);

            SmartRoomSelectionRuntime.Complete(
                __instance,
                __result,
                __state);
        }
    }

    [HarmonyPatch(
        typeof(MapScriptInterface),
        nameof(MapScriptInterface
            .FindLabSpecialistAssingedToARoomTagLowestWorkload),
        new Type[]
        {
            typeof(Entity),
            typeof(Department),
            typeof(string),
            typeof(GameDBSkill),
            typeof(GameDBEmployeeRole),
            typeof(bool),
            typeof(Shift)
        })]
    internal static class SmartRoomSelectionLabSpecialistPatch
    {
        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(
            Entity patient,
            Department department,
            string tag,
            GameDBSkill skill,
            GameDBEmployeeRole role,
            bool hasToBeFree,
            Shift shift,
            ref Entity __result)
        {
            SmartRoomSelectionRuntime
                .CompareAndMaybeOverrideLabSpecialist(
                    patient,
                    department,
                    tag,
                    skill,
                    role,
                    hasToBeFree,
                    shift,
                    ref __result);
        }
    }
}
