using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalTrafficControl.Patches
{
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
                Plugin.Log?.LogWarning(
                    "[BathroomDebug] wc-reservation-overwrite" +
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
                Plugin.Log?.LogWarning(
                    "[BathroomDebug] sink-reserve-collision" +
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

            for (int x = 1; x < floor.Size.m_x - 1; x++)
            {
                for (int y = 1; y < floor.Size.m_y - 1; y++)
                {
                    AuditWcCandidate(
                        floor.m_tileObjects[x, y].m_centerObject,
                        lockDetails,
                        ref ghostCount);
                    AuditWcCandidate(
                        floor.m_tileObjects[x, y].m_attachmentObject,
                        lockDetails,
                        ref ghostCount);
                }
            }

            StringBuilder signatureBuilder = new StringBuilder();
            signatureBuilder.Append("locked=").Append(lockDetails.Count);
            signatureBuilder.Append("|ghost=").Append(ghostCount);
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
                " | ghost=" + ghostCount);

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
            ref int ghostCount)
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
            TileObject userSitting = null;

            if (userWalk != null &&
                userWalk.m_state != null &&
                userWalk.m_state.m_objectToSitOn != null)
            {
                userSitting = userWalk.m_state.m_objectToSitOn.GetEntity();
            }

            bool reservedMatches = user != null && userReserved == toilet;
            bool currentMatches = user != null && userCurrent == toilet;
            bool sittingMatches = user != null && userSitting == toilet;
            bool userGhost =
                user != null &&
                !reservedMatches &&
                !currentMatches &&
                !sittingMatches;

            ProcedureScriptNeedBladder ownerScript = toilet.Owner as ProcedureScriptNeedBladder;
            string ownerState = "<none>";
            Entity ownerMainCharacter = null;
            bool ownerReservedMatches = false;
            bool ownerCurrentMatches = false;
            bool ownerGhost = false;

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

            bool ghost = userGhost || ownerGhost;
            if (ghost)
            {
                ghostCount++;
            }

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
                ",sittingMatches=" + sittingMatches +
                ",ownerReservedMatches=" + ownerReservedMatches +
                ",ownerCurrentMatches=" + ownerCurrentMatches +
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
                    Plugin.Log?.LogWarning(
                        "[BathroomDebug] handoff-hold-collision" +
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
                    Plugin.Log?.LogWarning(
                        "[BathroomDebug] handoff-hold-skipped-reserved-object" +
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
            if (toilet == null ||
                toilet.User != null ||
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
            BathroomFlowDiagnostics.LogInitialWcReservation(__instance);
            BathroomFlowDiagnostics.AuditWcLocks(__instance);
        }
    }

    [HarmonyPatch(typeof(ProcedureScriptNeedBladder), "ScriptUpdate")]
    internal static class BathroomWcAuditPatch
    {
        private static void Postfix(ProcedureScriptNeedBladder __instance)
        {
            BathroomFlowDiagnostics.AuditWcLocks(__instance);
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
