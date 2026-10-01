using GLib;
using HarmonyLib;
using Lopital;

namespace HospitalPorters
{
    [HarmonyPatch(typeof(HiringPanelController), nameof(HiringPanelController.Update))]
    internal static class PorterHiringInitialSelectionPatch
    {

        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void Prefix(HiringPanelController __instance)
        {
            if (__instance == null || PorterHiringState.Active ||
                HiringManager.Instance == null || HiringManager.Instance.m_workspace == null ||
                Hospital.Instance == null || Hospital.Instance.m_activeDepartment.GetEntity() == null)
            {
                return;
            }

            EntityIDPointer<TileObject> workspace = HiringManager.Instance.m_workspace;
            if (!PorterHiringUi.IsPorterWorkspace(workspace))
            {
                return;
            }

            GameDBDepartment department =
                Hospital.Instance.m_activeDepartment.GetEntity().GetDepartmentType();
            PorterCandidatePool.Ensure(department);
            PorterHiringUi.ConfigureNurseDelegate(
                __instance,
                workspace,
                forceReception: false);

            PorterHiringState.Active = true;
            PorterHiringState.SelectingPorter = true;
            try
            {
                // Porters remain CharacterNurse internally. Select the Porter presentation before
                // Update() builds cards; the Update prefix supplies the Porter candidate pool.
                __instance.SetCharacterType(
                    LopitalTypes.CharacterNurse,
                    PorterHiringIcons.PorterIcon,
                    LocalizationManager.Get(PorterIds.PorterCandidates));
            }
            finally
            {
                PorterHiringState.SelectingPorter = false;
            }
        }
    }
}
