using System;
using System.Collections.Generic;
using Lopital;

namespace HospitalPorters
{
    internal static class ExternalSamplePickupRuntime
    {
        private static readonly
            Dictionary<LabProcedure, HashSet<string>>
                Sources =
                    new Dictionary<LabProcedure, HashSet<string>>();

        internal static bool TrySetState(
            LabProcedure procedure,
            string sourceId,
            bool available)
        {
            if (procedure == null)
            {
                return false;
            }

            string normalizedSourceId =
                NormalizeSourceId(sourceId);
            if (normalizedSourceId == null)
            {
                return false;
            }

            if (!available)
            {
                HashSet<string> existing;
                if (!Sources.TryGetValue(
                        procedure,
                        out existing))
                {
                    return true;
                }

                existing.Remove(normalizedSourceId);
                if (existing.Count == 0)
                {
                    Sources.Remove(procedure);
                }

                return true;
            }

            if (!IsStructurallyAvailable(procedure))
            {
                Sources.Remove(procedure);
                return false;
            }

            HashSet<string> sourceIds;
            if (!Sources.TryGetValue(
                    procedure,
                    out sourceIds))
            {
                sourceIds =
                    new HashSet<string>(
                        StringComparer.Ordinal);
                Sources[procedure] = sourceIds;
            }

            sourceIds.Add(normalizedSourceId);
            return true;
        }

        internal static bool IsAvailable(
            LabProcedure procedure)
        {
            if (procedure == null ||
                Sources.Count == 0)
            {
                return false;
            }

            HashSet<string> sourceIds;
            if (!Sources.TryGetValue(
                    procedure,
                    out sourceIds) ||
                sourceIds == null ||
                sourceIds.Count == 0)
            {
                return false;
            }

            if (!IsStructurallyAvailable(procedure))
            {
                Sources.Remove(procedure);
                return false;
            }

            return true;
        }

        internal static void CleanupIfInvalid(
            LabProcedure procedure)
        {
            if (procedure == null ||
                Sources.Count == 0 ||
                !Sources.ContainsKey(procedure))
            {
                return;
            }

            if (!IsStructurallyAvailable(procedure))
            {
                Sources.Remove(procedure);
            }
        }

        internal static void Reset()
        {
            Sources.Clear();
        }

        private static string NormalizeSourceId(
            string sourceId)
        {
            if (sourceId == null)
            {
                return null;
            }

            string normalized = sourceId.Trim();
            return normalized.Length == 0
                ? null
                : normalized;
        }

        private static bool IsStructurallyAvailable(
            LabProcedure procedure)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_labProcedureState !=
                    LabProcedureState.SamplingFinished ||
                procedure.m_state.m_usedEquipment == null ||
                procedure.m_state.m_usedEquipment.GetEntity() == null ||
                procedure.m_state.m_patient == null ||
                procedure.m_state.m_patient.GetEntity() == null)
            {
                return false;
            }

            return true;
        }
    }
}
