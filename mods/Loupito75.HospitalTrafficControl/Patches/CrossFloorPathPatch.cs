using System;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    internal static class CrossFloorVisitorRecovery
    {
        [ThreadStatic]
        private static DLCProcedureRoomVisit s_activatingRoomVisit;

        [ThreadStatic]
        private static DLCProcedureLoungeVisit s_activatingLoungeVisit;

        [ThreadStatic]
        private static DLCProcedureRoomVisit s_pendingRoomVisit;

        [ThreadStatic]
        private static DLCProcedureLoungeVisit s_pendingLoungeVisit;

        internal static void BeginRoomVisit(DLCProcedureRoomVisit procedure)
        {
            s_activatingRoomVisit = procedure;
        }

        internal static void EndRoomVisit(DLCProcedureRoomVisit procedure)
        {
            if (object.ReferenceEquals(s_activatingRoomVisit, procedure))
            {
                s_activatingRoomVisit = null;
            }

            CompleteRoomVisit(procedure);
        }

        internal static void BeginLoungeVisit(DLCProcedureLoungeVisit procedure)
        {
            s_activatingLoungeVisit = procedure;
        }

        internal static void EndLoungeVisit(DLCProcedureLoungeVisit procedure)
        {
            if (object.ReferenceEquals(s_activatingLoungeVisit, procedure))
            {
                s_activatingLoungeVisit = null;
            }

            CompleteLoungeVisit(procedure);
        }

        internal static bool TryPrepare(WalkComponent walk)
        {
            Entity character = CharacterAccess.GetEntity(walk);
            if (character == null)
            {
                return false;
            }

            BehaviorVisitor visitor = character.GetComponent<BehaviorVisitor>();
            ProcedureScript currentProcedure = GetCurrentProcedure(character);
            DLCProcedureRoomVisit roomVisit = currentProcedure as DLCProcedureRoomVisit;
            if (visitor != null && roomVisit != null)
            {
                s_pendingRoomVisit = roomVisit;
                walk.Stop();

                // During Activate(), the vanilla script writes its travelling state
                // after SetDestination() returns. Defer completion until the Postfix.
                // Loaded/stale states do not pass through Activate(), so recover now.
                if (!object.ReferenceEquals(s_activatingRoomVisit, roomVisit))
                {
                    CompleteRoomVisit(roomVisit);
                }
                return true;
            }

            BehaviorPatient patientBehavior = character.GetComponent<BehaviorPatient>();
            if (patientBehavior == null ||
                patientBehavior.m_state == null ||
                patientBehavior.m_state.m_visitor == null ||
                patientBehavior.m_state.m_visitor.GetEntity() == null)
            {
                return false;
            }

            Entity visitorEntity = patientBehavior.m_state.m_visitor.GetEntity();
            DLCProcedureLoungeVisit loungeVisit =
                GetCurrentProcedure(visitorEntity) as DLCProcedureLoungeVisit;
            if (loungeVisit == null)
            {
                return false;
            }

            HospitalizationComponent hospitalization =
                character.GetComponent<HospitalizationComponent>();
            if (hospitalization == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_bed == null ||
                hospitalization.m_state.m_bed.GetEntity() == null)
            {
                return false;
            }

            s_pendingLoungeVisit = loungeVisit;
            walk.Stop();

            if (!object.ReferenceEquals(s_activatingLoungeVisit, loungeVisit))
            {
                CompleteLoungeVisit(loungeVisit);
            }
            return true;
        }

        private static void CompleteRoomVisit(DLCProcedureRoomVisit procedure)
        {
            if (procedure == null || !object.ReferenceEquals(s_pendingRoomVisit, procedure))
            {
                return;
            }

            s_pendingRoomVisit = null;

            Entity visitorEntity = procedure.m_stateData.m_procedureScene.MainCharacter;
            if (visitorEntity == null)
            {
                procedure.SwitchState("IDLE");
                return;
            }

            BehaviorVisitor visitor = visitorEntity.GetComponent<BehaviorVisitor>();
            Entity patient = null;
            if (visitor != null && visitor.m_state != null && visitor.m_state.m_patient != null)
            {
                patient = visitor.m_state.m_patient.GetEntity();
            }

            if (patient != null)
            {
                HospitalizationComponent hospitalization =
                    patient.GetComponent<HospitalizationComponent>();
                if (hospitalization != null &&
                    hospitalization.m_state != null &&
                    hospitalization.m_state.m_hospitalizationState ==
                        HospitalizationState.OverridenByVisit)
                {
                    hospitalization.SwitchState(HospitalizationState.InBed);
                }
            }

            procedure.SwitchState("IDLE");
            LogRecovery(visitorEntity, "room-visit-aborted");
        }

        private static void CompleteLoungeVisit(DLCProcedureLoungeVisit procedure)
        {
            if (procedure == null || !object.ReferenceEquals(s_pendingLoungeVisit, procedure))
            {
                return;
            }

            s_pendingLoungeVisit = null;

            Entity visitorEntity = procedure.m_stateData.m_procedureScene.MainCharacter;
            if (visitorEntity == null)
            {
                procedure.SwitchState("IDLE");
                return;
            }

            BehaviorVisitor visitor = visitorEntity.GetComponent<BehaviorVisitor>();
            Entity patient = null;
            if (visitor != null && visitor.m_state != null && visitor.m_state.m_patient != null)
            {
                patient = visitor.m_state.m_patient.GetEntity();
            }

            if (patient == null)
            {
                procedure.SwitchState("IDLE");
                LogRecovery(visitorEntity, "lounge-visit-aborted-no-patient");
                return;
            }

            WalkComponent patientWalk = patient.GetComponent<WalkComponent>();
            HospitalizationComponent hospitalization =
                patient.GetComponent<HospitalizationComponent>();

            if (patientWalk == null ||
                hospitalization == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_bed == null ||
                hospitalization.m_state.m_bed.GetEntity() == null)
            {
                procedure.SwitchState("IDLE");
                LogRecovery(visitorEntity, "lounge-visit-aborted-no-bed");
                return;
            }

            ReleasePendingSeat(patientWalk, patient);
            patientWalk.Stop();
            procedure.SwitchState("IDLE");
            patientWalk.GoSit(hospitalization.m_state.m_bed.GetEntity());
            hospitalization.SwitchState(HospitalizationState.GoingToBed);
            LogRecovery(visitorEntity, "lounge-visit-aborted");
        }

        internal static void ReleasePendingSeat(WalkComponent walk, Entity character)
        {
            if (walk == null || walk.m_state == null)
            {
                return;
            }

            if (walk.m_state.m_objectToSitOn != null &&
                walk.m_state.m_objectToSitOn.GetEntity() != null &&
                walk.m_state.m_objectToSitOn.GetEntity().User == character)
            {
                walk.m_state.m_objectToSitOn.GetEntity().User = null;
            }

            walk.m_state.m_objectToSitOn = null;
        }

        private static ProcedureScript GetCurrentProcedure(Entity character)
        {
            ProcedureComponent procedure = character.GetComponent<ProcedureComponent>();
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_currentProcedureScript == null)
            {
                return null;
            }

            return procedure.m_state.m_currentProcedureScript.GetEntity();
        }

        private static void LogRecovery(Entity visitor, string recovery)
        {
            if (!TrafficControlConfig.PathfindingDebug)
            {
                return;
            }

            string characterName = visitor == null
                ? "<unknown>"
                : (visitor.Name ?? string.Empty).Trim();

            Plugin.Log?.LogWarning(
                "[PathDebug] CROSS_FLOOR_VISITOR_RECOVERY entity='" +
                characterName + "' recovery=" + recovery +
                ". Invalid visit movement was cancelled through native movement and procedure states.");
        }
    }

    [HarmonyPatch(typeof(WalkComponent), "UpdateDestinationSet")]
    internal static class CrossFloorPathPatch
    {
        private static bool Prefix(WalkComponent __instance)
        {
            if (__instance == null ||
                __instance.m_state == null ||
                __instance.Floor == null ||
                __instance.m_state.m_lying)
            {
                return true;
            }

            int currentFloor = __instance.Floor.m_floorIndex;
            int destinationFloor = __instance.m_state.m_destinationFloor;

            if (currentFloor == destinationFloor ||
                __instance.m_state.m_walkMidpoint1 != null)
            {
                return true;
            }

            // Retry the native inter-floor split first. This also recovers stale
            // DestinationSet states restored from a save if the graph is valid now.
            try
            {
                __instance.CheckElevator();
            }
            catch (Exception exception)
            {
                LogBlockedCrossFloorPath(__instance, currentFloor, destinationFloor, exception);
                if (!CrossFloorVisitorRecovery.TryPrepare(__instance))
                {
                    EnterNoPath(__instance);
                }
                return false;
            }

            if (__instance.Floor.m_floorIndex == __instance.m_state.m_destinationFloor ||
                __instance.m_state.m_walkMidpoint1 != null)
            {
                return true;
            }

            // Without a native midpoint, SetupJob() would use the final destination
            // coordinates with the character's current Floor as NavigationInfoProvider.
            LogBlockedCrossFloorPath(__instance, currentFloor, destinationFloor, null);
            if (!CrossFloorVisitorRecovery.TryPrepare(__instance))
            {
                EnterNoPath(__instance);
            }
            return false;
        }

        private static void EnterNoPath(WalkComponent walk)
        {
            walk.SwitchState(WalkState.NoPath);
            Entity entity = CharacterAccess.GetEntity(walk);
            CrossFloorVisitorRecovery.ReleasePendingSeat(walk, entity);
        }

        private static void LogBlockedCrossFloorPath(
            WalkComponent walk,
            int currentFloor,
            int destinationFloor,
            Exception exception)
        {
            if (!TrafficControlConfig.PathfindingDebug)
            {
                return;
            }

            Entity entity = CharacterAccess.GetEntity(walk);
            string characterName = entity == null
                ? "<unknown>"
                : (entity.Name ?? string.Empty).Trim();

            string message =
                "[PathDebug] CROSS_FLOOR_NO_MIDPOINT entity='" + characterName +
                "' current=" + walk.GetCurrentTileSafe() +
                " floor=" + currentFloor +
                " destination=" + walk.GetDestinationTile() +
                " destinationFloor=" + destinationFloor +
                ". Skipping invalid same-floor PathfinderJob.";

            if (exception != null)
            {
                message += " CheckElevator failed with " +
                           exception.GetType().Name + ": " + exception.Message;
            }

            Plugin.Log?.LogWarning(message);
        }
    }

    [HarmonyPatch(typeof(DLCProcedureRoomVisit), "Activate")]
    internal static class CrossFloorRoomVisitActivatePatch
    {
        private static void Prefix(DLCProcedureRoomVisit __instance)
        {
            CrossFloorVisitorRecovery.BeginRoomVisit(__instance);
        }

        private static void Postfix(DLCProcedureRoomVisit __instance)
        {
            CrossFloorVisitorRecovery.EndRoomVisit(__instance);
        }
    }

    [HarmonyPatch(typeof(DLCProcedureLoungeVisit), "Activate")]
    internal static class CrossFloorLoungeVisitActivatePatch
    {
        private static void Prefix(DLCProcedureLoungeVisit __instance)
        {
            CrossFloorVisitorRecovery.BeginLoungeVisit(__instance);
        }

        private static void Postfix(DLCProcedureLoungeVisit __instance)
        {
            CrossFloorVisitorRecovery.EndLoungeVisit(__instance);
        }
    }
}
