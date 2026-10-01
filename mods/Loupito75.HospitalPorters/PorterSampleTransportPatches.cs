using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal enum PorterSampleTransportPhase
    {
        GoingToCart,
        GoingToSampleParking,
        GoingToSample,
        TakingSample,
        ReturningToCartAfterSample,
        WaitingForFridge,
        GoingToFridgeParking,
        GoingToFridge,
        StoringSample,
        ReturningToCartAfterFridge,
        ReturningCart
    }

    internal sealed class PorterSampleTransportJob
    {
        internal LabProcedure Procedure;
        internal readonly List<LabProcedure> CollectedProcedures =
            new List<LabProcedure>();
        internal Room StatLab;
        internal TileObject Cart;
        internal Room CartHomeRoom;
        internal Vector2i CartHomeTile;
        internal int CartHomeFloorIndex;
        internal Direction CartHomeOrientation;
        internal bool NativeTimeout;
        internal PorterSampleTransportPhase Phase;
    }

    internal sealed class PorterSampleFallbackWait
    {
        internal int RetryCount;
        internal float NextRetryAt;
    }

    internal static class PorterSampleTransportRuntime
    {
        private static readonly FieldInfo NurseEntityField =
            AccessTools.Field(typeof(BehaviorNurse), "m_entity");

        private static readonly Dictionary<Entity, PorterSampleTransportJob> Jobs =
            new Dictionary<Entity, PorterSampleTransportJob>();

        private static readonly Dictionary<LabProcedure, PorterSampleFallbackWait>
            FallbackWaits =
                new Dictionary<LabProcedure, PorterSampleFallbackWait>();

        private static readonly HashSet<LabProcedure> ReleasedFallbacks =
            new HashSet<LabProcedure>();

        private static readonly HashSet<Department>
            MissingCartNotifications =
                new HashSet<Department>();

        internal static void Reset()
        {
            Jobs.Clear();
            FallbackWaits.Clear();
            ReleasedFallbacks.Clear();
            MissingCartNotifications.Clear();
        }

        internal static bool IsBusy(Entity porter)
        {
            return porter != null && Jobs.ContainsKey(porter);
        }

        internal static bool IsCartInActiveJob(TileObject cart)
        {
            if (cart == null)
            {
                return false;
            }

            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job = pair.Value;
                if (job != null && job.Cart == cart)
                {
                    return true;
                }
            }

            return false;
        }

        internal static int CountActiveCartsAwayFromStation(
            Room station)
        {
            if (station == null)
            {
                return 0;
            }

            int count = 0;
            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job = pair.Value;
                TileObject cart =
                    job == null
                        ? null
                        : job.Cart;
                if (job == null ||
                    cart == null ||
                    job.CartHomeRoom != station)
                {
                    continue;
                }

                bool fixedInsideStation =
                    !cart.m_state.m_moving &&
                    cart.GetFloorIndex() ==
                        station.GetFloorIndex() &&
                    station.IsPositionInRoom(
                        cart.m_state.m_position);
                if (!fixedInsideStation)
                {
                    count++;
                }
            }

            return count;
        }

        internal static List<KeyValuePair<Entity, PorterSampleTransportJob>>
            GetActiveJobsSnapshot()
        {
            return new List<KeyValuePair<Entity, PorterSampleTransportJob>>(
                Jobs);
        }

        internal static bool RestorePersistedJob(
            Entity porter,
            PorterSampleTransportJob job)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                job == null ||
                job.Cart == null ||
                job.CartHomeRoom == null ||
                job.StatLab == null ||
                porter.GetComponent<BehaviorNurse>() == null ||
                Jobs.ContainsKey(porter) ||
                IsCartInActiveJob(job.Cart))
            {
                return false;
            }

            if (job.Cart.User != null &&
                job.Cart.User != porter)
            {
                return false;
            }

            job.Cart.User = porter;
            job.Cart.m_state.m_originalFloorIndex =
                job.CartHomeFloorIndex;
            Jobs[porter] = job;

            if (job.Procedure != null &&
                job.Phase !=
                    PorterSampleTransportPhase.ReturningCart)
            {
                ShowSampleBubble(
                    porter,
                    job.Procedure);
            }


            return true;
        }

        internal static void RestoreActiveCartDepartments()
        {
            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job = pair.Value;
                TileObject cart =
                    job == null
                        ? null
                        : job.Cart;
                Room homeRoom =
                    job == null
                        ? null
                        : job.CartHomeRoom;
                Department homeDepartment =
                    homeRoom == null ||
                    homeRoom.m_roomPersistentData.m_department == null
                        ? null
                        : homeRoom.m_roomPersistentData.m_department.GetEntity();

                if (cart == null ||
                    homeDepartment == null)
                {
                    continue;
                }

                Department currentDepartment =
                    cart.m_state.m_department == null
                        ? null
                        : cart.m_state.m_department.GetEntity();
                if (currentDepartment != homeDepartment)
                {
                    if (currentDepartment != null)
                    {
                        currentDepartment.RemoveObject(
                            cart);
                    }

                    cart.m_state.m_department =
                        homeDepartment;
                }

                if (!homeDepartment
                        .m_departmentPersistentData
                        .m_objects.Contains(cart))
                {
                    homeDepartment.AddObject(cart);
                }
            }
        }

        internal static bool UpdateActiveMission(BehaviorNurse nurse)
        {
            Entity porter = GetEntity(nurse);
            if (!PorterIdentity.IsPorter(porter))
            {
                return false;
            }

            PorterSampleTransportJob job;
            if (!Jobs.TryGetValue(porter, out job))
            {
                PorterSampleCartRuntime.RecoverOrphanedCartForPorter(porter);
                RecoverOrphanedUseReservation(porter, nurse);
                return false;
            }

            UpdateJob(porter, nurse, job);
            return true;
        }

        internal static void TryAssignFromIdlePorter(BehaviorNurse nurse)
        {
            Entity porter = GetEntity(nurse);
            if (!IsAvailablePorter(porter, null))
            {
                return;
            }

            LabProcedure best;
            if (TryFindBestProcedureForPorter(
                    porter,
                    foreignDepartmentOnly: false,
                    out best))
            {
                StartJob(
                    porter,
                    nurse,
                    best,
                    allowForeignDepartment: false);
                return;
            }

            if (TryFindBestProcedureForPorter(
                    porter,
                    foreignDepartmentOnly: true,
                    out best))
            {
                StartJob(
                    porter,
                    nurse,
                    best,
                    allowForeignDepartment: true);
            }
        }

        internal static bool ShouldDeferLabFallback(LabProcedure procedure)
        {
            if (IsProcedureClaimed(procedure))
            {
                ClearFallbackTracking(procedure);
                return true;
            }

            if (!IsHospitalizedSampleWaiting(procedure))
            {
                ClearFallbackTracking(procedure);
                return false;
            }

            if (TryAssignAvailablePorter(procedure))
            {
                ClearFallbackTracking(procedure);
                return true;
            }

            if (ReleasedFallbacks.Contains(procedure))
            {
                return false;
            }

            if (!HasConfiguredReachableSamplePorter(procedure))
            {
                ClearFallbackTracking(procedure);
                return false;
            }

            int maxFallbackRetries =
                PorterTransportConfig.SampleFallbackRetries;
            if (maxFallbackRetries <= 0)
            {
                ClearFallbackTracking(procedure);
                return false;
            }

            PorterSampleFallbackWait wait;
            float now = procedure.m_state.m_timeInState;
            int retryMinutes =
                PorterTransportConfig.SampleFallbackRetryMinutes;
            float retryDelay =
                DayTime.Instance.IngameTimeHoursToRealTimeSeconds(
                    retryMinutes / 60f);

            if (!FallbackWaits.TryGetValue(procedure, out wait))
            {
                wait = new PorterSampleFallbackWait
                {
                    RetryCount = 0,
                    NextRetryAt = now + retryDelay
                };
                FallbackWaits[procedure] = wait;

                return true;
            }

            if (now < wait.NextRetryAt)
            {
                return true;
            }

            wait.RetryCount++;
            if (wait.RetryCount >= maxFallbackRetries)
            {
                FallbackWaits.Remove(procedure);
                ReleasedFallbacks.Add(procedure);
                NotifyMissingSampleCartAfterRetries(procedure);

                PorterDiagnostics.Log(
                    "sample fallback: patient=" +
                    GetPatientName(procedure) +
                    "; examination=" +
                    procedure.m_state.m_examination.Entry.DatabaseID +
                    "; retries=" +
                    maxFallbackRetries +
                    "; native Lab Specialist released.");
                return false;
            }

            wait.NextRetryAt = now + retryDelay;
            return true;
        }

        internal static void CleanupFallbackTracking(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState != LabProcedureState.SamplingFinished)
            {
                ClearFallbackTracking(procedure);
            }
        }

        internal static bool TryAssignAvailablePorter(LabProcedure procedure)
        {
            if (!IsHospitalizedSampleWaiting(procedure))
            {
                return false;
            }

            Entity patient = procedure.m_state.m_patient.GetEntity();
            Department department = patient == null
                ? null
                : patient.GetComponent<BehaviorPatient>()?.GetDepartment();
            if (department == null ||
                department.m_departmentPersistentData.m_nurses == null)
            {
                return false;
            }

            Entity best = null;
            BehaviorNurse bestNurse = null;
            float bestDistance = float.MaxValue;

            foreach (EntityIDPointer<Entity> pointer in
                department.m_departmentPersistentData.m_nurses)
            {
                Entity candidate = pointer.GetEntity();
                float distance;
                if (!CanTakeProcedure(
                        candidate,
                        procedure,
                        allowForeignDepartment: false,
                        out distance))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                    bestNurse = candidate.GetComponent<BehaviorNurse>();
                }
            }

            if (best != null && bestNurse != null)
            {
                return StartJob(
                    best,
                    bestNurse,
                    procedure,
                    allowForeignDepartment: false);
            }

            if (!TryFindForeignSampleHelper(
                    procedure,
                    department,
                    out best,
                    out bestNurse))
            {
                return false;
            }

            return StartJob(
                best,
                bestNurse,
                procedure,
                allowForeignDepartment: true);
        }

        internal static bool HasPendingLocalSampleWork(Entity porter)
        {
            LabProcedure procedure;
            return TryFindBestProcedureForPorter(
                porter,
                foreignDepartmentOnly: false,
                out procedure);
        }

        internal static void RecoverOrphanedDelivery(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState != LabProcedureState.SampleBeingDelivered)
            {
                return;
            }

            Entity patient = procedure.m_state.m_patient.GetEntity();
            if (patient == null ||
                patient.GetComponent<HospitalizationComponent>() == null ||
                !patient.GetComponent<HospitalizationComponent>().IsHospitalized() ||
                IsProcedureClaimed(procedure) ||
                IsHandledByLabSpecialist(procedure))
            {
                return;
            }

            TileObject source = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            if (source != null && source.User != null &&
                PorterIdentity.IsPorter(source.User))
            {
                Entity stalePorter = source.User;
                UseComponent use = stalePorter.GetComponent<UseComponent>();
                if (use != null &&
                    (use.m_state.m_reservedObject != null || use.IsUsing()))
                {
                    use.Interrupt();
                }
                if (source.User == stalePorter)
                {
                    source.User = null;
                }
            }

            procedure.SwitchState(LabProcedureState.SamplingFinished);
            ClearFallbackTracking(procedure);
            PorterDiagnostics.Log(
                "sample recovery: patient=" +
                patient.Name +
                "; examination=" +
                procedure.m_state.m_examination.Entry.DatabaseID +
                "; action=requeued orphaned delivery.");
        }

        private static Entity GetEntity(BehaviorNurse nurse)
        {
            return nurse == null || object.ReferenceEquals(NurseEntityField, null)
                ? null
                : NurseEntityField.GetValue(nurse) as Entity;
        }

        private static bool HasConfiguredReachableSamplePorter(
            LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                Database.Instance == null || GridMap.GetInstance() == null)
            {
                return false;
            }

            Entity patient = procedure.m_state.m_patient.GetEntity();
            Department department = patient == null
                ? null
                : patient.GetComponent<BehaviorPatient>()?.GetDepartment();
            TileObject source = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            if (department == null || source == null ||
                department.m_departmentPersistentData.m_nurses == null)
            {
                return false;
            }

            if (procedure.m_state.m_statLab == null ||
                procedure.m_state.m_statLab.GetEntity() == null)
            {
                procedure.FindRoom(onlyFree: false);
            }

            Room statLab = procedure.m_state.m_statLab == null
                ? null
                : procedure.m_state.m_statLab.GetEntity();
            if (statLab == null ||
                statLab.m_roomPersistentData.m_roomType == null ||
                !statLab.m_roomPersistentData.m_roomType.Entry.HasTag("stat_lab"))
            {
                return false;
            }

            foreach (EntityIDPointer<Entity> pointer in
                department.m_departmentPersistentData.m_nurses)
            {
                Entity candidate = pointer.GetEntity();
                if (!IsConfiguredSamplePorter(candidate, department))
                {
                    continue;
                }

                WalkComponent walk = candidate.GetComponent<WalkComponent>();
                if (walk == null)
                {
                    continue;
                }

                float routeDistance = GridMap.GetInstance().GetDistance(
                    walk.GetFloorIndex(),
                    walk.GetCurrentTile(),
                    source.GetFloorIndex(),
                    source.m_state.m_position,
                    AccessRights.STAFF);
                if (routeDistance >= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsConfiguredSamplePorter(
            Entity porter,
            Department department)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                Database.Instance == null || department == null)
            {
                return false;
            }

            EmployeeComponent employee = porter.GetComponent<EmployeeComponent>();
            GameDBEmployeeRole role =
                Database.Instance.GetEntry<GameDBEmployeeRole>(
                    PorterIds.SampleTransportRole);
            Room homeRoom = employee == null || employee.m_state.m_homeRoom == null
                ? null
                : employee.m_state.m_homeRoom.GetEntity();

            return employee != null &&
                role != null &&
                !employee.IsFired() &&
                employee.m_state.m_shift == DayTime.Instance.GetShift() &&
                employee.m_state.m_department.GetEntity() == department &&
                employee.HasRole(role) &&
                homeRoom != null &&
                homeRoom.GetEquipmentOk() &&
                homeRoom.m_roomPersistentData.m_roomType != null &&
                PorterStationRegistry.IsPorterStationOrLegacyNursesStation(
                    homeRoom.m_roomPersistentData.m_roomType.Entry);
        }

        private static void NotifyMissingSampleCartAfterRetries(
            LabProcedure procedure)
        {
            Department department =
                GetProcedureDepartment(procedure);
            if (department == null ||
                MissingCartNotifications.Contains(department) ||
                !IsDepartmentWaitingForFreeSampleCart(
                    procedure,
                    department))
            {
                return;
            }

            MissingCartNotifications.Add(department);

            GameDBObject cartEntry =
                Database.Instance.GetEntry<GameDBObject>(
                    PorterIds.SampleCart);
            string iconAssetId =
                cartEntry == null ||
                cartEntry.CustomIconAssetRef == null
                    ? null
                    : cartEntry.CustomIconAssetRef.XmlID;
            int icon =
                string.IsNullOrEmpty(iconAssetId) &&
                cartEntry != null
                    ? cartEntry.IconIndex
                    : 0;
            string departmentName =
                StringTable.GetInstance().GetLocalizedText(
                    department.m_departmentPersistentData.m_departmentType.Entry);

            NotificationManager.GetInstance().AddMessage(
                null,
                PorterIds.SampleCartNotification,
                departmentName,
                string.Empty,
                string.Empty,
                icon,
                0,
                0,
                0,
                iconAssetId);

            PorterDiagnostics.Log(
                "sample cart capacity notification: department=" +
                department.m_departmentPersistentData.m_departmentType.Entry.DatabaseID +
                ".");
        }

        private static bool IsDepartmentWaitingForFreeSampleCart(
            LabProcedure procedure,
            Department department)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                department == null ||
                department.m_departmentPersistentData.m_nurses == null)
            {
                return false;
            }

            TileObject source =
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            if (source == null ||
                source.User != null ||
                source.Owner != null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            foreach (EntityIDPointer<Entity> pointer in
                department.m_departmentPersistentData.m_nurses)
            {
                Entity porter = pointer.GetEntity();
                if (!IsAvailablePorter(
                        porter,
                        procedure))
                {
                    continue;
                }

                EmployeeComponent employee =
                    porter.GetComponent<EmployeeComponent>();
                WalkComponent walk =
                    porter.GetComponent<WalkComponent>();
                Room station =
                    employee == null ||
                    employee.m_state.m_homeRoom == null
                        ? null
                        : employee.m_state.m_homeRoom.GetEntity();

                if (employee == null ||
                    walk == null ||
                    employee.m_state.m_department.GetEntity() != department ||
                    station == null ||
                    !station.GetEquipmentOk() ||
                    station.m_roomPersistentData.m_roomType == null ||
                    !PorterStationRegistry.IsPorterStationOrLegacyNursesStation(
                        station.m_roomPersistentData.m_roomType.Entry))
                {
                    continue;
                }

                int cartCount =
                    PorterSampleCartRuntime.CountTotalCartsForStation(
                        station);
                if (cartCount <= 0 ||
                    PorterSampleCartRuntime.HasFreeCart(porter))
                {
                    continue;
                }

                float routeDistance =
                    GridMap.GetInstance().GetDistance(
                        walk.GetFloorIndex(),
                        walk.GetCurrentTile(),
                        source.GetFloorIndex(),
                        source.m_state.m_position,
                        AccessRights.STAFF);
                if (routeDistance >= 0f)
                {
                    return true;
                }
            }

            return false;
        }

        internal static void ResetMissingCartNotification(
            Department department)
        {
            if (department != null)
            {
                MissingCartNotifications.Remove(department);
            }
        }

        private static string GetPatientName(LabProcedure procedure)
        {
            Entity patient = procedure == null || procedure.m_state == null
                ? null
                : procedure.m_state.m_patient.GetEntity();
            return patient == null ? "UNKNOWN" : patient.Name;
        }

        private static void ClearFallbackTracking(LabProcedure procedure)
        {
            if (procedure == null)
            {
                return;
            }

            FallbackWaits.Remove(procedure);
            ReleasedFallbacks.Remove(procedure);
        }

        private static bool TryFindBestProcedureForPorter(
            Entity porter,
            bool foreignDepartmentOnly,
            out LabProcedure best)
        {
            best = null;
            if (!IsAvailablePorter(porter, null))
            {
                return false;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            Department homeDepartment =
                employee == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            List<LabProcedure> procedures =
                LabProcedureManager.Instance == null
                    ? null
                    : LabProcedureManager.Instance.m_labProcedures;
            if (employee == null ||
                walk == null ||
                homeDepartment == null ||
                procedures == null)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            for (int i = 0; i < procedures.Count; i++)
            {
                LabProcedure procedure = procedures[i];
                Department procedureDepartment =
                    GetProcedureDepartment(procedure);
                if (procedureDepartment == null)
                {
                    continue;
                }

                bool foreign =
                    procedureDepartment != homeDepartment;
                if (foreign != foreignDepartmentOnly)
                {
                    continue;
                }

                float distance;
                if (!CanTakeProcedure(
                        porter,
                        procedure,
                        allowForeignDepartment: foreign,
                        out distance))
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = procedure;
                }
            }

            return best != null;
        }

        private static bool TryFindForeignSampleHelper(
            LabProcedure procedure,
            Department targetDepartment,
            out Entity best,
            out BehaviorNurse bestNurse)
        {
            best = null;
            bestNurse = null;
            if (procedure == null ||
                targetDepartment == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_departments == null)
            {
                return false;
            }

            float bestDistance = float.MaxValue;
            foreach (Department department in
                Hospital.Instance.m_departments)
            {
                if (department == null ||
                    department == targetDepartment ||
                    department.m_departmentPersistentData.m_nurses == null)
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
                    if (employee == null ||
                        employee.m_state.m_department.GetEntity() != department ||
                        HasPendingLocalSampleWork(candidate))
                    {
                        continue;
                    }

                    float distance;
                    if (!CanTakeProcedure(
                            candidate,
                            procedure,
                            allowForeignDepartment: true,
                            out distance))
                    {
                        continue;
                    }

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                        bestNurse =
                            candidate.GetComponent<BehaviorNurse>();
                    }
                }
            }

            return best != null && bestNurse != null;
        }

        private static Department GetProcedureDepartment(
            LabProcedure procedure)
        {
            Entity patient =
                procedure == null ||
                procedure.m_state == null
                    ? null
                    : procedure.m_state.m_patient.GetEntity();
            return patient == null
                ? null
                : patient.GetComponent<BehaviorPatient>()?.GetDepartment();
        }

        private static bool TryContinueSampleTour(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            if (porter == null ||
                nurse == null ||
                job == null ||
                job.StatLab == null ||
                job.CollectedProcedures.Count >=
                    PorterTransportConfig.SampleTourMaxSamples)
            {
                return false;
            }

            LabProcedure next;
            float routeDistance;
            if (!TryFindNextTourProcedure(
                    porter,
                    job,
                    out next,
                    out routeDistance))
            {
                return false;
            }

            TileObject source =
                next.m_state.m_usedEquipment.GetEntity();
            UseComponent use =
                porter.GetComponent<UseComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (source == null ||
                use == null ||
                walk == null)
            {
                return false;
            }

            use.ReserveObject(source);
            if (source.User != porter)
            {
                return false;
            }

            Vector2i parkingTile;
            if (!PorterSampleCartRuntime.TryGetInteractionParkingTile(
                    porter,
                    job,
                    source,
                    out parkingTile))
            {
                use.Interrupt();
                return false;
            }

            job.Procedure = next;
            job.Phase =
                PorterSampleTransportPhase.GoingToSampleParking;

            ClearFallbackTracking(next);
            ShowSampleBubble(porter, next);
            walk.SetDestination(
                parkingTile,
                source.GetFloorIndex());

            return true;
        }

        private static bool TryFindNextTourProcedure(
            Entity porter,
            PorterSampleTransportJob job,
            out LabProcedure best,
            out float bestRouteDistance)
        {
            best = null;
            bestRouteDistance = float.MaxValue;

            EmployeeComponent employee =
                porter == null
                    ? null
                    : porter.GetComponent<EmployeeComponent>();
            Department homeDepartment =
                employee == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            List<LabProcedure> procedures =
                LabProcedureManager.Instance == null
                    ? null
                    : LabProcedureManager.Instance.m_labProcedures;
            if (homeDepartment == null ||
                procedures == null ||
                job == null ||
                job.StatLab == null)
            {
                return false;
            }

            if (TryFindNextTourProcedure(
                    porter,
                    job,
                    homeDepartment,
                    procedures,
                    foreignDepartmentOnly: false,
                    out best,
                    out bestRouteDistance))
            {
                return true;
            }

            return TryFindNextTourProcedure(
                porter,
                job,
                homeDepartment,
                procedures,
                foreignDepartmentOnly: true,
                out best,
                out bestRouteDistance);
        }

        private static bool TryFindNextTourProcedure(
            Entity porter,
            PorterSampleTransportJob job,
            Department homeDepartment,
            List<LabProcedure> procedures,
            bool foreignDepartmentOnly,
            out LabProcedure best,
            out float bestRouteDistance)
        {
            best = null;
            bestRouteDistance = float.MaxValue;

            for (int i = 0; i < procedures.Count; i++)
            {
                LabProcedure procedure = procedures[i];
                Department procedureDepartment =
                    GetProcedureDepartment(procedure);
                if (procedureDepartment == null)
                {
                    continue;
                }

                bool foreign =
                    procedureDepartment != homeDepartment;
                if (foreign != foreignDepartmentOnly)
                {
                    continue;
                }

                float routeDistance;
                if (!CanAddProcedureToTour(
                        porter,
                        job,
                        procedure,
                        out routeDistance))
                {
                    continue;
                }

                if (routeDistance < bestRouteDistance)
                {
                    bestRouteDistance = routeDistance;
                    best = procedure;
                }
            }

            return best != null;
        }

        private static bool CanAddProcedureToTour(
            Entity porter,
            PorterSampleTransportJob job,
            LabProcedure procedure,
            out float routeDistance)
        {
            routeDistance = float.MaxValue;

            if (porter == null ||
                job == null ||
                procedure == null ||
                procedure == job.Procedure ||
                IsProcedureClaimed(procedure) ||
                !IsHospitalizedSampleWaiting(procedure))
            {
                return false;
            }

            if (procedure.m_state.m_statLab == null ||
                procedure.m_state.m_statLab.GetEntity() == null)
            {
                procedure.FindRoom(onlyFree: false);
            }

            Room statLab =
                procedure.m_state.m_statLab == null
                    ? null
                    : procedure.m_state.m_statLab.GetEntity();
            if (statLab == null ||
                statLab != job.StatLab)
            {
                return false;
            }

            TileObject source =
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment.GetEntity();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (source == null ||
                source.User != null ||
                source.Owner != null ||
                walk == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            // After the first pickup, only extend the tour on the floor
            // the Porter is currently working on. The configured search distance
            // and detour guard apply from this point onward; the lab itself may
            // still be on any reachable floor.
            if (source.GetFloorIndex() != walk.GetFloorIndex())
            {
                return false;
            }

            float directToLab;
            float routeViaSample;
            float distanceToSample;
            if (!TryGetTourRouteDistances(
                    porter,
                    source,
                    job.StatLab,
                    out directToLab,
                    out routeViaSample,
                    out distanceToSample))
            {
                return false;
            }

            if (distanceToSample >
                PorterTransportConfig.SampleTourSearchDistance)
            {
                return false;
            }

            float detourAllowance =
                Math.Max(8f, directToLab * 0.5f);
            if (routeViaSample >
                directToLab + detourAllowance)
            {
                return false;
            }

            routeDistance = routeViaSample;
            return true;
        }

        private static bool TryGetTourRouteDistances(
            Entity porter,
            TileObject source,
            Room statLab,
            out float directToLab,
            out float routeViaSample,
            out float distanceToSample)
        {
            directToLab = float.MaxValue;
            routeViaSample = float.MaxValue;
            distanceToSample = float.MaxValue;

            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            Department labDepartment =
                statLab == null ||
                statLab.m_roomPersistentData.m_department == null
                    ? null
                    : statLab.m_roomPersistentData.m_department.GetEntity();
            if (walk == null ||
                source == null ||
                labDepartment == null ||
                labDepartment.m_departmentPersistentData.m_objects == null ||
                MapScriptInterface.Instance == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            float toSource = GetRouteDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                source.GetFloorIndex(),
                source.m_state.m_position);
            if (toSource < 0f)
            {
                return false;
            }
            distanceToSample = toSource;

            foreach (EntityIDPointer<TileObject> pointer in
                labDepartment.m_departmentPersistentData.m_objects)
            {
                TileObject fridge = pointer.GetEntity();
                if (fridge == null ||
                    !fridge.HasTag("medical_fridge") ||
                    !fridge.IsValid() ||
                    fridge.IsBroken() ||
                    MapScriptInterface.Instance.GetRoomAt(
                        fridge.m_state.m_position,
                        fridge.GetFloorIndex()) != statLab)
                {
                    continue;
                }

                Vector2i fridgePosition =
                    fridge.m_state.m_position;

                float direct = GetRouteDistance(
                    walk.GetFloorIndex(),
                    walk.GetCurrentTile(),
                    fridge.GetFloorIndex(),
                    fridgePosition);
                float sourceToLab = GetRouteDistance(
                    source.GetFloorIndex(),
                    source.m_state.m_position,
                    fridge.GetFloorIndex(),
                    fridgePosition);
                if (direct >= 0f &&
                    direct < directToLab)
                {
                    directToLab = direct;
                }
                if (sourceToLab >= 0f &&
                    toSource + sourceToLab <
                        routeViaSample)
                {
                    routeViaSample =
                        toSource + sourceToLab;
                }
            }

            return
                directToLab < float.MaxValue &&
                routeViaSample < float.MaxValue;
        }

        private static float GetRouteDistance(
            int fromFloor,
            Vector2i from,
            int toFloor,
            Vector2i to)
        {
            float distance =
                GridMap.GetInstance().GetDistance(
                    fromFloor,
                    from,
                    toFloor,
                    to,
                    AccessRights.STAFF);
            if (distance < 0f)
            {
                return -1f;
            }

            return distance +
                Math.Abs(fromFloor - toFloor) * 100f;
        }

        private static int GetPorterZoneFloor(
            Entity porter)
        {
            EmployeeComponent employee =
                porter == null
                    ? null
                    : porter.GetComponent<EmployeeComponent>();
            Room homeRoom =
                employee == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();
            if (homeRoom != null)
            {
                return homeRoom.GetFloorIndex();
            }

            WalkComponent walk =
                porter == null
                    ? null
                    : porter.GetComponent<WalkComponent>();
            return walk == null ? -1 : walk.GetFloorIndex();
        }

        private static bool CanTakeProcedure(
            Entity porter,
            LabProcedure procedure,
            bool allowForeignDepartment,
            out float distance)
        {
            distance = float.MaxValue;
            if (!IsAvailablePorter(porter, procedure) ||
                !IsHospitalizedSampleWaiting(procedure))
            {
                return false;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            Department homeDepartment =
                employee == null
                    ? null
                    : employee.m_state.m_department.GetEntity();
            Department procedureDepartment =
                GetProcedureDepartment(procedure);
            if (homeDepartment == null ||
                procedureDepartment == null)
            {
                return false;
            }

            bool foreign =
                procedureDepartment != homeDepartment;
            if (foreign && !allowForeignDepartment)
            {
                return false;
            }

            TileObject source = procedure.m_state.m_usedEquipment == null
                ? null
                : procedure.m_state.m_usedEquipment.GetEntity();
            if (source == null ||
                source.User != null ||
                source.Owner != null)
            {
                return false;
            }

            if (procedure.m_state.m_statLab == null ||
                procedure.m_state.m_statLab.GetEntity() == null)
            {
                procedure.FindRoom(onlyFree: false);
            }
            Room statLab = procedure.m_state.m_statLab == null
                ? null
                : procedure.m_state.m_statLab.GetEntity();
            if (statLab == null ||
                statLab.m_roomPersistentData.m_roomType == null ||
                !statLab.m_roomPersistentData.m_roomType.Entry.HasTag("stat_lab"))
            {
                return false;
            }

            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (walk == null || GridMap.GetInstance() == null ||
                !PorterSampleCartRuntime.HasFreeCart(porter))
            {
                return false;
            }

            // Own-department Sample work is valid on any reachable floor.
            // Cross-department help is anchored to the Porter's station floor,
            // not to a transient floor such as the lab after a delivery.
            if (foreign &&
                source.GetFloorIndex() != GetPorterZoneFloor(porter))
            {
                return false;
            }

            float routeDistance = GridMap.GetInstance().GetDistance(
                walk.GetFloorIndex(),
                walk.GetCurrentTile(),
                source.GetFloorIndex(),
                source.m_state.m_position,
                AccessRights.STAFF);
            if (routeDistance < 0f)
            {
                return false;
            }

            distance = routeDistance +
                Math.Abs(
                    walk.GetFloorIndex() -
                    source.GetFloorIndex()) * 100f;
            return true;
        }

        private static bool IsAvailablePorter(
            Entity porter,
            LabProcedure procedure)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                IsBusy(porter) ||
                Database.Instance == null)
            {
                return false;
            }

            BehaviorNurse nurse =
                porter.GetComponent<BehaviorNurse>();
            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            GameDBEmployeeRole role =
                Database.Instance.GetEntry<GameDBEmployeeRole>(
                    PorterIds.SampleTransportRole);
            if (nurse == null ||
                employee == null ||
                walk == null ||
                role == null ||
                employee.IsFired() ||
                employee.ShouldGoToTraining() ||
                employee.m_state.m_shift != DayTime.Instance.GetShift() ||
                employee.m_state.m_homeRoom == null ||
                !PorterStationRegistry.IsPorterStationOrLegacyNursesStation(
                    employee.GetHomeRoomType()) ||
                !employee.HasRole(role))
            {
                return false;
            }

            NurseState state = nurse.m_state.m_nurseState;
            return
                (state == NurseState.Idle ||
                 state == NurseState.GoingToWorkplace ||
                 state == NurseState.FinishedProcedure) &&
                nurse.m_state.m_currentPatient == null;
        }

        private static bool IsHospitalizedSampleWaiting(LabProcedure procedure)
        {
            if (procedure == null || procedure.m_state == null ||
                procedure.m_state.m_labProcedureState != LabProcedureState.SamplingFinished ||
                procedure.m_state.m_usedEquipment == null ||
                !procedure.IsIdle())
            {
                return false;
            }

            Entity patient = procedure.m_state.m_patient.GetEntity();
            HospitalizationComponent hospitalization = patient == null
                ? null
                : patient.GetComponent<HospitalizationComponent>();
            return hospitalization != null && hospitalization.IsHospitalized();
        }

        private static bool StartJob(
            Entity porter,
            BehaviorNurse nurse,
            LabProcedure procedure,
            bool allowForeignDepartment)
        {
            float distance;
            if (!CanTakeProcedure(porter, procedure, allowForeignDepartment, out distance))
            {
                return false;
            }

            TileObject source = procedure.m_state.m_usedEquipment.GetEntity();
            Room statLab = procedure.m_state.m_statLab == null ? null : procedure.m_state.m_statLab.GetEntity();
            UseComponent use = porter.GetComponent<UseComponent>();
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (source == null || statLab == null || use == null || walk == null)
            {
                return false;
            }

            TileObject cart;
            Room cartHomeRoom;
            if (!PorterSampleCartRuntime.TryReserveCart(
                    porter,
                    out cart,
                    out cartHomeRoom))
            {
                return false;
            }

            nurse.CancelBrowsing();
            AnimModelComponent anim = porter.GetComponent<AnimModelComponent>();
            if (anim != null)
            {
                anim.PlayAnimation(walk.IsSitting() ? "sit_relax_pc_out" : "stand_idle", looping: false);
            }
            nurse.SwitchState(NurseState.Idle);

            use.ReserveObject(source);
            if (source.User != porter)
            {
                PorterSampleCartRuntime.ReleaseReservedCart(cart, porter);
                return false;
            }

            PorterSampleTransportJob job = new PorterSampleTransportJob
            {
                Procedure = procedure,
                StatLab = statLab,
                Cart = cart,
                CartHomeRoom = cartHomeRoom,
                CartHomeTile = cart.m_state.m_position,
                CartHomeFloorIndex = cart.GetFloorIndex(),
                CartHomeOrientation = cart.Orientation,
                Phase = PorterSampleTransportPhase.GoingToCart
            };
            ClearFallbackTracking(procedure);
            Jobs[porter] = job;

            ShowSampleBubble(porter, procedure);
            walk.SetDestination(cart.GetDefaultUsePosition(), cart.GetFloorIndex());

            Entity patient = procedure.m_state.m_patient.GetEntity();
            EmployeeComponent employee = porter.GetComponent<EmployeeComponent>();
            Department homeDepartment = employee == null ? null : employee.m_state.m_department.GetEntity();
            Department targetDepartment = GetProcedureDepartment(procedure);
            bool helpingOtherDepartment =
                homeDepartment != null &&
                targetDepartment != null &&
                homeDepartment != targetDepartment;

            PorterDiagnostics.Log(
                "sample assigned: porter=" +
                porter.Name +
                "; patient=" +
                (patient == null ? "UNKNOWN" : patient.Name) +
                "; examination=" +
                procedure.m_state.m_examination.Entry.DatabaseID +
                "; floors=" +
                source.GetFloorIndex() +
                "->" +
                statLab.GetFloorIndex() +
                "; crossDepartment=" +
                helpingOtherDepartment +
                ".");
            return true;
        }

        private static void UpdateJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            if (job == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (job.Phase == PorterSampleTransportPhase.ReturningCart)
            {
                UpdateReturningCart(porter, nurse, job);
                return;
            }

            if (job.Procedure == null || job.Procedure.m_state == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            LabProcedure procedure = job.Procedure;
            LabProcedureState state = procedure.m_state.m_labProcedureState;
            if (state == LabProcedureState.Finished)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (job.Phase == PorterSampleTransportPhase.GoingToCart)
            {
                if (state != LabProcedureState.SamplingFinished)
                {
                    CancelJob(porter, nurse, job, revertSample: false);
                    return;
                }
                UpdateGoingToCart(porter, nurse, job);
                return;
            }

            if (job.Phase ==
                    PorterSampleTransportPhase.GoingToSampleParking)
            {
                if (state != LabProcedureState.SamplingFinished)
                {
                    CancelJob(porter, nurse, job, revertSample: false);
                    return;
                }
                UpdateGoingToSampleParking(porter, nurse, job);
                return;
            }

            if (job.Phase == PorterSampleTransportPhase.GoingToSample)
            {
                if (state != LabProcedureState.SamplingFinished)
                {
                    CancelJob(porter, nurse, job, revertSample: false);
                    return;
                }
                UpdateGoingToSample(porter, nurse, job);
                return;
            }

            if (state == LabProcedureState.DeliveredToStatLab)
            {
                DeliverCollectedProcedures(job);
                CompleteJob(porter, nurse, job, timedOut: true);
                return;
            }

            if (state != LabProcedureState.SampleBeingDelivered)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            switch (job.Phase)
            {
                case PorterSampleTransportPhase.TakingSample:
                    UpdateTakingSample(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.ReturningToCartAfterSample:
                    UpdateReturningToCartAfterSample(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.WaitingForFridge:
                    TryGoToFridge(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.GoingToFridgeParking:
                    UpdateGoingToFridgeParking(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.GoingToFridge:
                    UpdateGoingToFridge(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.StoringSample:
                    UpdateStoringSample(porter, nurse, job);
                    break;
                case PorterSampleTransportPhase.ReturningToCartAfterFridge:
                    UpdateReturningToCartAfterFridge(porter, nurse, job);
                    break;
            }
        }

        private static void UpdateGoingToCart(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            TileObject source =
                job.Procedure.m_state.m_usedEquipment == null
                    ? null
                    : job.Procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null ||
                source == null ||
                job.Cart == null ||
                job.Cart.User != porter)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            Vector2i parkingTile;
            if (!PorterSampleCartRuntime.TryGetInteractionParkingTile(
                    porter,
                    job,
                    source,
                    out parkingTile))
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (!PorterSampleCartRuntime.TryAttachCart(porter, job.Cart))
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            walk.SetDestination(
                parkingTile,
                source.GetFloorIndex());
            job.Phase =
                PorterSampleTransportPhase.GoingToSampleParking;

        }

        private static void UpdateGoingToSampleParking(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            TileObject source =
                job.Procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null ||
                source == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!PorterSampleCartRuntime.TryParkCartAtCurrentTile(
                    porter,
                    job))
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            walk.SetDestination(
                source.GetDefaultUsePosition(),
                source.GetFloorIndex());
            job.Phase =
                PorterSampleTransportPhase.GoingToSample;
        }

        private static void UpdateGoingToSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            UseComponent use = porter.GetComponent<UseComponent>();
            TileObject source = job.Procedure.m_state.m_usedEquipment.GetEntity();
            if (walk == null || use == null || source == null)
            {
                CancelJob(porter, nurse, job, revertSample: false);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            job.Procedure.SwitchState(LabProcedureState.SampleBeingDelivered);
            use.Activate(UseComponentMode.SINGLE_USE);
            job.Phase = PorterSampleTransportPhase.TakingSample;
        }

        private static void UpdateTakingSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            UseComponent use = porter.GetComponent<UseComponent>();
            if (use == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }
            if (use.IsBusy())
            {
                return;
            }

            if (!job.CollectedProcedures.Contains(job.Procedure))
            {
                job.CollectedProcedures.Add(job.Procedure);
            }


            if (!PorterSampleCartRuntime.TryWalkToParkedCart(
                    porter,
                    job))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            job.Phase =
                PorterSampleTransportPhase.ReturningToCartAfterSample;
        }

        private static void UpdateReturningToCartAfterSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!PorterSampleCartRuntime.TryAttachCart(
                    porter,
                    job.Cart))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (TryContinueSampleTour(porter, nurse, job))
            {
                return;
            }

            job.Phase =
                PorterSampleTransportPhase.WaitingForFridge;
            TryGoToFridge(porter, nurse, job);
        }

        private static void TryGoToFridge(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            Room statLab = job == null
                ? null
                : job.StatLab;
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            UseComponent use = porter.GetComponent<UseComponent>();
            if (statLab == null || walk == null || use == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            TileObject fridge = MapScriptInterface.Instance.FindClosestFreeObjectWithTag(
                porter,
                null,
                walk.GetCurrentTile(),
                statLab,
                "medical_fridge",
                AccessRights.STAFF);
            if (fridge == null)
            {
                return;
            }

            use.ReserveObject(fridge);
            if (fridge.User != porter)
            {
                return;
            }

            Vector2i parkingTile;
            if (!PorterSampleCartRuntime.TryGetInteractionParkingTile(
                    porter,
                    job,
                    fridge,
                    out parkingTile))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            walk.SetDestination(
                parkingTile,
                fridge.GetFloorIndex());
            job.Phase =
                PorterSampleTransportPhase.GoingToFridgeParking;
        }

        private static void UpdateGoingToFridgeParking(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            UseComponent use =
                porter.GetComponent<UseComponent>();
            TileObject fridge =
                use == null ||
                use.m_state.m_reservedObject == null
                    ? null
                    : use.m_state.m_reservedObject.GetEntity();
            if (walk == null ||
                use == null ||
                fridge == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!PorterSampleCartRuntime.TryParkCartAtCurrentTile(
                    porter,
                    job))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            walk.SetDestination(
                fridge.GetDefaultUsePosition(),
                fridge.GetFloorIndex());
            job.Phase =
                PorterSampleTransportPhase.GoingToFridge;
        }

        private static void UpdateGoingToFridge(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            UseComponent use = porter.GetComponent<UseComponent>();
            if (walk == null || use == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }
            if (walk.IsBusy())
            {
                return;
            }

            use.Activate(UseComponentMode.SINGLE_USE);
            job.Phase = PorterSampleTransportPhase.StoringSample;
        }

        private static void UpdateStoringSample(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            UseComponent use = porter.GetComponent<UseComponent>();
            if (use == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }
            if (use.IsBusy())
            {
                return;
            }

            if (!PorterSampleCartRuntime.TryWalkToParkedCart(
                    porter,
                    job))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            job.Phase =
                PorterSampleTransportPhase.ReturningToCartAfterFridge;
        }

        private static void UpdateReturningToCartAfterFridge(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk =
                porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            if (!PorterSampleCartRuntime.TryAttachCart(
                    porter,
                    job.Cart))
            {
                CancelJob(porter, nurse, job, revertSample: true);
                return;
            }

            int delivered =
                DeliverCollectedProcedures(job);
            PorterExperience.AwardWorkExperience(
                porter,
                delivered);
            PorterDiagnostics.Log(
                "sample completed: porter=" +
                porter.Name +
                "; delivered=" +
                delivered +
                "; labFloor=" +
                job.StatLab.GetFloorIndex() +
                ".");

            CompleteJob(porter, nurse, job, timedOut: false);
        }

        private static void UpdateReturningCart(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job)
        {
            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (walk == null)
            {
                PorterSampleCartRuntime.RestoreCart(job, porter);
                FinalizeJob(porter, nurse, job, job.NativeTimeout);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            PorterSampleCartRuntime.RestoreCart(job, porter);

            FinalizeJob(porter, nurse, job, job.NativeTimeout);
        }

        private static int DeliverCollectedProcedures(PorterSampleTransportJob job)
        {
            if (job == null)
            {
                return 0;
            }

            int delivered = 0;
            for (int i = 0; i < job.CollectedProcedures.Count; i++)
            {
                LabProcedure procedure = job.CollectedProcedures[i];
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState != LabProcedureState.SampleBeingDelivered)
                {
                    continue;
                }

                procedure.SwitchState(LabProcedureState.DeliveredToStatLab);
                ClearFallbackTracking(procedure);
                delivered++;
            }

            if (job.Procedure != null &&
                job.Procedure.m_state != null &&
                job.Procedure.m_state.m_labProcedureState == LabProcedureState.SampleBeingDelivered &&
                !job.CollectedProcedures.Contains(job.Procedure))
            {
                job.Procedure.SwitchState(LabProcedureState.DeliveredToStatLab);
                ClearFallbackTracking(job.Procedure);
                delivered++;
            }

            return delivered;
        }

        private static void CompleteJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool timedOut)
        {
            SpeechComponent speech = porter.GetComponent<SpeechComponent>();
            if (speech != null)
            {
                speech.HideBubble();
            }

            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (job != null &&
                job.Cart != null &&
                walk != null &&
                job.Cart.m_state.m_moving &&
                job.Cart.User == porter)
            {
                job.NativeTimeout = timedOut;
                job.Phase = PorterSampleTransportPhase.ReturningCart;
                walk.SetDestination(
                    job.CartHomeTile,
                    job.CartHomeFloorIndex);

                return;
            }

            PorterSampleCartRuntime.RestoreCart(job, porter);
            FinalizeJob(porter, nurse, job, timedOut);
        }

        private static void FinalizeJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool timedOut)
        {
            Jobs.Remove(porter);
            nurse.SwitchState(NurseState.Idle);

        }

        private static void CancelJob(
            Entity porter,
            BehaviorNurse nurse,
            PorterSampleTransportJob job,
            bool revertSample)
        {
            Jobs.Remove(porter);

            UseComponent use = porter.GetComponent<UseComponent>();
            if (use != null && (use.m_state.m_reservedObject != null || use.IsUsing()))
            {
                use.Interrupt();
            }

            WalkComponent walk = porter.GetComponent<WalkComponent>();
            if (walk != null)
            {
                walk.Stop();
            }
            SpeechComponent speech = porter.GetComponent<SpeechComponent>();
            if (speech != null)
            {
                speech.HideBubble();
            }

            int requeued = RequeueUndeliveredSamples(job, revertSample);
            PorterSampleCartRuntime.RestoreCart(job, porter);
            nurse.SwitchState(NurseState.Idle);

            if (requeued > 0)
            {
                PorterDiagnostics.Log(
                    "sample cancelled: porter=" +
                    porter.Name +
                    "; requeued=" +
                    requeued +
                    ".");
            }
        }

        private static int RequeueUndeliveredSamples(
            PorterSampleTransportJob job,
            bool includeCurrent)
        {
            if (job == null)
            {
                return 0;
            }

            HashSet<LabProcedure> procedures = new HashSet<LabProcedure>();
            for (int i = 0; i < job.CollectedProcedures.Count; i++)
            {
                if (job.CollectedProcedures[i] != null)
                {
                    procedures.Add(job.CollectedProcedures[i]);
                }
            }
            if (includeCurrent && job.Procedure != null)
            {
                procedures.Add(job.Procedure);
            }

            int requeued = 0;
            foreach (LabProcedure procedure in procedures)
            {
                if (procedure == null ||
                    procedure.m_state == null ||
                    procedure.m_state.m_labProcedureState != LabProcedureState.SampleBeingDelivered)
                {
                    continue;
                }

                procedure.SwitchState(LabProcedureState.SamplingFinished);
                ClearFallbackTracking(procedure);
                requeued++;
            }
            return requeued;
        }

        private static void RecoverOrphanedUseReservation(
            Entity porter,
            BehaviorNurse nurse)
        {
            if (!PorterIdentity.IsPorter(porter) ||
                nurse.m_state.m_nurseState != NurseState.Idle)
            {
                return;
            }

            UseComponent use = porter.GetComponent<UseComponent>();
            if (use == null ||
                (use.m_state.m_reservedObject == null && !use.IsUsing()))
            {
                return;
            }

            use.Interrupt();
            PorterDiagnostics.Log(
                "sample recovery: porter=" +
                porter.Name +
                "; action=cleared orphaned equipment reservation.");
        }

        private static bool IsProcedureClaimed(LabProcedure procedure)
        {
            if (procedure == null)
            {
                return false;
            }

            foreach (KeyValuePair<Entity, PorterSampleTransportJob> pair in Jobs)
            {
                PorterSampleTransportJob job = pair.Value;
                if (job == null)
                {
                    continue;
                }

                if (job.Procedure == procedure ||
                    job.CollectedProcedures.Contains(procedure))
                {
                    return true;
                }
            }
            return false;
        }

        internal static float GetDeliveryTimeoutHours(LabProcedure procedure)
        {
            return IsProcedureClaimed(procedure)
                ? float.MaxValue
                : 4f;
        }

        private static bool IsHandledByLabSpecialist(LabProcedure procedure)
        {
            if (Hospital.Instance == null || Hospital.Instance.m_characters == null)
            {
                return false;
            }

            foreach (Entity character in Hospital.Instance.m_characters)
            {
                BehaviorLabSpecialist lab = character == null
                    ? null
                    : character.GetComponent<BehaviorLabSpecialist>();
                if (lab != null &&
                    lab.m_state.m_currentLabProcedure != null &&
                    lab.m_state.m_currentLabProcedure.GetEntity() == procedure)
                {
                    return true;
                }
            }
            return false;
        }

        private static void ShowSampleBubble(Entity porter, LabProcedure procedure)
        {
            if (porter == null || procedure == null || procedure.m_state == null)
            {
                return;
            }

            GameDBExamination testing =
                procedure.m_state.m_examination.Entry.LabTestingExaminationRef.Entry;
            SpeechComponent speech = porter.GetComponent<SpeechComponent>();
            if (testing == null || speech == null)
            {
                return;
            }

            if (testing.CustomIconBigAssetRef != null)
            {
                speech.SetBubble(
                    "BUBBLE_EMPTY",
                    testing.CustomIconBigAssetRef.XmlID,
                    4f);
            }
            else
            {
                speech.SetBubble("BUBBLE_EMPTY", testing.IconIndex, 4f);
            }
        }
    }

    [HarmonyPatch(
        typeof(Department),
        nameof(Department.OnDayStart),
        new Type[] { })]
    internal static class PorterSampleCartNotificationDayResetPatch
    {
        private static void Postfix(Department __instance)
        {
            PorterSampleTransportRuntime.ResetMissingCartNotification(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorNurse),
        nameof(BehaviorNurse.Update),
        new Type[] { typeof(float) })]
    internal static class PorterSampleTransportNurseUpdatePatch
    {
        private static bool Prefix(
            BehaviorNurse __instance,
            ref bool __state)
        {
            __state = PorterSampleTransportRuntime.UpdateActiveMission(__instance);
            return !__state;
        }

        private static void Postfix(
            BehaviorNurse __instance,
            bool __state)
        {
            if (!__state)
            {
                PorterSampleTransportRuntime.TryAssignFromIdlePorter(__instance);
            }
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        "SelectNextLabProcedureStep",
        new Type[] { typeof(LabProcedure) })]
    internal static class PorterSampleTransportLabFallbackPatch
    {
        private static bool Prefix(
            LabProcedure procedure,
            ref bool __result)
        {
            if (!PorterSampleTransportRuntime.ShouldDeferLabFallback(procedure))
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(
        typeof(LabProcedure),
        nameof(LabProcedure.Update),
        new Type[] { typeof(int), typeof(float) })]
    internal static class PorterSampleTransportRecoveryPatch
    {
        private static void Prefix(LabProcedure __instance)
        {
            PorterSampleTransportRuntime.RecoverOrphanedDelivery(__instance);
        }

        private static void Postfix(LabProcedure __instance)
        {
            PorterSampleTransportRuntime.CleanupFallbackTracking(__instance);
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes = new List<CodeInstruction>(instructions);
            MethodInfo conversion = AccessTools.Method(
                typeof(DayTime),
                nameof(DayTime.IngameTimeHoursToRealTimeSeconds),
                new Type[] { typeof(float) });
            MethodInfo timeoutHelper = AccessTools.Method(
                typeof(PorterSampleTransportRuntime),
                nameof(PorterSampleTransportRuntime.GetDeliveryTimeoutHours),
                new Type[] { typeof(LabProcedure) });

            if (conversion == null || timeoutHelper == null)
            {
                Plugin.Log?.LogWarning(
                    "Porter sample timeout patch could not resolve the native conversion/helper; vanilla 4-hour timeout remains active.");
                return codes;
            }

            int matchIndex = -1;
            int matches = 0;
            for (int i = 0; i + 1 < codes.Count; i++)
            {
                if (codes[i].opcode != OpCodes.Ldc_R4 ||
                    !(codes[i].operand is float) ||
                    (float)codes[i].operand != 4f)
                {
                    continue;
                }

                MethodInfo called = codes[i + 1].operand as MethodInfo;
                if (called != conversion)
                {
                    continue;
                }

                matchIndex = i;
                matches++;
            }

            if (matches != 1)
            {
                Plugin.Log?.LogWarning(
                    "Porter sample timeout patch expected one native 4-hour delivery threshold but found " +
                    matches + "; vanilla timeout remains unchanged.");
                return codes;
            }

            CodeInstruction loadProcedure = new CodeInstruction(OpCodes.Ldarg_0);
            loadProcedure.labels.AddRange(codes[matchIndex].labels);
            codes[matchIndex].labels.Clear();
            codes[matchIndex] = loadProcedure;
            codes.Insert(matchIndex + 1, new CodeInstruction(OpCodes.Call, timeoutHelper));

            return codes;
        }
    }

}
