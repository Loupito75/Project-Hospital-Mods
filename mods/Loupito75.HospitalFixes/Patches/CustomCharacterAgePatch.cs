using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalFixes.Patches
{
    [HarmonyPatch(typeof(CharacterEditorController), "OnEditAgeDelegate")]
    internal static class CustomCharacterAgePatch
    {
        [HarmonyPrefix]
        private static bool Prefix(ref string value)
        {
            if (!FixConfig.FixCustomCharacterAge)
            {
                return true;
            }

            GameDBAgeCategory youngest =
                Database.Instance.GetEntry<GameDBAgeCategory>("AGE_YOUNG");
            GameDBAgeCategory oldest =
                Database.Instance.GetEntry<GameDBAgeCategory>("AGE_SENIOR");

            if (youngest == null || oldest == null)
            {
                return true;
            }

            string originalValue = value;
            int age;
            if (!int.TryParse(value, out age))
            {
                UISoundManager.sm_instance.PlaySoundEvent(
                    "SFX_UI_FORBIDDEN");

                if (HfxDiagnostics.Enabled)
                {
                    HfxDiagnostics.Intervention(
                        "FixCustomCharacterAge",
                        "CharacterEditorController.OnEditAgeDelegate",
                        "non-numeric-rejected",
                        "input=" + originalValue + " reason=non-numeric");
                }
                return false;
            }

            if (age < youngest.MinAge)
            {
                UISoundManager.sm_instance.PlaySoundEvent(
                    "SFX_UI_FORBIDDEN");
                age = youngest.MinAge;
                value = age.ToString();
            }
            else if (age > oldest.MaxAge)
            {
                UISoundManager.sm_instance.PlaySoundEvent(
                    "SFX_UI_FORBIDDEN");
                age = oldest.MaxAge;
                value = age.ToString();
            }

            if (HfxDiagnostics.Enabled &&
                !string.Equals(
                    originalValue,
                    value,
                    System.StringComparison.Ordinal))
            {
                HfxDiagnostics.Intervention(
                    "FixCustomCharacterAge",
                    "CharacterEditorController.OnEditAgeDelegate",
                    "age-clamped-to-vanilla-category-range",
                    "input=" + originalValue +
                    " applied=" + value);
            }

            // Let Vanilla apply the (possibly clamped) value, update its
            // label, restore the normal UI state, and close the age dialog.
            return true;
        }

        [HarmonyPostfix]
        private static void Postfix(string value, Entity ___m_character)
        {
            if (!FixConfig.FixCustomCharacterAge || ___m_character == null)
            {
                return;
            }

            CharacterPersonalInfoComponent component = ___m_character.GetComponent<CharacterPersonalInfoComponent>();
            if (component != null && component.m_personalInfo != null)
            {
                component.m_personalInfo.m_ageCategory =
                    CharacterPersonalInfo.GetAgeCategoryForAge(component.m_personalInfo.m_age);
            }
        }
    }
}
