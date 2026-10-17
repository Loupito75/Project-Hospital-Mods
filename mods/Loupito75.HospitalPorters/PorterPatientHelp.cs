using System;
using GLib;
using Lopital;

namespace HospitalPorters
{
    // Patient assignment remains owned by HPO. This is a stable preference,
    // not an RNG call: repeating the same assignment scan never rerolls it.
    internal static class PorterPatientHelp
    {
        private const uint RollSalt = 2949723187u;
        private const uint PersonalitySalt = 2416121981u;
        private const uint MissionSalt = 3474513529u;
        private const int PersonalityRange = 10;
        private const int MissionRange = 5;
        private const int DistancePenaltyDivisor = 12;
        private const int DistancePenaltyCap = 20;

        internal static bool ShouldAccept(
            Entity porter,
            Entity patient,
            TileObject destination,
            Vehicle requestedVehicle,
            float staffRouteDistance,
            bool hasWaited)
        {
            if (!PorterTransportConfig.PatientCrossDepartmentHelp ||
                porter == null ||
                patient == null ||
                staffRouteDistance < 0f)
            {
                return false;
            }

            EmployeeComponent employee =
                porter.GetComponent<EmployeeComponent>();
            WalkComponent porterWalk =
                porter.GetComponent<WalkComponent>();
            WalkComponent patientWalk =
                patient.GetComponent<WalkComponent>();
            Room homeRoom =
                employee == null ||
                employee.m_state == null ||
                employee.m_state.m_homeRoom == null
                    ? null
                    : employee.m_state.m_homeRoom.GetEntity();
            if (porterWalk == null || patientWalk == null)
            {
                return false;
            }

            int chance =
                PorterTransportConfig.PatientCrossDepartmentHelpBaseChance;
            if (homeRoom != null &&
                homeRoom.GetFloorIndex() == patientWalk.GetFloorIndex())
            {
                chance +=
                    PorterTransportConfig.PatientCrossDepartmentStationFloorBonus;
            }
            if (porterWalk.GetFloorIndex() ==
                patientWalk.GetFloorIndex())
            {
                chance +=
                    PorterTransportConfig.PatientCrossDepartmentCurrentFloorBonus;
            }
            if (hasWaited)
            {
                chance +=
                    PorterTransportConfig.PatientCrossDepartmentWaitingBonus;
            }

            int distancePenalty =
                (int)(staffRouteDistance / DistancePenaltyDivisor);
            chance -= Math.Min(DistancePenaltyCap, distancePenalty);

            uint porterId = porter.GetEntityID();
            uint requestHash = 2166136261u;
            requestHash = HashStep(requestHash, patient.GetEntityID());
            requestHash = HashStep(
                requestHash,
                destination == null ? 0u : destination.GetEntityID());
            requestHash = HashStep(requestHash, (uint)requestedVehicle);

            chance += SignedOffset(
                porterId,
                PersonalitySalt,
                PersonalityRange);
            chance += SignedOffset(
                requestHash,
                MissionSalt,
                MissionRange);
            chance = Math.Max(15, Math.Min(100, chance));

            uint rollHash = 2166136261u;
            rollHash = HashStep(rollHash, porterId);
            rollHash = HashStep(rollHash, requestHash);
            rollHash = HashStep(rollHash, RollSalt);
            int stableRoll = (int)(rollHash % 100u);
            return stableRoll < chance;
        }

        private static int SignedOffset(
            uint id,
            uint salt,
            int range)
        {
            uint hash = HashStep(
                HashStep(2166136261u, id),
                salt);
            return (int)(hash % (uint)(2 * range + 1)) - range;
        }

        private static uint HashStep(uint hash, uint value)
        {
            unchecked
            {
                return (hash ^ value) * 16777619u;
            }
        }
    }
}
