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
        internal const string SamplePorterCapacityNotification = "HPO_NOTIF_SAMPLE_PORTER_CAPACITY";
        internal const string SamplePorterCapacityNotificationText = "HPO_NOTIF_SAMPLE_PORTER_CAPACITY_TEXT";
        internal const string PatientPorterCapacityNotification = "HPO_NOTIF_PATIENT_PORTER_CAPACITY";
        internal const string PatientPorterCapacityNotificationText = "HPO_NOTIF_PATIENT_PORTER_CAPACITY_TEXT";
        internal const string PorterCandidates = "HPO_PORTERS";
        internal const string PorterTooltip = "HPO_PORTERS_TOOLTIP";
        internal const string PorterStaffingTooltip = "HPO_PORTERS_STAFFING_TOOLTIP";
        internal const string EmployeesHeading = "HPO_EMPLOYEES_HEADING_PORTERS";
        internal const string PorterLevel1 = "HPO_PORTER_LEVEL_1";
        internal const string PorterLevel2 = "HPO_PORTER_LEVEL_2";
        internal const string PorterLevel3 = "HPO_PORTER_LEVEL_3";
        internal const string MaleClothingStyle = "HPO_CLTHSTL_MALE_PORTER";
        internal const string FemaleClothingStyle = "HPO_CLTHSTL_FEMALE_PORTER";

        // Project Hospital interns database IDs through ID.CreateID(). Comparing these
        // IDs is cheaper than converting them to strings and remains stable even if a
        // DatabaseEntry instance is replaced by a later Database.ReadFiles(add: true).
        internal static readonly ID OccupationId = ID.CreateID(Occupation);
        internal static readonly ID PorterQualificationId = ID.CreateID(PorterQualification);
        internal static readonly ID PorterStationRoomId = ID.CreateID(PorterStationRoom);
        internal static readonly ID PatientTransportRoleId = ID.CreateID(PatientTransportRole);
        internal static readonly ID SampleTransportRoleId = ID.CreateID(SampleTransportRole);

        internal static bool IsPorterOccupation(
            GameDBPointer<GameDBOccupation> occupation)
        {
            return occupation.m_id == OccupationId;
        }

        internal static bool IsPorterQualification(
            GameDBPointer<GameDBSkill> skill)
        {
            return skill.m_id == PorterQualificationId;
        }

        internal static bool IsPorterStationRoom(GameDBRoomType roomType)
        {
            return roomType != null &&
                roomType.DatabaseID == PorterStationRoomId;
        }

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

        internal static bool IsPatientTransportRole(
            GameDBEmployeeRole role)
        {
            return role != null &&
                role.DatabaseID == PatientTransportRoleId;
        }

        internal static bool IsSampleTransportRole(
            GameDBEmployeeRole role)
        {
            return role != null &&
                role.DatabaseID == SampleTransportRoleId;
        }

        internal static bool IsPorterRole(GameDBEmployeeRole role)
        {
            return IsPatientTransportRole(role) ||
                IsSampleTransportRole(role);
        }
    }

    internal static class ModDatabase
    {
        private static bool s_loading;
        private static bool s_loaded;

        internal static bool IsLoading => s_loading;
        internal static bool IsReady => s_loaded && !s_loading;

        internal static void ResetForBaseLoad()
        {
            s_loaded = false;
            PorterHiringState.Reset();
            PorterCandidatePool.Clear(destroyEntities: true);
            PorterSampleTransportRuntime.Reset();
            PorterFullSpeechBubblePatch.Reset();
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

                GameDBOccupation porterOccupation =
                    database.GetEntry<GameDBOccupation>(PorterIds.Occupation);
                bool valid =
                    porterOccupation != null &&
                    database.GetEntry<GameDBSkill>(PorterIds.PorterQualification) != null &&
                    database.GetEntry<GameDBRoomType>(PorterIds.PorterStationRoom) != null &&
                    database.GetEntry<GameDBClothingStyle>(PorterIds.MaleClothingStyle) != null &&
                    database.GetEntry<GameDBClothingStyle>(PorterIds.FemaleClothingStyle) != null &&
                    database.GetEntry<GameDBEmployeeRole>(PorterIds.PatientTransportRole) != null &&
                    database.GetEntry<GameDBEmployeeRole>(PorterIds.SampleTransportRole) != null &&
                    database.GetEntry<GameDBObject>(PorterIds.SampleCart) != null &&
                    database.GetEntry<GameDBNotification>(PorterIds.SampleCartNotification) != null &&
                    database.GetEntry<GameDBNotification>(PorterIds.SamplePorterCapacityNotification) != null &&
                    database.GetEntry<GameDBNotification>(PorterIds.PatientPorterCapacityNotification) != null;

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
            CharacterPersonalInfoComponent personalInfoComponent =
                entity?.GetComponent<CharacterPersonalInfoComponent>();
            CharacterPersonalInfo personalInfo =
                personalInfoComponent == null
                    ? null
                    : personalInfoComponent.m_personalInfo;

            return personalInfo != null &&
                PorterIds.IsPorterOccupation(personalInfo.m_occupation);
        }

        internal static bool SetPorterOccupation(Entity entity)
        {
            if (entity == null || Database.Instance == null)
            {
                return false;
            }

            GameDBOccupation porterOccupation =
                Database.Instance.GetEntry<GameDBOccupation>(
                    PorterIds.Occupation);
            CharacterPersonalInfoComponent personalInfoComponent =
                entity.GetComponent<CharacterPersonalInfoComponent>();
            if (porterOccupation == null ||
                personalInfoComponent == null ||
                personalInfoComponent.m_personalInfo == null)
            {
                return false;
            }

            CharacterPersonalInfo personalInfo =
                personalInfoComponent.m_personalInfo;
            personalInfo.m_occupation = porterOccupation;

            // CreateCharacterNurse already chose nurse clothes before we replace the occupation.
            // Re-run the game's own clothing-style selection against the Porter occupation, then
            // rebuild the already-created animated model before the hiring portrait is generated.
            GameDBGender gender = personalInfo.m_gender.Entry;
            if (gender != null)
            {
                GameDBClothingStyle clothingStyle =
                    CharacterPersonalInfo.RandomizeClothingStyle(
                        porterOccupation,
                        gender);
                if (clothingStyle != null)
                {
                    personalInfo.m_clothingStyle = clothingStyle;
                    AnimModelComponent animModel =
                        entity.GetComponent<AnimModelComponent>();
                    animModel?.Reset();
                }
            }

            // This method is only used to convert a freshly generated native Nurse.
            // Replace that Nurse skill set once here; later compatibility/experience
            // checks repair the Porter qualification without discarding other data.
            if (!InitializePorterQualification(entity))
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
            if (!IsPorter(entity) || Database.Instance == null)
            {
                return false;
            }

            EmployeeComponent employee =
                entity.GetComponent<EmployeeComponent>();
            GameDBSkill qualification =
                Database.Instance.GetEntry<GameDBSkill>(
                    PorterIds.PorterQualification);
            if (employee == null ||
                employee.m_state == null ||
                qualification == null)
            {
                return false;
            }

            SkillSet current = employee.m_state.m_skillSet;
            if (current == null)
            {
                current = new SkillSet();
                employee.m_state.m_skillSet = current;
            }
            else if (current.m_qualifications == null)
            {
                current.m_qualifications = new List<Skill>();
            }

            Skill porterQualification =
                FindPorterQualification(current);
            if (porterQualification != null)
            {
                // Rebind the pointer to the current canonical DB object while preserving
                // the saved skill level. This also keeps native AddSkillPoints(), which
                // compares GameDBSkill instances, reliable after any DB-entry replacement.
                porterQualification.m_gameDBSkill = qualification;
                return true;
            }

            // Compatibility repair for an existing Porter: add only the missing HPO
            // qualification. Do not discard other qualifications/specializations that may
            // belong to a save or another compatible mod.
            current.m_qualifications.Add(
                new Skill(
                    qualification,
                    CreateInitialQualificationLevel(employee)));
            return true;
        }

        private static bool InitializePorterQualification(
            Entity entity)
        {
            EmployeeComponent employee =
                entity == null
                    ? null
                    : entity.GetComponent<EmployeeComponent>();
            GameDBSkill qualification =
                Database.Instance == null
                    ? null
                    : Database.Instance.GetEntry<GameDBSkill>(
                        PorterIds.PorterQualification);
            if (employee == null ||
                employee.m_state == null ||
                qualification == null)
            {
                return false;
            }

            SkillSet porterSkills = new SkillSet();
            porterSkills.m_qualifications.Add(
                new Skill(
                    qualification,
                    CreateInitialQualificationLevel(employee)));
            employee.m_state.m_skillSet = porterSkills;
            return true;
        }

        private static Skill FindPorterQualification(
            SkillSet skillSet)
        {
            if (skillSet == null ||
                skillSet.m_qualifications == null)
            {
                return null;
            }

            for (int i = 0;
                i < skillSet.m_qualifications.Count;
                i++)
            {
                Skill skill = skillSet.m_qualifications[i];
                if (skill != null &&
                    PorterIds.IsPorterQualification(
                        skill.m_gameDBSkill))
                {
                    return skill;
                }
            }

            return null;
        }

        private static float CreateInitialQualificationLevel(
            EmployeeComponent employee)
        {
            // Match native DEBUG_CreateNurseSkillSet() for qualification progress:
            // Random.Range(Math.Max(1f, level - 1f), level + 1f).
            float level =
                employee == null || employee.m_state == null
                    ? 1f
                    : (float)employee.m_state.m_level;
            return UnityEngine.Random.Range(
                Math.Max(1f, level - 1f),
                level + 1f);
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
        private static bool s_loggedDatabaseUnavailable;

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
            if (department == null)
            {
                return list;
            }

            if (!ModDatabase.IsReady ||
                Database.Instance == null ||
                Database.Instance.GetEntry<GameDBOccupation>(
                    PorterIds.Occupation) == null ||
                Database.Instance.GetEntry<GameDBSkill>(
                    PorterIds.PorterQualification) == null)
            {
                if (!s_loggedDatabaseUnavailable)
                {
                    s_loggedDatabaseUnavailable = true;
                    Plugin.Log?.LogWarning(
                        "Porter candidate generation is unavailable because the HPO database is not fully initialized.");
                }
                return list;
            }

            s_loggedDatabaseUnavailable = false;

            int desiredCount = SettingsManager.Instance.m_gameSettings.m_numberOfCharactersForHire;
            while (list.Count < desiredCount)
            {
                Entity entity = LopitalEntityFactory.CreateCharacterNurse(null, Vector2i.ZERO_VECTOR);
                if (!PorterIdentity.SetPorterOccupation(entity))
                {
                    Plugin.Log?.LogError(
                        "Porter candidate generation failed while converting the generated Nurse; candidate generation stopped.");
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
            s_loggedDatabaseUnavailable = false;
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
