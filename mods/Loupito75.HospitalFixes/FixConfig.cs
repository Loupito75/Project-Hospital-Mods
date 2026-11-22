using System;
using System.IO;
using System.Reflection;
using System.Xml;

namespace HospitalFixes
{
    internal static class FixConfig
    {
        private const string FileName = "Loupito75.HospitalFixes.Config.xml";

        internal static bool FixHiringPerkRevealCost = true;
        internal static bool FixMobileObjectGhosts = true;
        internal static bool FixCustomCharacterAge = true;
        internal static bool FixMovingObjectLighting = true;
        internal static bool RememberTransparencyPreferences = true;
        internal static bool PauseAfterLoadingSave = true;
        internal static bool PreserveBlockedMobileEquipment = true;
        internal static bool AllowElevatorExteriorWallDecorations = true;
        internal static bool PreventPrematureOutpatientExit = true;
        internal static bool PreventImmediateObservationReadmission = true;
        internal static bool RecoverMissingHospitalBed = true;
        internal static bool PreventPrematureTransportBedRelease = true;
        internal static bool FixHospitalizedProcedureEquipmentSelection = true;
        internal static bool FixIntrinsicWorkspaceRoles = true;
        internal static bool FixLabProcedureStatLabConsistency = true;
        internal static bool LabSpecialistSampleDestinationWaitEnabled = true;
        internal static int LabSpecialistSampleDestinationFridgeRetryMinutes = 2;
        internal static int LabSpecialistSampleDestinationFridgeContentionAttempts = 2;
        internal static bool LabSpecialistSampleDestinationFridgeShowPopup = true;
        internal static bool PreventDuplicateEmployeeDayStart = true;
        internal static bool AllowWallAndCeilingObjectsAboveGroundObjects = true;
        internal static bool ClarifyWaitingRoomNotifications = true;
        internal static bool FixPopupTextOverflow = true;
        internal static bool FixSupervisorDisplay = true;
        internal static bool FixPatientListHeading = true;
        internal static bool FixEmployeeListLevelDisplay = true;
        internal static bool FixWorkspaceEmployeeSelection = true;
        internal static bool PreventDuplicateJanitorCleaning = true;
        internal static bool ClinicalProcedureWaitingVisuals = true;
        internal static bool CollapseTransportNurseWaitingVisuals = true;
        internal static bool ClinicalProcedureThinkingVisuals = true;
        internal static bool FixCharacterVisualDepth = true;
        internal static bool FixAttachedPatientElevatorFade = true;
        internal static bool AvoidRedundantExamClothingChanges = true;
        internal static bool AnimatePatientClothingChanges = true;
        internal static bool AnimateExternalClothingChanges = true;
        internal static bool EnableDiagnostics = false;


        internal static void Load()
        {
            FixHiringPerkRevealCost = true;
            FixMobileObjectGhosts = true;
            FixCustomCharacterAge = true;
            FixMovingObjectLighting = true;
            RememberTransparencyPreferences = true;
            PauseAfterLoadingSave = true;
            PreserveBlockedMobileEquipment = true;
            AllowElevatorExteriorWallDecorations = true;
            PreventPrematureOutpatientExit = true;
            PreventImmediateObservationReadmission = true;
            RecoverMissingHospitalBed = true;
            PreventPrematureTransportBedRelease = true;
            FixHospitalizedProcedureEquipmentSelection = true;
            FixIntrinsicWorkspaceRoles = true;
            FixLabProcedureStatLabConsistency = true;
            LabSpecialistSampleDestinationWaitEnabled = true;
            LabSpecialistSampleDestinationFridgeRetryMinutes = 2;
            LabSpecialistSampleDestinationFridgeContentionAttempts = 2;
            LabSpecialistSampleDestinationFridgeShowPopup = true;
            PreventDuplicateEmployeeDayStart = true;
            AllowWallAndCeilingObjectsAboveGroundObjects = true;
            ClarifyWaitingRoomNotifications = true;
            FixPopupTextOverflow = true;
            FixSupervisorDisplay = true;
            FixPatientListHeading = true;
            FixEmployeeListLevelDisplay = true;
            FixWorkspaceEmployeeSelection = true;
            PreventDuplicateJanitorCleaning = true;
            ClinicalProcedureWaitingVisuals = true;
            CollapseTransportNurseWaitingVisuals = true;
            ClinicalProcedureThinkingVisuals = true;
            FixCharacterVisualDepth = true;
            FixAttachedPatientElevatorFade = true;
            AvoidRedundantExamClothingChanges = true;
            AnimatePatientClothingChanges = true;
            AnimateExternalClothingChanges = true;
            EnableDiagnostics = false;

            string folder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string path = Path.Combine(folder ?? string.Empty, FileName);
            if (!File.Exists(path))
            {
                Plugin.Log.LogWarning(
                    FileName +
                    " not found; fixes default to true and diagnostics default to false.");
                return;
            }

            try
            {
                XmlDocument document = new XmlDocument();
                document.XmlResolver = null;
                document.Load(path);
                if (document.DocumentType != null || document.DocumentElement == null ||
                    document.DocumentElement.Name != "HospitalFixesConfig")
                {
                    throw new FormatException("Invalid config root or DTD.");
                }

                XmlElement root = document.DocumentElement;

                FixHiringPerkRevealCost =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "FixHiringPerkRevealCost",
                        true);
                FixCustomCharacterAge =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "FixCustomCharacterAge",
                        true);
                RememberTransparencyPreferences =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "RememberTransparencyPreferences",
                        true);
                PauseAfterLoadingSave =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "PauseAfterLoadingSave",
                        true);
                ClarifyWaitingRoomNotifications =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "ClarifyWaitingRoomNotifications",
                        true);
                FixPopupTextOverflow =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "FixPopupTextOverflow",
                        true);
                FixSupervisorDisplay =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "FixSupervisorDisplay",
                        true);
                FixPatientListHeading =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "FixPatientListHeading",
                        true);
                FixEmployeeListLevelDisplay =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "FixEmployeeListLevelDisplay",
                        true);
                FixWorkspaceEmployeeSelection =
                    ReadGroupedBool(
                        root,
                        "InterfaceAndPreferences",
                        "FixWorkspaceEmployeeSelection",
                        true);

                FixMobileObjectGhosts =
                    ReadGroupedBool(
                        root,
                        "RenderingAndVisuals",
                        "FixMobileObjectGhosts",
                        true);
                FixMovingObjectLighting =
                    ReadGroupedBool(
                        root,
                        "RenderingAndVisuals",
                        "FixMovingObjectLighting",
                        true);
                FixCharacterVisualDepth =
                    ReadGroupedBool(
                        root,
                        "RenderingAndVisuals",
                        "FixCharacterVisualDepth",
                        true);
                FixAttachedPatientElevatorFade =
                    ReadGroupedBool(
                        root,
                        "RenderingAndVisuals",
                        "FixAttachedPatientElevatorFade",
                        true);
                PreserveBlockedMobileEquipment =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "PreserveBlockedMobileEquipment",
                        true);
                PreventPrematureOutpatientExit =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "PreventPrematureOutpatientExit",
                        true);
                PreventImmediateObservationReadmission =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "PreventImmediateObservationReadmission",
                        true);
                RecoverMissingHospitalBed =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "RecoverMissingHospitalBed",
                        true);
                PreventPrematureTransportBedRelease =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "PreventPrematureTransportBedRelease",
                        true);
                FixHospitalizedProcedureEquipmentSelection =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "FixHospitalizedProcedureEquipmentSelection",
                        true);
                FixIntrinsicWorkspaceRoles =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "FixIntrinsicWorkspaceRoles",
                        true);
                FixLabProcedureStatLabConsistency =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "FixLabProcedureStatLabConsistency",
                        true);
                ReadLabSpecialistSampleDestinationWait(
                    root);
                PreventDuplicateJanitorCleaning =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "PreventDuplicateJanitorCleaning",
                        true);
                ClinicalProcedureWaitingVisuals =
                    ReadGroupedBool(
                        root,
                        "ProcedureVisuals",
                        "ClinicalProcedureWaitingVisuals",
                        true);
                CollapseTransportNurseWaitingVisuals =
                    ReadGroupedBool(
                        root,
                        "ProcedureVisuals",
                        "CollapseTransportNurseWaitingVisuals",
                        true);
                ClinicalProcedureThinkingVisuals =
                    ReadGroupedBool(
                        root,
                        "ProcedureVisuals",
                        "ClinicalProcedureThinkingVisuals",
                        true);

                PreventDuplicateEmployeeDayStart =
                    ReadGroupedBool(
                        root,
                        "DailyStaff",
                        "PreventDuplicateEmployeeDayStart",
                        true);

                ReadConstructionPlacement(root);

                AvoidRedundantExamClothingChanges =
                    ReadGroupedBool(
                        root,
                        "Clothing",
                        "AvoidRedundantExamClothingChanges",
                        true);
                AnimatePatientClothingChanges =
                    ReadGroupedBool(
                        root,
                        "Clothing",
                        "AnimatePatientClothingChanges",
                        true);
                // Keep the public 1.0.0 XML key stable. The setting now
                // controls the producer-agnostic External Clothing Change API.
                AnimateExternalClothingChanges =
                    ReadGroupedBool(
                        root,
                        "Clothing",
                        "AnimateHshStaffClothingChanges",
                        true);

                EnableDiagnostics =
                    ReadGroupedBool(
                        root,
                        "Diagnostics",
                        "EnableDiagnostics",
                        false);
            }
            catch (Exception exception)
            {
                // On invalid XML, keep the safe, documented built-in defaults.
                FixHiringPerkRevealCost = true;
                FixMobileObjectGhosts = true;
                FixCustomCharacterAge = true;
                FixMovingObjectLighting = true;
                RememberTransparencyPreferences = true;
                PauseAfterLoadingSave = true;
                PreserveBlockedMobileEquipment = true;
                AllowElevatorExteriorWallDecorations = true;
                PreventPrematureOutpatientExit = true;
                PreventImmediateObservationReadmission = true;
                RecoverMissingHospitalBed = true;
                PreventPrematureTransportBedRelease = true;
                FixHospitalizedProcedureEquipmentSelection = true;
                FixIntrinsicWorkspaceRoles = true;
                FixLabProcedureStatLabConsistency = true;
                LabSpecialistSampleDestinationWaitEnabled = true;
                LabSpecialistSampleDestinationFridgeRetryMinutes = 2;
                LabSpecialistSampleDestinationFridgeContentionAttempts = 2;
                LabSpecialistSampleDestinationFridgeShowPopup = true;
                PreventDuplicateEmployeeDayStart = true;
                AllowWallAndCeilingObjectsAboveGroundObjects = true;
                ClarifyWaitingRoomNotifications = true;
                FixPopupTextOverflow = true;
                FixSupervisorDisplay = true;
                FixPatientListHeading = true;
                FixEmployeeListLevelDisplay = true;
                FixWorkspaceEmployeeSelection = true;
                PreventDuplicateJanitorCleaning = true;
                ClinicalProcedureWaitingVisuals = true;
                CollapseTransportNurseWaitingVisuals = true;
                ClinicalProcedureThinkingVisuals = true;
                FixCharacterVisualDepth = true;
                FixAttachedPatientElevatorFade = true;
                AvoidRedundantExamClothingChanges = true;
                AnimatePatientClothingChanges = true;
                AnimateExternalClothingChanges = true;
                EnableDiagnostics = false;
                Plugin.Log.LogWarning(
                    "Invalid " + FileName +
                    "; defaults retained: " + exception.Message);
            }

            if (EnableDiagnostics)
            {
                Plugin.Log.LogInfo(
                    "[Diagnostics] Fixes: Perks=" + FixHiringPerkRevealCost +
                ", Ghosts=" + FixMobileObjectGhosts +
                ", Age=" + FixCustomCharacterAge +
                ", Lighting=" + FixMovingObjectLighting +
                ", Transparency=" + RememberTransparencyPreferences +
                ", PauseAfterLoadingSave=" + PauseAfterLoadingSave +
                ", PreserveBlockedMobileEquipment=" + PreserveBlockedMobileEquipment +
                ", AllowElevatorExteriorWallDecorations=" + AllowElevatorExteriorWallDecorations +
                ", PreventPrematureOutpatientExit=" + PreventPrematureOutpatientExit +
                ", PreventImmediateObservationReadmission=" + PreventImmediateObservationReadmission +
                ", RecoverMissingHospitalBed=" + RecoverMissingHospitalBed +
                ", PreventPrematureTransportBedRelease=" + PreventPrematureTransportBedRelease +
                ", FixHospitalizedProcedureEquipmentSelection=" + FixHospitalizedProcedureEquipmentSelection +
                ", LabSpecialistSampleDestinationWaitEnabled=" + LabSpecialistSampleDestinationWaitEnabled +
                ", LabSpecialistSampleDestinationFridgeRetryMinutes=" + LabSpecialistSampleDestinationFridgeRetryMinutes +
                ", LabSpecialistSampleDestinationFridgeContentionAttempts=" + LabSpecialistSampleDestinationFridgeContentionAttempts +
                ", LabSpecialistSampleDestinationFridgeShowPopup=" + LabSpecialistSampleDestinationFridgeShowPopup +
                ", PreventDuplicateEmployeeDayStart=" + PreventDuplicateEmployeeDayStart +
                ", AllowWallAndCeilingObjectsAboveGroundObjects=" + AllowWallAndCeilingObjectsAboveGroundObjects +
                ", ClarifyWaitingRoomNotifications=" + ClarifyWaitingRoomNotifications +
                ", FixPopupTextOverflow=" + FixPopupTextOverflow +
                ", FixSupervisorDisplay=" + FixSupervisorDisplay +
                ", FixPatientListHeading=" + FixPatientListHeading +
                ", FixEmployeeListLevelDisplay=" + FixEmployeeListLevelDisplay +
                ", FixWorkspaceEmployeeSelection=" + FixWorkspaceEmployeeSelection +
                ", FixIntrinsicWorkspaceRoles=" + FixIntrinsicWorkspaceRoles +
                ", FixLabProcedureStatLabConsistency=" + FixLabProcedureStatLabConsistency +
                ", PreventDuplicateJanitorCleaning=" + PreventDuplicateJanitorCleaning +
                ", ClinicalProcedureWaitingVisuals=" + ClinicalProcedureWaitingVisuals +
                ", CollapseTransportNurseWaitingVisuals=" + CollapseTransportNurseWaitingVisuals +
                ", ClinicalProcedureThinkingVisuals=" + ClinicalProcedureThinkingVisuals +
                ", FixCharacterVisualDepth=" + FixCharacterVisualDepth +
                ", FixAttachedPatientElevatorFade=" + FixAttachedPatientElevatorFade +
                ", AvoidRedundantExamClothingChanges=" + AvoidRedundantExamClothingChanges +
                ", AnimatePatientClothingChanges=" + AnimatePatientClothingChanges +
                ", AnimateExternalClothingChanges=" + AnimateExternalClothingChanges +
                ", Diagnostics=" + EnableDiagnostics);
            }
        }


        internal static bool ExternalClothingChangeVisualsEnabled
        {
            get
            {
                return AnimateExternalClothingChanges;
            }
        }

        private static void ReadLabSpecialistSampleDestinationWait(
            XmlElement root)
        {
            XmlElement hospitalOperations =
                root.SelectSingleNode(
                    "HospitalOperations")
                as XmlElement;

            XmlElement wait =
                hospitalOperations == null
                    ? null
                    : hospitalOperations.SelectSingleNode(
                        "LabSpecialistSampleDestinationWait")
                        as XmlElement;

            if (wait == null)
            {
                // Compatibility with the short-lived internal 1.0.0.11 key.
                LabSpecialistSampleDestinationWaitEnabled =
                    ReadGroupedBool(
                        root,
                        "HospitalOperations",
                        "FixLabSpecialistSampleDestinationWait",
                        true);
                LabSpecialistSampleDestinationFridgeRetryMinutes = 2;
                LabSpecialistSampleDestinationFridgeContentionAttempts = 2;
                LabSpecialistSampleDestinationFridgeShowPopup = true;
                return;
            }

            LabSpecialistSampleDestinationWaitEnabled =
                ReadBool(
                    wait,
                    "Enabled",
                    true);
            LabSpecialistSampleDestinationFridgeRetryMinutes =
                ReadInt(
                    wait,
                    "RetryMinutes",
                    2,
                    1,
                    60);
            LabSpecialistSampleDestinationFridgeContentionAttempts =
                ReadInt(
                    wait,
                    "ContentionAttempts",
                    2,
                    1,
                    10);
            LabSpecialistSampleDestinationFridgeShowPopup =
                ReadBool(
                    wait,
                    "ShowEquipmentShortagePopup",
                    true);
        }

        private static void ReadConstructionPlacement(XmlElement root)
        {
            XmlElement construction =
                root.SelectSingleNode(
                    "Construction")
                as XmlElement;

            if (construction == null)
            {
                return;
            }

            AllowWallAndCeilingObjectsAboveGroundObjects =
                ReadBool(
                    construction,
                    "WallAndCeilingObjectsAboveGroundObjects",
                    true);
            AllowElevatorExteriorWallDecorations =
                ReadBool(
                    construction,
                    "ElevatorExteriorWallDecorations",
                    true);
        }

        private static bool ReadGroupedBool(
            XmlElement root,
            string groupName,
            string optionName,
            bool defaultValue)
        {
            XmlElement group =
                root.SelectSingleNode(
                    groupName)
                as XmlElement;

            if (group == null)
            {
                return defaultValue;
            }

            return ReadBool(
                group,
                optionName,
                defaultValue);
        }

        private static int ReadInt(
            XmlElement root,
            string name,
            int defaultValue,
            int min,
            int max)
        {
            XmlNode node =
                root.SelectSingleNode(name);

            if (node == null)
            {
                return defaultValue;
            }

            int parsed;
            if (!int.TryParse(
                    node.InnerText.Trim(),
                    out parsed) ||
                parsed < min ||
                parsed > max)
            {
                throw new FormatException(
                    name +
                    " must be between " +
                    min +
                    " and " +
                    max +
                    ".");
            }

            return parsed;
        }

        private static bool ReadBool(XmlElement root, string name, bool defaultValue)
        {
            XmlNode node = root.SelectSingleNode(name);
            if (node == null)
            {
                return defaultValue;
            }

            bool parsed;
            if (!bool.TryParse(node.InnerText.Trim(), out parsed))
            {
                throw new FormatException(name + " must be true or false.");
            }

            return parsed;
        }
    }
}
