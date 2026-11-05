using HarmonyLib;
using UnityEngine.UI;

namespace HospitalFixes.Patches
{
    internal static class PatientListHeadingText
    {
        internal static string GetHeading(int categoryIndex)
        {
            if (categoryIndex == -1)
            {
                categoryIndex = 4;
            }

            if (categoryIndex < 0 ||
                categoryIndex >=
                    HfxLocalizationIds.PatientFilterLabels.Length)
            {
                return null;
            }

            StringTable table = StringTable.GetInstance();
            if (table == null)
            {
                return null;
            }

            string heading =
                table.GetLocalizedText(
                    "TABLE_PATIENTS_HEADING");
            if (string.IsNullOrEmpty(heading))
            {
                return null;
            }

            string category =
                LocalizationManager.Get(
                    HfxLocalizationIds
                        .PatientFilterLabels[
                            categoryIndex]);

            if (string.IsNullOrEmpty(category))
            {
                return heading;
            }

            return heading + " - " + category;
        }
    }

    [HarmonyPatch(
        typeof(PatientsTable),
        nameof(PatientsTable.SetBackGroundForButton))]
    internal static class PatientListHeadingPatch
    {
        private static void Postfix(
            PatientsTable __instance,
            int index)
        {
            if (!FixConfig.FixPatientListHeading ||
                __instance == null)
            {
                return;
            }

            string heading =
                PatientListHeadingText.GetHeading(index);
            Text headingText =
                __instance.gameObject
                    .GetComponentInChildren<Text>();
            if (headingText != null &&
                !string.IsNullOrEmpty(heading) &&
                headingText.text != heading)
            {
                headingText.text = heading;
            }
        }
    }
}
