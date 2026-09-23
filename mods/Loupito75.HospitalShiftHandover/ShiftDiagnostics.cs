using Lopital;

namespace HospitalShiftHandover
{
    // Release no-op surface kept so the validated gameplay pipeline does not need to be
    // rewritten solely to remove development diagnostics. No Harmony patches are attached.
    internal static class ShiftDiagnostics
    {
        internal static void RecordCommuteTrigger(EmployeeComponent employee, bool modAdvanced)
        {
        }

        internal static void RecordCommonAreaRoute(Behavior behavior, EmployeeComponent employee)
        {
        }

        internal static void RecordWorkplaceDispatch(
            Behavior behavior,
            EmployeeComponent employee,
            float routeEstimateMinutes)
        {
        }
    }
}
