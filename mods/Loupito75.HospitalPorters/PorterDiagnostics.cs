namespace HospitalPorters
{
    internal static class PorterDiagnostics
    {
        internal static void Log(string message)
        {
            if (!PorterTransportConfig.EnableDiagnostics ||
                string.IsNullOrEmpty(message))
            {
                return;
            }

            Plugin.Log?.LogInfo("[diagnostic] " + message);
        }
    }
}
