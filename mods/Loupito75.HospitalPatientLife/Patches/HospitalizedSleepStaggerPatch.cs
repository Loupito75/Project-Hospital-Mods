using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPatientLife.Patches
{
    internal static class HospitalizedSleepStaggerRules
    {
        private const string StaffScheduleId = "SCHEDULE_OPENING_HOURS_STAFF";
        private const int BedtimeSalt = 211;
        private const int WakeUpSalt = 419;

        internal static bool IsOpenForStaffForBedtime(
            DayTime dayTime,
            HospitalizationComponent hospitalization)
        {
            if (dayTime == null)
            {
                return false;
            }

            bool nativeOpen = dayTime.IsOpenForStaff();
            if (nativeOpen ||
                hospitalization == null ||
                hospitalization.m_entity == null ||
                HospitalPatientLifeConfig.BedtimeStaggerMinutes <= 0)
            {
                return nativeOpen;
            }

            // Trauma never enters the ordinary hospitalized Sleeping state here. Keep its
            // native 20:00 ResetFlags behavior instead of applying a bedtime delay to it.
            if (hospitalization.m_state != null &&
                hospitalization.m_state.m_hospitalizationTreatment ==
                    Database.Instance.GetEntry<GameDBTreatment>("TRT_HOSPITALIZATION_TRAUMA"))
            {
                return nativeOpen;
            }

            GameDBSchedule schedule =
                Database.Instance.GetEntry<GameDBSchedule>(StaffScheduleId);
            if (schedule == null)
            {
                return nativeOpen;
            }

            float elapsedMinutes = GetForwardMinutes(
                schedule.EndTime,
                dayTime.GetDayTimeHours());
            if (elapsedMinutes > HospitalPatientLifeConfig.BedtimeStaggerMinutes)
            {
                return nativeOpen;
            }

            int personalDelay = GetStableDelayMinutes(
                hospitalization.m_entity,
                dayTime.GetDay(),
                BedtimeSalt,
                HospitalPatientLifeConfig.BedtimeStaggerMinutes);

            // Pretend the staff-open test is still true only for this hospitalization
            // sleep decision. UpdateStateInBed therefore continues through its normal
            // medical/needs logic until this patient's personal bedtime.
            return elapsedMinutes < personalDelay;
        }

        internal static bool IsOpenForStaffForWakeUp(
            DayTime dayTime,
            HospitalizationComponent hospitalization)
        {
            if (dayTime == null)
            {
                return false;
            }

            bool nativeOpen = dayTime.IsOpenForStaff();
            if (!nativeOpen ||
                hospitalization == null ||
                hospitalization.m_entity == null ||
                HospitalPatientLifeConfig.WakeUpStaggerMinutes <= 0)
            {
                return nativeOpen;
            }

            GameDBSchedule schedule =
                Database.Instance.GetEntry<GameDBSchedule>(StaffScheduleId);
            if (schedule == null)
            {
                return nativeOpen;
            }

            float elapsedMinutes = GetForwardMinutes(
                schedule.StartTime,
                dayTime.GetDayTimeHours());
            if (elapsedMinutes > HospitalPatientLifeConfig.WakeUpStaggerMinutes)
            {
                return nativeOpen;
            }

            int personalDelay = GetStableDelayMinutes(
                hospitalization.m_entity,
                dayTime.GetDay(),
                WakeUpSalt,
                HospitalPatientLifeConfig.WakeUpStaggerMinutes);

            // UpdateStateSleeping keeps running while this returns false, including its
            // native SelectNextStep() calls. Examinations/treatments can therefore wake
            // the patient before the ordinary staggered wake-up.
            return elapsedMinutes >= personalDelay;
        }

        private static int GetStableDelayMinutes(
            Entity patient,
            int day,
            int salt,
            int maximumMinutes)
        {
            if (patient == null || maximumMinutes <= 0)
            {
                return 0;
            }

            long value = 17L;
            value = value * 31L + patient.GetEntityID();
            value = value * 31L + day;
            value = value * 31L + salt;
            value &= 0x7fffffffL;

            return (int)(value % (maximumMinutes + 1));
        }

        private static float GetForwardMinutes(float startHour, float currentHour)
        {
            float hours = currentHour - startHour;
            if (hours < 0f)
            {
                hours += 24f;
            }

            return hours * 60f;
        }

        internal static bool IsHospitalizedFreeTimeDisabled(GenericFlag<bool> nativeFlag)
        {
            if (nativeFlag == null || nativeFlag.m_value)
            {
                return true;
            }

            // After the native 20:00 staff boundary, an awake staggered patient may still
            // satisfy real personal needs, but should not start a fresh leisure procedure.
            return DayTime.Instance != null && !DayTime.Instance.IsOpenForStaff();
        }

        internal static IEnumerable<CodeInstruction> ReplaceIsOpenForStaffCall(
            IEnumerable<CodeInstruction> instructions,
            int expectedOccurrences,
            int targetOccurrence,
            MethodInfo replacement,
            string patchName)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            MethodInfo nativeMethod = AccessTools.Method(
                typeof(DayTime),
                "IsOpenForStaff",
                Type.EmptyTypes);

            if (nativeMethod == null || replacement == null)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        patchName +
                        " could not resolve IsOpenForStaff/helper; vanilla timing is unchanged.");
                }
                return codes;
            }

            List<int> matches = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                MethodInfo called = codes[i].operand as MethodInfo;
                if (called == nativeMethod)
                {
                    matches.Add(i);
                }
            }

            if (matches.Count != expectedOccurrences ||
                targetOccurrence < -1 ||
                targetOccurrence >= matches.Count)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        patchName + " expected " + expectedOccurrences +
                        " IsOpenForStaff call(s) but found " + matches.Count +
                        "; vanilla timing is unchanged.");
                }
                return codes;
            }

            if (targetOccurrence == -1)
            {
                // Patch from the end so inserting Ldarg_0 does not invalidate earlier indexes.
                for (int matchIndex = matches.Count - 1; matchIndex >= 0; matchIndex--)
                {
                    ReplaceCall(codes, matches[matchIndex], replacement);
                }
            }
            else
            {
                ReplaceCall(codes, matches[targetOccurrence], replacement);
            }

            return codes;
        }

        private static void ReplaceCall(
            List<CodeInstruction> codes,
            int index,
            MethodInfo replacement)
        {
            CodeInstruction loadComponent =
                new CodeInstruction(OpCodes.Ldarg_0);
            loadComponent.labels.AddRange(codes[index].labels);
            codes[index].labels.Clear();

            codes.Insert(index, loadComponent);
            codes[index + 1].opcode = OpCodes.Call;
            codes[index + 1].operand = replacement;
        }
    }

    [HarmonyPatch]
    internal static class HospitalizedBedtimeStaggerPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(HospitalizationComponent),
                "UpdateStateInBed",
                new Type[] { typeof(float) });

            if (method == null)
            {
                throw new MissingMethodException(
                    "HospitalizationComponent.UpdateStateInBed(float) was not found.");
            }

            return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(
                typeof(HospitalizedSleepStaggerRules),
                "IsOpenForStaffForBedtime",
                new Type[]
                {
                    typeof(DayTime),
                    typeof(HospitalizationComponent)
                });

            // UpdateStateInBed contains two consecutive native checks: the first resets
            // daily hospitalization flags and the second starts Sleeping. Both must follow the
            // same personal bedtime, otherwise ResetFlags() would repeat throughout the stagger.
            return HospitalizedSleepStaggerRules.ReplaceIsOpenForStaffCall(
                instructions,
                2,
                -1,
                replacement,
                "Hospitalized bedtime stagger");
        }
    }

    [HarmonyPatch]
    internal static class HospitalizedBedtimeFreeTimeGuardPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(HospitalizationComponent),
                "CheckNeeds",
                Type.EmptyTypes);

            if (method == null)
            {
                throw new MissingMethodException(
                    "HospitalizationComponent.CheckNeeds() was not found.");
            }

            return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);

            FieldInfo disableFreeTimeField = AccessTools.Field(
                typeof(DebugSettings),
                "m_disableFreeTime");
            FieldInfo flagValueField = AccessTools.Field(
                typeof(GenericFlag<bool>),
                "m_value");
            MethodInfo replacement = AccessTools.Method(
                typeof(HospitalizedSleepStaggerRules),
                "IsHospitalizedFreeTimeDisabled",
                new Type[] { typeof(GenericFlag<bool>) });

            if (disableFreeTimeField == null ||
                flagValueField == null ||
                replacement == null)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Hospitalized bedtime free-time guard could not resolve its fields/helper; native free-time behavior is unchanged.");
                }
                return codes;
            }

            List<int> matches = new List<int>();
            for (int i = 1; i < codes.Count; i++)
            {
                FieldInfo previousField = codes[i - 1].operand as FieldInfo;
                FieldInfo currentField = codes[i].operand as FieldInfo;
                if (previousField == disableFreeTimeField &&
                    currentField == flagValueField)
                {
                    matches.Add(i);
                }
            }

            if (matches.Count != 2)
            {
                if (Plugin.Log != null)
                {
                    Plugin.Log.LogWarning(
                        "Hospitalized bedtime free-time guard expected two free-time flag reads but found " +
                        matches.Count + "; vanilla free-time behavior is unchanged.");
                }
                return codes;
            }

            for (int i = 0; i < matches.Count; i++)
            {
                codes[matches[i]].opcode = OpCodes.Call;
                codes[matches[i]].operand = replacement;
            }

            return codes;
        }
    }

    [HarmonyPatch]
    internal static class HospitalizedWakeUpStaggerPatch
    {
        private static MethodBase TargetMethod()
        {
            MethodInfo method = AccessTools.Method(
                typeof(HospitalizationComponent),
                "UpdateStateSleeping",
                new Type[] { typeof(float) });

            if (method == null)
            {
                throw new MissingMethodException(
                    "HospitalizationComponent.UpdateStateSleeping(float) was not found.");
            }

            return method;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(
                typeof(HospitalizedSleepStaggerRules),
                "IsOpenForStaffForWakeUp",
                new Type[]
                {
                    typeof(DayTime),
                    typeof(HospitalizationComponent)
                });

            return HospitalizedSleepStaggerRules.ReplaceIsOpenForStaffCall(
                instructions,
                1,
                0,
                replacement,
                "Hospitalized wake-up stagger");
        }
    }
}
