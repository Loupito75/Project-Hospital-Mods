namespace HospitalPatientLife
{
    internal static class PatientLeisureConfig
    {
        internal static int RoomRestActivityWeight
        {
            get { return HospitalPatientLifeConfig.RoomRestActivityWeight; }
        }

        internal static int RoomEducationActivityWeight
        {
            get { return HospitalPatientLifeConfig.RoomEducationActivityWeight; }
        }

        internal static int RoomVisualActivityWeight
        {
            get { return HospitalPatientLifeConfig.RoomVisualActivityWeight; }
        }

        internal static int RoomTelevisionActivityWeight
        {
            get { return HospitalPatientLifeConfig.RoomTelevisionActivityWeight; }
        }

        internal static int LyingRestActivityWeight
        {
            get { return HospitalPatientLifeConfig.LyingRestActivityWeight; }
        }

        internal static int LyingNurseBookActivityWeight
        {
            get { return HospitalPatientLifeConfig.LyingNurseBookActivityWeight; }
        }

        internal static int LyingTelevisionActivityWeight
        {
            get { return HospitalPatientLifeConfig.LyingTelevisionActivityWeight; }
        }

        internal static bool AllowYellowLeisureObjectInteraction
        {
            get
            {
                return HospitalPatientLifeConfig.AllowYellowLeisureObjectInteraction;
            }
        }

        internal static void EnsureLoaded()
        {
            HospitalPatientLifeConfig.EnsureLoaded();
        }
    }
}
