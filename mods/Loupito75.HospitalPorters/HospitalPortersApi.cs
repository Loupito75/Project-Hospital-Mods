using GLib;
using Lopital;

namespace HospitalPorters
{
    public static class HospitalPortersApi
    {
        public static int GetApiVersion()
        {
            return 1;
        }

        // Patient continuation is available during the normal HPO return.
        // No waiting period or activation toggle is required.
        // The existing Sample v1 contract is unchanged.
        public static int GetPatientContinuationApiVersion()
        {
            return 2;
        }

        public static int GetSampleContinuationApiVersion()
        {
            return 2;
        }

        public static bool IsPorter(Entity entity)
        {
            return PorterIdentity.IsPorter(entity);
        }

        public static bool IsPatientContinuationWindowOpen(Entity porter)
        {
            return PorterPatientContinuationRuntime.IsWindowOpen(porter);
        }

        public static Entity[] GetPatientContinuationCandidates(Entity porter)
        {
            return PorterPatientContinuationRuntime.GetCandidates(porter);
        }

        public static Entity[] GetPatientContinuationPreflightCandidates(
            Entity porter,
            Entity sourcePatient)
        {
            return PorterPatientContinuationRuntime.GetPreflightCandidates(
                porter,
                sourcePatient);
        }

        public static bool TryStartPatientContinuation(
            Entity porter, Entity targetPatient)
        {
            return PorterPatientContinuationRuntime.TryStart(
                porter, targetPatient);
        }

        public static bool FinishPatientContinuationWindow(Entity porter)
        {
            return PorterPatientContinuationRuntime.FinishWindow(porter);
        }

        public static bool IsSampleContinuationWindowOpen(
            Entity porter)
        {
            return PorterSampleTransportRuntime
                .IsSampleContinuationWindowOpen(porter);
        }

        public static LabProcedure[]
            GetSampleContinuationCandidates(
                Entity porter)
        {
            return PorterSampleTransportRuntime
                .GetSampleContinuationCandidates(porter);
        }

        public static bool TryStartSampleContinuation(
            Entity porter,
            LabProcedure procedure)
        {
            return PorterSampleTransportRuntime
                .TryStartSampleContinuation(
                    porter,
                    procedure);
        }

        public static bool FinishSampleContinuationWindow(
            Entity porter)
        {
            return PorterSampleTransportRuntime
                .FinishSampleContinuationWindow(porter);
        }
    }
}
