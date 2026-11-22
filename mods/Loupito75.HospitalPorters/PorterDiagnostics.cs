namespace HospitalPorters
{
    internal static class PorterDiagnostics
    {
        internal static bool Enabled
        {
            get { return PorterTransportConfig.EnableDiagnostics; }
        }

        internal static void Log(string message)
        {
            if (!Enabled ||
                string.IsNullOrEmpty(message))
            {
                return;
            }

            Plugin.Log?.LogInfo("[diagnostic] " + message);
        }
    }
}
