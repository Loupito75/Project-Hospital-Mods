using System.Collections.Generic;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal static class PorterNeedsRuntime
    {
        private const string StaffLunchProcedureId =
            "CONTROL_PROCEDURE_STAFF_LUNCH";
        private const string FreeTimeProcedureId =
            "CONTROL_PROCEDURE_NURSE_FREE_TIME";

        internal static bool TryStartNativeIdleActivity(
            BehaviorNurse nurse)
        {
            Entity porter =
                PorterIdleResting.GetEntity(nurse);
            if (!PorterIdentity.IsPorter(porter) ||
                nurse == null ||
                nurse.m_state == null ||
                nurse.m_state.m_nurseState != NurseState.Idle ||
                nurse.m_state.m_currentPatient != null ||
                nurse.m_state.m_timeInState <= 5f ||
                PorterSampleTransportRuntime.IsBusy(porter) ||
                PorterPatientContinuationRuntime.IsBusy(porter) ||
                Database.Instance == null ||
                DayTime.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            ProcedureComponent procedures =
                porter.GetComponent<ProcedureComponent>();
            MoodComponent mood =
                porter.GetComponent<MoodComponent>();
            PerkComponent perks =
                porter.GetComponent<PerkComponent>();
            SpeechComponent speech =
                porter.GetComponent<SpeechComponent>();
            Department department =
                employee == null ||
                employee.m_state.m_department == null
                    ? null
                    : employee.m_state.m_department.GetEntity();

            if (employee == null ||
                procedures == null ||
                mood == null ||
                perks == null ||
                department == null ||
                employee.IsFired() ||
                employee.ShouldGoToTraining() ||
                employee.m_state.m_shift !=
                    DayTime.Instance.GetShift() ||
                procedures.IsBusy())
            {
                return false;
            }

            // BehaviorNurse normally suppresses lunch/needs/free time while
            // its clinical department is under pressure. A Porter cannot
            // perform those Nurse clinical duties, so reuse the exact native
            // staff procedures without inheriting that Nurse-only gate.
            if (DayTime.Instance.IsScheduledActionTime(
                    "SCHEDULE_STAFF_LUNCH",
                    onlyStart: true) &&
                !nurse.m_state.m_hadLunch)
            {
                GameDBProcedure lunch =
                    Database.Instance.GetEntry<GameDBProcedure>(
                        StaffLunchProcedureId);
                if (lunch != null &&
                    procedures.GetProcedureAvailabilty(
                        lunch,
                        porter,
                        department,
                        AccessRights.STAFF,
                        EquipmentListRules
                            .ONLY_FREE_SAME_FLOOR_PREFER_DPT) ==
                    ProcedureSceneAvailability.AVAILABLE)
                {
                    procedures.StartProcedure(
                        lunch,
                        porter,
                        department,
                        AccessRights.STAFF,
                        EquipmentListRules
                            .ONLY_FREE_SAME_FLOOR_PREFER_DPT);
                    nurse.m_state.m_hadLunch = true;
                    if (speech != null)
                    {
                        speech.HideBubble();
                    }
                    nurse.SwitchState(
                        NurseState.FulfillingNeeds);
                    return true;
                }
            }

            List<Need> needs =
                mood.GetNeedsSortedFromMostCritical();
            for (int i = 0;
                i < needs.Count;
                i++)
            {
                Need need =
                    needs[i];
                if (need == null ||
                    need.m_currentValue <= 50f ||
                    need.m_gameDBNeed.Entry.Procedure == null)
                {
                    continue;
                }

                GameDBProcedure needProcedure =
                    need.m_gameDBNeed.Entry.Procedure;
                if (procedures.GetProcedureAvailabilty(
                        needProcedure,
                        porter,
                        department,
                        AccessRights.STAFF_ONLY,
                        EquipmentListRules
                            .ONLY_FREE_SAME_FLOOR_PREFER_DPT) !=
                    ProcedureSceneAvailability.AVAILABLE)
                {
                    continue;
                }

                procedures.StartProcedure(
                    needProcedure,
                    porter,
                    department,
                    AccessRights.STAFF_ONLY,
                    EquipmentListRules
                        .ONLY_FREE_SAME_FLOOR_PREFER_DPT);
                if (speech != null)
                {
                    speech.HideBubble();
                }
                nurse.SwitchState(
                    NurseState.FulfillingNeeds);
                return true;
            }

            if (perks.m_perkSet.HasPerk(
                    "PERK_HARD_WORKER") ||
                SettingsManager.Instance == null ||
                SettingsManager.Instance.m_debugSettings
                    .m_disableFreeTime.m_value)
            {
                return false;
            }

            GameDBProcedure freeTime =
                Database.Instance.GetEntry<GameDBProcedure>(
                    FreeTimeProcedureId);
            if (freeTime == null ||
                procedures.GetProcedureAvailabilty(
                    freeTime,
                    porter,
                    department,
                    AccessRights.STAFF,
                    EquipmentListRules
                        .ONLY_FREE_SAME_FLOOR_PREFER_DPT) !=
                ProcedureSceneAvailability.AVAILABLE)
            {
                return false;
            }

            procedures.StartProcedure(
                freeTime,
                porter,
                department,
                AccessRights.STAFF,
                EquipmentListRules
                    .ONLY_FREE_SAME_FLOOR_PREFER_DPT);
            nurse.CancelBrowsing();
            if (speech != null)
            {
                speech.HideBubble();
            }
            nurse.SwitchState(
                NurseState.FillingFreeTime);
            return true;
        }
    }

    // Native free time is also used as the employee rest mechanic:
    // NEED_REST itself has no Procedure in Needs.xml. The vanilla script
    // shortens RESTING as soon as the department has waiting patients.
    // That pressure is a Nurse clinical concern, not a Porter responsibility.
    [HarmonyPatch(
        typeof(ProcedureScriptControlNurseFreeTime),
        "UpdateStateResting")]
    internal static class PorterFreeTimeRestingPatch
    {
        private static bool Prefix(
            ProcedureScriptControlNurseFreeTime __instance)
        {
            Entity porter =
                GetMainPorter(__instance);
            if (!PorterIdentity.IsPorter(porter))
            {
                return true;
            }

            if (!(__instance.m_stateData.m_timeInState > 2f))
            {
                return false;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            PerkComponent perks =
                porter.GetComponent<PerkComponent>();
            if (employee == null ||
                employee.m_state.m_department == null)
            {
                __instance.SwitchState(
                    "RESTING_STANDING_UP");
                return false;
            }

            if (__instance.GetParam(0) > 5f)
            {
                if (perks != null &&
                    perks.m_perkSet.HasPerk(
                        "PERK_SCHOLAR"))
                {
                    __instance.SwitchState(
                        "STUDYING_STANDING_UP");
                    AnimModelComponent animation =
                        porter.GetComponent<AnimModelComponent>();
                    if (animation != null)
                    {
                        animation.PlayAnimation(
                            "sit_read_idle",
                            looping: false);
                    }
                }
                else
                {
                    __instance.SwitchState(
                        "RESTING_STANDING_UP");
                }

                if (perks != null &&
                    perks.m_perkSet.HasHiddenPerk(
                        "PERK_SPARTAN"))
                {
                    perks.m_perkSet.RevealPerk(
                        "PERK_SPARTAN");
                }

                return false;
            }

            Behavior behavior =
                porter.GetComponent<Behavior>();
            if (behavior != null)
            {
                behavior.ReceiveMessage(
                    new Message(
                        Messages.REST_REDUCED,
                        5f));
            }

            __instance.m_stateData.m_timeInState = 0f;
            __instance.SetParam(
                0,
                __instance.GetParam(0) + 2f);
            return false;
        }

        private static Entity GetMainPorter(
            ProcedureScriptControlNurseFreeTime script)
        {
            return script == null ||
                script.m_stateData == null ||
                script.m_stateData.m_procedureScene == null
                    ? null
                    : script.m_stateData
                        .m_procedureScene.MainCharacter;
        }
    }

    [HarmonyPatch(
        typeof(ProcedureScriptControlNurseFreeTime),
        "UpdateStatePlaying")]
    internal static class PorterFreeTimePlayingPatch
    {
        private static bool Prefix(
            ProcedureScriptControlNurseFreeTime __instance)
        {
            Entity porter =
                __instance == null ||
                __instance.m_stateData == null ||
                __instance.m_stateData.m_procedureScene == null
                    ? null
                    : __instance.m_stateData
                        .m_procedureScene.MainCharacter;
            if (!PorterIdentity.IsPorter(porter))
            {
                return true;
            }

            if (!(__instance.m_stateData.m_timeInState > 1f))
            {
                return false;
            }

            PerkComponent perks =
                porter.GetComponent<PerkComponent>();
            if (__instance.GetParam(0) > 5f)
            {
                if (perks != null &&
                    perks.m_perkSet.HasHiddenPerk(
                        "PERK_SPARTAN"))
                {
                    perks.m_perkSet.RevealPerk(
                        "PERK_SPARTAN");
                }

                __instance.SetParam(
                    1,
                    6f);
                PropComponent prop =
                    porter.GetComponent<PropComponent>();
                if (prop != null)
                {
                    prop.m_state.m_prop = null;
                }

                SpeechComponent speech =
                    porter.GetComponent<SpeechComponent>();
                if (speech != null)
                {
                    speech.HideBubble();
                }

                __instance.SwitchState(
                    "RESTING_STANDING_UP");
                return false;
            }

            Behavior behavior =
                porter.GetComponent<Behavior>();
            if (behavior != null)
            {
                behavior.ReceiveMessage(
                    new Message(
                        Messages.REST_REDUCED,
                        10f));
            }

            __instance.m_stateData.m_timeInState = 0f;
            __instance.SetParam(
                0,
                __instance.GetParam(0) + 1f);
            return false;
        }
    }
}
