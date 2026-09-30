using HarmonyLib;
using Lopital;
using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    [HarmonyPatch(typeof(MapEditorUIController), "UpdateLogisticsHeading")]
    internal static class PorterDepartmentHeadingIconPatch
    {
        private static string s_configuredDepartmentId;
        private static GameObject s_configuredLeftButton;

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        private static void Postfix(MapEditorUIController __instance)
        {
            if (__instance == null || Hospital.Instance == null ||
                Hospital.Instance.m_activeDepartment == null ||
                Database.Instance == null || IconManager.Instance == null)
            {
                return;
            }

            Department department = Hospital.Instance.m_activeDepartment.GetEntity();
            GameDBDepartment liveType = department == null
                ? null
                : department.GetDepartmentType();
            if (liveType == null)
            {
                return;
            }

            GameDBDepartment canonicalType =
                Database.Instance.GetEntry<GameDBDepartment>(liveType.DatabaseID);
            if (canonicalType == null)
            {
                canonicalType = liveType;
            }

            Sprite sprite = GetDepartmentHeadingSprite(canonicalType);
            if (sprite == null)
            {
                return;
            }

            Image leftImage = __instance.m_buttonLogisticsSelectDepartment == null
                ? null
                : __instance.m_buttonLogisticsSelectDepartment.GetComponentInChildren<Image>();
            Image rightImage = __instance.m_buttonLogisticsSelectDepartmentRight == null
                ? null
                : __instance.m_buttonLogisticsSelectDepartmentRight.GetComponentInChildren<Image>();

            if (leftImage != null)
            {
                leftImage.enabled = true;
                if (leftImage.sprite != sprite)
                {
                    leftImage.sprite = sprite;
                }

                string departmentIdForButton = canonicalType.DatabaseID.ToString();
                if (s_configuredLeftButton != __instance.m_buttonLogisticsSelectDepartment ||
                    s_configuredDepartmentId != departmentIdForButton)
                {
                    IconButtonController leftButton =
                        __instance.m_buttonLogisticsSelectDepartment.GetComponent<IconButtonController>();
                    if (leftButton != null)
                    {
                        leftButton.SetIcon(sprite);
                        s_configuredLeftButton = __instance.m_buttonLogisticsSelectDepartment;
                        s_configuredDepartmentId = departmentIdForButton;
                    }
                }
            }

            if (rightImage != null)
            {
                rightImage.enabled = true;
                if (rightImage.sprite != sprite)
                {
                    rightImage.sprite = sprite;
                }
            }
        }

        private static Sprite GetDepartmentHeadingSprite(GameDBDepartment departmentType)
        {
            if (departmentType == null || IconManager.Instance == null)
            {
                return null;
            }

            if (departmentType.CustomIcon_1_DarkBackgroundAssetRef != null)
            {
                return IconManager.Instance.GetIcon(
                    departmentType.CustomIcon_1_DarkBackgroundAssetRef.XmlID);
            }

            return IconManager.Instance.GetIcon(departmentType.IconID + 1);
        }
    }
}
