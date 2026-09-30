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
        private sealed class PatientFallbackWait
        {
            internal TileObject Destination;
            internal int RetryCount;
            internal float NextRetryAt;
        }

        private static readonly FieldInfo EntityField =
            AccessTools.Field(typeof(StretcherComponent), "m_entity");

        private static readonly Dictionary<Entity, PatientFallbackWait>
            FallbackWaits =
                new Dictionary<Entity, PatientFallbackWait>();

        private static readonly HashSet<Entity> ReleasedFallbacks =
            new HashSet<Entity>();

        internal static void ResetFallbackTracking()
        {
            FallbackWaits.Clear();
            ReleasedFallbacks.Clear();
        }

        private static bool Prefix(
            StretcherComponent __instance,
            TileObject bed,
            Vehicle vehicle,
            ref Entity movingCharacter,
            ref bool __result)
        {
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

            Entity porter = FindAvailablePorter(
                department,
                porterRole);
            bool crossDepartment = false;
            if (porter == null)
            {
                porter = FindAvailableHelperPorter(
                    department,
                    porterRole,
                    patient,
                    vehicle);
                crossDepartment = porter != null;
            }

            if (porter != null)
            {
                ClearFallbackTracking(patient);
                movingCharacter = porter;

                WalkComponent patientWalk =
                    patient.GetComponent<WalkComponent>();
                PorterDiagnostics.Log(
                    "patient assigned: porter=" +
                    porter.Name +
                    "; patient=" +
                    patient.Name +
                    "; vehicle=" +
                    vehicle +
                    "; floors=" +
                    (patientWalk == null
                        ? -1
                        : patientWalk.GetFloorIndex()) +
                    "->" +
                    (bed == null ? -1 : bed.GetFloorIndex()) +
                    "; crossDepartment=" +
                    crossDepartment +
                    ".");

                return true;
            }

            if (ReleasedFallbacks.Contains(patient))
            {
                return true;
            }

            int maxFallbackRetries =
                PorterTransportConfig.PatientFallbackRetries;
            if (bed == null ||
                maxFallbackRetries <= 0 ||
                !HasConfiguredPatientTransportPorter(
                    department,
                    porterRole))
            {
                ClearFallbackTracking(patient);
                return true;
            }

            PatientFallbackWait wait;
            if (FallbackWaits.TryGetValue(patient, out wait) &&
                !object.ReferenceEquals(wait.Destination, bed))
            {
                ClearFallbackTracking(patient);
                wait = null;
            }

            float now = __instance.m_state.m_timeInState;
            int retryMinutes =
                PorterTransportConfig.PatientFallbackRetryMinutes;
            float retryDelay =
                DayTime.Instance.IngameTimeHoursToRealTimeSeconds(
                    retryMinutes / 60f);

            if (!FallbackWaits.TryGetValue(patient, out wait))
            {
                wait = new PatientFallbackWait
                {
                    Destination = bed,
                    RetryCount = 0,
                    NextRetryAt = now + retryDelay
                };
                FallbackWaits[patient] = wait;

                __result = false;
                return false;
            }

            if (now < wait.NextRetryAt)
            {
                __result = false;
                return false;
            }

            wait.RetryCount++;
            if (wait.RetryCount >= maxFallbackRetries)
            {
                FallbackWaits.Remove(patient);
                ReleasedFallbacks.Add(patient);

                PorterDiagnostics.Log(
                    "patient fallback: patient=" +
                    patient.Name +
                    "; retries=" +
                    maxFallbackRetries +
                    "; native Nurse released.");

                return true;
            }

            wait.NextRetryAt = now + retryDelay;
            __result = false;
            return false;
        }

        private static void Postfix(
            StretcherComponent __instance,
            bool __result)
        {
            if (__result)
            {
                ClearFallbackTracking(GetPatient(__instance));
            }
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

        private static Entity FindAvailableHelperPorter(
            Department targetDepartment,
            GameDBEmployeeRole porterRole,
            Entity patient,
            Vehicle vehicle)
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
                        walk.GetFloorIndex() != patientFloor ||
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
                        distance >= bestDistance)
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
                    !PorterStationRegistry.IsPorterStationOrLegacyNursesStation(
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
                PorterSampleTransportRuntime.IsBusy(candidate))
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
                !PorterStationRegistry.IsPorterStationOrLegacyNursesStation(
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

        private static string GetDepartmentId(
            Department department)
        {
            return department == null ||
                department.GetDepartmentType() == null
                    ? "UNKNOWN"
                    : department.GetDepartmentType()
                        .DatabaseID.ToString();
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
        private static void Prefix(
            StretcherComponent __instance,
            StretcherComponentState state)
        {
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

            PorterExperience.AwardWorkExperience(porter, 3);
            PorterDiagnostics.Log(
                "patient completed: porter=" +
                porter.Name +
                ".");
        }
    }

}
