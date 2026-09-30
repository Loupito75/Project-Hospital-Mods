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
                // Keep the real engine type as CharacterNurse. This prefix only guarantees that
                // every native hiring-panel entry path for a Porter locker (including the
                // "hire more" button) selects the Porter presentation before Update() builds
                // candidate cards. The existing lower-priority Update prefix then swaps in the
                // Porter candidate pool for this native CharacterNurse branch.
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
