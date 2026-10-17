using System;
using System.Collections.Generic;
using System.Reflection;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    [HarmonyPatch(
        typeof(Database),
        nameof(Database.ReadFiles),
        new Type[] { typeof(string), typeof(bool) })]
    internal static class DatabaseReadFilesPatch
    {
        private static void Prefix(bool add)
        {
            if (!add && !ModDatabase.IsLoading)
            {
                ModDatabase.ResetForBaseLoad();
            }
        }

        private static void Postfix(Database __instance, bool add)
        {
            if (!add && !ModDatabase.IsLoading)
            {
                ModDatabase.Load(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(StringTable),
        nameof(StringTable.GetLocalizedText),
        new Type[] { typeof(string), typeof(string[]) })]
    internal static class StringTableStringPatch
    {
        private static bool Prefix(
            StringTable __instance,
            string stringID,
            string[] parameters,
            ref string __result)
        {
            string languageCode = __instance?.GetCurrentLanguage();
            if (string.IsNullOrEmpty(languageCode))
            {
                languageCode = PlayerProfile.Instance.GetCurrentLanguage();
            }

            if (!LocalizationManager.TryGetLocalizedText(languageCode, stringID, parameters, out string localizedText))
            {
                return true;
            }

            __result = localizedText;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(StringTable),
        nameof(StringTable.GetLocalizedText),
        new Type[] { typeof(DatabaseEntry) })]
    internal static class StringTableDatabaseEntryPatch
    {
        private static bool Prefix(
            StringTable __instance,
            DatabaseEntry databaseItem,
            ref string __result)
        {
            if (databaseItem == null)
            {
                return true;
            }

            string languageCode = __instance?.GetCurrentLanguage();
            if (string.IsNullOrEmpty(languageCode))
            {
                languageCode = PlayerProfile.Instance.GetCurrentLanguage();
            }

            string stringId = databaseItem.DatabaseID.ToString();
            if (!LocalizationManager.TryGetLocalizedText(languageCode, stringId, null, out string localizedText))
            {
                return true;
            }

            __result = localizedText;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.IsFree),
        new Type[] { })]
    internal static class PorterNurseAvailabilityPatch
    {
        private static readonly FieldInfo EntityField = AccessTools.Field(typeof(BehaviorNurse), "m_entity");

        private static bool Prefix(BehaviorNurse __instance, ref bool __result)
        {
            Entity entity = EntityField?.GetValue(__instance) as Entity;
            if (!PorterIdentity.IsPorter(entity))
            {
                return true;
            }

            // A porter is deliberately invisible to generic native nurse selectors.
            // HospitalPorters selects it explicitly only for porter roles.
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(EmployeeComponent),
        nameof(EmployeeComponent.IsAvailable),
        new Type[] { })]
    internal static class PorterEmployeeAvailabilityPatch
    {
        private static readonly FieldInfo EntityField = AccessTools.Field(typeof(EmployeeComponent), "m_entity");

        private static bool Prefix(EmployeeComponent __instance, ref bool __result)
        {
            Entity entity = EntityField?.GetValue(__instance) as Entity;
            if (!PorterIdentity.IsPorter(entity))
            {
                return true;
            }

            // Department.ValidateInpatients() counts every available BehaviorNurse in a
            // nurses station toward MinHospitalizationNurses*. A porter must not satisfy
            // that staffing requirement merely because BehaviorNurse is its transport engine.
            __result = false;
            return false;
        }
    }

    internal sealed class PorterPatientFallbackSnapshot
    {
        internal Entity Patient;
        internal TileObject Destination;
        internal int RetryCount;
        internal int NextRetryGameMinute;
        internal bool Released;
    }

    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.SetDestination),
        new Type[]
        {
            typeof(TileObject),
            typeof(LyingState),
            typeof(StretcherUse),
            typeof(BedReservation),
            typeof(Entity),
            typeof(MovementType),
            typeof(Vehicle)
        })]
    internal static class StretcherDestinationPatch
    {
        private sealed class PatientFallbackRequestContext
        {
            internal HospitalizationState HospitalizationState;
            internal LyingState LyingState;
            internal StretcherUse StretcherUse;
            internal BedReservation BedReservation;
            internal MovementType MovementType;
            internal Vehicle Vehicle;
        }

        private sealed class PatientFallbackWait
        {
            internal TileObject Destination;
            internal PatientFallbackRequestContext Context;
            internal int RetryCount;
            internal int NextRetryGameMinute;
            internal int SuppressedSetDestinationCalls;
        }

        private sealed class PatientFallbackRelease
        {
            internal TileObject Destination;
            internal PatientFallbackRequestContext Context;
            internal bool NativeFailureObserved;
            internal int NativeFailureFirstMinute;
            internal int NativeFailureCauseMinute;
            internal string NativeFailureReason;
            internal bool NativeFailureStillBlockedLogged;
        }

        private sealed class NativeFallbackDiagnosticSnapshot
        {
            internal string Reason;
            internal int NursesTotal;
            internal int MissingNurseEntity;
            internal int MissingEmployee;
            internal int WrongShift;
            internal int Training;
            internal int NoHomeRoom;
            internal int WrongDepartment;
            internal int WrongHomeRoom;
            internal int Fired;
            internal int Busy;
            internal int MissingPatientTransfersRole;
            internal int Eligible;
            internal Entity NurseCandidate;
            internal Vehicle RequestedVehicle;
            internal Vehicle SelectedVehicle;
            internal TileObject VehicleCandidate;
            internal bool WheelchairFound;
            internal bool StretcherFound;
            internal bool StretcherFallbackSearched;
            internal bool StretcherFallbackFound;
            internal bool DestinationOccupied;
            internal TileObject DestinationOccupiedBy;
            internal bool DestinationReserved;
            internal Entity DestinationReservedBy;
        }

        private sealed class PatientFallbackPatchState
        {
            internal Entity Patient;
            internal Department Department;
            internal Entity SelectedPorter;
            internal NurseState SelectedPorterStateBeforeAssignment;
            internal bool HasSelectedPorterStateBeforeAssignment;
            internal bool ReleasedToNative;
            internal NativeFallbackDiagnosticSnapshot
                NativeFallbackDiagnostic;
        }

        private const int PatientPorterCapacityWindowMinutes =
            24 * 60;
        private const int PatientPorterCapacityFallbackThreshold =
            3;
        private const int NativeFallbackStillBlockedMinutes =
            15;

        private static readonly FieldInfo EntityField =
            AccessTools.Field(typeof(StretcherComponent), "m_entity");

        private static readonly Dictionary<Entity, PatientFallbackWait>
            FallbackWaits =
                new Dictionary<Entity, PatientFallbackWait>();

        private static readonly Dictionary<Entity, PatientFallbackRelease>
            ReleasedFallbacks =
                new Dictionary<Entity, PatientFallbackRelease>();

        private static readonly Dictionary<Department, List<int>>
            PatientPorterCapacityFallbackEvents =
                new Dictionary<Department, List<int>>();

        private static readonly HashSet<Department>
            PatientPorterCapacityNotifications =
                new HashSet<Department>();

        internal static void ResetFallbackTracking()
        {
            PorterPatientContinuationRuntime.Clear();
            FallbackWaits.Clear();
            ReleasedFallbacks.Clear();
            PatientPorterCapacityFallbackEvents.Clear();
            PatientPorterCapacityNotifications.Clear();
        }

        private static bool Prefix(
            StretcherComponent __instance,
            TileObject bed,
            LyingState lyingState,
            StretcherUse stretcherUse,
            BedReservation freeBed,
            ref Entity movingCharacter,
            MovementType movementType,
            Vehicle vehicle,
            ref bool __result,
            out PatientFallbackPatchState __state)
        {
            __state = new PatientFallbackPatchState();
            if (__instance == null ||
                __instance.m_state == null)
            {
                return true;
            }

            Entity patient = GetPatient(__instance);
            Department department =
                patient == null
                    ? null
                    : patient.GetComponent<BehaviorPatient>()?.GetDepartment();
            __state.Patient = patient;
            __state.Department = department;

            PatientFallbackRequestContext requestContext =
                BuildPatientFallbackRequestContext(
                    patient,
                    lyingState,
                    stretcherUse,
                    freeBed,
                    movementType,
                    vehicle);

            if (PorterPatientContinuationRuntime
                    .TryAcknowledgeNativeContinuationReplay(
                        __instance,
                        bed,
                        lyingState,
                        stretcherUse,
                        freeBed,
                        movementType,
                        vehicle))
            {
                __result = true;
                return false;
            }

            if (movingCharacter != null ||
                __instance.m_state.m_movingCharacter != null)
            {
                return true;
            }

            GameDBEmployeeRole porterRole =
                Database.Instance == null
                    ? null
                    : Database.Instance.GetEntry<GameDBEmployeeRole>(
                        PorterIds.PatientTransportRole);
            if (porterRole == null)
            {
                return true;
            }

            if (patient == null ||
                department == null ||
                department.m_departmentPersistentData.m_nurses == null)
            {
                ClearFallbackTracking(patient);
                return true;
            }

            PatientFallbackWait wait;
            if (FallbackWaits.TryGetValue(patient, out wait))
            {
                if (wait.Context == null)
                {
                    wait.Context = requestContext;
                }
                else if (!IsSamePatientFallbackRequest(
                             wait.Context,
                             requestContext))
                {
                    PorterDiagnostics.Log(
                        "patient fallback request changed: patient=" +
                        (patient.Name ?? string.Empty).Trim() +
                        "; state=waiting" +
                        "; oldContext={" +
                        DescribePatientFallbackRequestContext(
                            wait.Context) +
                        "}; newContext={" +
                        DescribePatientFallbackRequestContext(
                            requestContext) +
                        "}; action=reset-cooldown.");

                    ClearFallbackTracking(patient);
                    wait = null;
                }
                else if (!object.ReferenceEquals(
                             wait.Destination,
                             bed))
                {
                    PorterDiagnostics.Log(
                        "patient fallback destination retargeted: patient=" +
                        (patient.Name ?? string.Empty).Trim() +
                        "; state=waiting" +
                        "; oldDestination={" +
                        DescribePatientFallbackDestination(
                            wait.Destination) +
                        "}; newDestination={" +
                        DescribePatientFallbackDestination(bed) +
                        "}; action=preserve-cooldown.");

                    wait.Destination = bed;
                }
            }

            PatientFallbackRelease release;
            if (ReleasedFallbacks.TryGetValue(
                    patient,
                    out release) &&
                release != null)
            {
                if (release.Context == null)
                {
                    release.Context = requestContext;
                }
                else if (!IsSamePatientFallbackRequest(
                             release.Context,
                             requestContext))
                {
                    PorterDiagnostics.Log(
                        "patient fallback request changed: patient=" +
                        (patient.Name ?? string.Empty).Trim() +
                        "; state=released" +
                        "; oldContext={" +
                        DescribePatientFallbackRequestContext(
                            release.Context) +
                        "}; newContext={" +
                        DescribePatientFallbackRequestContext(
                            requestContext) +
                        "}; action=reset-release.");

                    ClearFallbackTracking(patient);
                    release = null;
                }
                else if (!object.ReferenceEquals(
                             release.Destination,
                             bed))
                {
                    PorterDiagnostics.Log(
                        "patient fallback destination retargeted: patient=" +
                        (patient.Name ?? string.Empty).Trim() +
                        "; state=released" +
                        "; oldDestination={" +
                        DescribePatientFallbackDestination(
                            release.Destination) +
                        "}; newDestination={" +
                        DescribePatientFallbackDestination(bed) +
                        "}; action=preserve-release.");

                    release.Destination = bed;
                }
            }

            if (release != null)
            {
                __state.ReleasedToNative = true;
                if (PorterTransportConfig.EnableDiagnostics)
                {
                    __state.NativeFallbackDiagnostic =
                        BuildNativeFallbackDiagnosticSnapshot(
                            patient,
                            department,
                            bed,
                            vehicle);
                }
                return true;
            }

            int nowMinute = GetPatientFallbackClockMinute();
            if (wait != null &&
                nowMinute < wait.NextRetryGameMinute)
            {
                wait.SuppressedSetDestinationCalls++;
                __result = false;
                return false;
            }

            int maxFallbackRetries =
                PorterTransportConfig.PatientFallbackRetries;
            bool isRetryAttempt = wait != null;
            int scheduledRetryMinute =
                isRetryAttempt
                    ? wait.NextRetryGameMinute
                    : 0;

            // HPO owns patient-transport assignment: only the initial
            // opportunity and due retry instants search for a Porter.
            Entity porter = FindAvailablePorter(
                department,
                porterRole);
            if (porter == null)
            {
                porter = FindAvailableHelperPorter(
                    department,
                    porterRole,
                    patient,
                    bed,
                    vehicle,
                    isRetryAttempt);
            }

            if (isRetryAttempt)
            {
                PorterDiagnostics.Log(
                    "patient fallback retry attempt: patient=" +
                    patient.Name +
                    "; retry=" +
                    (wait.RetryCount + 1) +
                    "/" +
                    maxFallbackRetries +
                    "; scheduledMinute=" +
                    scheduledRetryMinute +
                    "; nowMinute=" +
                    nowMinute +
                    "; suppressedSetDestinationCalls=" +
                    wait.SuppressedSetDestinationCalls +
                    "; result=" +
                    (porter == null
                        ? "no-porter"
                        : "porter-found") +
                    ".");
            }

            if (porter != null)
            {
                BehaviorNurse porterBehavior =
                    porter.GetComponent<BehaviorNurse>();
                if (porterBehavior != null &&
                    porterBehavior.m_state != null)
                {
                    __state.SelectedPorterStateBeforeAssignment =
                        porterBehavior.m_state.m_nurseState;
                    __state.HasSelectedPorterStateBeforeAssignment =
                        true;
                }

                ClearFallbackTracking(patient);
                movingCharacter = porter;
                __state.SelectedPorter = porter;
                return true;
            }

            if (bed == null ||
                maxFallbackRetries <= 0 ||
                !HasConfiguredPatientTransportPorter(
                    department,
                    porterRole))
            {
                ClearFallbackTracking(patient);
                return true;
            }

            int retryMinutes =
                PorterTransportConfig.PatientFallbackRetryMinutes;

            if (wait == null)
            {
                wait = new PatientFallbackWait
                {
                    Destination = bed,
                    Context = requestContext,
                    RetryCount = 0,
                    NextRetryGameMinute =
                        nowMinute + retryMinutes
                };
                FallbackWaits[patient] = wait;

                PorterDiagnostics.Log(
                    "patient fallback initial attempt failed: patient=" +
                    patient.Name +
                    "; nowMinute=" +
                    nowMinute +
                    "; retry=1/" +
                    maxFallbackRetries +
                    "; delayMinutes=" +
                    retryMinutes +
                    "; deadlineMinute=" +
                    wait.NextRetryGameMinute +
                    "; destination={" +
                    DescribePatientFallbackDestination(bed) +
                    "}; note=no Porter search before deadline.");

                __result = false;
                return false;
            }

            wait.RetryCount++;
            if (wait.RetryCount >= maxFallbackRetries)
            {
                FallbackWaits.Remove(patient);
                ReleasedFallbacks[patient] =
                    new PatientFallbackRelease
                    {
                        Destination = bed,
                        Context = requestContext
                    };
                __state.ReleasedToNative = true;

                // Capacity is a Porter-side fact at the handoff boundary:
                // HPO waited, retried and still found no Porter. Count it
                // before vanilla succeeds or fails to assign a Nurse.
                RecordPatientPorterCapacityFallback(
                    department);

                if (PorterTransportConfig.EnableDiagnostics)
                {
                    __state.NativeFallbackDiagnostic =
                        BuildNativeFallbackDiagnosticSnapshot(
                            patient,
                            department,
                            bed,
                            vehicle);
                }

                PorterDiagnostics.Log(
                    "patient fallback: patient=" +
                    patient.Name +
                    "; retries=" +
                    maxFallbackRetries +
                    "; native Nurse released.");

                return true;
            }

            wait.NextRetryGameMinute =
                nowMinute + retryMinutes;
            wait.SuppressedSetDestinationCalls = 0;
            PorterDiagnostics.Log(
                "patient fallback next retry scheduled: patient=" +
                patient.Name +
                "; retry=" +
                (wait.RetryCount + 1) +
                "/" +
                maxFallbackRetries +
                "; nowMinute=" +
                nowMinute +
                "; delayMinutes=" +
                retryMinutes +
                "; deadlineMinute=" +
                wait.NextRetryGameMinute +
                "; note=no Porter search before deadline.");

            __result = false;
            return false;
        }

        private static void Postfix(
            StretcherComponent __instance,
            TileObject bed,
            Vehicle vehicle,
            bool __result,
            PatientFallbackPatchState __state)
        {
            Entity patient =
                __state == null || __state.Patient == null
                    ? GetPatient(__instance)
                    : __state.Patient;

            PatientFallbackRelease release = null;
            if (__state != null &&
                __state.ReleasedToNative &&
                patient != null)
            {
                ReleasedFallbacks.TryGetValue(
                    patient,
                    out release);

                if (!__result)
                {
                    ReportNativeFallbackFailure(
                        __instance,
                        patient,
                        __state.Department,
                        bed,
                        __state.NativeFallbackDiagnostic,
                        release);
                }
                else
                {
                    ReportNativeFallbackRecovery(
                        __instance,
                        patient,
                        bed,
                        release);
                }
            }

            if (__result)
            {
                ClearFallbackTracking(patient);
            }

            Entity selectedPorter =
                __state == null
                    ? null
                    : __state.SelectedPorter;

            if (selectedPorter == null ||
                !PorterTransportConfig.EnableDiagnostics)
            {
                return;
            }

            Department patientDepartment =
                patient == null
                    ? null
                    : patient.GetComponent<BehaviorPatient>()?.GetDepartment();
            EmployeeComponent employee =
                selectedPorter.GetComponent<EmployeeComponent>();
            Department porterDepartment =
                employee == null ||
                employee.m_state == null ||
                employee.m_state.m_department == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            WalkComponent walk =
                patient == null
                    ? null
                    : patient.GetComponent<WalkComponent>();
            bool porterStored =
                __instance != null &&
                __instance.m_state != null &&
                __instance.m_state.m_movingCharacter != null &&
                __instance.m_state.m_movingCharacter.GetEntity() ==
                    selectedPorter;
            bool equipmentSelected =
                __instance != null &&
                __instance.m_state != null &&
                __instance.m_state.m_stretcherPatientSide != null &&
                __instance.m_state.m_stretcherPatientSide.GetEntity() != null;

            bool destinationOccupied = false;
            bool destinationReserved = false;
            if (!__result && bed != null &&
                MapScriptInterface.Instance != null)
            {
                destinationOccupied =
                    MapScriptInterface.Instance.GetCenterObjectAt(
                        bed.GetDefaultStretcherTile(),
                        bed.GetFloorIndex()) != null;
                destinationReserved =
                    MapScriptInterface.Instance.GetTileReservedBy(
                        bed.GetDefaultStretcherTile(),
                        bed.GetFloorIndex()) != null;
            }

            PorterDiagnostics.Log(
                (__result
                    ? "patient assignment accepted: porter="
                    : "patient assignment rejected: porter=") +
                (selectedPorter.Name ?? string.Empty).Trim() +
                "; patient=" +
                (patient == null
                    ? "UNKNOWN"
                    : (patient.Name ?? string.Empty).Trim()) +
                "; patientDepartment=" +
                GetDepartmentDiagnosticId(patientDepartment) +
                "; porterDepartment=" +
                GetDepartmentDiagnosticId(porterDepartment) +
                "; porterStateBeforeAssignment=" +
                (__state != null &&
                 __state.HasSelectedPorterStateBeforeAssignment
                    ? __state.SelectedPorterStateBeforeAssignment.ToString()
                    : "UNKNOWN") +
                "; requestedVehicle=" + vehicle +
                "; effectiveVehicle=" +
                (__instance == null || __instance.m_state == null
                    ? "UNKNOWN"
                    : __instance.m_state.m_vehicle.ToString()) +
                "; floors=" +
                (walk == null ? -1 : walk.GetFloorIndex()) +
                "->" + (bed == null ? -1 : bed.GetFloorIndex()) +
                "; crossDepartment=" +
                (patientDepartment != null &&
                 porterDepartment != null &&
                 patientDepartment != porterDepartment) +
                "; result=" + __result +
                "; stretcherState=" +
                (__instance == null || __instance.m_state == null
                    ? "UNKNOWN"
                    : __instance.m_state.m_stretcherState.ToString()) +
                "; porterStored=" + porterStored +
                "; equipmentSelected=" + equipmentSelected +
                "; destinationOccupied=" + destinationOccupied +
                "; destinationReserved=" + destinationReserved +
                ".");
        }

        private static Entity GetPatient(
            StretcherComponent stretcher)
        {
            return stretcher == null ||
                object.ReferenceEquals(EntityField, null)
                    ? null
                    : EntityField.GetValue(stretcher) as Entity;
        }

        private static Entity FindAvailablePorter(
            Department department,
            GameDBEmployeeRole porterRole)
        {
            if (department == null ||
                department.m_departmentPersistentData.m_nurses == null)
            {
                return null;
            }

            foreach (EntityIDPointer<Entity> pointer in
                department.m_departmentPersistentData.m_nurses)
            {
                Entity candidate = pointer.GetEntity();
                EmployeeComponent employee =
                    candidate == null
                        ? null
                        : candidate.GetComponent<EmployeeComponent>();
                if (employee == null ||
                    employee.m_state.m_department.GetEntity() != department ||
                    !IsAvailablePorter(candidate, porterRole))
                {
                    continue;
                }

                return candidate;
            }

            return null;
        }

        internal static bool HasFreePatientTransportEquipment(
            Entity patient, Department porterDepartment, Vehicle vehicle)
        {
            return patient != null &&
                porterDepartment != null &&
                HasFreeTransportEquipment(
                    patient, porterDepartment, vehicle);
        }

        internal static bool HasAvailableLocalPatientPorter(Department department)
        {
            GameDBEmployeeRole role =
                Database.Instance == null
                    ? null
                    : Database.Instance.GetEntry<GameDBEmployeeRole>(
                        PorterIds.PatientTransportRole);
            return role != null &&
                FindAvailablePorter(department, role) != null;
        }

        // A restored Context remains null until the game issues a fresh request.
        internal static bool TryGetPatientContinuationRequest(
            Entity patient,
            out TileObject destination,
            out LyingState lyingState,
            out StretcherUse stretcherUse,
            out BedReservation bedReservation,
            out MovementType movementType,
            out Vehicle vehicle,
            out bool waited)
        {
            destination = null;
            lyingState = LyingState.COVERED;
            stretcherUse = StretcherUse.RETURN_STRETCHER;
            bedReservation = BedReservation.FREE_BED;
            movementType = MovementType.WALKING;
            vehicle = Vehicle.STRETCHER;
            waited = false;
            if (patient == null)
            {
                return false;
            }

            PatientFallbackRequestContext context = null;
            PatientFallbackWait waiting;
            PatientFallbackRelease released;
            if (FallbackWaits.TryGetValue(patient, out waiting) &&
                waiting != null)
            {
                context = waiting.Context;
                destination = waiting.Destination;
                waited = waiting.RetryCount > 0;
            }
            else if (ReleasedFallbacks.TryGetValue(
                         patient, out released) && released != null)
            {
                context = released.Context;
                destination = released.Destination;
                waited = true;
            }

            if (context == null ||
                destination == null ||
                context.StretcherUse != StretcherUse.RETURN_STRETCHER ||
                !IsSamePatientFallbackRequest(
                    context,
                    BuildPatientFallbackRequestContext(
                        patient,
                        context.LyingState,
                        context.StretcherUse,
                        context.BedReservation,
                        context.MovementType,
                        context.Vehicle)))
            {
                // The hospitalization has advanced since HPO recorded this
                // request. Do not resurrect an obsolete clinical transport.
                return false;
            }
            lyingState = context.LyingState;
            stretcherUse = context.StretcherUse;
            bedReservation = context.BedReservation;
            movementType = context.MovementType;
            vehicle = context.Vehicle;
            return true;
        }

        internal static void ClearPatientContinuationFallback(Entity patient)
        {
            ClearFallbackTracking(patient);
        }

        private static Entity FindAvailableHelperPorter(
            Department targetDepartment,
            GameDBEmployeeRole porterRole,
            Entity patient,
            TileObject destination,
            Vehicle vehicle,
            bool hasWaited)
        {
            if (targetDepartment == null ||
                porterRole == null ||
                patient == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_departments == null ||
                GridMap.GetInstance() == null)
            {
                return null;
            }

            WalkComponent patientWalk =
                patient.GetComponent<WalkComponent>();
            if (patientWalk == null)
            {
                return null;
            }

            int patientFloor =
                patientWalk.GetFloorIndex();
            Entity best = null;
            float bestDistance = float.MaxValue;

            foreach (Department department in
                Hospital.Instance.m_departments)
            {
                if (department == null ||
                    department == targetDepartment ||
                    department.m_departmentPersistentData.m_nurses == null ||
                    !HasFreeTransportEquipment(
                        patient,
                        department,
                        vehicle))
                {
                    continue;
                }

                foreach (EntityIDPointer<Entity> pointer in
                    department.m_departmentPersistentData.m_nurses)
                {
                    Entity candidate = pointer.GetEntity();
                    EmployeeComponent employee =
                        candidate == null
                            ? null
                            : candidate.GetComponent<EmployeeComponent>();
                    WalkComponent walk =
                        candidate == null
                            ? null
                            : candidate.GetComponent<WalkComponent>();

                    if (employee == null ||
                        walk == null ||
                        employee.m_state.m_department.GetEntity() != department ||
                        !IsAvailablePorter(
                            candidate,
                            porterRole) ||
                        PorterSampleTransportRuntime
                            .HasPendingLocalSampleWork(candidate))
                    {
                        continue;
                    }

                    float distance =
                        GridMap.GetInstance().GetDistance(
                            walk.GetFloorIndex(),
                            walk.GetCurrentTile(),
                            patientFloor,
                            patientWalk.GetCurrentTile(),
                            AccessRights.STAFF);
                    if (distance < 0f ||
                        distance >= bestDistance ||
                        !PorterPatientHelp.ShouldAccept(
                            candidate,
                            patient,
                            destination,
                            vehicle,
                            distance,
                            hasWaited))
                    {
                        continue;
                    }

                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static bool HasFreeTransportEquipment(
            Entity patient,
            Department helperDepartment,
            Vehicle vehicle)
        {
            if (patient == null ||
                helperDepartment == null ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            WalkComponent patientWalk =
                patient.GetComponent<WalkComponent>();
            if (patientWalk == null)
            {
                return false;
            }

            string tag =
                vehicle != Vehicle.STRETCHER
                    ? "wheelchair_patient"
                    : "stretcher_patient";
            bool onlyComposite =
                vehicle == Vehicle.STRETCHER;

            TileObject transport =
                MapScriptInterface.Instance.FindClosestObjectWithTag(
                    patientWalk.GetCurrentTile(),
                    patientWalk.GetFloorIndex(),
                    helperDepartment,
                    tag,
                    AccessRights.STAFF,
                    null,
                    allowedOutsideOfRoom: false,
                    needsToBeFree: true,
                    hospitalized: false,
                    onlyComposite);

            if (transport == null &&
                vehicle == Vehicle.WHEELCHAIR)
            {
                transport =
                    MapScriptInterface.Instance.FindClosestObjectWithTag(
                        patientWalk.GetCurrentTile(),
                        patientWalk.GetFloorIndex(),
                        helperDepartment,
                        "stretcher_patient",
                        AccessRights.STAFF,
                        null,
                        allowedOutsideOfRoom: false,
                        needsToBeFree: true,
                        hospitalized: false,
                        onlyComposite: true);
            }

            return transport != null;
        }

        private static bool HasConfiguredPatientTransportPorter(
            Department department,
            GameDBEmployeeRole porterRole)
        {
            if (department == null ||
                porterRole == null ||
                department.m_departmentPersistentData.m_nurses == null)
            {
                return false;
            }

            foreach (EntityIDPointer<Entity> pointer in
                department.m_departmentPersistentData.m_nurses)
            {
                Entity candidate = pointer.GetEntity();
                if (!PorterIdentity.IsPorter(candidate))
                {
                    continue;
                }

                BehaviorNurse nurse =
                    candidate.GetComponent<BehaviorNurse>();
                EmployeeComponent employee =
                    candidate.GetComponent<EmployeeComponent>();
                Room homeRoom =
                    employee == null ||
                    employee.m_state.m_homeRoom == null
                        ? null
                        : employee.m_state.m_homeRoom.GetEntity();

                if (nurse == null ||
                    employee == null ||
                    employee.IsFired() ||
                    employee.ShouldGoToTraining() ||
                    employee.m_state.m_department.GetEntity() != department ||
                    employee.m_state.m_shift != DayTime.Instance.GetShift() ||
                    !employee.HasRole(porterRole) ||
                    homeRoom == null ||
                    !homeRoom.GetEquipmentOk() ||
                    homeRoom.m_roomPersistentData.m_roomType == null ||
                    !PorterStationRegistry.IsPorterStation(
                        homeRoom.m_roomPersistentData.m_roomType.Entry))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private static bool IsAvailablePorter(
            Entity candidate,
            GameDBEmployeeRole porterRole)
        {
            if (!PorterIdentity.IsPorter(candidate) ||
                PorterSampleTransportRuntime.IsBusy(candidate) ||
                PorterPatientContinuationRuntime.IsBusy(candidate))
            {
                return false;
            }

            BehaviorNurse nurse =
                candidate.GetComponent<BehaviorNurse>();
            EmployeeComponent employee =
                candidate.GetComponent<EmployeeComponent>();
            Room homeRoom =
                employee == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();

            if (nurse == null ||
                employee == null ||
                employee.IsFired() ||
                employee.ShouldGoToTraining() ||
                employee.m_state.m_shift != DayTime.Instance.GetShift() ||
                !employee.HasRole(porterRole) ||
                homeRoom == null ||
                !homeRoom.GetEquipmentOk() ||
                homeRoom.m_roomPersistentData.m_roomType == null ||
                !PorterStationRegistry.IsPorterStation(
                    homeRoom.m_roomPersistentData.m_roomType.Entry))
            {
                return false;
            }

            NurseState state =
                nurse.m_state.m_nurseState;
            return
                ((state == NurseState.Idle ||
                    state == NurseState.GoingToWorkplace) &&
                    nurse.m_state.m_currentPatient == null) ||
                state == NurseState.FinishedProcedure;
        }

        private static string GetDepartmentDiagnosticId(
            Department department)
        {
            return department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_departmentType == null ||
                department.m_departmentPersistentData.m_departmentType.Entry == null
                    ? "NONE"
                    : department.m_departmentPersistentData
                        .m_departmentType.Entry.DatabaseID.ToString();
        }

        private static PatientFallbackRequestContext
            BuildPatientFallbackRequestContext(
                Entity patient,
                LyingState lyingState,
                StretcherUse stretcherUse,
                BedReservation bedReservation,
                MovementType movementType,
                Vehicle vehicle)
        {
            HospitalizationState hospitalizationState =
                HospitalizationState.NoHospitalization;
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();
            if (hospitalization != null &&
                hospitalization.m_state != null)
            {
                hospitalizationState =
                    hospitalization.m_state.m_hospitalizationState;
            }

            PatientFallbackRequestContext context =
                new PatientFallbackRequestContext();
            context.HospitalizationState =
                hospitalizationState;
            context.LyingState = lyingState;
            context.StretcherUse = stretcherUse;
            context.BedReservation = bedReservation;
            context.MovementType = movementType;
            context.Vehicle = vehicle;
            return context;
        }

        private static bool IsSamePatientFallbackRequest(
            PatientFallbackRequestContext left,
            PatientFallbackRequestContext right)
        {
            // LyingState is deliberately not part of request identity.
            // Vanilla can alternate COVERED / UNCOVERED while evaluating
            // different destinations inside the same hospitalization step.
            return left != null &&
                right != null &&
                left.HospitalizationState ==
                    right.HospitalizationState &&
                left.StretcherUse == right.StretcherUse &&
                left.BedReservation == right.BedReservation &&
                left.MovementType == right.MovementType &&
                left.Vehicle == right.Vehicle;
        }

        private static string DescribePatientFallbackRequestContext(
            PatientFallbackRequestContext context)
        {
            if (context == null)
            {
                return "UNKNOWN";
            }

            return "hospitalizationState=" +
                context.HospitalizationState +
                "; lyingState=" +
                context.LyingState +
                "; stretcherUse=" +
                context.StretcherUse +
                "; bedReservation=" +
                context.BedReservation +
                "; movementType=" +
                context.MovementType +
                "; vehicle=" +
                context.Vehicle;
        }

        private static string DescribePatientFallbackDestination(
            TileObject destination)
        {
            if (destination == null)
            {
                return "NONE";
            }

            string databaseId = "UNKNOWN";
            Vector2i position = Vector2i.ZERO_VECTOR;

            if (destination.m_state != null)
            {
                position = destination.m_state.m_position;

                if (destination.m_state.m_gameDBObject != null &&
                    !destination.m_state.m_gameDBObject.IsDeleted &&
                    destination.m_state.m_gameDBObject.Entry != null)
                {
                    databaseId =
                        destination.m_state.m_gameDBObject.Entry.DatabaseID
                            .ToString();
                }
            }

            return "entityId=" +
                destination.GetEntityID() +
                "; databaseId=" +
                databaseId +
                "; position=" +
                position +
                "; floor=" +
                destination.GetFloorIndex();
        }

        private static int GetPatientFallbackClockMinute()
        {
            if (DayTime.Instance == null)
            {
                return 0;
            }

            return (int)(
                DayTime.Instance.GetDay() * 1440f +
                DayTime.Instance.GetDayTimeHours() * 60f);
        }

        internal static List<PorterPatientFallbackSnapshot>
            GetPatientFallbackStateSnapshot()
        {
            List<PorterPatientFallbackSnapshot> snapshot =
                new List<PorterPatientFallbackSnapshot>();

            foreach (KeyValuePair<Entity, PatientFallbackWait> pair in
                FallbackWaits)
            {
                if (pair.Key == null ||
                    pair.Value == null ||
                    pair.Value.Destination == null)
                {
                    continue;
                }

                PorterPatientFallbackSnapshot entry =
                    new PorterPatientFallbackSnapshot();
                entry.Patient = pair.Key;
                entry.Destination = pair.Value.Destination;
                entry.RetryCount = pair.Value.RetryCount;
                entry.NextRetryGameMinute =
                    pair.Value.NextRetryGameMinute;
                entry.Released = false;
                snapshot.Add(entry);
            }

            foreach (KeyValuePair<Entity, PatientFallbackRelease> pair in
                ReleasedFallbacks)
            {
                if (pair.Key == null ||
                    pair.Value == null ||
                    pair.Value.Destination == null)
                {
                    continue;
                }

                PorterPatientFallbackSnapshot entry =
                    new PorterPatientFallbackSnapshot();
                entry.Patient = pair.Key;
                entry.Destination = pair.Value.Destination;
                entry.RetryCount =
                    PorterTransportConfig.PatientFallbackRetries;
                entry.NextRetryGameMinute = 0;
                entry.Released = true;
                snapshot.Add(entry);
            }

            return snapshot;
        }

        internal static bool RestorePatientFallbackState(
            Entity patient,
            TileObject destination,
            int retryCount,
            int nextRetryGameMinute,
            bool released)
        {
            if (patient == null ||
                destination == null)
            {
                return false;
            }

            if (released)
            {
                ReleasedFallbacks[patient] =
                    new PatientFallbackRelease
                    {
                        Destination = destination,
                        Context = null
                    };
                return true;
            }

            if (retryCount < 0 ||
                nextRetryGameMinute <= 0)
            {
                return false;
            }

            FallbackWaits[patient] =
                new PatientFallbackWait
                {
                    Destination = destination,
                    Context = null,
                    RetryCount = retryCount,
                    NextRetryGameMinute =
                        nextRetryGameMinute
                };
            return true;
        }

        internal static List<KeyValuePair<Department, int>>
            GetPatientPorterCapacityFallbackHistorySnapshot()
        {
            TrimPatientPorterCapacityFallbackHistory();
            List<KeyValuePair<Department, int>> snapshot =
                new List<KeyValuePair<Department, int>>();

            foreach (KeyValuePair<Department, List<int>> pair in
                PatientPorterCapacityFallbackEvents)
            {
                if (pair.Key == null ||
                    pair.Value == null)
                {
                    continue;
                }

                for (int i = 0;
                    i < pair.Value.Count;
                    i++)
                {
                    snapshot.Add(
                        new KeyValuePair<Department, int>(
                            pair.Key,
                            pair.Value[i]));
                }
            }

            return snapshot;
        }

        internal static List<Department>
            GetPatientPorterCapacityNotificationsSnapshot()
        {
            return new List<Department>(
                PatientPorterCapacityNotifications);
        }

        internal static bool RestorePatientPorterCapacityFallbackEvent(
            Department department,
            int gameMinute)
        {
            if (department == null ||
                gameMinute < 0)
            {
                return false;
            }

            int now =
                GetPatientFallbackClockMinute();
            if (now - gameMinute >
                PatientPorterCapacityWindowMinutes)
            {
                return false;
            }

            List<int> events;
            if (!PatientPorterCapacityFallbackEvents.TryGetValue(
                    department,
                    out events))
            {
                events = new List<int>();
                PatientPorterCapacityFallbackEvents[department] =
                    events;
            }

            events.Add(gameMinute);
            return true;
        }

        internal static bool RestorePatientPorterCapacityNotification(
            Department department)
        {
            return department != null &&
                PatientPorterCapacityNotifications.Add(
                    department);
        }

        internal static void ResetPatientPorterCapacityNotification(
            Department department)
        {
            if (department != null)
            {
                PatientPorterCapacityNotifications.Remove(
                    department);
            }
        }

        private static void TrimPatientPorterCapacityFallbackHistory()
        {
            int now =
                GetPatientFallbackClockMinute();
            List<Department> empty =
                new List<Department>();

            foreach (KeyValuePair<Department, List<int>> pair in
                PatientPorterCapacityFallbackEvents)
            {
                List<int> events = pair.Value;
                if (events == null)
                {
                    empty.Add(pair.Key);
                    continue;
                }

                for (int i = events.Count - 1;
                    i >= 0;
                    i--)
                {
                    if (now - events[i] >
                        PatientPorterCapacityWindowMinutes)
                    {
                        events.RemoveAt(i);
                    }
                }

                if (events.Count == 0)
                {
                    empty.Add(pair.Key);
                }
            }

            for (int i = 0;
                i < empty.Count;
                i++)
            {
                PatientPorterCapacityFallbackEvents.Remove(
                    empty[i]);
            }
        }


        private static NativeFallbackDiagnosticSnapshot
            BuildNativeFallbackDiagnosticSnapshot(
                Entity patient,
                Department department,
                TileObject destination,
                Vehicle requestedVehicle)
        {
            NativeFallbackDiagnosticSnapshot snapshot =
                new NativeFallbackDiagnosticSnapshot();
            snapshot.Reason = "UNKNOWN_FALSE";
            snapshot.RequestedVehicle = requestedVehicle;
            snapshot.SelectedVehicle = requestedVehicle;

            if (patient == null ||
                department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_nurses == null ||
                Database.Instance == null ||
                DayTime.Instance == null ||
                MapScriptInterface.Instance == null)
            {
                return snapshot;
            }

            GameDBRoomType nursesStation =
                Database.Instance.GetEntry<GameDBRoomType>(
                    "ROOM_TYPE_NURSES_STATION");
            GameDBEmployeeRole patientTransfersRole =
                Database.Instance.GetEntry<GameDBEmployeeRole>(
                    "EMPL_ROLE_PATIENT_TRANSFERS");
            if (nursesStation == null ||
                patientTransfersRole == null)
            {
                return snapshot;
            }

            Shift currentShift =
                DayTime.Instance.GetShift();

            foreach (EntityIDPointer<Entity> nursePointer in
                department.m_departmentPersistentData.m_nurses)
            {
                snapshot.NursesTotal++;

                Entity nurseEntity =
                    nursePointer.GetEntity();
                if (nurseEntity == null)
                {
                    snapshot.MissingNurseEntity++;
                    continue;
                }

                BehaviorNurse nurse =
                    nurseEntity.GetComponent<BehaviorNurse>();
                EmployeeComponent employee =
                    nurseEntity.GetComponent<EmployeeComponent>();
                if (nurse == null ||
                    nurse.m_state == null ||
                    employee == null ||
                    employee.m_state == null)
                {
                    snapshot.MissingEmployee++;
                    continue;
                }

                bool wrongShift =
                    currentShift != employee.m_state.m_shift;
                bool training =
                    nurse.m_state.m_nurseState ==
                    NurseState.Training;
                bool noHomeRoom =
                    employee.m_state.m_homeRoom == null;
                Room homeRoom =
                    noHomeRoom
                        ? null
                        : employee.m_state.m_homeRoom.GetEntity();
                bool wrongDepartment =
                    employee.m_state.m_department == null ||
                    employee.m_state.m_department.GetEntity() !=
                        department;
                bool wrongHomeRoom =
                    !noHomeRoom &&
                    (homeRoom == null ||
                     homeRoom.m_roomPersistentData == null ||
                     homeRoom.m_roomPersistentData.m_roomType == null ||
                     homeRoom.m_roomPersistentData.m_roomType.Entry !=
                        nursesStation);
                bool fired =
                    employee.IsFired();
                bool busy =
                    !nurse.IsFree();
                bool missingRole =
                    !employee.HasRole(
                        patientTransfersRole);

                if (wrongShift)
                {
                    snapshot.WrongShift++;
                }
                if (training)
                {
                    snapshot.Training++;
                }
                if (noHomeRoom)
                {
                    snapshot.NoHomeRoom++;
                }
                if (wrongDepartment)
                {
                    snapshot.WrongDepartment++;
                }
                if (wrongHomeRoom)
                {
                    snapshot.WrongHomeRoom++;
                }
                if (fired)
                {
                    snapshot.Fired++;
                }
                if (busy)
                {
                    snapshot.Busy++;
                }
                if (missingRole)
                {
                    snapshot.MissingPatientTransfersRole++;
                }

                if (wrongShift ||
                    training ||
                    noHomeRoom ||
                    wrongDepartment ||
                    wrongHomeRoom ||
                    fired ||
                    busy ||
                    missingRole)
                {
                    continue;
                }

                snapshot.Eligible++;
                if (snapshot.NurseCandidate == null)
                {
                    snapshot.NurseCandidate = nurseEntity;
                }
            }

            if (snapshot.NurseCandidate == null)
            {
                snapshot.Reason = "NO_ELIGIBLE_NURSE";
                return snapshot;
            }

            EmployeeComponent candidateEmployee =
                snapshot.NurseCandidate
                    .GetComponent<EmployeeComponent>();
            WalkComponent patientWalk =
                patient.GetComponent<WalkComponent>();
            Department vehicleDepartment =
                candidateEmployee == null ||
                candidateEmployee.m_state == null ||
                candidateEmployee.m_state.m_department == null
                    ? null
                    : candidateEmployee.m_state.m_department
                        .GetEntity();

            if (patientWalk == null ||
                vehicleDepartment == null)
            {
                return snapshot;
            }

            string vehicleTag =
                requestedVehicle == Vehicle.STRETCHER
                    ? "stretcher_patient"
                    : "wheelchair_patient";
            bool onlyComposite =
                requestedVehicle == Vehicle.STRETCHER;

            TileObject vehicleCandidate =
                MapScriptInterface.Instance
                    .FindClosestObjectWithTag(
                        patientWalk.GetCurrentTile(),
                        patientWalk.GetFloorIndex(),
                        vehicleDepartment,
                        vehicleTag,
                        AccessRights.STAFF,
                        null,
                        allowedOutsideOfRoom: false,
                        needsToBeFree: true,
                        hospitalized: false,
                        onlyComposite);

            if (requestedVehicle == Vehicle.STRETCHER)
            {
                snapshot.StretcherFound =
                    vehicleCandidate != null;
            }
            else
            {
                snapshot.WheelchairFound =
                    vehicleCandidate != null;
                if (vehicleCandidate == null)
                {
                    snapshot.StretcherFallbackSearched = true;
                    vehicleCandidate =
                        MapScriptInterface.Instance
                            .FindClosestObjectWithTag(
                                patientWalk.GetCurrentTile(),
                                patientWalk.GetFloorIndex(),
                                vehicleDepartment,
                                "stretcher_patient",
                                AccessRights.STAFF,
                                null,
                                allowedOutsideOfRoom: false,
                                needsToBeFree: true,
                                hospitalized: false,
                                onlyComposite: true);
                    snapshot.StretcherFallbackFound =
                        vehicleCandidate != null;
                    if (vehicleCandidate != null)
                    {
                        snapshot.SelectedVehicle =
                            Vehicle.STRETCHER;
                    }
                }
            }

            snapshot.VehicleCandidate =
                vehicleCandidate;
            if (vehicleCandidate == null)
            {
                snapshot.Reason = "NO_FREE_VEHICLE";
                return snapshot;
            }

            if (destination != null)
            {
                Vector2i destinationTile =
                    destination.GetDefaultStretcherTile();
                int destinationFloor =
                    destination.GetFloorIndex();

                snapshot.DestinationOccupiedBy =
                    MapScriptInterface.Instance
                        .GetCenterObjectAt(
                            destinationTile,
                            destinationFloor);
                snapshot.DestinationOccupied =
                    snapshot.DestinationOccupiedBy != null;
                snapshot.DestinationReservedBy =
                    MapScriptInterface.Instance
                        .GetTileReservedBy(
                            destinationTile,
                            destinationFloor);
                snapshot.DestinationReserved =
                    snapshot.DestinationReservedBy != null;

                if (snapshot.DestinationOccupied ||
                    snapshot.DestinationReserved)
                {
                    snapshot.Reason =
                        "DESTINATION_BLOCKED";
                    return snapshot;
                }
            }

            return snapshot;
        }

        private static string ResolveNativeFallbackFailureReason(
            NativeFallbackDiagnosticSnapshot snapshot)
        {
            if (snapshot == null)
            {
                return "UNKNOWN_FALSE";
            }

            if (!string.Equals(
                    snapshot.Reason,
                    "UNKNOWN_FALSE",
                    StringComparison.Ordinal))
            {
                return snapshot.Reason;
            }

            TileObject vehicle =
                snapshot.VehicleCandidate;
            if (vehicle == null)
            {
                return "UNKNOWN_FALSE";
            }

            if (vehicle.m_state == null ||
                vehicle.m_state.m_error > 0)
            {
                return "BROKEN_VEHICLE";
            }

            if (snapshot.SelectedVehicle ==
                    Vehicle.STRETCHER &&
                (vehicle.m_state.m_compositeParent == null ||
                 vehicle.m_state.m_compositeParent.GetEntity() ==
                    null))
            {
                return "BROKEN_STRETCHER_COMPOSITE";
            }

            return "UNKNOWN_FALSE";
        }

        private static void ReportNativeFallbackFailure(
            StretcherComponent stretcher,
            Entity patient,
            Department department,
            TileObject destination,
            NativeFallbackDiagnosticSnapshot snapshot,
            PatientFallbackRelease release)
        {
            if (!PorterTransportConfig.EnableDiagnostics ||
                patient == null ||
                release == null)
            {
                return;
            }

            int nowMinute =
                GetPatientFallbackClockMinute();
            string reason =
                ResolveNativeFallbackFailureReason(
                    snapshot);
            bool causeChanged =
                !release.NativeFailureObserved ||
                !string.Equals(
                    release.NativeFailureReason,
                    reason,
                    StringComparison.Ordinal);

            if (!release.NativeFailureObserved)
            {
                release.NativeFailureObserved = true;
                release.NativeFailureFirstMinute =
                    nowMinute;
            }

            if (causeChanged)
            {
                release.NativeFailureReason = reason;
                release.NativeFailureCauseMinute =
                    nowMinute;
                release.NativeFailureStillBlockedLogged =
                    false;

                LogNativeFallbackFailureDetails(
                    stretcher,
                    patient,
                    department,
                    destination,
                    snapshot,
                    reason);
                return;
            }

            if (!release.NativeFailureStillBlockedLogged &&
                nowMinute -
                    release.NativeFailureCauseMinute >=
                    NativeFallbackStillBlockedMinutes)
            {
                release.NativeFailureStillBlockedLogged =
                    true;
                PorterDiagnostics.Log(
                    "patient native fallback still-blocked: patient=" +
                    (patient.Name ?? string.Empty).Trim() +
                    "; department=" +
                    DescribeDepartment(department) +
                    "; reason=" +
                    reason +
                    "; blockedMinutes=" +
                    Math.Max(
                        0,
                        nowMinute -
                            release.NativeFailureFirstMinute) +
                    "; destination={" +
                    DescribePatientFallbackDestination(
                        destination) +
                    "}.");
            }
        }

        private static void ReportNativeFallbackRecovery(
            StretcherComponent stretcher,
            Entity patient,
            TileObject destination,
            PatientFallbackRelease release)
        {
            if (!PorterTransportConfig.EnableDiagnostics ||
                patient == null ||
                release == null ||
                !release.NativeFailureObserved)
            {
                return;
            }

            int nowMinute =
                GetPatientFallbackClockMinute();
            Entity mover =
                stretcher == null ||
                stretcher.m_state == null ||
                stretcher.m_state.m_movingCharacter == null
                    ? null
                    : stretcher.m_state.m_movingCharacter
                        .GetEntity();

            PorterDiagnostics.Log(
                "patient native fallback recovered: patient=" +
                (patient.Name ?? string.Empty).Trim() +
                "; blockedMinutes=" +
                Math.Max(
                    0,
                    nowMinute -
                        release.NativeFailureFirstMinute) +
                "; mover=" +
                DescribeEntity(mover) +
                "; vehicle=" +
                (stretcher == null ||
                 stretcher.m_state == null
                    ? "UNKNOWN"
                    : stretcher.m_state.m_vehicle.ToString()) +
                "; destination={" +
                DescribePatientFallbackDestination(
                    destination) +
                "}.");
        }

        private static void LogNativeFallbackFailureDetails(
            StretcherComponent stretcher,
            Entity patient,
            Department department,
            TileObject destination,
            NativeFallbackDiagnosticSnapshot snapshot,
            string reason)
        {
            BehaviorPatient behaviorPatient =
                patient == null
                    ? null
                    : patient.GetComponent<BehaviorPatient>();
            HospitalizationComponent hospitalization =
                patient == null
                    ? null
                    : patient.GetComponent<HospitalizationComponent>();
            Entity existingMover =
                stretcher == null ||
                stretcher.m_state == null ||
                stretcher.m_state.m_movingCharacter == null
                    ? null
                    : stretcher.m_state.m_movingCharacter
                        .GetEntity();

            PorterDiagnostics.Log(
                "patient native fallback failed: patient=" +
                (patient == null
                    ? "UNKNOWN"
                    : (patient.Name ?? string.Empty).Trim()) +
                "; department=" +
                DescribeDepartment(department) +
                "; destination={" +
                DescribePatientFallbackDestination(
                    destination) +
                "}; requestedVehicle=" +
                (snapshot == null
                    ? "UNKNOWN"
                    : snapshot.RequestedVehicle.ToString()) +
                "; selectedVehicle=" +
                (snapshot == null
                    ? "UNKNOWN"
                    : snapshot.SelectedVehicle.ToString()) +
                "; reason=" +
                reason +
                "; nursesTotal=" +
                (snapshot == null
                    ? 0
                    : snapshot.NursesTotal) +
                "; missingNurseEntity=" +
                (snapshot == null
                    ? 0
                    : snapshot.MissingNurseEntity) +
                "; missingEmployee=" +
                (snapshot == null
                    ? 0
                    : snapshot.MissingEmployee) +
                "; wrongShift=" +
                (snapshot == null
                    ? 0
                    : snapshot.WrongShift) +
                "; training=" +
                (snapshot == null
                    ? 0
                    : snapshot.Training) +
                "; noHomeRoom=" +
                (snapshot == null
                    ? 0
                    : snapshot.NoHomeRoom) +
                "; wrongDepartment=" +
                (snapshot == null
                    ? 0
                    : snapshot.WrongDepartment) +
                "; wrongHomeRoom=" +
                (snapshot == null
                    ? 0
                    : snapshot.WrongHomeRoom) +
                "; fired=" +
                (snapshot == null
                    ? 0
                    : snapshot.Fired) +
                "; busy=" +
                (snapshot == null
                    ? 0
                    : snapshot.Busy) +
                "; missingPatientTransfersRole=" +
                (snapshot == null
                    ? 0
                    : snapshot.MissingPatientTransfersRole) +
                "; eligible=" +
                (snapshot == null
                    ? 0
                    : snapshot.Eligible) +
                "; nurseCandidate=" +
                DescribeEntity(
                    snapshot == null
                        ? null
                        : snapshot.NurseCandidate) +
                "; wheelchairFound=" +
                (snapshot != null &&
                 snapshot.WheelchairFound) +
                "; stretcherFound=" +
                (snapshot != null &&
                 snapshot.StretcherFound) +
                "; stretcherFallbackSearched=" +
                (snapshot != null &&
                 snapshot.StretcherFallbackSearched) +
                "; stretcherFallbackFound=" +
                (snapshot != null &&
                 snapshot.StretcherFallbackFound) +
                "; vehicleCandidate={" +
                (snapshot == null
                    ? "NONE"
                    : DescribePatientFallbackDestination(
                        snapshot.VehicleCandidate)) +
                "}; occupied=" +
                (snapshot != null &&
                 snapshot.DestinationOccupied) +
                "; occupiedBy={" +
                (snapshot == null
                    ? "NONE"
                    : DescribePatientFallbackDestination(
                        snapshot.DestinationOccupiedBy)) +
                "}; reserved=" +
                (snapshot != null &&
                 snapshot.DestinationReserved) +
                "; reservedBy=" +
                DescribeEntity(
                    snapshot == null
                        ? null
                        : snapshot.DestinationReservedBy) +
                "; patientState=" +
                (behaviorPatient == null ||
                 behaviorPatient.m_state == null
                    ? "UNKNOWN"
                    : behaviorPatient.m_state.m_patientState
                        .ToString()) +
                "; hospitalizationState=" +
                (hospitalization == null ||
                 hospitalization.m_state == null
                    ? "UNKNOWN"
                    : hospitalization.m_state
                        .m_hospitalizationState.ToString()) +
                "; stretcherState=" +
                (stretcher == null ||
                 stretcher.m_state == null
                    ? "UNKNOWN"
                    : stretcher.m_state.m_stretcherState
                        .ToString()) +
                "; existingMover=" +
                DescribeEntity(existingMover) +
                "; existingVehicle=" +
                (stretcher == null ||
                 stretcher.m_state == null
                    ? "UNKNOWN"
                    : stretcher.m_state.m_vehicle.ToString()) +
                ".");
        }

        private static string DescribeDepartment(
            Department department)
        {
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData
                    .m_departmentType == null ||
                department.m_departmentPersistentData
                    .m_departmentType.Entry == null)
            {
                return "UNKNOWN";
            }

            return department.m_departmentPersistentData
                .m_departmentType.Entry.DatabaseID.ToString();
        }

        private static string DescribeEntity(
            Entity entity)
        {
            if (entity == null)
            {
                return "NONE";
            }

            string name =
                (entity.Name ?? string.Empty).Trim();
            return string.IsNullOrEmpty(name)
                ? "UNNAMED"
                : name;
        }

        private static void RecordPatientPorterCapacityFallback(
            Department department)
        {
            if (department == null)
            {
                return;
            }

            TrimPatientPorterCapacityFallbackHistory();

            List<int> events;
            if (!PatientPorterCapacityFallbackEvents.TryGetValue(
                    department,
                    out events))
            {
                events = new List<int>();
                PatientPorterCapacityFallbackEvents[department] =
                    events;
            }

            events.Add(
                GetPatientFallbackClockMinute());
            if (events.Count <
                    PatientPorterCapacityFallbackThreshold ||
                PatientPorterCapacityNotifications.Contains(
                    department))
            {
                return;
            }

            NotifyPatientPorterCapacity(
                department,
                events.Count);
            PatientPorterCapacityNotifications.Add(
                department);
        }

        private static void NotifyPatientPorterCapacity(
            Department department,
            int fallbackCount)
        {
            if (department == null ||
                department.m_departmentPersistentData == null ||
                department.m_departmentPersistentData.m_departmentType == null ||
                department.m_departmentPersistentData.m_departmentType.Entry == null ||
                NotificationManager.GetInstance() == null)
            {
                return;
            }

            string departmentName =
                StringTable.GetInstance().GetLocalizedText(
                    department.m_departmentPersistentData
                        .m_departmentType.Entry);
            NotificationManager.GetInstance().AddMessage(
                null,
                PorterIds.PatientPorterCapacityNotification,
                departmentName,
                string.Empty,
                string.Empty,
                0,
                0,
                0,
                0,
                PorterVisuals.CategoryAsset);

            PorterDiagnostics.Log(
                "patient Porter capacity notification: department=" +
                department.m_departmentPersistentData
                    .m_departmentType.Entry.DatabaseID +
                "; fallbacks=" +
                fallbackCount +
                "; windowMinutes=" +
                PatientPorterCapacityWindowMinutes +
                ".");
        }

        private static void ClearFallbackTracking(
            Entity patient)
        {
            if (patient == null)
            {
                return;
            }

            FallbackWaits.Remove(patient);
            ReleasedFallbacks.Remove(patient);
        }
    }

    internal static class PorterExperience
    {
        private static readonly FieldInfo EmployeeEntityField =
            AccessTools.Field(typeof(EmployeeComponent), "m_entity");

        internal static void AwardWorkExperience(
            Entity porter,
            int points)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                points <= 0 ||
                !PorterIdentity.EnsurePorterQualification(porter))
            {
                return;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            if (employee == null)
            {
                return;
            }

            employee.AddSkillPoints(
                PorterIds.PorterQualification,
                points);
        }

        internal static bool TryHandleOverallExperience(
            EmployeeComponent employee,
            int points)
        {
            Entity porter = GetEmployeeEntity(employee);
            if (!PorterIdentity.IsPorter(porter) ||
                employee == null ||
                employee.m_state == null)
            {
                return false;
            }

            float scaledPoints =
                (float)points *
                (Database.Instance
                    .GetEntry<GameDBTweakableFloat>(
                        "LEVELING_RATE_PERCENT")
                    .Value / 100f);

            PerkComponent perkComponent =
                porter.GetComponent<PerkComponent>();
            if (perkComponent.m_perkSet.HasPerk(
                    "PERK_FAST_LEARNER"))
            {
                employee.m_state.m_points +=
                    (int)scaledPoints * 110 / 100;
            }
            else if (perkComponent.m_perkSet.HasPerk(
                         "PERK_SLOW_LEARNER"))
            {
                employee.m_state.m_points +=
                    (int)scaledPoints * 90 / 100;
            }
            else
            {
                employee.m_state.m_points +=
                    (int)scaledPoints;
            }

            if (employee.m_state.m_points <
                    employee.GetPointsNeededForNextLevel() ||
                employee.m_state.m_level >= 3)
            {
                return true;
            }

            employee.m_state.m_points -=
                employee.GetPointsNeededForNextLevel();
            employee.m_state.m_level++;
            employee.m_state.m_leveledUpAfterHire = true;

            if (perkComponent.m_perkSet.HasHiddenPerk(
                    "PERK_FAST_LEARNER"))
            {
                perkComponent.m_perkSet.RevealPerk(
                    "PERK_FAST_LEARNER");
            }
            if (perkComponent.m_perkSet.HasHiddenPerk(
                    "PERK_SLOW_LEARNER"))
            {
                perkComponent.m_perkSet.RevealPerk(
                    "PERK_SLOW_LEARNER");
            }

            string levelName =
                StringTable.GetInstance().GetLocalizedText(
                    PorterIds.GetPorterLevelLocalizationId(
                        employee.m_state.m_level));
            NotificationManager.GetInstance().AddMessage(
                porter,
                "NOTIF_CHARACTER_LEVELED_UP",
                levelName,
                string.Empty,
                string.Empty);

            return true;
        }

        private static Entity GetEmployeeEntity(
            EmployeeComponent employee)
        {
            if (employee == null ||
                object.ReferenceEquals(
                    EmployeeEntityField,
                    null))
            {
                return null;
            }

            return
                EmployeeEntityField.GetValue(employee)
                as Entity;
        }
    }

    [HarmonyPatch(
        typeof(EmployeeComponent),
        nameof(EmployeeComponent.AddExperiencePoints),
        new Type[] { typeof(int) })]
    internal static class PorterExperienceLevelPatch
    {
        private static bool Prefix(
            EmployeeComponent __instance,
            int points)
        {
            return !PorterExperience.TryHandleOverallExperience(
                __instance,
                points);
        }
    }

    [HarmonyPatch(
        typeof(StretcherComponent),
        nameof(StretcherComponent.SwitchState),
        new Type[] { typeof(StretcherComponentState) })]
    internal static class PorterPatientTransportExperiencePatch
    {
        private static readonly FieldInfo PatientField =
            AccessTools.Field(typeof(StretcherComponent), "m_entity");

        private static void Prefix(
            StretcherComponent __instance,
            StretcherComponentState state)
        {
            PorterPatientContinuationRuntime
                .RestoreActiveVehicleContinuationOrigin(
                    __instance,
                    "SwitchState:" + state);

            if (__instance == null ||
                __instance.m_state == null ||
                state !=
                    StretcherComponentState.ReleasingMovingCharacter ||
                __instance.m_state.m_stretcherState ==
                    StretcherComponentState.ReleasingMovingCharacter ||
                __instance.m_state.m_movingCharacter == null)
            {
                return;
            }

            Entity porter =
                __instance.m_state.m_movingCharacter.GetEntity();
            if (!PorterIdentity.IsPorter(porter))
            {
                return;
            }

            // StretcherComponent.SwitchState is called before the native
            // state transition: the patient and assigned mover are still linked.
            Entity patient = object.ReferenceEquals(PatientField, null)
                ? null
                : PatientField.GetValue(__instance) as Entity;

            PorterExperience.AwardWorkExperience(porter, 3);
            PorterDiagnostics.Log(
                "patient completed: porter=" +
                (porter.Name ?? string.Empty).Trim() +
                "; patient=" +
                (patient == null
                    ? "UNKNOWN"
                    : (patient.Name ?? string.Empty).Trim()) +
                "; vehicle=" +
                __instance.m_state.m_vehicle +
                "; fromState=" +
                __instance.m_state.m_stretcherState +
                "; toState=" + state +
                ".");
        }
    }

}
