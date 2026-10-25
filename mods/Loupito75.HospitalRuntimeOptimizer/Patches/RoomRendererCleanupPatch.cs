using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Lopital;

namespace HospitalRuntimeOptimizer
{
    internal static class RoomRendererCleanupPatch
    {
        [ThreadStatic]
        private static List<Room> sm_removedRooms;

        internal static void Install(Harmony harmony)
        {
            if (harmony == null)
            {
                throw new ArgumentNullException("harmony");
            }

            MethodInfo renderRooms = AccessTools.Method(
                typeof(RoomRenderer),
                "RenderRooms",
                new Type[]
                {
                    typeof(Floor),
                    typeof(FloorRenderData),
                    typeof(LightMap2DUnity),
                    typeof(float)
                });
            MethodInfo transpiler = AccessTools.Method(
                typeof(RoomRendererCleanupPatch),
                "Transpiler");
            MethodInfo destroy = AccessTools.Method(
                typeof(RoomRenderer),
                "Destroy",
                Type.EmptyTypes);
            MethodInfo resetPostfix = AccessTools.Method(
                typeof(RoomRendererCleanupPatch),
                "ResetPostfix");

            if (renderRooms == null)
            {
                throw new MissingMethodException(
                    typeof(RoomRenderer).FullName,
                    "RenderRooms(Floor, FloorRenderData, LightMap2DUnity, Single)");
            }
            if (destroy == null)
            {
                throw new MissingMethodException(
                    typeof(RoomRenderer).FullName,
                    "Destroy()");
            }
            if (transpiler == null || resetPostfix == null)
            {
                throw new MissingMethodException(
                    typeof(RoomRendererCleanupPatch).FullName,
                    "HRO RoomRenderer cleanup patch method");
            }

            harmony.Patch(
                renderRooms,
                transpiler: new HarmonyMethod(transpiler));
            harmony.Patch(
                destroy,
                postfix: new HarmonyMethod(resetPostfix));

            Plugin.Log.LogInfo(
                "OptimizeRoomRendererCleanup installed: RoomRenderer reuses its temporary removed-room list.");
        }

        internal static void Reset()
        {
            if (sm_removedRooms != null)
            {
                sm_removedRooms.Clear();
            }
        }

        private static void ResetPostfix()
        {
            Reset();
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            ConstructorInfo listConstructor = AccessTools.Constructor(
                typeof(List<Room>),
                Type.EmptyTypes);
            MethodInfo replacement = AccessTools.Method(
                typeof(RoomRendererCleanupPatch),
                "GetRemovalBuffer");

            if (listConstructor == null)
            {
                throw new MissingMethodException(
                    typeof(List<Room>).FullName,
                    ".ctor()");
            }
            if (replacement == null)
            {
                throw new MissingMethodException(
                    typeof(RoomRendererCleanupPatch).FullName,
                    "GetRemovalBuffer");
            }

            List<CodeInstruction> codes =
                new List<CodeInstruction>(instructions);
            int replacements = 0;

            for (int i = 0; i < codes.Count; i++)
            {
                CodeInstruction instruction = codes[i];
                if (instruction.opcode != OpCodes.Newobj ||
                    !listConstructor.Equals(instruction.operand))
                {
                    continue;
                }

                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                replacements++;
            }

            if (replacements != 1)
            {
                throw new InvalidOperationException(
                    "Expected exactly one List<Room>() allocation in " +
                    "RoomRenderer.RenderRooms(), found " +
                    replacements + ".");
            }

            return codes;
        }

        private static List<Room> GetRemovalBuffer()
        {
            if (sm_removedRooms == null)
            {
                sm_removedRooms = new List<Room>();
            }
            else
            {
                sm_removedRooms.Clear();
            }

            return sm_removedRooms;
        }
    }
}
