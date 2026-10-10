using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
    internal sealed class BathroomCollapseLock
    {
        internal Room Room;
        internal string Scope;
        internal int Component = -1;
        internal bool AcquiredLogged;
        internal readonly List<TileObject> Fixtures =
            new List<TileObject>();
        internal readonly HashSet<TileObject> CollisionLoggedFixtures =
            new HashSet<TileObject>();
    }

    internal static class SingleToiletBathroomLockManager
    {
        private static readonly HashSet<ProcedureScriptNeedBladder>
            ExclusiveScripts =
                new HashSet<ProcedureScriptNeedBladder>();

        private static readonly Dictionary<Entity, BathroomCollapseLock>
            CollapseLocks =
                new Dictionary<Entity, BathroomCollapseLock>();

        internal static void Reset()
        {
            List<ProcedureScriptNeedBladder> scripts =
                new List<ProcedureScriptNeedBladder>(
                    ExclusiveScripts);
            for (int i = 0; i < scripts.Count; i++)
            {
                ProcedureScriptNeedBladder script =
                    scripts[i];
                Room room =
                    GetObjectRoom(
                        script == null
                            ? null
                            : script.GetEquipment(0));
                List<TileObject> fixtures =
                    GetBathroomFixtures(room);
                for (int j = 0; j < fixtures.Count; j++)
                {
                    TileObject fixture = fixtures[j];
                    if (fixture != null &&
                        !fixture.HasTag("wc") &&
                        ReferenceEquals(
                            fixture.Owner,
                            script))
                    {
                        fixture.Owner = null;
                    }
                }
            }

            List<Entity> patients =
                new List<Entity>(CollapseLocks.Keys);
            for (int i = 0; i < patients.Count; i++)
            {
                ReleaseCollapseLock(
                    patients[i],
                    "runtime-reset",
                    false);
            }

            ExclusiveScripts.Clear();
            CollapseLocks.Clear();
        }

        internal static bool IsSingleToiletRoom(
            TileObject toilet)
        {
            Room room = GetObjectRoom(toilet);
            TileObject singleToilet;
            return TryGetSingleToilet(
                room,
                out singleToilet) &&
                ReferenceEquals(
                    singleToilet,
                    toilet);
        }

        internal static void RefreshProcedureLock(
            ProcedureScriptNeedBladder script)
        {
            if (!TrafficControlConfig.ReleaseToiletOwnerAfterUse ||
                script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null)
            {
                return;
            }

            TileObject toilet = script.GetEquipment(0);
            Room room = GetObjectRoom(toilet);
            TileObject singleToilet;
            if (!TryGetSingleToilet(
                    room,
                    out singleToilet) ||
                !ReferenceEquals(
                    singleToilet,
                    toilet))
            {
                ReleaseScriptOwners(
                    script,
                    true);
                return;
            }

            if (script.m_stateData.m_state ==
                ProcedureScriptNeedBladder.STATE_IDLE)
            {
                ReleaseScriptOwners(
                    script,
                    true);
                return;
            }

            List<TileObject> fixtures =
                GetBathroomFixtures(room);
            bool newlyLocked =
                ExclusiveScripts.Add(script);

            for (int i = 0; i < fixtures.Count; i++)
            {
                TileObject fixture = fixtures[i];
                if (fixture == null)
                {
                    continue;
                }

                if (fixture.Owner == null ||
                    ReferenceEquals(
                        fixture.Owner,
                        script))
                {
                    fixture.Owner = script;
                    continue;
                }

                if (newlyLocked && TrafficControlConfig.BathroomFlowDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[BathroomDebug] single-wc-lock-collision" +
                        " | character=" +
                            BathroomFlowDiagnostics.CharacterName(
                                script.m_stateData.m_procedureScene
                                    .MainCharacter) +
                        " | fixture=" +
                            BathroomFlowDiagnostics.ObjectName(
                                fixture) +
                        " | existingOwner=" +
                            fixture.Owner.GetType().Name +
                        ".");
                }
            }

            if (newlyLocked &&
                TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] single-wc-exclusive-lock" +
                    " | character=" +
                        BathroomFlowDiagnostics.CharacterName(
                            script.m_stateData.m_procedureScene
                                .MainCharacter) +
                    " | wc=" +
                        BathroomFlowDiagnostics.ObjectName(
                            toilet) +
                    " | fixtures=" +
                        fixtures.Count +
                    " | state=" +
                        script.m_stateData.m_state +
                    ".");
            }
        }

        internal static void RefreshCollapsedPatient(
            BehaviorPatient behavior)
        {
            if (!TrafficControlConfig.ReleaseToiletOwnerAfterUse ||
                behavior == null ||
                behavior.m_entity == null ||
                behavior.m_state == null)
            {
                return;
            }

            Entity patient = behavior.m_entity;
            WalkComponent walk =
                patient.GetComponent<WalkComponent>();
            Room room =
                walk == null ||
                MapScriptInterface.Instance == null
                    ? null
                    : MapScriptInterface.Instance.GetRoomAt(
                        walk.GetCurrentTile(),
                        walk.GetFloorIndex());

            bool collapseLike =
                behavior.m_state.m_patientState ==
                    PatientState.GoingToCollapse ||
                behavior.m_state.m_patientState ==
                    PatientState.Collapsing ||
                (walk != null &&
                 walk.m_state != null &&
                 walk.m_state.m_lying);

            List<TileObject> fixtures;
            string scope;
            int component;
            if (collapseLike &&
                walk != null &&
                TryBuildCollapseLockScope(
                    room,
                    walk.GetCurrentTile(),
                    out fixtures,
                    out scope,
                    out component))
            {
                EnsureCollapseLock(
                    patient,
                    room,
                    fixtures,
                    scope,
                    component);
                return;
            }

            if (CollapseLocks.ContainsKey(patient))
            {
                ReleaseCollapseLock(
                    patient,
                    "patient-left-or-recovered",
                    true);
            }
        }

        private static bool TryBuildCollapseLockScope(
            Room room,
            Vector2i patientTile,
            out List<TileObject> fixtures,
            out string scope,
            out int component)
        {
            fixtures = new List<TileObject>();
            scope = null;
            component = -1;

            TileObject singleToilet;
            if (TryGetSingleToilet(
                    room,
                    out singleToilet))
            {
                fixtures.AddRange(
                    GetBathroomFixtures(room));
                scope = "single-room";
                return HasToiletFixture(fixtures);
            }

            HospitalTrafficControl.BathroomCleaningTopology topology =
                HospitalTrafficControl.BathroomCleaningTopology
                    .CreateGeometry(room);
            if (topology == null ||
                !topology.TryGetToiletCompartment(
                    patientTile,
                    out component))
            {
                return false;
            }

            topology.GetBathroomFixturesInCompartment(
                component,
                fixtures);
            scope = "stall";
            return HasToiletFixture(fixtures);
        }

        private static bool HasToiletFixture(
            List<TileObject> fixtures)
        {
            if (fixtures == null)
            {
                return false;
            }

            for (int i = 0; i < fixtures.Count; i++)
            {
                if (fixtures[i] != null &&
                    fixtures[i].HasTag("wc"))
                {
                    return true;
                }
            }

            return false;
        }

        private static void EnsureCollapseLock(
            Entity patient,
            Room room,
            List<TileObject> fixtures,
            string scope,
            int component)
        {
            BathroomCollapseLock existing;
            if (CollapseLocks.TryGetValue(
                    patient,
                    out existing) &&
                ReferenceEquals(
                    existing.Room,
                    room) &&
                existing.Component == component &&
                existing.Scope == scope)
            {
                ApplyCollapseOwners(
                    patient,
                    existing);
                return;
            }

            if (existing != null)
            {
                ReleaseCollapseLock(
                    patient,
                    "room-or-compartment-changed",
                    true);
            }

            BathroomCollapseLock collapseLock =
                new BathroomCollapseLock
                {
                    Room = room,
                    Scope = scope,
                    Component = component
                };
            collapseLock.Fixtures.AddRange(fixtures);
            CollapseLocks[patient] =
                collapseLock;

            ApplyCollapseOwners(
                patient,
                collapseLock);
        }

        private static void ApplyCollapseOwners(
            Entity patient,
            BathroomCollapseLock collapseLock)
        {
            if (patient == null ||
                collapseLock == null)
            {
                return;
            }

            PreemptApproachingBladderReservations(
                patient,
                collapseLock);

            bool allOwned = true;
            for (int i = 0;
                 i < collapseLock.Fixtures.Count;
                 i++)
            {
                TileObject fixture =
                    collapseLock.Fixtures[i];
                if (fixture == null)
                {
                    continue;
                }

                if (ReferenceEquals(
                        fixture.Owner,
                        patient))
                {
                    continue;
                }

                if (fixture.Owner == null ||
                    IsOwnedByCharacterBladderScript(
                        fixture.Owner,
                        patient))
                {
                    fixture.Owner = patient;
                    continue;
                }

                allOwned = false;
                if (TrafficControlConfig.BathroomFlowDebug &&
                    collapseLock.CollisionLoggedFixtures.Add(
                        fixture))
                {
                    Plugin.Log?.LogInfo(
                        "[BathroomDebug] bathroom-collapse-lock-pending" +
                        " | character=" +
                            BathroomFlowDiagnostics.CharacterName(
                                patient) +
                        " | scope=" +
                            collapseLock.Scope +
                        " | component=" +
                            collapseLock.Component +
                        " | fixture=" +
                            BathroomFlowDiagnostics.ObjectName(
                                fixture) +
                        " | existingOwner=" +
                            fixture.Owner.GetType().Name +
                        ".");
                }
            }

            if (allOwned &&
                !collapseLock.AcquiredLogged)
            {
                collapseLock.AcquiredLogged = true;
                if (TrafficControlConfig.BathroomFlowDebug)
                {
                    Plugin.Log?.LogInfo(
                        "[BathroomDebug] bathroom-collapse-lock-acquired" +
                        " | character=" +
                            BathroomFlowDiagnostics.CharacterName(
                                patient) +
                        " | floor=" +
                            (collapseLock.Room == null
                                ? -1
                                : collapseLock.Room.GetFloorIndex()) +
                        " | scope=" +
                            collapseLock.Scope +
                        " | component=" +
                            collapseLock.Component +
                        " | fixtures=" +
                            collapseLock.Fixtures.Count +
                        ".");
                }
            }
        }

        private static void PreemptApproachingBladderReservations(
            Entity patient,
            BathroomCollapseLock collapseLock)
        {
            HashSet<ProcedureScriptNeedBladder> checkedScripts =
                new HashSet<ProcedureScriptNeedBladder>();

            for (int i = 0;
                 i < collapseLock.Fixtures.Count;
                 i++)
            {
                TileObject fixture =
                    collapseLock.Fixtures[i];
                if (fixture == null)
                {
                    continue;
                }

                ProcedureScriptNeedBladder ownerScript =
                    fixture.Owner as ProcedureScriptNeedBladder;
                if (ownerScript != null &&
                    checkedScripts.Add(ownerScript))
                {
                    TryPreemptApproachingBladderScript(
                        patient,
                        collapseLock,
                        ownerScript);
                }

                Entity user = fixture.User;
                if (user == null ||
                    ReferenceEquals(user, patient))
                {
                    continue;
                }

                ProcedureScriptNeedBladder userScript =
                    GetCurrentBladderScript(user);
                if (userScript != null &&
                    checkedScripts.Add(userScript))
                {
                    TryPreemptApproachingBladderScript(
                        patient,
                        collapseLock,
                        userScript);
                }
            }
        }

        private static bool TryPreemptApproachingBladderScript(
            Entity collapsedPatient,
            BathroomCollapseLock collapseLock,
            ProcedureScriptNeedBladder script)
        {
            if (collapsedPatient == null ||
                collapseLock == null ||
                script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_state !=
                    ProcedureScriptNeedBladder.STATE_GOING_TO_OBJECT ||
                script.m_stateData.m_procedureScene == null)
            {
                return false;
            }

            Entity character =
                script.m_stateData.m_procedureScene.MainCharacter;
            TileObject targetToilet =
                script.GetEquipment(0);
            if (character == null ||
                ReferenceEquals(
                    character,
                    collapsedPatient) ||
                targetToilet == null ||
                !collapseLock.Fixtures.Contains(
                    targetToilet))
            {
                return false;
            }

            UseComponent use =
                character.GetComponent<UseComponent>();
            if (use != null &&
                use.m_state != null &&
                use.m_state.m_reservedObject != null)
            {
                use.Interrupt();
            }

            script.SwitchState(
                ProcedureScriptNeedBladder.STATE_IDLE);
            ReleaseScriptOwners(
                script,
                false);
            StopPreemptedBladderTravel(
                character);

            if (TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] bathroom-collapse-preempt-pending-user" +
                    " | collapsed=" +
                        BathroomFlowDiagnostics.CharacterName(
                            collapsedPatient) +
                    " | displaced=" +
                        BathroomFlowDiagnostics.CharacterName(
                            character) +
                    " | wc=" +
                        BathroomFlowDiagnostics.ObjectName(
                            targetToilet) +
                    " | scope=" +
                        collapseLock.Scope +
                    " | component=" +
                        collapseLock.Component +
                    ".");
            }

            return true;
        }

        private static ProcedureScriptNeedBladder GetCurrentBladderScript(
            Entity character)
        {
            ProcedureComponent procedure =
                character == null
                    ? null
                    : character.GetComponent<ProcedureComponent>();
            return procedure == null ||
                   procedure.m_state == null ||
                   procedure.m_state.m_currentProcedureScript == null
                ? null
                : procedure.m_state.m_currentProcedureScript.GetEntity()
                    as ProcedureScriptNeedBladder;
        }

        private static void StopPreemptedBladderTravel(
            Entity character)
        {
            WalkComponent walk =
                character == null
                    ? null
                    : character.GetComponent<WalkComponent>();
            if (walk == null ||
                walk.m_state == null)
            {
                return;
            }

            walk.Stop();
            walk.m_route = null;
            walk.m_blockedCount = 0;
            walk.m_state.m_nextPosition =
                walk.m_state.m_currentPosition;
            walk.m_state.m_walkMidpoint1 = null;
            walk.m_state.m_walkMidpoint2 = null;

            if (!walk.m_state.m_lying)
            {
                AnimModelComponent anim =
                    character.GetComponent<AnimModelComponent>();
                if (anim != null)
                {
                    anim.PlayAnimation(
                        walk.IsSitting()
                            ? "sit_relax_pc_out"
                            : "stand_idle",
                        looping: false);
                }
            }
        }

        private static bool IsOwnedByCharacterBladderScript(
            Entity owner,
            Entity character)
        {
            ProcedureScriptNeedBladder script =
                owner as ProcedureScriptNeedBladder;
            return script != null &&
                   script.m_stateData != null &&
                   script.m_stateData.m_procedureScene != null &&
                   ReferenceEquals(
                       script.m_stateData.m_procedureScene
                           .MainCharacter,
                       character);
        }

        private static void ReleaseScriptOwners(
            ProcedureScriptNeedBladder script,
            bool log)
        {
            if (script == null)
            {
                return;
            }

            TileObject toilet = script.GetEquipment(0);
            Room room = GetObjectRoom(toilet);
            if (room != null)
            {
                List<TileObject> fixtures =
                    GetBathroomFixtures(room);
                for (int i = 0; i < fixtures.Count; i++)
                {
                    if (ReferenceEquals(
                            fixtures[i].Owner,
                            script))
                    {
                        fixtures[i].Owner = null;
                    }
                }
            }

            bool removed =
                ExclusiveScripts.Remove(script);
            if (removed &&
                log &&
                TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] single-wc-exclusive-release" +
                    " | character=" +
                        BathroomFlowDiagnostics.CharacterName(
                            script.m_stateData == null ||
                            script.m_stateData.m_procedureScene == null
                                ? null
                                : script.m_stateData.m_procedureScene
                                    .MainCharacter) +
                    " | wc=" +
                        BathroomFlowDiagnostics.ObjectName(
                            toilet) +
                    ".");
            }
        }

        private static void ReleaseCollapseLock(
            Entity patient,
            string reason,
            bool log)
        {
            BathroomCollapseLock collapseLock;
            if (!CollapseLocks.TryGetValue(
                    patient,
                    out collapseLock))
            {
                return;
            }

            for (int i = 0;
                 i < collapseLock.Fixtures.Count;
                 i++)
            {
                TileObject fixture =
                    collapseLock.Fixtures[i];
                if (fixture != null &&
                    ReferenceEquals(
                        fixture.Owner,
                        patient))
                {
                    fixture.Owner = null;
                }
            }

            CollapseLocks.Remove(patient);

            if (log &&
                TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] bathroom-collapse-release" +
                    " | character=" +
                        BathroomFlowDiagnostics.CharacterName(
                            patient) +
                    " | scope=" +
                        collapseLock.Scope +
                    " | component=" +
                        collapseLock.Component +
                    " | reason=" + reason +
                    ".");
            }
        }

        private static Room GetObjectRoom(
            TileObject tileObject)
        {
            if (tileObject == null ||
                tileObject.m_state == null ||
                MapScriptInterface.Instance == null)
            {
                return null;
            }

            Room room =
                MapScriptInterface.Instance.GetRoomAt(
                    tileObject.m_state.m_position,
                    tileObject.GetFloorIndex());
            if (room != null)
            {
                return room;
            }

            Vector2f usePosition =
                tileObject.GetDefaultUsePosition();
            Vector2i useTile =
                new Vector2i(
                    (int)(usePosition.m_x + 0.5f),
                    (int)(usePosition.m_y + 0.5f));
            return MapScriptInterface.Instance.GetRoomAt(
                useTile,
                tileObject.GetFloorIndex());
        }

        private static bool TryGetSingleToilet(
            Room room,
            out TileObject toilet)
        {
            toilet = null;

            if (room == null ||
                room.m_roomPersistentData == null ||
                room.m_roomPersistentData.m_roomType.Entry == null ||
                !room.m_roomPersistentData.m_roomType.Entry
                    .HasTag("wc") ||
                Hospital.Instance == null)
            {
                return false;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return false;
            }

            List<TileObject> objects =
                room.GetAllObjects(
                    Hospital.Instance.m_floors[floorIndex]);
            int count = 0;
            for (int i = 0; i < objects.Count; i++)
            {
                TileObject candidate = objects[i];
                if (candidate == null ||
                    !candidate.HasTag("wc"))
                {
                    continue;
                }

                count++;
                toilet = candidate;
                if (count > 1)
                {
                    toilet = null;
                    return false;
                }
            }

            return count == 1 &&
                   toilet != null;
        }

        private static List<TileObject> GetBathroomFixtures(
            Room room)
        {
            List<TileObject> fixtures =
                new List<TileObject>();
            if (room == null ||
                Hospital.Instance == null)
            {
                return fixtures;
            }

            int floorIndex = room.GetFloorIndex();
            if (floorIndex < 0 ||
                floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return fixtures;
            }

            List<TileObject> objects =
                room.GetAllObjects(
                    Hospital.Instance.m_floors[floorIndex]);
            for (int i = 0; i < objects.Count; i++)
            {
                TileObject fixture = objects[i];
                if (fixture != null &&
                    (fixture.HasTag("wc") ||
                     fixture.HasTag("washing") ||
                     fixture.HasTag("dryer")))
                {
                    fixtures.Add(fixture);
                }
            }

            return fixtures;
        }
    }

    internal static class BathroomFlowDiagnostics
    {
        private static readonly Dictionary<int, string> LastWcLockSnapshotByFloor =
            new Dictionary<int, string>();

        internal static void Reset()
        {
            LastWcLockSnapshotByFloor.Clear();
        }

        internal static string CharacterName(Entity character)
        {
            if (character == null)
            {
                return "<none>";
            }

            string name = character.Name;
            name = name == null ? string.Empty : name.Trim();
            return string.IsNullOrEmpty(name) ? "<unknown>" : name;
        }

        internal static bool HasGermaphobePerk(Entity character)
        {
            if (character == null)
            {
                return false;
            }

            PerkComponent perkComponent = character.GetComponent<PerkComponent>();
            return perkComponent != null &&
                   perkComponent.m_perkSet != null &&
                   perkComponent.m_perkSet.HasPerk("PERK_GERMAPHOBE");
        }

        internal static string ObjectName(TileObject tileObject)
        {
            if (tileObject == null || tileObject.m_state == null)
            {
                return "<none>";
            }

            string databaseId = "<unknown>";
            if (tileObject.m_state.m_gameDBObject.Entry != null)
            {
                databaseId = tileObject.m_state.m_gameDBObject.Entry.DatabaseID.ToString();
            }

            return databaseId + "@" +
                   tileObject.m_state.m_position.m_x + "," +
                   tileObject.m_state.m_position.m_y +
                   ",floor=" + tileObject.GetFloorIndex();
        }

        internal static void LogInitialWcReservation(ProcedureScriptNeedBladder script)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null)
            {
                return;
            }

            Entity character = script.m_stateData.m_procedureScene.MainCharacter;
            TileObject toilet = script.GetEquipment(0);
            UseComponent useComponent = character == null ? null : character.GetComponent<UseComponent>();
            WalkComponent walkComponent = character == null ? null : character.GetComponent<WalkComponent>();
            TileObject reservedObject = BathroomFixtureHandoff.GetReservedObject(useComponent);
            TileObject currentObject = BathroomFixtureHandoff.GetCurrentObject(useComponent);

            bool indicatorEnabled =
                SettingsManager.Instance != null &&
                SettingsManager.Instance.m_viewSettings.m_showObjectReservation.m_value;
            bool reservedMatches = toilet != null && reservedObject == toilet;
            bool currentMatches = toilet != null && currentObject == toilet;
            bool userMatches = toilet != null && toilet.User == character;
            bool ownerMatches = toilet != null && toilet.Owner == script;
            bool sameFloor =
                toilet != null &&
                walkComponent != null &&
                toilet.GetFloorIndex() == walkComponent.GetFloorIndex();
            bool hasWcTag = toilet != null && toilet.HasTag("wc");

            Plugin.Log?.LogInfo(
                "[BathroomDebug] wc-reservation" +
                " | character=" + CharacterName(character) +
                " | wc=" + ObjectName(toilet) +
                " | state=" + script.m_stateData.m_state +
                " | reservedObjectMatches=" + reservedMatches +
                " | currentObjectMatches=" + currentMatches +
                " | userMatches=" + userMatches +
                " | ownerMatches=" + ownerMatches +
                " | sameFloor=" + sameFloor +
                " | hasWcTag=" + hasWcTag +
                " | reservationPortraitsEnabled=" + indicatorEnabled);

            BathroomAvailabilityAudit.LogHospitalizedSelectionSnapshot(
                character,
                toilet);
        }

        internal static void LogReserveObjectAnomalies(UseComponent useComponent, TileObject tileObject)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                useComponent == null ||
                useComponent.m_state == null ||
                tileObject == null)
            {
                return;
            }

            Entity character = useComponent.m_entity;
            TileObject previousReserved = BathroomFixtureHandoff.GetReservedObject(useComponent);

            if (previousReserved != null &&
                previousReserved != tileObject &&
                previousReserved.HasTag("wc"))
            {
                Plugin.Log?.LogInfo("[BathroomDebug] wc-reservation-overwrite" +
                    " | character=" + CharacterName(character) +
                    " | oldWc=" + ObjectName(previousReserved) +
                    " | oldWcUser=" + CharacterName(previousReserved.User) +
                    " | newObject=" + ObjectName(tileObject));
            }

            if (!tileObject.HasTag("washing"))
            {
                return;
            }

            if (tileObject.User != null && tileObject.User != character)
            {
                Plugin.Log?.LogInfo("[BathroomDebug] sink-reserve-collision" +
                    " | requester=" + CharacterName(character) +
                    " | requesterGermaphobe=" + HasGermaphobePerk(character) +
                    " | sink=" + ObjectName(tileObject) +
                    " | existingUser=" + CharacterName(tileObject.User) +
                    " | existingUserGermaphobe=" + HasGermaphobePerk(tileObject.User));
            }
        }

        internal static void AuditWcLocks(ProcedureScriptNeedBladder observerScript)
        {
            if (!TrafficControlConfig.BathroomFlowDebug ||
                observerScript == null ||
                observerScript.m_stateData == null ||
                observerScript.m_stateData.m_procedureScene == null ||
                Hospital.Instance == null)
            {
                return;
            }

            Entity observer = observerScript.m_stateData.m_procedureScene.MainCharacter;
            WalkComponent observerWalk = observer == null ? null : observer.GetComponent<WalkComponent>();
            if (observerWalk == null)
            {
                return;
            }

            int floorIndex = observerWalk.GetFloorIndex();
            if (floorIndex < 0 || floorIndex >= Hospital.Instance.m_floors.Count)
            {
                return;
            }

            Floor floor = Hospital.Instance.m_floors[floorIndex];
            if (floor == null)
            {
                return;
            }

            List<string> lockDetails = new List<string>();
            int ghostCount = 0;
            int transitionCandidateCount = 0;
            int expectedExclusiveCount = 0;

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    AuditWcCandidate(
                        floor.m_tileObjects[x, y].m_centerObject,
                        lockDetails,
                        ref ghostCount,
                        ref transitionCandidateCount,
                        ref expectedExclusiveCount);
                    AuditWcCandidate(
                        floor.m_tileObjects[x, y].m_attachmentObject,
                        lockDetails,
                        ref ghostCount,
                        ref transitionCandidateCount,
                        ref expectedExclusiveCount);
                }
            }

            StringBuilder signatureBuilder = new StringBuilder();
            signatureBuilder.Append("locked=").Append(lockDetails.Count);
            signatureBuilder.Append("|ghost=").Append(ghostCount);
            signatureBuilder.Append("|transitionCandidates=").Append(transitionCandidateCount);
            signatureBuilder.Append("|expectedExclusive=").Append(expectedExclusiveCount);
            for (int i = 0; i < lockDetails.Count; i++)
            {
                signatureBuilder.Append('|').Append(lockDetails[i]);
            }

            string signature = signatureBuilder.ToString();
            string previous;
            if (LastWcLockSnapshotByFloor.TryGetValue(floorIndex, out previous) &&
                previous == signature)
            {
                return;
            }

            LastWcLockSnapshotByFloor[floorIndex] = signature;
            Plugin.Log?.LogInfo(
                "[BathroomDebug] wc-lock-snapshot" +
                " | floor=" + floorIndex +
                " | locked=" + lockDetails.Count +
                " | ghost=" + ghostCount +
                " | transitionCandidates=" + transitionCandidateCount +
                " | expectedExclusive=" + expectedExclusiveCount);

            for (int i = 0; i < lockDetails.Count; i++)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] wc-lock" +
                    " | floor=" + floorIndex +
                    " | " + lockDetails[i]);
            }
        }

        private static void AuditWcCandidate(
            TileObject toilet,
            List<string> lockDetails,
            ref int ghostCount,
            ref int transitionCandidateCount,
            ref int expectedExclusiveCount)
        {
            if (toilet == null ||
                !toilet.HasTag("wc") ||
                (toilet.User == null && toilet.Owner == null))
            {
                return;
            }

            Entity user = toilet.User;
            UseComponent userUse = user == null ? null : user.GetComponent<UseComponent>();
            WalkComponent userWalk = user == null ? null : user.GetComponent<WalkComponent>();
            TileObject userReserved = BathroomFixtureHandoff.GetReservedObject(userUse);
            TileObject userCurrent = BathroomFixtureHandoff.GetCurrentObject(userUse);
            TileObject userSitTarget = null;
            TileObject userActuallySitting = null;
            string userWalkState = "<none>";

            if (userWalk != null && userWalk.m_state != null)
            {
                userWalkState = userWalk.m_state.m_walkState.ToString();

                if (userWalk.m_state.m_objectToSitOn != null)
                {
                    userSitTarget =
                        userWalk.m_state.m_objectToSitOn.GetEntity();
                }

                if (userWalk.m_state.m_objectSittingOn != null)
                {
                    userActuallySitting =
                        userWalk.m_state.m_objectSittingOn.GetEntity();
                }
            }

            bool reservedMatches = user != null && userReserved == toilet;
            bool currentMatches = user != null && userCurrent == toilet;
            bool sitTargetMatches = user != null && userSitTarget == toilet;
            bool actuallySittingMatches =
                user != null && userActuallySitting == toilet;
            bool userGhost =
                user != null &&
                !reservedMatches &&
                !currentMatches &&
                !sitTargetMatches &&
                !actuallySittingMatches;

            ProcedureScriptNeedBladder ownerScript = toilet.Owner as ProcedureScriptNeedBladder;
            string ownerState = "<none>";
            Entity ownerMainCharacter = null;
            bool ownerReservedMatches = false;
            bool ownerCurrentMatches = false;
            bool ownerGhost = false;
            bool singleToiletRoom = ownerScript != null &&
                SingleToiletBathroomLockManager.IsSingleToiletRoom(toilet);

            if (ownerScript != null && ownerScript.m_stateData != null)
            {
                ownerState = string.IsNullOrEmpty(ownerScript.m_stateData.m_state)
                    ? "<empty>"
                    : ownerScript.m_stateData.m_state;

                if (ownerScript.m_stateData.m_procedureScene != null)
                {
                    ownerMainCharacter = ownerScript.m_stateData.m_procedureScene.MainCharacter;
                }

                UseComponent ownerUse = ownerMainCharacter == null
                    ? null
                    : ownerMainCharacter.GetComponent<UseComponent>();
                ownerReservedMatches =
                    BathroomFixtureHandoff.GetReservedObject(ownerUse) == toilet;
                ownerCurrentMatches =
                    BathroomFixtureHandoff.GetCurrentObject(ownerUse) == toilet;

                bool ownerUserMismatch =
                    ownerMainCharacter == null || toilet.User != ownerMainCharacter;

                if (ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_OBJECT)
                {
                    ownerGhost = ownerUserMismatch || !ownerReservedMatches;
                }
                else if (ownerState == ProcedureScriptNeedBladder.STATE_USING_OBJECT)
                {
                    ownerGhost = ownerUserMismatch || !ownerCurrentMatches;
                }
                else if (TrafficControlConfig.ReleaseToiletOwnerAfterUse &&
                         !singleToiletRoom &&
                         (ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_SINK ||
                          ownerState == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                          ownerState == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE ||
                          ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_DRYER ||
                          ownerState == ProcedureScriptNeedBladder.STATE_USING_DRYER ||
                          ownerState == ProcedureScriptNeedBladder.STATE_IDLE))
                {
                    ownerGhost = true;
                }
            }

            // A mismatch does not prove an orphaned reservation. Vanilla
            // may release User before updating the bladder script state.
            bool afterPhysicalUse =
                ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_SINK ||
                ownerState == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                ownerState == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE ||
                ownerState == ProcedureScriptNeedBladder.STATE_GOING_TO_DRYER ||
                ownerState == ProcedureScriptNeedBladder.STATE_USING_DRYER;
            bool exclusiveAfterUse =
                TrafficControlConfig.ReleaseToiletOwnerAfterUse &&
                singleToiletRoom && afterPhysicalUse;
            bool transitionCandidate =
                ownerGhost &&
                ownerState == ProcedureScriptNeedBladder.STATE_USING_OBJECT &&
                ownerMainCharacter != null && user == null &&
                !ownerCurrentMatches;
            bool ghost = userGhost || ownerGhost;
            if (ghost)
            {
                ghostCount++;
            }
            if (transitionCandidate)
            {
                transitionCandidateCount++;
            }
            if (exclusiveAfterUse)
            {
                expectedExclusiveCount++;
            }
            string auditClass = exclusiveAfterUse
                ? "expected-single-wc-exclusive"
                : transitionCandidate
                    ? "possible-use-to-sink-transition"
                    : ghost ? "reservation-mismatch" : "normal";

            bool portraitsEnabled =
                SettingsManager.Instance != null &&
                SettingsManager.Instance.m_viewSettings.m_showObjectReservation.m_value;

            lockDetails.Add(
                "wc=" + ObjectName(toilet) +
                "{user=" + CharacterName(user) +
                ",owner=" + FormatOwner(toilet.Owner) +
                ",ownerState=" + ownerState +
                ",ownerMain=" + CharacterName(ownerMainCharacter) +
                ",reservedMatches=" + reservedMatches +
                ",currentMatches=" + currentMatches +
                ",sitTargetMatches=" + sitTargetMatches +
                ",actuallySittingMatches=" + actuallySittingMatches +
                ",walkState=" + userWalkState +
                ",ownerReservedMatches=" + ownerReservedMatches +
                ",ownerCurrentMatches=" + ownerCurrentMatches +
                ",singleToiletRoom=" + singleToiletRoom +
                ",exclusiveAfterUse=" + exclusiveAfterUse +
                ",transitionCandidate=" + transitionCandidate +
                ",auditClass=" + auditClass +
                ",portraitExpected=" + (portraitsEnabled && reservedMatches) +
                ",ghost=" + ghost + "}");
        }

        private static string FormatOwner(Entity owner)
        {
            return owner == null ? "<none>" : owner.GetType().Name;
        }
    }

    internal static class BathroomFixtureHandoff
    {
        private static readonly Dictionary<Entity, TileObject> HeldFixtureByCharacter =
            new Dictionary<Entity, TileObject>();

        internal static void Reset()
        {
            HeldFixtureByCharacter.Clear();
        }

        internal static TileObject GetReservedObject(UseComponent useComponent)
        {
            if (useComponent == null ||
                useComponent.m_state == null ||
                useComponent.m_state.m_reservedObject == null)
            {
                return null;
            }

            return useComponent.m_state.m_reservedObject.GetEntity();
        }

        internal static TileObject GetCurrentObject(UseComponent useComponent)
        {
            if (useComponent == null ||
                useComponent.m_state == null ||
                useComponent.m_state.m_object == null)
            {
                return null;
            }

            return useComponent.m_state.m_object.GetEntity();
        }

        internal static void HoldAfterNaturalUse(UseComponent useComponent, TileObject fixture)
        {
            if (useComponent == null ||
                useComponent.m_state == null ||
                fixture == null ||
                !IsBathroomFollowupFixture(fixture))
            {
                return;
            }

            Entity character = useComponent.m_entity;
            if (!IsFixtureUsedByActiveBladderScript(character, fixture))
            {
                return;
            }

            if (fixture.User != null && fixture.User != character)
            {
                if (TrafficControlConfig.BathroomFlowDebug)
                {
                    Plugin.Log?.LogInfo("[BathroomDebug] handoff-hold-collision" +
                        " | character=" + BathroomFlowDiagnostics.CharacterName(character) +
                        " | fixture=" + BathroomFlowDiagnostics.ObjectName(fixture) +
                        " | existingUser=" + BathroomFlowDiagnostics.CharacterName(fixture.User));
                }
                return;
            }

            TileObject reservedObject = GetReservedObject(useComponent);
            if (reservedObject != null && reservedObject != fixture)
            {
                if (TrafficControlConfig.BathroomFlowDebug)
                {
                    Plugin.Log?.LogInfo("[BathroomDebug] handoff-hold-skipped-reserved-object" +
                        " | character=" + BathroomFlowDiagnostics.CharacterName(character) +
                        " | fixture=" + BathroomFlowDiagnostics.ObjectName(fixture) +
                        " | reservedObject=" + BathroomFlowDiagnostics.ObjectName(reservedObject));
                }
                return;
            }

            fixture.User = character;
            useComponent.m_state.m_reservedObject = fixture;
            HeldFixtureByCharacter[character] = fixture;

            if (TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] handoff-hold" +
                    " | character=" + BathroomFlowDiagnostics.CharacterName(character) +
                    " | fixture=" + BathroomFlowDiagnostics.ObjectName(fixture));
            }
        }

        internal static void BeforeReserveObject(UseComponent useComponent, TileObject nextObject)
        {
            if (useComponent == null || useComponent.m_state == null)
            {
                return;
            }

            Entity character = useComponent.m_entity;
            TileObject heldFixture = GetHeldFixture(character, useComponent);
            if (heldFixture == null)
            {
                return;
            }

            if (heldFixture == nextObject)
            {
                HeldFixtureByCharacter.Remove(character);
                return;
            }

            ReleaseHeldFixture(character, useComponent, heldFixture, "next-reservation");
        }

        internal static void BeforeMovement(Entity character)
        {
            if (character == null)
            {
                return;
            }

            UseComponent useComponent = character.GetComponent<UseComponent>();
            if (useComponent == null || useComponent.m_state == null)
            {
                return;
            }

            TileObject heldFixture = GetHeldFixture(character, useComponent);
            if (heldFixture != null)
            {
                ReleaseHeldFixture(character, useComponent, heldFixture, "movement-start");
            }
        }

        internal static void ForgetReleasedHold(UseComponent useComponent)
        {
            if (useComponent == null || useComponent.m_entity == null)
            {
                return;
            }

            TileObject heldFixture;
            if (!HeldFixtureByCharacter.TryGetValue(useComponent.m_entity, out heldFixture))
            {
                return;
            }

            TileObject reservedObject = GetReservedObject(useComponent);
            if (reservedObject != heldFixture || heldFixture.User != useComponent.m_entity)
            {
                HeldFixtureByCharacter.Remove(useComponent.m_entity);
            }
        }

        private static TileObject GetHeldFixture(Entity character, UseComponent useComponent)
        {
            TileObject heldFixture;
            if (HeldFixtureByCharacter.TryGetValue(character, out heldFixture))
            {
                return heldFixture;
            }

            TileObject reservedObject = GetReservedObject(useComponent);
            if (reservedObject == null || !IsBathroomFollowupFixture(reservedObject))
            {
                return null;
            }

            if (!IsRecoveredHoldFromLoadedBladderScript(character, reservedObject))
            {
                return null;
            }

            HeldFixtureByCharacter[character] = reservedObject;
            return reservedObject;
        }

        private static bool IsRecoveredHoldFromLoadedBladderScript(
            Entity character,
            TileObject fixture)
        {
            ProcedureScriptNeedBladder script = FindBladderScript(character);
            if (script == null || script.m_stateData == null)
            {
                return false;
            }

            string state = script.m_stateData.m_state;
            if (fixture.HasTag("washing"))
            {
                return script.GetEquipment(1) == fixture &&
                       (state == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                        state == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE ||
                        state == ProcedureScriptNeedBladder.STATE_IDLE);
            }

            if (fixture.HasTag("dryer"))
            {
                return state == ProcedureScriptNeedBladder.STATE_USING_DRYER ||
                       state == ProcedureScriptNeedBladder.STATE_IDLE;
            }

            return false;
        }

        private static void ReleaseHeldFixture(
            Entity character,
            UseComponent useComponent,
            TileObject fixture,
            string reason)
        {
            if (fixture.User == character)
            {
                fixture.User = null;
            }

            if (GetReservedObject(useComponent) == fixture)
            {
                useComponent.m_state.m_reservedObject = null;
            }

            HeldFixtureByCharacter.Remove(character);

            if (TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] handoff-release" +
                    " | character=" + BathroomFlowDiagnostics.CharacterName(character) +
                    " | fixture=" + BathroomFlowDiagnostics.ObjectName(fixture) +
                    " | reason=" + reason);
            }
        }

        private static bool IsFixtureUsedByActiveBladderScript(
            Entity character,
            TileObject fixture)
        {
            ProcedureScriptNeedBladder script = FindBladderScript(character);
            if (script == null || script.m_stateData == null)
            {
                return false;
            }

            string state = script.m_stateData.m_state;
            if (fixture.HasTag("washing"))
            {
                return script.GetEquipment(1) == fixture &&
                       (state == ProcedureScriptNeedBladder.STATE_USING_SINK ||
                        state == ProcedureScriptNeedBladder.STATE_USING_SINK_GERMAPHOBE);
            }

            return fixture.HasTag("dryer") &&
                   state == ProcedureScriptNeedBladder.STATE_USING_DRYER;
        }

        private static bool IsBathroomFollowupFixture(TileObject fixture)
        {
            return fixture != null &&
                   (fixture.HasTag("washing") || fixture.HasTag("dryer"));
        }

        private static ProcedureScriptNeedBladder FindBladderScript(Entity character)
        {
            if (character == null || ProcedureManager.GetInstance() == null)
            {
                return null;
            }

            List<ProcedureScript> scripts = ProcedureManager.GetInstance().m_scriptEntities;
            if (scripts == null)
            {
                return null;
            }

            for (int i = 0; i < scripts.Count; i++)
            {
                ProcedureScriptNeedBladder script = scripts[i] as ProcedureScriptNeedBladder;
                if (script == null ||
                    script.m_stateData == null ||
                    script.m_stateData.m_procedureScene == null)
                {
                    continue;
                }

                if (script.m_stateData.m_procedureScene.MainCharacter == character)
                {
                    return script;
                }
            }

            return null;
        }
    }

    internal sealed class BathroomNaturalUseState
    {
        internal TileObject Fixture;
    }

    [HarmonyPatch]
    internal static class BathroomFlowPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(ProcedureScriptNeedBladder),
                "UpdateStateUsingObject");
        }

        private static void Postfix(ProcedureScriptNeedBladder __instance)
        {
            if (!TrafficControlConfig.ReleaseToiletOwnerAfterUse ||
                __instance == null ||
                __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null)
            {
                return;
            }

            string state = __instance.m_stateData.m_state;
            if (state != ProcedureScriptNeedBladder.STATE_GOING_TO_SINK &&
                state != ProcedureScriptNeedBladder.STATE_IDLE)
            {
                return;
            }

            TileObject toilet = __instance.GetEquipment(0);
            if (toilet == null)
            {
                return;
            }

            if (SingleToiletBathroomLockManager.IsSingleToiletRoom(
                    toilet))
            {
                SingleToiletBathroomLockManager.RefreshProcedureLock(
                    __instance);
                return;
            }

            if (toilet.User != null ||
                toilet.Owner != __instance)
            {
                return;
            }

            toilet.Owner = null;

            if (TrafficControlConfig.BathroomFlowDebug)
            {
                Plugin.Log?.LogInfo(
                    "[BathroomDebug] Released WC owner after physical use" +
                    " | character=" +
                    BathroomFlowDiagnostics.CharacterName(
                        __instance.m_stateData.m_procedureScene.MainCharacter) +
                    " | wc=" + BathroomFlowDiagnostics.ObjectName(toilet) +
                    " | nextState=" + state);
            }
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptNeedBladder), "Activate")]
    internal static class BathroomReservationDiagnosticsPatch
    {
        private static void Postfix(ProcedureScriptNeedBladder __instance)
        {
            SingleToiletBathroomLockManager.RefreshProcedureLock(__instance);
            BathroomFlowDiagnostics.LogInitialWcReservation(__instance);
            BathroomFlowDiagnostics.AuditWcLocks(__instance);
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptNeedBladder), "ScriptUpdate")]
    internal static class BathroomWcAuditPatch
    {
        private static void Postfix(ProcedureScriptNeedBladder __instance)
        {
            SingleToiletBathroomLockManager.RefreshProcedureLock(__instance);
            BathroomFlowDiagnostics.AuditWcLocks(__instance);
        }
    }

    [HarmonyPatch(
        typeof(BehaviorPatient),
        nameof(BehaviorPatient.Update),
        new Type[] { typeof(float) })]
    internal static class SingleToiletCollapseProtectionPatch
    {
        private static void Postfix(BehaviorPatient __instance)
        {
            SingleToiletBathroomLockManager.RefreshCollapsedPatient(
                __instance);
        }
    }

    internal static class BathroomNaturalUsePatchHelper
    {
        internal static void Prefix(UseComponent useComponent, out BathroomNaturalUseState state)
        {
            state = new BathroomNaturalUseState();
            if (useComponent == null || useComponent.m_state == null)
            {
                return;
            }

            TileObject fixture = BathroomFixtureHandoff.GetCurrentObject(useComponent);
            if (fixture != null &&
                (fixture.HasTag("washing") || fixture.HasTag("dryer")))
            {
                state.Fixture = fixture;
            }
        }

        internal static void Postfix(UseComponent useComponent, BathroomNaturalUseState state)
        {
            if (state == null ||
                state.Fixture == null ||
                useComponent == null ||
                useComponent.m_state == null ||
                useComponent.m_state.m_useComponentState != UseComponentState.IDLE ||
                BathroomFixtureHandoff.GetCurrentObject(useComponent) != null)
            {
                return;
            }

            BathroomFixtureHandoff.HoldAfterNaturalUse(useComponent, state.Fixture);
        }
    }

    [HarmonyPatch]
    internal static class BathroomUsingCompletionHandoffPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(UseComponent), "UpdateStateUsing");
        }

        private static void Prefix(UseComponent __instance, out BathroomNaturalUseState __state)
        {
            BathroomNaturalUsePatchHelper.Prefix(__instance, out __state);
        }

        private static void Postfix(UseComponent __instance, BathroomNaturalUseState __state)
        {
            BathroomNaturalUsePatchHelper.Postfix(__instance, __state);
        }
    }

    [HarmonyPatch]
    internal static class BathroomFinishingCompletionHandoffPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(typeof(UseComponent), "UpdateStateFinishing");
        }

        private static void Prefix(UseComponent __instance, out BathroomNaturalUseState __state)
        {
            BathroomNaturalUsePatchHelper.Prefix(__instance, out __state);
        }

        private static void Postfix(UseComponent __instance, BathroomNaturalUseState __state)
        {
            BathroomNaturalUsePatchHelper.Postfix(__instance, __state);
        }
    }

    [HarmonyPatch]
    internal static class BathroomReserveObjectPatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(UseComponent),
                "ReserveObject",
                new[] { typeof(TileObject) });
        }

        private static void Prefix(UseComponent __instance, TileObject tileObject)
        {
            BathroomFixtureHandoff.BeforeReserveObject(__instance, tileObject);
            BathroomFlowDiagnostics.LogReserveObjectAnomalies(__instance, tileObject);
        }
    }

    [HarmonyPatch(typeof(UseComponent), "Interrupt")]
    internal static class BathroomInterruptCleanupPatch
    {
        private static void Postfix(UseComponent __instance)
        {
            BathroomFixtureHandoff.ForgetReleasedHold(__instance);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), "SetDestination", new Type[]
    {
        typeof(Vector2i),
        typeof(int),
        typeof(MovementType)
    })]
    internal static class BathroomVector2iMovementHandoffPatch
    {
        private static void Prefix(WalkComponent __instance)
        {
            BathroomFixtureHandoff.BeforeMovement(__instance == null ? null : __instance.m_entity);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), "SetDestination", new Type[]
    {
        typeof(Vector2f),
        typeof(int),
        typeof(MovementType)
    })]
    internal static class BathroomVector2fMovementHandoffPatch
    {
        private static void Prefix(WalkComponent __instance)
        {
            BathroomFixtureHandoff.BeforeMovement(__instance == null ? null : __instance.m_entity);
        }
    }

    [HarmonyPatch(typeof(WalkComponent), "GoSit", new Type[]
    {
        typeof(TileObject),
        typeof(MovementType)
    })]
    internal static class BathroomGoSitHandoffPatch
    {
        private static void Prefix(WalkComponent __instance)
        {
            BathroomFixtureHandoff.BeforeMovement(__instance == null ? null : __instance.m_entity);
        }
    }

    [HarmonyPatch(typeof(ProcedureManager), "Reset")]
    internal static class BathroomProcedureManagerResetPatch
    {
        private static void Prefix()
        {
            BathroomFixtureHandoff.Reset();
            BathroomFlowDiagnostics.Reset();
        }
    }
}
