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
    }
}
