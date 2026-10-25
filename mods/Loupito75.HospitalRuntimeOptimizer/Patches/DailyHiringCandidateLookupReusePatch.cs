using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class DailyHiringCandidateLookupReusePatch
    {
        private const int ExpectedLookupCount = 95;
        private const int ExpectedCandidateGroups = 19;
        private const int LookupsPerCandidate = 5;

        private static readonly string[] DatabasePatchOwnersToRunFirst =
        {
            "Loupito75:HospitalTrafficControl",
            "Loupito75:HospitalFlowOptimizer",
            "Loupito75:HospitalPorters"
        };

        private static Harmony s_harmony;
        private static bool s_applied;
        private static bool s_applying;
        private static bool s_failed;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            s_harmony = harmony;
            s_applied = false;
            s_applying = false;
            s_failed = false;

            MethodInfo readFiles = AccessTools.Method(
                typeof(Database),
                "ReadFiles",
                new Type[] { typeof(string), typeof(bool) });
            MethodInfo postfix = AccessTools.Method(
                typeof(DailyHiringCandidateLookupReusePatch),
                "DatabaseReadFilesPostfix");

            if (readFiles == null)
            {
                throw new MissingMethodException(
                    typeof(Database).FullName,
                    "ReadFiles(String, Boolean)");
            }
            if (postfix == null)
            {
                throw new MissingMethodException(
                    typeof(DailyHiringCandidateLookupReusePatch).FullName,
                    "DatabaseReadFilesPostfix");
            }

            HarmonyMethod deferredPostfix =
                new HarmonyMethod(postfix);
            deferredPostfix.after =
                DatabasePatchOwnersToRunFirst;

            harmony.Patch(
                readFiles,
                postfix: deferredPostfix);

            Database database = Database.Instance;
            if (database != null && database.Loaded)
            {
                TryApplyNow(database);
            }
            else
            {
                Plugin.Log.LogInfo(
                    "OptimizeDailyHiringCandidateLookups armed: HiringManager patch deferred until Database.ReadFiles completes.");
            }
        }

        internal static void Reset()
        {
            s_harmony = null;
            s_applied = false;
            s_applying = false;
            s_failed = false;
        }

        private static void DatabaseReadFilesPostfix(
            Database __instance,
            bool add)
        {
            if (add || s_applied || s_failed)
            {
                return;
            }

            TryApplyNow(__instance);
        }

        private static void TryApplyNow(
            Database database)
        {
            if (s_harmony == null ||
                s_applied ||
                s_applying ||
                s_failed ||
                database == null ||
                !database.Loaded)
            {
                return;
            }

            s_applying = true;
            MethodInfo target = null;

            try
            {
                // HiringManager owns a static singleton whose constructor
                // calls Reset(), which reads Database entries. Do not touch
                // the type until the complete base/mod Database load has
                // finished.
                HiringManager nativeManager =
                    HiringManager.Instance;
                if (nativeManager == null)
                {
                    throw new InvalidOperationException(
                        "HiringManager.Instance returned null after Database.Loaded.");
                }

                target = AccessTools.Method(
                    typeof(HiringManager),
                    "GenerateAvailableCharacters",
                    new Type[]
                    {
                        typeof(AvailableCharacters),
                        typeof(GameDBDepartment),
                        typeof(LopitalTypes),
                        typeof(bool)
                    });
                MethodInfo transpiler = AccessTools.Method(
                    typeof(DailyHiringCandidateLookupReusePatch),
                    "Transpiler");

                if (target == null)
                {
                    throw new MissingMethodException(
                        typeof(HiringManager).FullName,
                        "GenerateAvailableCharacters(AvailableCharacters, GameDBDepartment, LopitalTypes, Boolean)");
                }
                if (transpiler == null)
                {
                    throw new MissingMethodException(
                        typeof(DailyHiringCandidateLookupReusePatch).FullName,
                        "Transpiler");
                }

                s_harmony.Patch(
                    target,
                    transpiler: new HarmonyMethod(transpiler));

                s_applied = true;
                Plugin.Log.LogInfo(
                    "OptimizeDailyHiringCandidateLookups installed after Database.Loaded: EmployeeComponent initialization lookups are reused for each generated hiring candidate.");
            }
            catch (Exception exception)
            {
                s_failed = true;

                if (target != null && s_harmony != null)
                {
                    try
                    {
                        s_harmony.Unpatch(
                            target,
                            HarmonyPatchType.Transpiler,
                            Plugin.HarmonyId);
                    }
                    catch
                    {
                    }
                }

                Plugin.Log.LogError(
                    "OptimizeDailyHiringCandidateLookups could not be installed after Database.Loaded; only this feature is disabled for this session: " +
                    exception);
            }
            finally
            {
                s_applying = false;
            }
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions,
            ILGenerator generator)
        {
            if (generator == null)
            {
                throw new ArgumentNullException("generator");
            }

            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);
            MethodInfo employeeGetter =
                GetEmployeeComponentGetter();

            if (employeeGetter == null)
            {
                throw new MissingMethodException(
                    typeof(Entity).FullName,
                    "GetComponent<EmployeeComponent>()");
            }

            List<int> calls = new List<int>();
            for (int i = 0; i < codes.Count; i++)
            {
                if (!codes[i].Calls(employeeGetter))
                {
                    continue;
                }

                if (i < 1 || !codes[i - 1].IsLdloc())
                {
                    throw new InvalidOperationException(
                        "Unexpected EmployeeComponent receiver in HiringManager.GenerateAvailableCharacters().");
                }

                calls.Add(i);
            }

            if (calls.Count != ExpectedLookupCount)
            {
                throw new InvalidOperationException(
                    "Expected " + ExpectedLookupCount +
                    " EmployeeComponent lookups in HiringManager.GenerateAvailableCharacters(), found " +
                    calls.Count + ".");
            }

            LocalBuilder employeeLocal =
                generator.DeclareLocal(typeof(EmployeeComponent));

            for (int group = 0;
                group < ExpectedCandidateGroups;
                group++)
            {
                int groupStart =
                    group * LookupsPerCandidate;
                CodeInstruction firstReceiver =
                    codes[calls[groupStart] - 1];

                for (int offset = 1;
                    offset < LookupsPerCandidate;
                    offset++)
                {
                    int callIndex =
                        calls[groupStart + offset];
                    CodeInstruction receiver =
                        codes[callIndex - 1];

                    if (!SameLocalLoad(
                        firstReceiver,
                        receiver))
                    {
                        throw new InvalidOperationException(
                            "Hiring candidate EmployeeComponent lookup group " +
                            group +
                            " does not use one Entity local.");
                    }

                    if (receiver.labels.Count != 0 ||
                        codes[callIndex].labels.Count != 0 ||
                        receiver.blocks.Count != 0 ||
                        codes[callIndex].blocks.Count != 0)
                    {
                        throw new InvalidOperationException(
                            "Hiring candidate lookup group " +
                            group +
                            " has an unexpected branch/exception entry point.");
                    }

                    receiver.opcode = OpCodes.Nop;
                    receiver.operand = null;

                    codes[callIndex].opcode =
                        OpCodes.Ldloc;
                    codes[callIndex].operand =
                        employeeLocal;
                }
            }

            // Insert from the end so the original call indexes for earlier
            // candidate groups remain valid.
            for (int group =
                ExpectedCandidateGroups - 1;
                group >= 0;
                group--)
            {
                int firstCall =
                    calls[group * LookupsPerCandidate];

                codes.Insert(
                    firstCall + 1,
                    new CodeInstruction(OpCodes.Dup));
                codes.Insert(
                    firstCall + 2,
                    new CodeInstruction(
                        OpCodes.Stloc,
                        employeeLocal));
            }

            return codes;
        }

        private static bool SameLocalLoad(
            CodeInstruction left,
            CodeInstruction right)
        {
            return left.opcode == right.opcode &&
                object.Equals(left.operand, right.operand);
        }

        private static MethodInfo GetEmployeeComponentGetter()
        {
            MethodInfo[] methods = typeof(Entity).GetMethods(
                BindingFlags.Instance |
                BindingFlags.Public);

            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method.Name != "GetComponent" ||
                    !method.IsGenericMethodDefinition ||
                    method.GetGenericArguments().Length != 1 ||
                    method.GetParameters().Length != 0)
                {
                    continue;
                }

                return method.MakeGenericMethod(
                    typeof(EmployeeComponent));
            }

            return null;
        }
    }
}
