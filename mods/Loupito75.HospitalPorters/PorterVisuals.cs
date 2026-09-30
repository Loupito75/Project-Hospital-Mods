using UnityEngine;
using UnityEngine.UI;

namespace HospitalPorters
{
    internal static class PorterVisuals
    {
        internal const int CategoryFallbackIcon = 2418;
        internal const int CategoryDisabledFallbackIcon = 2420;
        internal const int StationFallbackIcon = 824;

        internal const string CategoryAsset = "HPO_ASSET_PORTER";
        internal const string CategoryDisabledAsset = "HPO_ASSET_PORTER_DISABLED";
        internal const string CategoryLightAsset = "HPO_ASSET_PORTER_LIGHT";
        internal const string WorkspaceEmptyAsset = "HPO_ASSET_PORTER_WORKSPACE_EMPTY";
        internal const string EmployeesAsset = "HPO_ASSET_PORTER_EMPLOYEES";
        internal const string EmployeesDisabledAsset = "HPO_ASSET_PORTER_EMPLOYEES_DISABLED";
        internal const string RightPanelAsset = "HPO_ASSET_PORTER_RIGHT_PANEL";
        internal const string LevelEmptyAsset = "HPO_ASSET_PORTER_LEVEL_EMPTY";
        internal const string LevelFullAsset = "HPO_ASSET_PORTER_LEVEL_FULL";
        internal const string LevelEmptyLightAsset = "HPO_ASSET_PORTER_LEVEL_EMPTY_LIGHT";
        internal const string LevelFullLightAsset = "HPO_ASSET_PORTER_LEVEL_FULL_LIGHT";
        internal const string QualificationAsset = "HPO_ASSET_PORTER_QUALIFICATION";
        internal const string QualificationLightAsset = "HPO_ASSET_PORTER_QUALIFICATION_LIGHT";
        internal const string PatientRoleActiveAsset = "HPO_ASSET_PORTER_ROLE_PATIENT_TRANSPORT_ACTIVE";
        internal const string PatientRoleDisabledAsset = "HPO_ASSET_PORTER_ROLE_PATIENT_TRANSPORT_DISABLED";
        internal const string SampleRoleActiveAsset = "HPO_ASSET_PORTER_ROLE_SAMPLE_TRANSPORT_ACTIVE";
        internal const string SampleRoleDisabledAsset = "HPO_ASSET_PORTER_ROLE_SAMPLE_TRANSPORT_DISABLED";
        internal const string PatientWorkspaceRoleActiveAsset =
            "HPO_ASSET_PORTER_ROLE_PATIENT_TRANSPORT_WORKSPACE_ACTIVE";
        internal const string PatientWorkspaceRoleActiveLightAsset =
            "HPO_ASSET_PORTER_ROLE_PATIENT_TRANSPORT_WORKSPACE_ACTIVE_LIGHT";
        internal const string PatientWorkspaceRoleDisabledAsset =
            "HPO_ASSET_PORTER_ROLE_PATIENT_TRANSPORT_WORKSPACE_DISABLED";
        internal const string SampleWorkspaceRoleActiveAsset =
            "HPO_ASSET_PORTER_ROLE_SAMPLE_TRANSPORT_WORKSPACE_ACTIVE";
        internal const string SampleWorkspaceRoleActiveLightAsset =
            "HPO_ASSET_PORTER_ROLE_SAMPLE_TRANSPORT_WORKSPACE_ACTIVE_LIGHT";
        internal const string SampleWorkspaceRoleDisabledAsset =
            "HPO_ASSET_PORTER_ROLE_SAMPLE_TRANSPORT_WORKSPACE_DISABLED";
        internal const string StationAsset = "HPO_ASSET_PORTER_STATION";
        internal const string StationLockedAsset = "HPO_ASSET_PORTER_STATION_LOCKED";
        internal const string StationFloorAsset = "HPO_ASSET_PORTER_STATION_FLOOR";

        private static Sprite s_levelEmptySprite;
        private static Sprite s_levelFullSprite;
        private static Sprite s_levelEmptyLightSprite;
        private static Sprite s_levelFullLightSprite;
        private static bool s_loggedInvalidLevelAssets;
        private static bool s_loggedInvalidLightLevelAssets;

        internal static Sprite GetCategorySprite()
        {
            return GetCustomIcon(CategoryAsset, CategoryFallbackIcon);
        }

        internal static Sprite GetCategoryDisabledSprite()
        {
            return GetCustomIcon(CategoryDisabledAsset, CategoryDisabledFallbackIcon);
        }

        internal static Sprite GetCategoryLightSprite()
        {
            return GetCustomIcon(CategoryLightAsset, CategoryFallbackIcon);
        }

        internal static Sprite GetRightPanelSprite()
        {
            return GetCustomIcon(RightPanelAsset, CategoryFallbackIcon);
        }

        internal static void ApplyCategoryIcons(IconButtonController button)
        {
            if (button == null)
            {
                return;
            }

            Sprite active = GetCategorySprite();
            Sprite disabled = GetCategoryDisabledSprite();

            if (active != null)
            {
                button.SetIcon(active);
            }
            else
            {
                button.SetIcon(CategoryFallbackIcon);
            }

            if (disabled != null)
            {
                button.SetInactiveIcon(disabled);
            }
            else
            {
                button.SetInactiveIcon(CategoryDisabledFallbackIcon);
            }
        }

        internal static void ApplyEmployeesIcons(IconButtonController button)
        {
            if (button == null)
            {
                return;
            }

            Sprite active = GetCustomIcon(EmployeesAsset, CategoryFallbackIcon);
            Sprite disabled = GetCustomIcon(
                EmployeesDisabledAsset,
                CategoryDisabledFallbackIcon);

            if (active != null)
            {
                button.SetIcon(active);
            }
            else
            {
                button.SetIcon(CategoryFallbackIcon);
            }

            if (disabled != null)
            {
                button.SetInactiveIcon(disabled);
            }
            else
            {
                button.SetInactiveIcon(CategoryDisabledFallbackIcon);
            }
        }

        internal static void ApplyCategoryIcon(IconController icon)
        {
            if (icon == null)
            {
                return;
            }

            if (HasLoadedTexture(CategoryAsset))
            {
                icon.SetIcon(CategoryAsset);
            }
            else
            {
                icon.SetIcon(CategoryFallbackIcon);
            }
        }

        internal static void ApplyCategoryLightIcon(IconController icon)
        {
            if (icon == null)
            {
                return;
            }

            if (HasLoadedTexture(CategoryLightAsset))
            {
                icon.SetIcon(CategoryLightAsset);
            }
            else
            {
                ApplyCategoryIcon(icon);
            }
        }

        internal static void ApplyWorkspaceEmptyIcon(IconController icon)
        {
            if (icon == null)
            {
                return;
            }

            if (HasLoadedTexture(WorkspaceEmptyAsset))
            {
                icon.SetIcon(WorkspaceEmptyAsset);
            }
            else if (HasLoadedTexture(CategoryDisabledAsset))
            {
                icon.SetIcon(CategoryDisabledAsset);
            }
            else
            {
                icon.SetIcon(CategoryDisabledFallbackIcon);
            }
        }

        internal static bool ApplyLevelVisuals(GaugeIconsController gauge)
        {
            return ApplyLevelVisuals(
                gauge,
                LevelEmptyAsset,
                LevelFullAsset,
                ref s_levelEmptySprite,
                ref s_levelFullSprite,
                ref s_loggedInvalidLevelAssets);
        }

        internal static bool ApplyLightLevelVisuals(GaugeIconsController gauge)
        {
            return ApplyLevelVisuals(
                gauge,
                LevelEmptyLightAsset,
                LevelFullLightAsset,
                ref s_levelEmptyLightSprite,
                ref s_levelFullLightSprite,
                ref s_loggedInvalidLightLevelAssets);
        }

        private static bool ApplyLevelVisuals(
            GaugeIconsController gauge,
            string emptyAsset,
            string fullAsset,
            ref Sprite emptySprite,
            ref Sprite fullSprite,
            ref bool loggedInvalid)
        {
            if (gauge == null)
            {
                return false;
            }

            Image baseImage = GetGaugeBaseImage(gauge);
            Image topImage = GetGaugeTopImage(gauge);
            Texture emptyTexture = GetTextureAsset(emptyAsset);
            Texture fullTexture = GetTextureAsset(fullAsset);
            if (baseImage == null || topImage == null ||
                baseImage.sprite == null || topImage.sprite == null ||
                emptyTexture == null || fullTexture == null)
            {
                return false;
            }

            int expectedEmptyWidth = Mathf.RoundToInt(baseImage.sprite.rect.width);
            int expectedEmptyHeight = Mathf.RoundToInt(baseImage.sprite.rect.height);
            int expectedFullWidth = Mathf.RoundToInt(topImage.sprite.rect.width);
            int expectedFullHeight = Mathf.RoundToInt(topImage.sprite.rect.height);

            if (emptyTexture.width != expectedEmptyWidth ||
                emptyTexture.height != expectedEmptyHeight ||
                fullTexture.width != expectedFullWidth ||
                fullTexture.height != expectedFullHeight)
            {
                if (!loggedInvalid)
                {
                    loggedInvalid = true;
                    Plugin.Log?.LogWarning(
                        "Porter level sprites do not match the native Nurse gauge canvas. " +
                        "Custom empty=" + emptyTexture.width + "x" + emptyTexture.height +
                        ", expected=" + expectedEmptyWidth + "x" + expectedEmptyHeight +
                        "; custom full=" + fullTexture.width + "x" + fullTexture.height +
                        ", expected=" + expectedFullWidth + "x" + expectedFullHeight +
                        ". Native Nurse level visuals will be used.");
                }

                return false;
            }

            Sprite empty = GetFullTextureSprite(emptyAsset, ref emptySprite);
            Sprite full = GetFullTextureSprite(fullAsset, ref fullSprite);
            if (empty == null || full == null)
            {
                return false;
            }

            baseImage.sprite = empty;
            topImage.sprite = full;
            return true;
        }

        internal static bool ApplyNativeNurseLevelVisuals(GaugeIconsController gauge)
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            GameObject prefab = assets == null
                ? null
                : assets.m_prefabCharacterLevelPanelNurse as GameObject;
            GaugeIconsController source = prefab == null
                ? null
                : prefab.GetComponentInChildren<GaugeIconsController>(includeInactive: true);
            return CopyGaugeVisuals(gauge, source);
        }

        internal static bool ApplyNativeTableLevelVisuals(GaugeIconsController gauge)
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            GameObject prefab = assets == null
                ? null
                : assets.m_tableItemLevelDoctor as GameObject;
            GaugeIconsController source = prefab == null
                ? null
                : prefab.GetComponentInChildren<GaugeIconsController>(includeInactive: true);
            return CopyGaugeVisuals(gauge, source);
        }

        private static bool CopyGaugeVisuals(
            GaugeIconsController target,
            GaugeIconsController source)
        {
            if (target == null || source == null)
            {
                return false;
            }

            Image targetBase = GetGaugeBaseImage(target);
            Image targetTop = GetGaugeTopImage(target);
            Image sourceBase = GetGaugeBaseImage(source);
            Image sourceTop = GetGaugeTopImage(source);
            if (targetBase == null || targetTop == null ||
                sourceBase == null || sourceTop == null ||
                sourceBase.sprite == null || sourceTop.sprite == null)
            {
                return false;
            }

            targetBase.sprite = sourceBase.sprite;
            targetTop.sprite = sourceTop.sprite;
            return true;
        }

        internal static Sprite GetQualificationSprite(bool lightBackground)
        {
            return GetCustomIcon(
                lightBackground ? QualificationLightAsset : QualificationAsset,
                CategoryFallbackIcon);
        }

        internal static Sprite GetRoleSprite(GameDBEmployeeRole role, bool active)
        {
            if (role == null)
            {
                return null;
            }

            string id = role.DatabaseID.ToString();
            if (id == PorterIds.PatientTransportRole)
            {
                return GetCustomIcon(
                    active ? PatientRoleActiveAsset : PatientRoleDisabledAsset,
                    role.Icon + (active ? 1 : 2));
            }

            if (id == PorterIds.SampleTransportRole)
            {
                return GetCustomIcon(
                    active ? SampleRoleActiveAsset : SampleRoleDisabledAsset,
                    role.Icon + (active ? 1 : 2));
            }

            return null;
        }

        internal static void ApplyWorkspaceRoleIcon(
            IconController icon,
            GameDBEmployeeRole role,
            bool active)
        {
            if (icon == null || role == null)
            {
                return;
            }

            string roleId = role.DatabaseID.ToString();
            string assetId = null;

            if (roleId == PorterIds.PatientTransportRole)
            {
                assetId = active
                    ? PatientWorkspaceRoleActiveLightAsset
                    : PatientWorkspaceRoleDisabledAsset;
            }
            else if (roleId == PorterIds.SampleTransportRole)
            {
                assetId = active
                    ? SampleWorkspaceRoleActiveLightAsset
                    : SampleWorkspaceRoleDisabledAsset;
            }

            if (!string.IsNullOrEmpty(assetId) && HasLoadedTexture(assetId))
            {
                icon.SetIcon(assetId);
            }
            else
            {
                icon.SetIcon(role.Icon + (active ? 3 : 4));
            }
        }

        internal static Sprite GetStationSprite(bool locked)
        {
            return GetCustomIcon(
                locked ? StationLockedAsset : StationAsset,
                StationFallbackIcon + (locked ? 1 : 0));
        }

        internal static Texture GetStationFloorTexture()
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            if (assets == null || assets.m_textureAssets == null)
            {
                return null;
            }

            Texture texture;
            return assets.m_textureAssets.TryGetValue(StationFloorAsset, out texture)
                ? texture
                : null;
        }

        internal static Image GetGaugeBaseImage(GaugeIconsController gauge)
        {
            if (gauge == null || gauge.transform == null || gauge.transform.parent == null)
            {
                return null;
            }

            return gauge.transform.parent.GetComponent<Image>();
        }

        internal static Image GetGaugeTopImage(GaugeIconsController gauge)
        {
            if (gauge == null || gauge.m_gaugeTopLayer == null)
            {
                return null;
            }

            return gauge.m_gaugeTopLayer.GetComponent<Image>();
        }

        private static Texture GetTextureAsset(string assetId)
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            if (assets == null || assets.m_textureAssets == null)
            {
                return null;
            }

            Texture texture;
            return assets.m_textureAssets.TryGetValue(assetId, out texture)
                ? texture
                : null;
        }

        private static Sprite GetCustomIcon(string assetId, int fallbackIcon)
        {
            IconManager iconManager = IconManager.Instance;
            if (iconManager == null)
            {
                return null;
            }

            if (HasLoadedTexture(assetId) && iconManager.m_initialized)
            {
                return iconManager.GetIcon(assetId);
            }

            return iconManager.GetIcon(fallbackIcon);
        }

        private static Sprite GetFullTextureSprite(string assetId, ref Sprite cached)
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            if (assets == null || assets.m_textureAssets == null)
            {
                return null;
            }

            Texture texture;
            if (!assets.m_textureAssets.TryGetValue(assetId, out texture) || texture == null)
            {
                return null;
            }

            if (cached != null && cached.texture == texture)
            {
                return cached;
            }

            cached = assets.CreateSprite(
                assetId,
                new Rect(0f, 0f, texture.width, texture.height));
            return cached;
        }

        private static bool HasLoadedTexture(string assetId)
        {
            StreamingAssetManager assets = StreamingAssetManager.GetInstance();
            return assets != null &&
                assets.m_textureAssets != null &&
                assets.m_textureAssets.ContainsKey(assetId);
        }
    }
}
