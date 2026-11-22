using System;
using System.Collections.Generic;
using Lopital;

namespace HospitalFlowOptimizer
{
    public static class HospitalFlowOptimizerApi
    {
        public const int ApiVersion = 1;

        public static bool TrySetExternalLabProcedureManagedState(
            LabProcedure procedure,
            string sourceId,
            bool managed)
        {
            return ExternalLabProcedureManagedStateRuntime.TrySet(
                procedure,
                sourceId,
                managed);
        }
    }

    internal static class ExternalLabProcedureManagedStateRuntime
    {
        private static readonly object SyncRoot = new object();

        private static readonly Dictionary<LabProcedure, HashSet<string>>
            ManagedSources =
                new Dictionary<LabProcedure, HashSet<string>>();

        private static volatile bool HasRegistrations;

        internal static bool TrySet(
            LabProcedure procedure,
            string sourceId,
            bool managed)
        {
            if (procedure == null)
            {
                return false;
            }

            string normalizedSourceId =
                sourceId == null
                    ? string.Empty
                    : sourceId.Trim();

            if (normalizedSourceId.Length == 0)
            {
                return false;
            }

            lock (SyncRoot)
            {
                if (!managed)
                {
                    HashSet<string> existingSources;
                    if (ManagedSources.TryGetValue(
                            procedure,
                            out existingSources))
                    {
                        existingSources.Remove(normalizedSourceId);
                        if (existingSources.Count == 0)
                        {
                            ManagedSources.Remove(procedure);
                            HasRegistrations =
                                ManagedSources.Count > 0;
                        }
                    }

                    return true;
                }

                if (!IsProcedureValid(procedure))
                {
                    ManagedSources.Remove(procedure);
                    HasRegistrations =
                        ManagedSources.Count > 0;
                    return false;
                }

                HashSet<string> sources;
                if (!ManagedSources.TryGetValue(
                        procedure,
                        out sources))
                {
                    sources =
                        new HashSet<string>(StringComparer.Ordinal);
                    ManagedSources.Add(procedure, sources);
                }

                sources.Add(normalizedSourceId);
                HasRegistrations = true;
                return true;
            }
        }

        internal static bool IsManaged(
            LabProcedure procedure)
        {
            if (procedure == null)
            {
                return false;
            }

            lock (SyncRoot)
            {
                if (!IsProcedureValid(procedure))
                {
                    ManagedSources.Remove(procedure);
                    HasRegistrations =
                        ManagedSources.Count > 0;
                    return false;
                }

                HashSet<string> sources;
                return ManagedSources.TryGetValue(
                        procedure,
                        out sources) &&
                    sources.Count > 0;
            }
        }

        internal static void PruneInvalid()
        {
            if (!HasRegistrations)
            {
                return;
            }

            lock (SyncRoot)
            {
                if (ManagedSources.Count == 0)
                {
                    HasRegistrations = false;
                    return;
                }

                List<LabProcedure> invalid = null;

                foreach (KeyValuePair<LabProcedure, HashSet<string>> pair
                    in ManagedSources)
                {
                    if (IsProcedureValid(pair.Key) &&
                        pair.Value != null &&
                        pair.Value.Count > 0)
                    {
                        continue;
                    }

                    if (invalid == null)
                    {
                        invalid = new List<LabProcedure>();
                    }

                    invalid.Add(pair.Key);
                }

                if (invalid == null)
                {
                    return;
                }

                for (int i = 0; i < invalid.Count; i++)
                {
                    ManagedSources.Remove(invalid[i]);
                }

                HasRegistrations =
                    ManagedSources.Count > 0;
            }
        }

        internal static void Clear()
        {
            lock (SyncRoot)
            {
                ManagedSources.Clear();
                HasRegistrations = false;
            }
        }

        private static bool IsProcedureValid(
            LabProcedure procedure)
        {
            return procedure != null &&
                !procedure.DEBUG_destroyed &&
                procedure.m_state != null &&
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.Finished;
        }
    }
}
