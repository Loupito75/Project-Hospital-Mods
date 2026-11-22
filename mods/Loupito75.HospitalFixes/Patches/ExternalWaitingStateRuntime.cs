using System;
using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;
using UnityEngine;

namespace HospitalFixes.Patches
{
    internal sealed class ExternalWaitingPositionState
    {
        internal Vector2i Tile;
        internal int FloorIndex;
        internal float NextMoveRetryAt;
    }

    internal static class ExternalWaitingStateRuntime
    {
        private const float RepositionRetrySeconds = 0.5f;
        private const string LabSpecialistSampleDestinationSourceId =
            "loupito75.HospitalFixes:lab-specialist-sample-destination";

        private static readonly Dictionary<Entity, HashSet<string>>
            WaitingSources =
                new Dictionary<Entity, HashSet<string>>();

        private static readonly Dictionary<
            Entity,
            Dictionary<string, TileObject[]>>
            RepositionRequests =
                new Dictionary<
                    Entity,
                    Dictionary<string, TileObject[]>>();

        private static readonly Dictionary<
            Entity,
            Dictionary<string, int>>
            PresentationModes =
                new Dictionary<
                    Entity,
                    Dictionary<string, int>>();

        private static readonly Dictionary<Entity, ExternalWaitingPositionState>
            WaitPositions =
                new Dictionary<Entity, ExternalWaitingPositionState>();

        private static readonly Dictionary<Entity, int>
            LabSpecialistNextFridgeRetryMinutes =
                new Dictionary<Entity, int>();

        private static readonly Dictionary<Entity, int>
            LabSpecialistFridgeContentionAttempts =
                new Dictionary<Entity, int>();

        private static readonly List<Entity>
            RefreshBuffer =
                new List<Entity>();

        private static readonly List<Entity>
            ClearBuffer =
                new List<Entity>();

        private static bool RuntimeActive;

        internal static void SetRuntimeActive(
            bool active)
        {
            RuntimeActive = active;

            if (!active)
            {
                ClearAll();
            }
        }

        internal static bool TrySetWaitingState(
            Entity character,
            string sourceId,
            bool waiting)
        {
            return TrySetWaitingState(
                character,
                sourceId,
                waiting,
                false,
                null,
                HospitalFixesApi.ExternalWaitingPresentationDefault);
        }

        internal static bool TrySetWaitingState(
            Entity character,
            string sourceId,
            bool waiting,
            TileObject[] interactionObjects)
        {
            return TrySetWaitingState(
                character,
                sourceId,
                waiting,
                true,
                interactionObjects,
                HospitalFixesApi.ExternalWaitingPresentationDefault);
        }

        internal static bool TrySetWaitingState(
            Entity character,
            string sourceId,
            bool waiting,
            int presentationMode)
        {
            return TrySetWaitingState(
                character,
                sourceId,
                waiting,
                false,
                null,
                presentationMode);
        }

        internal static void RefreshInternalRepositionState(
            Entity character,
            string sourceId,
            bool active,
            TileObject[] interactionObjects)
        {
            if (character == null ||
                string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            string normalizedSourceId =
                sourceId.Trim();

            if (normalizedSourceId.Length == 0)
            {
                return;
            }

            if (!active)
            {
                if (RemoveRepositionRequest(
                        character,
                        normalizedSourceId))
                {
                    ReleaseWaitPosition(
                        character,
                        true);
                }

                return;
            }

            bool changed =
                SetRepositionRequest(
                    character,
                    normalizedSourceId,
                    interactionObjects);

            if (changed)
            {
                ReleaseWaitPosition(
                    character,
                    true);
            }

            RefreshRepositionState(
                character);
        }

        private static bool TrySetWaitingState(
            Entity character,
            string sourceId,
            bool waiting,
            bool allowReposition,
            TileObject[] interactionObjects,
            int presentationMode)
        {
            if (character == null ||
                string.IsNullOrEmpty(sourceId))
            {
                return false;
            }

            string normalizedSourceId =
                sourceId.Trim();

            if (normalizedSourceId.Length == 0)
            {
                return false;
            }

            if (!waiting)
            {
                bool repositionRemoved =
                    RemoveRepositionRequest(
                        character,
                        normalizedSourceId);
                RemovePresentationMode(
                    character,
                    normalizedSourceId);

                HashSet<string> existingSources;
                if (!WaitingSources.TryGetValue(
                        character,
                        out existingSources))
                {
                    if (repositionRemoved)
                    {
                        ReleaseWaitPosition(
                            character,
                            true);
                    }

                    return true;
                }

                bool removed =
                    existingSources.Remove(
                        normalizedSourceId);

                if (repositionRemoved)
                {
                    ReleaseWaitPosition(
                        character,
                        true);
                }

                if (existingSources.Count == 0)
                {
                    WaitingSources.Remove(
                        character);
                    RepositionRequests.Remove(
                        character);
                    PresentationModes.Remove(
                        character);
                    ReleaseWaitPosition(
                        character,
                        true);

                    ReservedStaffWaitingVisuals
                        .HandleExternalWaitingCleared(
                            character);
                }

                if (removed &&
                    HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Result(
                        "ExternalWaitingVisuals",
                        "external waiting source",
                        "cleared",
                        "character={" +
                        HfxDiagnostics.EntityContext(character) +
                        "} sourceId=" +
                        normalizedSourceId +
                        " sourceCount=" +
                        existingSources.Count);
                }

                return true;
            }

            if (!RuntimeActive ||
                !IsValidPresentationMode(presentationMode))
            {
                return false;
            }

            if (character.DEBUG_destroyed ||
                character.GetComponent<Behavior>() == null ||
                character.GetComponent<WalkComponent>() == null ||
                character.GetComponent<SpeechComponent>() == null ||
                (presentationMode ==
                    HospitalFixesApi.ExternalWaitingPresentationDefault &&
                 character.GetComponent<AnimModelComponent>() == null))
            {
                return false;
            }

            HashSet<string> sources;
            if (!WaitingSources.TryGetValue(
                    character,
                    out sources))
            {
                sources =
                    new HashSet<string>(
                        StringComparer.Ordinal);
                WaitingSources.Add(
                    character,
                    sources);
            }

            bool added =
                sources.Add(
                    normalizedSourceId);

            SetPresentationMode(
                character,
                normalizedSourceId,
                presentationMode);

            bool repositionChanged;
            if (allowReposition &&
                presentationMode ==
                    HospitalFixesApi.ExternalWaitingPresentationDefault)
            {
                repositionChanged =
                    SetRepositionRequest(
                        character,
                        normalizedSourceId,
                        interactionObjects);
            }
            else
            {
                repositionChanged =
                    RemoveRepositionRequest(
                        character,
                        normalizedSourceId);
            }

            if (repositionChanged)
            {
                ReleaseWaitPosition(
                    character,
                    true);
            }

            if (added &&
                HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "ExternalWaitingVisuals",
                    "external waiting source",
                    "registered",
                    "character={" +
                    HfxDiagnostics.EntityContext(character) +
                    "} sourceId=" +
                    normalizedSourceId +
                    " sourceCount=" +
                    sources.Count +
                    " reposition=" +
                    (allowReposition &&
                     presentationMode ==
                        HospitalFixesApi.ExternalWaitingPresentationDefault) +
                    " presentationMode=" +
                    presentationMode);
            }

            return true;
        }

        internal static void EmergencyClearWaitingState(
            Entity character,
            string sourceId)
        {
            if (character == null ||
                string.IsNullOrEmpty(sourceId))
            {
                return;
            }

            string normalizedSourceId =
                sourceId.Trim();

            if (normalizedSourceId.Length == 0)
            {
                return;
            }

            bool repositionChanged =
                RemoveRepositionRequest(
                    character,
                    normalizedSourceId);
            RemovePresentationMode(
                character,
                normalizedSourceId);

            HashSet<string> sources;
            if (!WaitingSources.TryGetValue(
                    character,
                    out sources) ||
                sources == null)
            {
                if (repositionChanged)
                {
                    ReleaseWaitPosition(
                        character,
                        true);
                }

                return;
            }

            sources.Remove(
                normalizedSourceId);

            if (repositionChanged)
            {
                ReleaseWaitPosition(
                    character,
                    true);
            }

            if (sources.Count != 0)
            {
                return;
            }

            WaitingSources.Remove(
                character);
            RepositionRequests.Remove(
                character);
            PresentationModes.Remove(
                character);
            ReleaseWaitPosition(
                character,
                true);

            ReservedStaffWaitingVisuals
                .HandleExternalWaitingCleared(
                    character);
        }

        internal static bool HasWaitingState(
            Entity character)
        {
            if (character == null)
            {
                return false;
            }

            HashSet<string> sources;
            return WaitingSources.TryGetValue(
                    character,
                    out sources) &&
                sources != null &&
                sources.Count > 0;
        }

        internal static bool HasWaitingSource(
            Entity character,
            string sourceId)
        {
            if (character == null ||
                string.IsNullOrEmpty(sourceId))
            {
                return false;
            }

            HashSet<string> sources;
            return WaitingSources.TryGetValue(
                    character,
                    out sources) &&
                sources != null &&
                sources.Contains(sourceId);
        }

        internal static bool ShouldPreservePose(
            Entity character)
        {
            if (character == null)
            {
                return false;
            }

            Dictionary<string, int> modes;
            if (!PresentationModes.TryGetValue(
                    character,
                    out modes) ||
                modes == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, int> pair
                     in modes)
            {
                if (pair.Value ==
                    HospitalFixesApi
                        .ExternalWaitingPresentationPreservePose)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsValidPresentationMode(
            int presentationMode)
        {
            return presentationMode ==
                    HospitalFixesApi.ExternalWaitingPresentationDefault ||
                presentationMode ==
                    HospitalFixesApi.ExternalWaitingPresentationPreservePose;
        }

        private static void SetPresentationMode(
            Entity character,
            string sourceId,
            int presentationMode)
        {
            Dictionary<string, int> modes;
            if (!PresentationModes.TryGetValue(
                    character,
                    out modes))
            {
                modes =
                    new Dictionary<string, int>(
                        StringComparer.Ordinal);
                PresentationModes.Add(
                    character,
                    modes);
            }

            modes[sourceId] =
                presentationMode;
        }

        private static void RemovePresentationMode(
            Entity character,
            string sourceId)
        {
            Dictionary<string, int> modes;
            if (!PresentationModes.TryGetValue(
                    character,
                    out modes) ||
                modes == null)
            {
                return;
            }

            modes.Remove(sourceId);

            if (modes.Count == 0)
            {
                PresentationModes.Remove(character);
            }
        }

        internal static void RefreshLabSpecialistSampleDestinationWait(
            BehaviorLabSpecialist specialist)
        {
            if (specialist == null ||
                specialist.m_entity == null ||
                specialist.m_state == null)
            {
                return;
            }

            Entity character =
                specialist.m_entity;

            UseComponent use =
                specialist.GetComponent<UseComponent>();

            bool shouldWait =
                FixConfig.LabSpecialistSampleDestinationWaitEnabled &&
                specialist.m_state.m_labSpecialistState ==
                    LabSpecialistState.GoingToStoreSampleFromPatient &&
                use != null &&
                !use.IsBusy();

            if (!shouldWait)
            {
                if (HasWaitingSource(
                        character,
                        LabSpecialistSampleDestinationSourceId))
                {
                    TrySetWaitingState(
                        character,
                        LabSpecialistSampleDestinationSourceId,
                        false,
                        null);
                }

                return;
            }

            if (HasWaitingSource(
                    character,
                    LabSpecialistSampleDestinationSourceId))
            {
                return;
            }

            LabProcedure procedure =
                specialist.m_state.m_currentLabProcedure == null
                    ? null
                    : specialist.m_state.m_currentLabProcedure
                        .GetEntity();

            TileObject source =
                procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_usedEquipment == null
                    ? null
                    : procedure.m_state.m_usedEquipment
                        .GetEntity();

            TileObject[] interactionObjects =
                source == null
                    ? null
                    : new TileObject[] { source };

            TrySetWaitingState(
                character,
                LabSpecialistSampleDestinationSourceId,
                true,
                interactionObjects);
        }

        internal static bool ShouldRunLabSpecialistDestinationSearch(
            BehaviorLabSpecialist specialist,
            out bool trackedAttempt)
        {
            trackedAttempt = false;

            if (!FixConfig.LabSpecialistSampleDestinationWaitEnabled ||
                specialist == null ||
                specialist.m_entity == null ||
                specialist.m_state == null ||
                specialist.m_state.m_labSpecialistState !=
                    LabSpecialistState.GoingToStoreSampleFromPatient)
            {
                return true;
            }

            UseComponent use =
                specialist.GetComponent<UseComponent>();

            if (use == null ||
                use.IsBusy())
            {
                return true;
            }

            if (DayTime.Instance == null)
            {
                trackedAttempt = true;
                return true;
            }

            int now =
                GetCurrentGameMinute();

            int nextRetryMinute;
            if (LabSpecialistNextFridgeRetryMinutes.TryGetValue(
                    specialist.m_entity,
                    out nextRetryMinute) &&
                now < nextRetryMinute)
            {
                return false;
            }

            trackedAttempt = true;
            return true;
        }

        internal static void HandleLabSpecialistDestinationSearchCompleted(
            BehaviorLabSpecialist specialist,
            bool trackedAttempt)
        {
            if (!trackedAttempt ||
                specialist == null ||
                specialist.m_entity == null ||
                specialist.m_state == null)
            {
                return;
            }

            Entity character =
                specialist.m_entity;

            if (!FixConfig.LabSpecialistSampleDestinationWaitEnabled ||
                specialist.m_state.m_labSpecialistState !=
                    LabSpecialistState.GoingToStoreSampleFromPatient)
            {
                ClearLabSpecialistRetryState(character);
                return;
            }

            UseComponent use =
                specialist.GetComponent<UseComponent>();

            if (use == null ||
                use.IsBusy())
            {
                return;
            }

            int attempts;
            if (!LabSpecialistFridgeContentionAttempts.TryGetValue(
                    character,
                    out attempts))
            {
                attempts = 0;
            }

            attempts++;
            LabSpecialistFridgeContentionAttempts[character] =
                attempts;

            if (DayTime.Instance != null)
            {
                LabSpecialistNextFridgeRetryMinutes[character] =
                    GetCurrentGameMinute() +
                    FixConfig
                        .LabSpecialistSampleDestinationFridgeRetryMinutes;
            }

            if (!FixConfig
                    .LabSpecialistSampleDestinationFridgeShowPopup ||
                attempts <
                    FixConfig
                        .LabSpecialistSampleDestinationFridgeContentionAttempts)
            {
                return;
            }

            EmployeeComponent employee =
                specialist.GetComponent<EmployeeComponent>();
            Room statLab =
                employee == null ||
                employee.m_state == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();

            TileObject contextFridge;
            if (MedicalFridgeContentionNotificationRuntime
                    .TryGetContentionContext(
                        statLab,
                        out contextFridge))
            {
                MedicalFridgeContentionNotificationRuntime
                    .TryShow(contextFridge);
            }
        }

        private static void ClearLabSpecialistRetryState(
            Entity character)
        {
            if (character == null)
            {
                return;
            }

            LabSpecialistNextFridgeRetryMinutes.Remove(character);
            LabSpecialistFridgeContentionAttempts.Remove(character);
        }

        private static int GetCurrentGameMinute()
        {
            if (DayTime.Instance == null)
            {
                return 0;
            }

            return (int)(
                DayTime.Instance.GetDay() * 1440f +
                DayTime.Instance.GetDayTimeHours() * 60f);
        }

        internal static void HandleLabSpecialistStateChanged(
            BehaviorLabSpecialist specialist,
            LabSpecialistState state)
        {
            if (specialist == null ||
                specialist.m_entity == null ||
                state ==
                    LabSpecialistState.GoingToStoreSampleFromPatient)
            {
                return;
            }

            ClearLabSpecialistRetryState(
                specialist.m_entity);

            if (HasWaitingSource(
                    specialist.m_entity,
                    LabSpecialistSampleDestinationSourceId))
            {
                TrySetWaitingState(
                    specialist.m_entity,
                    LabSpecialistSampleDestinationSourceId,
                    false,
                    null);
            }
        }

        internal static void RefreshAll()
        {
            if (!RuntimeActive ||
                WaitingSources.Count == 0)
            {
                return;
            }

            RefreshBuffer.Clear();

            foreach (KeyValuePair<Entity, HashSet<string>> pair
                     in WaitingSources)
            {
                RefreshBuffer.Add(
                    pair.Key);
            }

            for (int i = 0;
                 i < RefreshBuffer.Count;
                 i++)
            {
                Entity character =
                    RefreshBuffer[i];

                if (character == null ||
                    character.DEBUG_destroyed)
                {
                    if (character != null)
                    {
                        ClearEntity(
                            character);
                    }

                    continue;
                }

                if (ShouldPreservePose(character))
                {
                    ReleaseWaitPosition(
                        character,
                        true);
                }
                else if (HasRepositionRequest(character))
                {
                    RefreshRepositionState(
                        character);
                }
                else
                {
                    ReleaseWaitPosition(
                        character,
                        true);
                }

                ReservedStaffWaitingVisuals
                    .RefreshExternalWaitingState(
                        character);
            }

            RefreshBuffer.Clear();
        }

        internal static void ClearEntity(
            Entity character)
        {
            if (character == null)
            {
                return;
            }

            bool hadWaiting =
                WaitingSources.Remove(character);
            RepositionRequests.Remove(character);
            PresentationModes.Remove(character);
            ClearLabSpecialistRetryState(character);
            ReleaseWaitPosition(
                character,
                true);

            if (!hadWaiting)
            {
                return;
            }

            ReservedStaffWaitingVisuals
                .ClearDestroyedCharacterState(
                    character);
        }

        internal static void ClearAll()
        {
            if (WaitPositions.Count > 0)
            {
                ClearBuffer.Clear();

                foreach (KeyValuePair<Entity, ExternalWaitingPositionState> pair
                         in WaitPositions)
                {
                    ClearBuffer.Add(
                        pair.Key);
                }

                for (int i = 0;
                     i < ClearBuffer.Count;
                     i++)
                {
                    ReleaseWaitPosition(
                        ClearBuffer[i],
                        true);
                }

                ClearBuffer.Clear();
            }

            WaitingSources.Clear();
            RepositionRequests.Clear();
            PresentationModes.Clear();
            WaitPositions.Clear();
            LabSpecialistNextFridgeRetryMinutes.Clear();
            LabSpecialistFridgeContentionAttempts.Clear();
            MedicalFridgeContentionNotificationRuntime.ClearAll();
            RefreshBuffer.Clear();
        }

        private static bool SetRepositionRequest(
            Entity character,
            string sourceId,
            TileObject[] interactionObjects)
        {
            Dictionary<string, TileObject[]> requests;
            if (!RepositionRequests.TryGetValue(
                    character,
                    out requests))
            {
                requests =
                    new Dictionary<string, TileObject[]>(
                        StringComparer.Ordinal);
                RepositionRequests.Add(
                    character,
                    requests);
            }

            TileObject[] normalized =
                NormalizeInteractionObjects(
                    interactionObjects);

            TileObject[] existing;
            if (requests.TryGetValue(
                    sourceId,
                    out existing) &&
                AreSameInteractionObjects(
                    existing,
                    normalized))
            {
                return false;
            }

            requests[sourceId] =
                normalized;

            return true;
        }

        private static bool RemoveRepositionRequest(
            Entity character,
            string sourceId)
        {
            Dictionary<string, TileObject[]> requests;
            if (!RepositionRequests.TryGetValue(
                    character,
                    out requests) ||
                requests == null ||
                !requests.Remove(sourceId))
            {
                return false;
            }

            if (requests.Count == 0)
            {
                RepositionRequests.Remove(
                    character);
            }

            return true;
        }

        private static bool HasRepositionRequest(
            Entity character)
        {
            Dictionary<string, TileObject[]> requests;
            return character != null &&
                RepositionRequests.TryGetValue(
                    character,
                    out requests) &&
                requests != null &&
                requests.Count > 0;
        }

        private static TileObject[] NormalizeInteractionObjects(
            TileObject[] interactionObjects)
        {
            if (interactionObjects == null ||
                interactionObjects.Length == 0)
            {
                return null;
            }

            List<TileObject> normalized =
                new List<TileObject>();

            for (int i = 0;
                 i < interactionObjects.Length;
                 i++)
            {
                TileObject item =
                    interactionObjects[i];

                if (item == null ||
                    item.DEBUG_destroyed ||
                    item.m_state == null ||
                    normalized.Contains(item))
                {
                    continue;
                }

                normalized.Add(item);
            }

            return normalized.Count == 0
                ? null
                : normalized.ToArray();
        }

        private static bool AreSameInteractionObjects(
            TileObject[] first,
            TileObject[] second)
        {
            int firstLength =
                first == null
                    ? 0
                    : first.Length;
            int secondLength =
                second == null
                    ? 0
                    : second.Length;

            if (firstLength != secondLength)
            {
                return false;
            }

            for (int i = 0;
                 i < firstLength;
                 i++)
            {
                if (first[i] != second[i])
                {
                    return false;
                }
            }

            return true;
        }

        private static void RefreshRepositionState(
            Entity character)
        {
            WalkComponent walk =
                character == null
                    ? null
                    : character.GetComponent<WalkComponent>();
            UseComponent use =
                character == null
                    ? null
                    : character.GetComponent<UseComponent>();

            if (walk == null ||
                walk.m_state == null ||
                MapScriptInterface.Instance == null)
            {
                ReleaseWaitPosition(
                    character,
                    true);
                return;
            }

            if (walk.IsSitting() ||
                (use != null &&
                 use.IsUsing()))
            {
                ReleaseWaitPosition(
                    character,
                    true);
                return;
            }

            ExternalWaitingPositionState position;
            if (WaitPositions.TryGetValue(
                    character,
                    out position) &&
                position != null)
            {
                if (!IsWaitPositionReservedBy(
                        character,
                        position))
                {
                    ReleaseWaitPosition(
                        character,
                        true);
                    position = null;
                }
            }

            if (position != null)
            {
                if (walk.GetFloorIndex() !=
                        position.FloorIndex)
                {
                    ReleaseWaitPosition(
                        character,
                        true);
                    return;
                }

                if (walk.GetCurrentTile() ==
                        position.Tile)
                {
                    PrepareNeutralWaitPose(
                        character);
                    return;
                }

                if (walk.IsBusy())
                {
                    if (walk.GetDestinationTile() ==
                            position.Tile &&
                        walk.m_state.m_destinationFloor ==
                            position.FloorIndex)
                    {
                        return;
                    }

                    ReleaseWaitPosition(
                        character,
                        false);
                    return;
                }

                if (Time.realtimeSinceStartup <
                        position.NextMoveRetryAt)
                {
                    return;
                }

                position.NextMoveRetryAt =
                    Time.realtimeSinceStartup +
                    RepositionRetrySeconds;

                walk.SetDestination(
                    position.Tile,
                    position.FloorIndex);
                return;
            }

            if (walk.IsBusy())
            {
                return;
            }

            Room room =
                MapScriptInterface.Instance.GetRoomAt(
                    walk);
            Vector2i waitingTile;

            if (room == null ||
                !TryFindWaitTile(
                    character,
                    room,
                    out waitingTile))
            {
                PrepareNeutralWaitPose(
                    character);
                return;
            }

            int floorIndex =
                room.GetFloorIndex();
            Floor floor =
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count
                    ? null
                    : Hospital.Instance.m_floors[floorIndex];

            Tile tile =
                floor == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tiles == null
                    ? null
                    : floor.m_mapPersistentData.m_tiles[
                        waitingTile.m_x,
                        waitingTile.m_y];

            if (tile == null ||
                tile.m_user != null)
            {
                return;
            }

            MapScriptInterface.Instance.ReserveTile(
                waitingTile,
                character,
                floorIndex);

            ExternalWaitingPositionState created =
                new ExternalWaitingPositionState
                {
                    Tile = waitingTile,
                    FloorIndex = floorIndex,
                    NextMoveRetryAt =
                        Time.realtimeSinceStartup +
                        RepositionRetrySeconds
                };

            WaitPositions[character] =
                created;

            if (!IsWaitPositionReservedBy(
                    character,
                    created))
            {
                WaitPositions.Remove(
                    character);
                return;
            }

            walk.SetDestination(
                waitingTile,
                floorIndex);

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.ResultRateLimited(
                    "ExternalWaitingReposition",
                    "external waiting staging",
                    "wait-position-reserved",
                    "character={" +
                    HfxDiagnostics.EntityContext(character) +
                    "} target=" +
                    waitingTile +
                    " floor=" +
                    floorIndex,
                    "ExternalWaitingReposition|" +
                    HfxDiagnostics.EntityLabel(character),
                    3000);
            }
        }

        private static void PrepareNeutralWaitPose(
            Entity character)
        {
            if (character == null)
            {
                return;
            }

            WalkComponent walk =
                character.GetComponent<WalkComponent>();
            UseComponent use =
                character.GetComponent<UseComponent>();
            AnimModelComponent animation =
                character.GetComponent<AnimModelComponent>();

            if (walk == null ||
                walk.m_state == null ||
                walk.IsBusy() ||
                walk.IsSitting() ||
                (use != null &&
                 use.IsUsing()) ||
                animation == null ||
                animation.m_state == null ||
                (animation.m_state.m_animationQueue != null &&
                 animation.m_state.m_animationQueue.Count > 0) ||
                ReservedStaffWaitingVisuals
                    .IsHfxWaitAnimation(animation) ||
                !animation.IsLooping())
            {
                return;
            }

            string currentAnimation =
                animation.m_state.m_currentAnimationID;

            if (string.IsNullOrEmpty(currentAnimation) ||
                string.Equals(
                    currentAnimation,
                    "stand_idle",
                    StringComparison.Ordinal))
            {
                return;
            }

            animation.PlayAnimation(
                "stand_idle");
        }

        private static bool TryFindWaitTile(
            Entity character,
            Room room,
            out Vector2i result)
        {
            result =
                Vector2i.ZERO_VECTOR;

            if (character == null ||
                room == null ||
                room.m_roomPersistentData == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                GridMap.GetInstance() == null)
            {
                return false;
            }

            int floorIndex =
                room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[floorIndex];
            WalkComponent walk =
                character.GetComponent<WalkComponent>();

            if (floor == null ||
                walk == null ||
                walk.m_state == null ||
                walk.GetFloorIndex() != floorIndex ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tiles == null ||
                floor.m_tileObjects == null ||
                floor.m_accessibility == null ||
                floor.m_characters == null)
            {
                return false;
            }

            Vector2i current =
                walk.GetCurrentTile();

            HashSet<int> blockedTiles =
                new HashSet<int>();
            List<Vector2i> interactionTiles =
                new List<Vector2i>();

            List<TileObject> objects =
                room.GetAllObjects(
                    floor);

            for (int i = 0;
                 i < objects.Count;
                 i++)
            {
                TileObject tileObject =
                    objects[i];

                if (tileObject == null ||
                    tileObject.m_state == null)
                {
                    continue;
                }

                bool sharedInteraction =
                    IsRequestedInteractionObject(
                        character,
                        tileObject) ||
                    tileObject.HasTag("doc_equipment") ||
                    tileObject.HasTag("clean_hands") ||
                    tileObject.HasTag("biohazard_trash") ||
                    tileObject.HasTag("medical_fridge");

                if (tileObject.HasTag("sit_exam") ||
                    sharedInteraction)
                {
                    Vector2i useTile =
                        tileObject.GetDefaultUseTile();

                    if (room.IsPositionInRoom(
                            useTile))
                    {
                        blockedTiles.Add(
                            WaitTileKey(
                                floor,
                                useTile));

                        if (sharedInteraction)
                        {
                            interactionTiles.Add(
                                useTile);
                        }
                    }
                }
            }

            for (int i = 0;
                 i < floor.m_characters.Count;
                 i++)
            {
                Entity other =
                    floor.m_characters[i];
                WalkComponent otherWalk =
                    other == null
                        ? null
                        : other.GetComponent<WalkComponent>();

                if (otherWalk == null ||
                    otherWalk.m_state == null ||
                    otherWalk.GetFloorIndex() !=
                        floorIndex)
                {
                    continue;
                }

                Vector2i otherTile =
                    otherWalk.GetCurrentTile();

                if (room.IsPositionInRoom(
                        otherTile))
                {
                    blockedTiles.Add(
                        WaitTileKey(
                            floor,
                            otherTile));
                }

                if (otherWalk.IsBusy() &&
                    otherWalk.m_state.m_destinationFloor ==
                        floorIndex)
                {
                    Vector2i destination =
                        otherWalk.GetDestinationTile();

                    if (room.IsPositionInRoom(
                            destination))
                    {
                        blockedTiles.Add(
                            WaitTileKey(
                                floor,
                                destination));
                    }
                }
            }

            int bestGridDistance =
                int.MaxValue;
            float bestRouteDistance =
                float.MaxValue;
            bool found =
                false;

            Vector2i bottom =
                room.m_roomPersistentData.m_positionBottom;
            Vector2i top =
                room.m_roomPersistentData.m_positionTop;

            for (int x = bottom.m_x;
                 x <= top.m_x;
                 x++)
            {
                for (int y = bottom.m_y;
                     y <= top.m_y;
                     y++)
                {
                    Vector2i tile =
                        new Vector2i(
                            x,
                            y);

                    if (tile == Vector2i.ZERO_VECTOR ||
                        tile == current ||
                        !room.IsPositionInRoom(tile) ||
                        blockedTiles.Contains(
                            WaitTileKey(
                                floor,
                                tile)) ||
                        floor.m_mapPersistentData
                            .m_tiles[x, y] == null ||
                        floor.m_mapPersistentData
                            .m_tiles[x, y].m_user != null ||
                        floor.m_tileObjects[x, y] == null ||
                        floor.m_tileObjects[x, y]
                            .HasAnyObject() ||
                        floor.m_accessibility[x, y] == 2 ||
                        !IsAwayFromInteractionTiles(
                            tile,
                            interactionTiles))
                    {
                        continue;
                    }

                    int dx =
                        tile.m_x -
                        current.m_x;
                    int dy =
                        tile.m_y -
                        current.m_y;
                    int gridDistance =
                        dx * dx +
                        dy * dy;

                    if (gridDistance >
                        bestGridDistance)
                    {
                        continue;
                    }

                    float routeDistance =
                        GridMap.GetInstance().GetDistance(
                            walk.GetFloorIndex(),
                            current,
                            floorIndex,
                            tile,
                            AccessRights.STAFF);

                    if (routeDistance < 0f)
                    {
                        continue;
                    }

                    if (!found ||
                        gridDistance <
                            bestGridDistance ||
                        (gridDistance ==
                            bestGridDistance &&
                         routeDistance <
                            bestRouteDistance))
                    {
                        found = true;
                        result = tile;
                        bestGridDistance =
                            gridDistance;
                        bestRouteDistance =
                            routeDistance;
                    }
                }
            }

            return found;
        }

        private static bool IsRequestedInteractionObject(
            Entity character,
            TileObject tileObject)
        {
            Dictionary<string, TileObject[]> requests;
            if (character == null ||
                tileObject == null ||
                !RepositionRequests.TryGetValue(
                    character,
                    out requests) ||
                requests == null)
            {
                return false;
            }

            foreach (KeyValuePair<string, TileObject[]> pair
                     in requests)
            {
                TileObject[] objects =
                    pair.Value;

                if (objects == null)
                {
                    continue;
                }

                for (int i = 0;
                     i < objects.Length;
                     i++)
                {
                    if (objects[i] == tileObject)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool IsAwayFromInteractionTiles(
            Vector2i tile,
            List<Vector2i> interactionTiles)
        {
            if (interactionTiles == null)
            {
                return true;
            }

            for (int i = 0;
                 i < interactionTiles.Count;
                 i++)
            {
                int dx =
                    tile.m_x -
                    interactionTiles[i].m_x;
                int dy =
                    tile.m_y -
                    interactionTiles[i].m_y;

                if (dx * dx +
                    dy * dy <
                    4)
                {
                    return false;
                }
            }

            return true;
        }

        private static int WaitTileKey(
            Floor floor,
            Vector2i tile)
        {
            return tile.m_y *
                floor.Size.m_x +
                tile.m_x;
        }

        private static bool IsWaitPositionReservedBy(
            Entity character,
            ExternalWaitingPositionState position)
        {
            if (character == null ||
                position == null ||
                Hospital.Instance == null ||
                Hospital.Instance.m_floors == null ||
                position.FloorIndex < 0 ||
                position.FloorIndex >=
                    Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            Floor floor =
                Hospital.Instance.m_floors[
                    position.FloorIndex];

            if (floor == null ||
                floor.m_mapPersistentData == null ||
                floor.m_mapPersistentData.m_tiles == null ||
                position.Tile.m_x < 0 ||
                position.Tile.m_y < 0 ||
                position.Tile.m_x >=
                    floor.Size.m_x ||
                position.Tile.m_y >=
                    floor.Size.m_y)
            {
                return false;
            }

            Tile tile =
                floor.m_mapPersistentData.m_tiles[
                    position.Tile.m_x,
                    position.Tile.m_y];

            Entity reservedBy =
                tile == null ||
                tile.m_user == null
                    ? null
                    : tile.m_user.GetEntity();

            return reservedBy ==
                character;
        }

        private static void ReleaseWaitPosition(
            Entity character,
            bool stopOwnedWalk)
        {
            if (character == null)
            {
                return;
            }

            ExternalWaitingPositionState position;
            if (!WaitPositions.TryGetValue(
                    character,
                    out position) ||
                position == null)
            {
                return;
            }

            WalkComponent walk =
                character.GetComponent<WalkComponent>();

            if (stopOwnedWalk &&
                walk != null &&
                walk.m_state != null &&
                walk.IsBusy() &&
                walk.GetDestinationTile() ==
                    position.Tile &&
                walk.m_state.m_destinationFloor ==
                    position.FloorIndex)
            {
                walk.Stop();
            }

            if (IsWaitPositionReservedBy(
                    character,
                    position) &&
                MapScriptInterface.Instance != null)
            {
                MapScriptInterface.Instance.FreeTile(
                    position.Tile,
                    position.FloorIndex);
            }

            WaitPositions.Remove(
                character);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorLabSpecialist),
        "UpdateStateGoingToStoreSampleFromPatient")]
    internal static class LabSpecialistSampleDestinationRetryPatch
    {
        [HarmonyPrefix]
        private static bool Prefix(
            BehaviorLabSpecialist __instance,
            ref bool __state)
        {
            return ExternalWaitingStateRuntime
                .ShouldRunLabSpecialistDestinationSearch(
                    __instance,
                    out __state);
        }

        [HarmonyPostfix]
        private static void Postfix(
            BehaviorLabSpecialist __instance,
            bool __state)
        {
            ExternalWaitingStateRuntime
                .HandleLabSpecialistDestinationSearchCompleted(
                    __instance,
                    __state);
        }
    }

    [HarmonyPatch(
        typeof(Hospital),
        nameof(Hospital.Update),
        new Type[]
        {
            typeof(int),
            typeof(float)
        })]
    internal static class ExternalWaitingHospitalUpdatePatch
    {
        [HarmonyPostfix]
        private static void Postfix()
        {
            ReservedStaffWaitingVisuals
                .RefreshPendingAnimationReleases();

            ExternalWaitingStateRuntime
                .RefreshAll();

            ExternalClothingChangeStateRuntime
                .RefreshAll();
        }
    }

    [HarmonyPatch(
        typeof(Entity),
        nameof(Entity.Destroy))]
    internal static class ExternalWaitingEntityDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix(
            Entity __instance)
        {
            ExternalWaitingStateRuntime
                .ClearEntity(__instance);

            ExternalClothingChangeStateRuntime
                .ClearEntity(__instance);
        }
    }

    [HarmonyPatch(
        typeof(Hospital),
        nameof(Hospital.Destroy),
        new Type[] { })]
    internal static class ExternalWaitingHospitalDestroyPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            ReservedStaffWaitingVisuals
                .ClearTransientState();

            ExternalClothingChangeStateRuntime
                .ClearAll();

            ClothingChangeEffectRenderer
                .ClearAll(destroyAssets: false);
        }
    }

    [HarmonyPatch(
        typeof(Hospital),
        nameof(Hospital.Reset),
        new Type[] { })]
    internal static class ExternalWaitingHospitalResetPatch
    {
        [HarmonyPrefix]
        private static void Prefix()
        {
            ReservedStaffWaitingVisuals
                .ClearTransientState();

            ExternalClothingChangeStateRuntime
                .ClearAll();

            ClothingChangeEffectRenderer
                .ClearAll(destroyAssets: false);
        }
    }
}
