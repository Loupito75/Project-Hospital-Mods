using System;
using System.Collections.Generic;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(
        typeof(LabProcedureManager),
        nameof(LabProcedureManager.GetIdleLabProcedures),
        new Type[]
        {
            typeof(Department),
            typeof(Room),
            typeof(bool),
            typeof(bool)
        })]
    internal static class LabProcedureStatLabConsistencyPatch
    {
        [HarmonyPostfix]
        private static void Postfix(
            Room statLab,
            ref List<LabProcedure> __result)
        {
            if (!FixConfig.FixLabProcedureStatLabConsistency ||
                statLab == null ||
                __result == null ||
                __result.Count == 0)
            {
                return;
            }

            for (int i = __result.Count - 1; i >= 0; i--)
            {
                LabProcedure procedure = __result[i];

                if (!IsHospitalized(procedure))
                {
                    continue;
                }

                Room assignedRoom = GetAssignedRoom(procedure);

                if (assignedRoom == null &&
                    procedure.m_state.m_labProcedureState ==
                        LabProcedureState.SamplingFinished)
                {
                    // A hospitalized sample can legitimately outlive the
                    // specialist that existed when it was created. Let the
                    // native selector choose a new valid primary/fallback lab
                    // before enforcing physical-room ownership.
                    if (procedure.m_state.m_statLab != null)
                    {
                        procedure.m_state.m_statLab = null;
                    }

                    procedure.FindRoom(onlyFree: false);
                    assignedRoom = GetAssignedRoom(procedure);
                }

                if (assignedRoom != statLab)
                {
                    __result.RemoveAt(i);
                }
            }
        }

        private static bool IsHospitalized(
            LabProcedure procedure)
        {
            return procedure != null &&
                procedure.m_state != null &&
                procedure.m_state.m_patient != null &&
                procedure.m_state.m_patient.GetEntity() != null &&
                procedure.m_state.m_patient.GetEntity()
                    .GetComponent<HospitalizationComponent>()
                    .IsHospitalized();
        }

        private static Room GetAssignedRoom(
            LabProcedure procedure)
        {
            if (procedure == null ||
                procedure.m_state == null ||
                procedure.m_state.m_statLab == null)
            {
                return null;
            }

            return procedure.m_state.m_statLab.GetEntity();
        }
    }
}
