using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    internal static class PorterSamplePersistence
    {
        private const string ScriptName = "GlobalScript";
        private const string StateHeader =
            "HPO_PORTER_SAMPLE_TRANSPORT_V2";

        internal static void AppendToSave(GameSave save)
        {
            if (save == null)
            {
                return;
            }

            List<KeyValuePair<Entity, PorterSampleTransportJob>>
                jobs =
                    PorterSampleTransportRuntime
                        .GetActiveJobsSnapshot();
            if (jobs == null || jobs.Count == 0)
            {
                return;
            }

            if (save.m_entityListEvents == null)
            {
                save.m_entityListEvents =
                    new EntityListSave();
            }

            GlobalScriptPersistentData data =
                new GlobalScriptPersistentData();
            data.m_scriptName = ScriptName;
            data.m_state = Serialize(jobs);

            save.m_entityListEvents.m_entities.Add(
                new EntitySave(data));

            PorterDiagnostics.Log(
                "save: activeSampleJobs=" +
                jobs.Count +
                ".");
        }

        internal static bool TryRestoreMarker(
            EntitySave entitySave)
        {
            GlobalScriptPersistentData data =
                entitySave == null
                    ? null
                    : entitySave.m_persistentData
                        as GlobalScriptPersistentData;
            if (data == null ||
                data.m_scriptName != ScriptName ||
                string.IsNullOrEmpty(data.m_state) ||
                !data.m_state.StartsWith(
                    StateHeader,
                    StringComparison.Ordinal))
            {
                return false;
            }

            Restore(data.m_state);
            return true;
        }

        private static string Serialize(
            List<KeyValuePair<Entity, PorterSampleTransportJob>>
                jobs)
        {
            StringBuilder builder =
                new StringBuilder();
            builder.Append(StateHeader);
            builder.Append('\n');

            for (int i = 0; i < jobs.Count; i++)
            {
                Entity porter = jobs[i].Key;
                PorterSampleTransportJob job =
                    jobs[i].Value;
                if (porter == null ||
                    job == null ||
                    job.Cart == null ||
                    job.StatLab == null ||
                    job.CartHomeRoom == null)
                {
                    continue;
                }

                builder.Append(
                    porter.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.Cart.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.Procedure == null
                        ? 0u
                        : job.Procedure.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.StatLab.GetEntityID());
                builder.Append('|');
                builder.Append(
                    job.CartHomeRoom.GetEntityID());
                builder.Append('|');
                builder.Append((int)job.Phase);
                builder.Append('|');
                builder.Append(
                    job.CartHomeTile.m_x);
                builder.Append('|');
                builder.Append(
                    job.CartHomeTile.m_y);
                builder.Append('|');
                builder.Append(
                    job.CartHomeFloorIndex);
                builder.Append('|');
                builder.Append(
                    (int)job.CartHomeOrientation);
                builder.Append('|');
                builder.Append(
                    job.NativeTimeout ? 1 : 0);
                builder.Append('|');

                bool firstCollected = true;
                for (int procedureIndex = 0;
                    procedureIndex <
                        job.CollectedProcedures.Count;
                    procedureIndex++)
                {
                    LabProcedure procedure =
                        job.CollectedProcedures[
                            procedureIndex];
                    if (procedure == null)
                    {
                        continue;
                    }

                    if (!firstCollected)
                    {
                        builder.Append(',');
                    }
                    builder.Append(
                        procedure.GetEntityID());
                    firstCollected = false;
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }

        private static void Restore(string serialized)
        {
            string[] lines =
                serialized.Split(
                    new char[] { '\n' },
                    StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0 ||
                lines[0] != StateHeader)
            {
                return;
            }

            HashSet<int> homeFloors =
                new HashSet<int>();
            int restored = 0;

            for (int i = 1; i < lines.Length; i++)
            {
                PorterSampleTransportJob job;
                Entity porter;
                if (!TryParseJob(
                        lines[i],
                        out porter,
                        out job))
                {
                    Plugin.Log?.LogWarning(
                        "Skipped invalid persisted Porter sample transport entry.");
                    continue;
                }

                if (!PorterSampleTransportRuntime
                        .RestorePersistedJob(
                            porter,
                            job))
                {
                    Plugin.Log?.LogWarning(
                        "Could not restore persisted Porter sample transport for entity " +
                        (porter == null
                            ? "UNKNOWN"
                            : porter.GetEntityID().ToString()) +
                        ".");
                    continue;
                }

                homeFloors.Add(
                    job.CartHomeFloorIndex);
                restored++;
            }

            if (Hospital.Instance != null &&
                Hospital.Instance.m_floors != null)
            {
                foreach (int floorIndex in homeFloors)
                {
                    if (floorIndex >= 0 &&
                        floorIndex <
                            Hospital.Instance.m_floors.Count)
                    {
                        Hospital.Instance
                            .m_floors[floorIndex]
                            .ValidateRooms();
                    }
                }
            }

            PorterDiagnostics.Log(
                "load: restoredSampleJobs=" +
                restored +
                ".");
        }

        private static bool TryParseJob(
            string line,
            out Entity porter,
            out PorterSampleTransportJob job)
        {
            porter = null;
            job = null;

            string[] parts =
                line.Split(
                    new char[] { '|' },
                    StringSplitOptions.None);
            if (parts.Length != 12)
            {
                return false;
            }

            uint porterId;
            uint cartId;
            uint procedureId;
            uint statLabId;
            uint homeRoomId;
            int phaseValue;
            int homeX;
            int homeY;
            int homeFloor;
            int homeOrientation;
            int nativeTimeout;

            if (!uint.TryParse(parts[0], out porterId) ||
                !uint.TryParse(parts[1], out cartId) ||
                !uint.TryParse(parts[2], out procedureId) ||
                !uint.TryParse(parts[3], out statLabId) ||
                !uint.TryParse(parts[4], out homeRoomId) ||
                !int.TryParse(parts[5], out phaseValue) ||
                !int.TryParse(parts[6], out homeX) ||
                !int.TryParse(parts[7], out homeY) ||
                !int.TryParse(parts[8], out homeFloor) ||
                !int.TryParse(parts[9], out homeOrientation) ||
                !int.TryParse(parts[10], out nativeTimeout) ||
                phaseValue <
                    (int)PorterSampleTransportPhase.GoingToCart ||
                phaseValue >
                    (int)PorterSampleTransportPhase.ReturningCart)
            {
                return false;
            }

            EntityManager manager =
                EntityManager.GetInstance();
            porter =
                manager.GetEntity(porterId);
            TileObject cart =
                manager.GetEntity(cartId)
                    as TileObject;
            LabProcedure procedure =
                procedureId == 0u
                    ? null
                    : manager.GetEntity(procedureId)
                        as LabProcedure;
            Room statLab =
                manager.GetEntity(statLabId)
                    as Room;
            Room homeRoom =
                manager.GetEntity(homeRoomId)
                    as Room;

            if (porter == null ||
                cart == null ||
                statLab == null ||
                homeRoom == null)
            {
                return false;
            }

            job =
                new PorterSampleTransportJob
                {
                    Procedure = procedure,
                    StatLab = statLab,
                    Cart = cart,
                    CartHomeRoom = homeRoom,
                    CartHomeTile =
                        new Vector2i(homeX, homeY),
                    CartHomeFloorIndex =
                        homeFloor,
                    CartHomeOrientation =
                        (Direction)homeOrientation,
                    NativeTimeout =
                        nativeTimeout != 0,
                    Phase =
                        (PorterSampleTransportPhase)
                            phaseValue
                };

            if (!string.IsNullOrEmpty(parts[11]))
            {
                string[] collectedIds =
                    parts[11].Split(
                        new char[] { ',' },
                        StringSplitOptions.RemoveEmptyEntries);
                for (int i = 0;
                    i < collectedIds.Length;
                    i++)
                {
                    uint collectedId;
                    if (!uint.TryParse(
                            collectedIds[i],
                            out collectedId))
                    {
                        continue;
                    }

                    LabProcedure collected =
                        manager.GetEntity(
                            collectedId)
                            as LabProcedure;
                    if (collected != null &&
                        !job.CollectedProcedures.Contains(
                            collected))
                    {
                        job.CollectedProcedures.Add(
                            collected);
                    }
                }
            }

            return true;
        }
    }

    [HarmonyPatch(
        typeof(InGameMenuController),
        nameof(InGameMenuController.Load),
        new Type[]
        {
            typeof(string),
            typeof(string)
        })]
    internal static class PorterRuntimeLoadResetPatch
    {
        private static void Prefix()
        {
            PorterSampleTransportRuntime.Reset();
            PorterIdleWorkstationVisuals.Reset();
            StretcherDestinationPatch.ResetFallbackTracking();
        }
    }

    [HarmonyPatch(
        typeof(InGameMenuController),
        nameof(InGameMenuController.CheckRoomObjectDepartments),
        new Type[] { })]
    internal static class PorterSamplePersistenceDepartmentRestorePatch
    {
        private static void Postfix()
        {
            PorterSampleTransportRuntime
                .RestoreActiveCartDepartments();
        }
    }

    [HarmonyPatch]
    internal static class PorterSamplePersistenceSavePatch
    {
        private static MethodBase TargetMethod()
        {
            return AccessTools.Constructor(
                typeof(GameSave),
                new Type[] { typeof(Hospital) });
        }

        private static void Postfix(GameSave __instance)
        {
            PorterSamplePersistence.AppendToSave(
                __instance);
        }
    }

    [HarmonyPatch(
        typeof(LopitalEntityFactory),
        nameof(LopitalEntityFactory.LoadEntity),
        new Type[]
        {
            typeof(EntitySave),
            typeof(Floor)
        })]
    internal static class PorterSamplePersistenceLoadPatch
    {
        private static bool Prefix(
            EntitySave entitySave,
            ref Entity __result)
        {
            if (!PorterSamplePersistence.TryRestoreMarker(
                    entitySave))
            {
                return true;
            }

            __result = null;
            return false;
        }

        private static void Postfix(Entity __result)
        {
            if (PorterIdentity.IsPorter(__result))
            {
                PorterIdentity.RefreshPorterName(__result);
            }
        }
    }
}
