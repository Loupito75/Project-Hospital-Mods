using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GLib;
using Lopital;
using UnityEngine;

namespace HospitalPorters
{
    internal static class PorterIds
    {
        internal const string Occupation = "HPO_OCCUPATION_PORTER";
        internal const string PorterQualification = "HPO_SKILL_PORTER_QUALIFICATION";
        internal const string PorterStationRoom = "HPO_ROOM_TYPE_PORTER_STATION";
        internal const string PorterWorkspaceTag = "porter_workspace";
        internal const string PatientTransportRole = "HPO_EMPL_ROLE_PATIENT_TRANSPORT";
        internal const string SampleTransportRole = "HPO_EMPL_ROLE_SAMPLE_TRANSPORT";
        internal const string SampleCart = "HPO_OBJECT_SAMPLE_CART";
        internal const string SampleCartTag = "porter_sample_cart";
        internal const string SampleCartUiTag = "ui_porter_sample_cart";
        internal const string SampleCartNotification = "HPO_NOTIF_NOT_ENOUGH_SAMPLE_CARTS";
        internal const string SampleCartNotificationText = "HPO_NOTIF_NOT_ENOUGH_SAMPLE_CARTS_TEXT";
        internal const string PorterCandidates = "HPO_PORTERS";
        internal const string PorterTooltip = "HPO_PORTERS_TOOLTIP";
        internal const string PorterStaffingTooltip = "HPO_PORTERS_STAFFING_TOOLTIP";
        internal const string EmployeesHeading = "HPO_EMPLOYEES_HEADING_PORTERS";
        internal const string PorterLevel1 = "HPO_PORTER_LEVEL_1";
        internal const string PorterLevel2 = "HPO_PORTER_LEVEL_2";
        internal const string PorterLevel3 = "HPO_PORTER_LEVEL_3";
        internal const string MaleClothingStyle = "HPO_CLTHSTL_MALE_PORTER";
        internal const string FemaleClothingStyle = "HPO_CLTHSTL_FEMALE_PORTER";

        internal static string GetPorterLevelLocalizationId(int level)
        {
            switch (level)
            {
                case 1:
                    return PorterLevel1;
                case 2:
                    return PorterLevel2;
                case 3:
                    return PorterLevel3;
                default:
                    return PorterLevel1;
            }
        }

        internal static bool IsPorterRole(GameDBEmployeeRole role)
        {
            if (role == null)
            {
                return false;
            }

            string id = role.DatabaseID.ToString();
            return id == PatientTransportRole || id == SampleTransportRole;
        }
    }

    internal static class ModDatabase
    {
        private static bool s_loading;
        private static bool s_loaded;

        internal static bool IsLoading => s_loading;

        internal static void ResetForBaseLoad()
        {
            s_loaded = false;
            PorterHiringState.Reset();
            PorterCandidatePool.Clear(destroyEntities: true);
            PorterSampleTransportRuntime.Reset();
            PorterIdleWorkstationVisuals.Reset();
            StretcherDestinationPatch.ResetFallbackTracking();
        }

        internal static void Load(Database database)
        {
            if (database == null || s_loading || s_loaded)
            {
                return;
            }

            string pluginRoot = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string databaseDirectory = string.IsNullOrEmpty(pluginRoot)
                ? null
                : Path.Combine(pluginRoot, "Database");

            if (string.IsNullOrEmpty(databaseDirectory) || !Directory.Exists(databaseDirectory))
            {
                Plugin.Log?.LogError("Hospital Porters Database directory is missing; porter database entries cannot be loaded.");
                return;
            }

            s_loading = true;
            try
            {
                database.ReadFiles(pluginRoot, add: true);

                bool valid =
                    database.GetEntry<GameDBOccupation>(PorterIds.Occupation) != null &&
                    database.GetEntry<GameDBSkill>(PorterIds.PorterQualification) != null &&
                    database.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom) != null &&
                    database.GetEntry<GameDBClothingStyle>(PorterIds.MaleClothingStyle) != null &&
                    database.GetEntry<GameDBClothingStyle>(PorterIds.FemaleClothingStyle) != null &&
                    database.GetEntry<GameDBEmployeeRole>(PorterIds.PatientTransportRole) != null &&
                    database.GetEntry<GameDBEmployeeRole>(PorterIds.SampleTransportRole) != null &&
                    database.GetEntry<GameDBObject>(PorterIds.SampleCart) != null &&
                    database.GetEntry<GameDBNotification>(PorterIds.SampleCartNotification) != null;

                s_loaded = valid;
                if (valid)
                {
                    PlayerProfile.Instance.FillNotificationLevels();
                    PorterStationRegistry.Register(database);
                    PorterDiagnostics.Log("database initialized.");
                    Plugin.EnsureDeferredHiringPatches();
                }
                else
                {
                    Plugin.Log?.LogError("Hospital Porters database file was read but one or more required entries are missing.");
                }
            }
            catch (Exception exception)
            {
                Plugin.Log?.LogError("Failed to load Hospital Porters database entries: " + exception);
            }
            finally
            {
                s_loading = false;
            }
        }
    }

    internal static class PorterIdentity
    {
        internal static bool IsPorter(Entity entity)
        {
            CharacterPersonalInfoComponent personalInfoComponent = entity?.GetComponent<CharacterPersonalInfoComponent>();
            if (personalInfoComponent == null || personalInfoComponent.m_personalInfo == null)
            {
                return false;
            }

            GameDBOccupation occupation = personalInfoComponent.m_personalInfo.m_occupation.Entry;
            return occupation != null && occupation.DatabaseID.ToString() == PorterIds.Occupation;
        }

        internal static bool SetPorterOccupation(Entity entity)
        {
            if (entity == null)
            {
                return false;
            }

            GameDBOccupation porterOccupation = Database.Instance.GetEntry<GameDBOccupation>(PorterIds.Occupation);
            CharacterPersonalInfoComponent personalInfoComponent = entity.GetComponent<CharacterPersonalInfoComponent>();
            if (porterOccupation == null || personalInfoComponent == null || personalInfoComponent.m_personalInfo == null)
            {
                return false;
            }

            CharacterPersonalInfo personalInfo = personalInfoComponent.m_personalInfo;
            personalInfo.m_occupation = porterOccupation;

            // CreateCharacterNurse already chose nurse clothes before we replace the occupation.
            // Re-run the game's own clothing-style selection against the Porter occupation, then
            // rebuild the already-created animated model before the hiring portrait is generated.
            GameDBGender gender = personalInfo.m_gender.Entry;
            if (gender != null)
            {
                GameDBClothingStyle clothingStyle =
                    CharacterPersonalInfo.RandomizeClothingStyle(porterOccupation, gender);
                if (clothingStyle != null)
                {
                    personalInfo.m_clothingStyle = clothingStyle;
                    AnimModelComponent animModel = entity.GetComponent<AnimModelComponent>();
                    animModel?.Reset();
                }
            }

            if (!EnsurePorterQualification(entity))
            {
                return false;
            }

            RefreshPorterName(entity);
            entity.GetComponent<EmployeeComponent>()?.CacheRoleCount();
            return true;
        }

        internal static void RefreshPorterName(Entity entity)
        {
            if (!IsPorter(entity))
            {
                return;
            }

            CharacterPersonalInfoComponent personalInfoComponent =
                entity.GetComponent<CharacterPersonalInfoComponent>();
            CharacterPersonalInfo personalInfo =
                personalInfoComponent == null
                    ? null
                    : personalInfoComponent.m_personalInfo;
            if (personalInfo == null)
            {
                return;
            }

            string fullName = personalInfo.GetFullName();
            fullName = fullName == null
                ? string.Empty
                : fullName.Trim();

            entity.Name = string.IsNullOrEmpty(fullName)
                ? "Porter"
                : "Porter " + fullName;
        }

        internal static bool EnsurePorterQualification(Entity entity)
        {
            if (!IsPorter(entity))
            {
                return false;
            }

            EmployeeComponent employee = entity.GetComponent<EmployeeComponent>();
            GameDBSkill qualification = Database.Instance.GetEntry<GameDBSkill>(PorterIds.PorterQualification);
            if (employee == null || qualification == null)
            {
                return false;
            }

            SkillSet current = employee.m_state.m_skillSet;
            if (current != null && current.HasSkill(qualification) &&
                current.m_qualifications.Count == 1 &&
                current.m_specialization1 == null &&
                current.m_specialization2 == null)
            {
                return true;
            }

            // Match native DEBUG_CreateNurseSkillSet() exactly for qualification progress:
            // Random.Range(Math.Max(1f, level - 1f), level + 1f). Existing valid Porter
            // qualification progress is preserved by the early return above.
            float level = (float)employee.m_state.m_level;
            float qualificationLevel = UnityEngine.Random.Range(
                Math.Max(1f, level - 1f),
                level + 1f);

            SkillSet porterSkills = new SkillSet();
            porterSkills.m_qualifications.Add(new Skill(qualification, qualificationLevel));
            employee.m_state.m_skillSet = porterSkills;
            return true;
        }
    }

    internal static class PorterStaffing
    {
        internal static int CountPorters(Department department, Shift shift)
        {
            if (department == null || department.m_departmentPersistentData.m_nurses == null)
            {
                return 0;
            }

            int count = 0;
            foreach (EntityIDPointer<Entity> pointer in department.m_departmentPersistentData.m_nurses)
            {
                Entity entity = pointer.GetEntity();
                if (!PorterIdentity.IsPorter(entity))
                {
                    continue;
                }

                EmployeeComponent employee = entity.GetComponent<EmployeeComponent>();
                if (employee != null && !employee.IsFired() &&
                    employee.m_state.m_shift == shift)
                {
                    count++;
                }
            }
            return count;
        }
    }

    internal static class PorterCandidatePool
    {
        private static readonly Dictionary<string, List<Entity>> Candidates =
            new Dictionary<string, List<Entity>>(StringComparer.Ordinal);

        internal static List<Entity> Get(GameDBDepartment department)
        {
            if (department == null)
            {
                return new List<Entity>();
            }

            string key = department.DatabaseID.ToString();
            if (!Candidates.TryGetValue(key, out List<Entity> list))
            {
                list = new List<Entity>();
                Candidates.Add(key, list);
            }
            return list;
        }

        internal static List<Entity> Ensure(GameDBDepartment department)
        {
            List<Entity> list = Get(department);
            if (department == null || Database.Instance.GetEntry<GameDBOccupation>(PorterIds.Occupation) == null)
            {
                return list;
            }

            int desiredCount = SettingsManager.Instance.m_gameSettings.m_numberOfCharactersForHire;
            while (list.Count < desiredCount)
            {
                Entity entity = LopitalEntityFactory.CreateCharacterNurse(null, Vector2i.ZERO_VECTOR);
                if (!PorterIdentity.SetPorterOccupation(entity))
                {
                    entity.Destroy();
                    break;
                }

                EmployeeComponent employee = entity.GetComponent<EmployeeComponent>();
                employee.m_state.m_hiredForDepartment = department;
                employee.m_state.m_hiredLevel = employee.m_state.m_level;
                employee.m_state.m_hiredSalaryRandomization = UnityEngine.Random.Range(0f, 1f);
                employee.m_state.m_employeeType = LopitalTypes.CharacterNurse;
                employee.CacheRoleCount();

                PortraitManager.Instance.CreatePortraitSlot(entity);
                list.Add(entity);
            }

            while (list.Count > desiredCount)
            {
                int lastIndex = list.Count - 1;
                Entity entity = list[lastIndex];
                list.RemoveAt(lastIndex);
                DestroyCandidate(entity);
            }

            return list;
        }

        internal static void Remove(Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            foreach (List<Entity> list in Candidates.Values)
            {
                if (list.Remove(entity))
                {
                    return;
                }
            }
        }

        internal static void DestroyForDepartment(GameDBDepartment department)
        {
            List<Entity> list = Get(department);
            foreach (Entity entity in list)
            {
                DestroyCandidate(entity);
            }
            list.Clear();
        }

        internal static void Clear(bool destroyEntities)
        {
            if (destroyEntities)
            {
                foreach (List<Entity> list in Candidates.Values)
                {
                    foreach (Entity entity in list)
                    {
                        DestroyCandidate(entity);
                    }
                }
            }
            Candidates.Clear();
        }

        private static void DestroyCandidate(Entity entity)
        {
            if (entity == null)
            {
                return;
            }

            if (PortraitManager.Instance.GetSlot(entity) != null)
            {
                PortraitManager.Instance.DestroyPortraitGeometry(entity);
            }
            entity.Destroy();
        }
    }

    internal static class PorterHiringState
    {
        internal static bool Active { get; set; }
        internal static bool SelectingPorter { get; set; }
        internal static bool HiringPorter { get; set; }

        internal static void Reset()
        {
            Active = false;
            SelectingPorter = false;
            HiringPorter = false;
        }
    }
}
