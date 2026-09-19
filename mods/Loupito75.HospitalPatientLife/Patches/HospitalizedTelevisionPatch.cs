using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalizedTelevisionAccess
    {
        private const string TelevisionObjectId = "OBJECT_TV";
        private const string RestingState = "RESTING";
        private const string AmbulatoryBedContext = "AMBULATORY_BED";

        private sealed class PendingAmbulatoryTelevision
        {
            internal Entity Patient;
            internal TileObject Seat;
            internal TileObject ExpectedResult;
            internal int ActivityRoll;
            internal int TotalWeight;
            internal int LocationRoll;
        }

        private static readonly Dictionary<ProcedureScriptControlHopitalizedFreeTime, PendingAmbulatoryTelevision>
            s_pendingAmbulatory =
                new Dictionary<ProcedureScriptControlHopitalizedFreeTime, PendingAmbulatoryTelevision>();

        private static readonly Dictionary<Entity, bool> s_activeAmbulatoryBed =
            new Dictionary<Entity, bool>();

        internal static void ClearPending(
            ProcedureScriptControlHopitalizedFreeTime script)
        {
            if (script != null)
            {
                s_pendingAmbulatory.Remove(script);
            }
        }

        internal static void QueueAmbulatoryTelevision(
            ProcedureScriptControlHopitalizedFreeTime script,
            Entity patient,
            TileObject seat,
            TileObject expectedResult,
            int activityRoll,
            int totalWeight,
            int locationRoll)
        {
            if (script == null || patient == null)
            {
                return;
            }

            s_pendingAmbulatory[script] = new PendingAmbulatoryTelevision
            {
                Patient = patient,
                Seat = seat,
                ExpectedResult = expectedResult,
                ActivityRoll = activityRoll,
                TotalWeight = totalWeight,
                LocationRoll = locationRoll
            };
        }

        private static void ClearPendingForPatient(Entity patient)
        {
            if (patient == null)
            {
                return;
            }

            ProcedureScriptControlHopitalizedFreeTime scriptToRemove = null;
            foreach (
                KeyValuePair<ProcedureScriptControlHopitalizedFreeTime, PendingAmbulatoryTelevision> entry
                in s_pendingAmbulatory)
            {
                if (entry.Value != null && entry.Value.Patient == patient)
                {
                    scriptToRemove = entry.Key;
                    break;
                }
            }

            if (scriptToRemove != null)
            {
                s_pendingAmbulatory.Remove(scriptToRemove);
            }
        }

        internal static void OnHospitalizationStateChanging(
            HospitalizationComponent hospitalization,
            HospitalizationState nextState)
        {
            if (hospitalization == null ||
                hospitalization.m_entity == null ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_hospitalizationState !=
                    HospitalizationState.FillingFreeTime ||
                nextState == HospitalizationState.FillingFreeTime)
            {
                return;
            }

            Entity patient = hospitalization.m_entity;
            ClearPendingForPatient(patient);

            if (!s_activeAmbulatoryBed.Remove(patient))
            {
                return;
            }

            if (nextState != HospitalizationState.InBed ||
                hospitalization.m_state.m_bed == null)
            {
                return;
            }

            TileObject bed = hospitalization.m_state.m_bed.GetEntity();
            WalkComponent walk =
                hospitalization.m_entity.GetComponent<WalkComponent>();
            if (bed == null ||
                walk == null ||
                !walk.IsSittingOn(bed))
            {
                return;
            }

            GameDBTreatment trauma =
                Database.Instance.GetEntry<GameDBTreatment>(
                    "TRT_HOSPITALIZATION_TRAUMA");
            LyingState lyingState =
                hospitalization.GetHospitalizationTreatment() == trauma
                    ? LyingState.UNCOVERED
                    : LyingState.COVERED;

            hospitalization.GetCovered(lyingState);

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(
                        hospitalization.m_entity) +
                    " | television-bed-return=RESTORED" +
                    " | lyingState=" + lyingState);
            }
        }

        internal static bool TryStartQueuedBedTelevision(
            ProcedureScriptControlHopitalizedFreeTime script,
            TileObject finalResult)
        {
            PendingAmbulatoryTelevision pending;
            if (!TryGetPending(script, out pending) || pending.Seat != null)
            {
                return false;
            }

            if (!object.ReferenceEquals(finalResult, pending.ExpectedResult))
            {
                s_pendingAmbulatory.Remove(script);
                return false;
            }

            HospitalizationComponent hospitalization;
            WalkComponent walk;
            TileObject bed;
            Room room;
            if (!TryGetPatientRoom(
                    pending.Patient,
                    true,
                    out hospitalization,
                    out walk,
                    out bed,
                    out room) ||
                !walk.IsSittingOn(bed))
            {
                s_pendingAmbulatory.Remove(script);
                return false;
            }

            TileObject television = FindClosestTelevision(walk, room);
            s_pendingAmbulatory.Remove(script);
            if (television == null)
            {
                return false;
            }

            return StartWatching(
                script,
                pending.Patient,
                television,
                "AMBULATORY_BED",
                pending.ActivityRoll,
                pending.TotalWeight,
                pending.LocationRoll);
        }

        internal static bool TryStartQueuedSeatTelevision(
            ProcedureScriptControlHopitalizedFreeTime script)
        {
            PendingAmbulatoryTelevision pending;
            if (!TryGetPending(script, out pending) || pending.Seat == null)
            {
                return false;
            }

            WalkComponent walk = pending.Patient.GetComponent<WalkComponent>();
            if (walk == null || walk.IsBusy())
            {
                return false;
            }

            if (!walk.IsSittingOn(pending.Seat))
            {
                s_pendingAmbulatory.Remove(script);
                return false;
            }

            HospitalizationComponent hospitalization;
            TileObject bed;
            Room room;
            if (!TryGetPatientRoom(
                    pending.Patient,
                    true,
                    out hospitalization,
                    out walk,
                    out bed,
                    out room))
            {
                s_pendingAmbulatory.Remove(script);
                return false;
            }

            TileObject television = FindClosestTelevision(walk, room);
            s_pendingAmbulatory.Remove(script);
            if (television == null)
            {
                return false;
            }

            return StartWatching(
                script,
                pending.Patient,
                television,
                "AMBULATORY_SEAT",
                pending.ActivityRoll,
                pending.TotalWeight,
                pending.LocationRoll);
        }

        internal static bool TryStartWeightedLyingTelevision(
            ProcedureScriptControlHopitalizedLyingFreeTime script,
            Entity patient)
        {
            HospitalizationComponent hospitalization;
            WalkComponent walk;
            TileObject bed;
            Room room;

            if (!TryGetPatientRoom(
                    patient,
                    false,
                    out hospitalization,
                    out walk,
                    out bed,
                    out room) ||
                !walk.IsSittingOn(bed))
            {
                return false;
            }

            PatientLeisureConfig.EnsureLoaded();
            int televisionWeight =
                PatientLeisureConfig.LyingTelevisionActivityWeight;
            if (televisionWeight <= 0)
            {
                return false;
            }

            TileObject television = FindClosestTelevision(walk, room);
            if (television == null)
            {
                return false;
            }

            int nonTelevisionWeight =
                PatientLeisureConfig.LyingRestActivityWeight +
                PatientLeisureConfig.LyingNurseBookActivityWeight;
            int totalWeight = nonTelevisionWeight + televisionWeight;
            if (totalWeight <= 0)
            {
                return false;
            }

            int roll = UnityEngine.Random.Range(0, totalWeight);
            if (roll < nonTelevisionWeight)
            {
                return false;
            }

            if (HospitalizedPatientTrace.Enabled)
            {
                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | lying-leisure=SELECTED" +
                    " | activity=TELEVISION" +
                    " | weights=" +
                    PatientLeisureConfig.LyingRestActivityWeight + "/" +
                    PatientLeisureConfig.LyingNurseBookActivityWeight + "/" +
                    PatientLeisureConfig.LyingTelevisionActivityWeight +
                    " | activityRoll=" + roll + "/" + totalWeight);
            }

            return StartWatching(
                script,
                patient,
                television,
                "LYING_BED",
                roll,
                totalWeight,
                -1);
        }

        internal static TileObject FindClosestTelevision(
            WalkComponent walk,
            Room room)
        {
            if (walk == null || room == null || MapScriptInterface.Instance == null)
            {
                return null;
            }

            List<TileObject> televisions =
                MapScriptInterface.Instance.FindAllObjectWithTags(
                    room,
                    new string[] { "tv" },
                    AccessRights.PATIENT);

            if (televisions == null || televisions.Count == 0)
            {
                return null;
            }

            Vector2i currentTile = walk.GetCurrentTile();
            TileObject closest = null;
            int bestDistanceSquared = int.MaxValue;

            for (int i = 0; i < televisions.Count; i++)
            {
                TileObject candidate = televisions[i];
                if (!IsNativeTelevision(candidate))
                {
                    continue;
                }

                int dx = candidate.m_state.m_position.m_x - currentTile.m_x;
                int dy = candidate.m_state.m_position.m_y - currentTile.m_y;
                int distanceSquared = dx * dx + dy * dy;
                if (distanceSquared < bestDistanceSquared)
                {
                    bestDistanceSquared = distanceSquared;
                    closest = candidate;
                }
            }

            return closest;
        }

        private static bool TryGetPending(
            ProcedureScriptControlHopitalizedFreeTime script,
            out PendingAmbulatoryTelevision pending)
        {
            pending = null;
            if (script == null ||
                !s_pendingAmbulatory.TryGetValue(script, out pending) ||
                pending == null ||
                pending.Patient == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null ||
                script.m_stateData.m_procedureScene.MainCharacter != pending.Patient)
            {
                if (script != null)
                {
                    s_pendingAmbulatory.Remove(script);
                }
                pending = null;
                return false;
            }

            return true;
        }

        private static bool TryGetPatientRoom(
            Entity patient,
            bool patientMustBeAllowedToWalk,
            out HospitalizationComponent hospitalization,
            out WalkComponent walk,
            out TileObject bed,
            out Room room)
        {
            hospitalization = null;
            walk = null;
            bed = null;
            room = null;

            if (patient == null || MapScriptInterface.Instance == null)
            {
                return false;
            }

            hospitalization = patient.GetComponent<HospitalizationComponent>();
            walk = patient.GetComponent<WalkComponent>();
            if (hospitalization == null ||
                !hospitalization.IsHospitalized() ||
                walk == null ||
                hospitalization.IsAllowedToWalk() != patientMustBeAllowedToWalk ||
                hospitalization.m_state == null ||
                hospitalization.m_state.m_bed == null)
            {
                return false;
            }

            bed = hospitalization.m_state.m_bed.GetEntity();
            if (bed == null || bed.m_state == null)
            {
                return false;
            }

            room = MapScriptInterface.Instance.GetRoomAt(
                bed.m_state.m_position,
                bed.GetFloorIndex());
            if (room == null)
            {
                return false;
            }

            return MapScriptInterface.Instance.GetRoomAt(walk) == room;
        }

        private static bool StartWatching(
            ProcedureScript script,
            Entity patient,
            TileObject television,
            string context,
            int activityRoll,
            int totalWeight,
            int locationRoll)
        {
            if (script == null ||
                patient == null ||
                television == null ||
                !IsNativeTelevision(television))
            {
                return false;
            }

            int duration = UnityEngine.Random.Range(10, 25);
            script.SetParam(1, duration);

            bool alreadyOn = television.m_state.m_lightEnabled;
            if (!alreadyOn)
            {
                television.SetLightEnabled(true);

                AnimatedObjectComponent animatedObject =
                    television.GetComponent<AnimatedObjectComponent>();
                if (animatedObject != null)
                {
                    animatedObject.ForceFrame(1);
                }

                television.PlayStartUseSound();
            }

            SpeechComponent speech = patient.GetComponent<SpeechComponent>();
            if (speech != null)
            {
                speech.SetBubble("BUBBLE_TV", 5f);
            }

            // RESTING supplies the native duration/end lifecycle without inventing a
            // persistent WATCHING_TV state that the game itself never updates.
            script.SwitchState(RestingState);

            if (context == AmbulatoryBedContext)
            {
                s_activeAmbulatoryBed[patient] = true;
            }
            else
            {
                s_activeAmbulatoryBed.Remove(patient);
            }

            if (HospitalizedPatientTrace.Enabled)
            {
                string weights = context == "LYING_BED"
                    ? PatientLeisureConfig.LyingRestActivityWeight + "/" +
                        PatientLeisureConfig.LyingNurseBookActivityWeight + "/" +
                        PatientLeisureConfig.LyingTelevisionActivityWeight
                    : PatientLeisureConfig.RoomRestActivityWeight + "/" +
                        PatientLeisureConfig.RoomEducationActivityWeight + "/" +
                        PatientLeisureConfig.RoomVisualActivityWeight + "/" +
                        PatientLeisureConfig.RoomTelevisionActivityWeight;

                HospitalizedPatientTrace.Log(
                    HospitalizedPatientTrace.GetName(patient) +
                    " | television=SELECTED" +
                    " | context=" + context +
                    " | weights=" + weights +
                    " | roll=" + activityRoll + "/" + totalWeight +
                    " | locationRoll=" + locationRoll +
                    " | duration=" + duration +
                    " | floor=" + television.GetFloorIndex() +
                    " | position=" + television.m_state.m_position.m_x +
                    "," + television.m_state.m_position.m_y +
                    " | alreadyOn=" + alreadyOn);
            }

            return true;
        }

        private static bool IsNativeTelevision(TileObject candidate)
        {
            if (candidate == null ||
                candidate.m_state == null ||
                candidate.IsBroken() ||
                !candidate.IsValid() ||
                !candidate.m_state.m_gameDBObject.IsValid ||
                candidate.m_state.m_gameDBObject.Entry == null ||
                ID.IsNullOrNoID(candidate.m_state.m_gameDBObject.Entry.DatabaseID))
            {
                return false;
            }

            return candidate.m_state.m_gameDBObject.Entry.DatabaseID.ToString() ==
                TelevisionObjectId;
        }
    }

    [HarmonyPatch(typeof(HospitalizationComponent), "SwitchState")]
    internal static class AmbulatoryBedTelevisionCoverRestorePatch
    {
        private static void Prefix(
            HospitalizationComponent __instance,
            HospitalizationState state)
        {
            HospitalizedTelevisionAccess.OnHospitalizationStateChanging(
                __instance,
                state);
        }
    }

    [HarmonyPatch]
    internal static class LyingPatientTelevisionPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedLyingFreeTime),
                "ChooseSomething",
                Type.EmptyTypes);

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedLyingFreeTime.ChooseSomething() was not found.");
            }

            return method;
        }

        [HarmonyPrefix]
        private static bool Prefix(
            ProcedureScriptControlHopitalizedLyingFreeTime __instance)
        {
            if (__instance == null ||
                __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null)
            {
                return true;
            }

            Entity patient = __instance.m_stateData.m_procedureScene.MainCharacter;
            return !HospitalizedTelevisionAccess.TryStartWeightedLyingTelevision(
                __instance,
                patient);
        }
    }

    [HarmonyPatch]
    internal static class AmbulatoryPatientBedTelevisionPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedFreeTime),
                "ChooseSomething",
                Type.EmptyTypes);

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedFreeTime.ChooseSomething() was not found.");
            }

            return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo getEntertainmentItem = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedFreeTime),
                "GetEntertainmentItem",
                new Type[]
                {
                    typeof(Entity),
                    typeof(string)
                });
            MethodInfo helper = AccessTools.Method(
                typeof(HospitalizedTelevisionAccess),
                "TryStartQueuedBedTelevision",
                new Type[]
                {
                    typeof(ProcedureScriptControlHopitalizedFreeTime),
                    typeof(TileObject)
                });

            if (getEntertainmentItem == null || helper == null)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Ambulatory bed-TV patch could not resolve its helper methods; room TV falls back to normal leisure.");
                }
                return codes;
            }

            List<int> matches = new List<int>();
            for (int i = 1; i + 1 < codes.Count; i++)
            {
                MethodInfo called = codes[i].operand as MethodInfo;
                if (called != getEntertainmentItem ||
                    codes[i - 1].opcode != OpCodes.Ldstr ||
                    !object.Equals(codes[i - 1].operand, "hospitalized_patient") ||
                    !IsStoreLocal(codes[i + 1]))
                {
                    continue;
                }

                matches.Add(i);
            }

            if (matches.Count != 1)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Ambulatory bed-TV patch expected one hospitalized-patient entertainment lookup but found " +
                        matches.Count + "; room TV falls back to normal leisure.");
                }
                return codes;
            }

            int callIndex = matches[0];
            int storeIndex = callIndex + 1;
            CodeInstruction loadResult = CreateLoadForStore(codes[storeIndex]);
            if (loadResult == null || storeIndex + 1 >= codes.Count)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Ambulatory bed-TV patch could not identify the entertainment local; room TV falls back to normal leisure.");
                }
                return codes;
            }

            int insertIndex = storeIndex + 1;
            Label continueLabel = generator.DefineLabel();
            codes[insertIndex].labels.Add(continueLabel);

            codes.InsertRange(
                insertIndex,
                new CodeInstruction[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    loadResult,
                    new CodeInstruction(OpCodes.Call, helper),
                    new CodeInstruction(OpCodes.Brfalse_S, continueLabel),
                    new CodeInstruction(OpCodes.Ret)
                });

            return codes;
        }

        private static bool IsStoreLocal(CodeInstruction instruction)
        {
            return instruction != null &&
                (instruction.opcode == OpCodes.Stloc ||
                 instruction.opcode == OpCodes.Stloc_S ||
                 instruction.opcode == OpCodes.Stloc_0 ||
                 instruction.opcode == OpCodes.Stloc_1 ||
                 instruction.opcode == OpCodes.Stloc_2 ||
                 instruction.opcode == OpCodes.Stloc_3);
        }

        private static CodeInstruction CreateLoadForStore(
            CodeInstruction store)
        {
            if (store.opcode == OpCodes.Stloc_0)
            {
                return new CodeInstruction(OpCodes.Ldloc_0);
            }
            if (store.opcode == OpCodes.Stloc_1)
            {
                return new CodeInstruction(OpCodes.Ldloc_1);
            }
            if (store.opcode == OpCodes.Stloc_2)
            {
                return new CodeInstruction(OpCodes.Ldloc_2);
            }
            if (store.opcode == OpCodes.Stloc_3)
            {
                return new CodeInstruction(OpCodes.Ldloc_3);
            }
            if (store.opcode == OpCodes.Stloc_S)
            {
                return new CodeInstruction(OpCodes.Ldloc_S, store.operand);
            }
            if (store.opcode == OpCodes.Stloc)
            {
                return new CodeInstruction(OpCodes.Ldloc, store.operand);
            }

            return null;
        }
    }

    [HarmonyPatch]
    internal static class AmbulatoryPatientSeatTelevisionPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(ProcedureScriptControlHopitalizedFreeTime),
                "UpdateStateGoingToSit",
                Type.EmptyTypes);

            if (method == null)
            {
                throw new MissingMethodException(
                    "ProcedureScriptControlHopitalizedFreeTime.UpdateStateGoingToSit() was not found.");
            }

            return method;
        }

        [HarmonyPrefix]
        private static bool Prefix(
            ProcedureScriptControlHopitalizedFreeTime __instance)
        {
            return !HospitalizedTelevisionAccess.TryStartQueuedSeatTelevision(
                __instance);
        }
    }
}
