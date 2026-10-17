using System.Collections.Generic;
using System.Globalization;
using GLib;
using Lopital;

namespace HospitalFlowOptimizer
{
    internal static class HfoDiagnostics
    {
        private const int MaximumRememberedStates = 4096;

        private static readonly Dictionary<string, string> LastMessages =
            new Dictionary<string, string>();

        internal static void Log(string area, string message)
        {
            if (!FlowOptimizerConfig.DoctorDiagnosticLogging || Plugin.Log == null)
            {
                return;
            }

            Plugin.Log.LogInfo("[Diagnostic][" + area + "] " + message);
        }

        internal static void LogState(
            string area,
            string key,
            string message)
        {
            if (!FlowOptimizerConfig.DoctorDiagnosticLogging || Plugin.Log == null)
            {
                return;
            }

            if (LastMessages.Count >= MaximumRememberedStates)
            {
                LastMessages.Clear();
            }

            string fullKey = area + ":" + (key ?? string.Empty);
            string previous;

            if (LastMessages.TryGetValue(fullKey, out previous) &&
                previous == message)
            {
                return;
            }

            LastMessages[fullKey] = message;
            Plugin.Log.LogInfo("[Diagnostic][" + area + "] " + message);
        }

        internal static void LogStateChange(
            string area,
            string key,
            string state,
            string message)
        {
            if (!FlowOptimizerConfig.DoctorDiagnosticLogging || Plugin.Log == null)
            {
                return;
            }

            if (LastMessages.Count >= MaximumRememberedStates)
            {
                LastMessages.Clear();
            }

            string fullKey = "state:" + area + ":" + (key ?? string.Empty);
            string previous;

            if (LastMessages.TryGetValue(fullKey, out previous) &&
                previous == state)
            {
                return;
            }

            LastMessages[fullKey] = state ?? string.Empty;
            Plugin.Log.LogInfo("[Diagnostic][" + area + "] " + message);
        }

        internal static string EntityName(Entity entity)
        {
            if (entity == null)
            {
                return "<null>";
            }

            string name = entity.Name;
            return string.IsNullOrEmpty(name) ? "<unnamed>" : name.Trim();
        }

        internal static string EntityKey(Entity entity)
        {
            return ObjectKey(entity);
        }

        internal static string ObjectKey(object value)
        {
            return value == null
                ? "null"
                : value.GetHashCode().ToString(CultureInfo.InvariantCulture);
        }

        internal static string DepartmentId(Department department)
        {
            if (department == null)
            {
                return "<null>";
            }

            GameDBDepartment departmentType = department.GetDepartmentType();
            if (departmentType == null)
            {
                return "<unknown>";
            }

            return departmentType.DatabaseID.ToString();
        }

        internal static int DoctorLevel(Entity doctor)
        {
            if (doctor == null)
            {
                return -1;
            }

            EmployeeComponent employee =
                doctor.GetComponent<EmployeeComponent>();

            return employee == null || employee.m_state == null
                ? -1
                : employee.m_state.m_level;
        }


        internal static string Number(float value)
        {
            return value.ToString("0.000", CultureInfo.InvariantCulture);
        }

        internal static void Clear()
        {
            LastMessages.Clear();
        }
    }
}
