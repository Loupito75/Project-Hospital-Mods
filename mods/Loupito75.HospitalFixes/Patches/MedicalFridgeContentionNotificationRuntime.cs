using System;
using System.Collections.Generic;
using Lopital;

namespace HospitalFixes.Patches
{
    internal static class MedicalFridgeContentionNotificationRuntime
    {
        private static readonly Dictionary<Room, int>
            NotificationDays =
                new Dictionary<Room, int>();

        internal static bool TryGetContentionContext(
            Room statLab,
            out TileObject contextFridge)
        {
            contextFridge = null;

            if (statLab == null ||
                statLab.m_roomPersistentData == null ||
                statLab.m_roomPersistentData.m_roomType == null ||
                statLab.m_roomPersistentData.m_roomType.Entry == null ||
                !statLab.m_roomPersistentData
                    .m_roomType.Entry.HasTag("stat_lab") ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            List<TileObject> fridges =
                MapScriptInterface.Instance.FindAllObjectWithTags(
                    statLab,
                    new string[] { "medical_fridge" },
                    AccessRights.STAFF);

            if (fridges == null ||
                fridges.Count == 0)
            {
                return false;
            }

            for (int i = 0;
                 i < fridges.Count;
                 i++)
            {
                TileObject fridge =
                    fridges[i];

                if (fridge == null ||
                    fridge.m_state == null ||
                    fridge.IsBroken() ||
                    !fridge.IsValid())
                {
                    continue;
                }

                if (contextFridge == null)
                {
                    contextFridge =
                        fridge;
                }

                if (fridge.User == null &&
                    fridge.Owner == null)
                {
                    contextFridge = null;
                    return false;
                }
            }

            return contextFridge != null;
        }

        internal static bool TryShow(
            TileObject unavailableFridge)
        {
            if (unavailableFridge == null ||
                unavailableFridge.m_state == null ||
                !unavailableFridge.HasTag("medical_fridge") ||
                MapScriptInterface.Instance == null)
            {
                return false;
            }

            Room statLab =
                MapScriptInterface.Instance.GetRoomAt(
                    unavailableFridge.m_state.m_position,
                    unavailableFridge.GetFloorIndex());

            TileObject contextFridge;
            if (!TryGetContentionContext(
                    statLab,
                    out contextFridge))
            {
                return false;
            }

            int day =
                DayTime.Instance == null
                    ? -1
                    : DayTime.Instance.GetDay();

            int notifiedDay;
            if (day >= 0 &&
                NotificationDays.TryGetValue(
                    statLab,
                    out notifiedDay) &&
                notifiedDay == day)
            {
                return true;
            }

            NotificationManager notifications =
                NotificationManager.GetInstance();
            Database database =
                Database.Instance;

            if (notifications == null ||
                database == null)
            {
                return false;
            }

            StringTable strings =
                StringTable.GetInstance();
            GameDBObject fridgeEntry =
                database.GetEntry<GameDBObject>(
                    "OBJECT_STAT_LAB_FRIDGE");

            string equipmentName =
                strings == null ||
                fridgeEntry == null
                    ? "medical refrigerator"
                    : strings.GetLocalizedText(
                        fridgeEntry);

            string labName =
                statLab.m_roomPersistentData == null ||
                statLab.m_roomPersistentData.m_roomType == null ||
                statLab.m_roomPersistentData.m_roomType.Entry == null
                    ? "laboratory"
                    : strings == null
                        ? statLab.m_roomPersistentData
                            .m_roomType.Entry.DatabaseID.ToString()
                        : strings.GetLocalizedText(
                            statLab.m_roomPersistentData
                                .m_roomType.Entry);

            int icon =
                fridgeEntry == null
                    ? 0
                    : fridgeEntry.IconIndex;

            notifications.AddMessage(
                null,
                HfxLocalizationIds
                    .LabEquipmentShortageNotification,
                equipmentName,
                labName,
                string.Empty,
                icon,
                contextFridge.m_state.m_position.m_x,
                contextFridge.m_state.m_position.m_y,
                contextFridge.GetFloorIndex());

            if (day >= 0)
            {
                NotificationDays[statLab] =
                    day;
            }

            if (HfxDiagnostics.Enabled)
            {
                HfxDiagnostics.Result(
                    "LabSampleDestinationWait",
                    "medical fridge contention",
                    "popup-shown",
                    "lab=" +
                    statLab.m_roomPersistentData
                        .m_roomType.Entry.DatabaseID +
                    " day=" +
                    day);
            }

            return true;
        }

        internal static void ClearAll()
        {
            NotificationDays.Clear();
        }
    }
}
