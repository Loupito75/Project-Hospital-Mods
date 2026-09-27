using System;
using System.Collections.Generic;
using System.IO;

namespace HospitalRadio
{
    internal sealed class AnnouncementEntry
    {
        internal string Id;
        internal string Period;
        internal string FilePath;
    }

    internal static class AnnouncementCatalog
    {
        internal static List<AnnouncementEntry> Load(string voiceDirectory)
        {
            List<AnnouncementEntry> entries = new List<AnnouncementEntry>();

            AddRange(entries, voiceDirectory, 1, 35, "ANY");
            AddRange(entries, voiceDirectory, 36, 50, "MORNING");
            AddRange(entries, voiceDirectory, 51, 70, "AFTERNOON");
            AddRange(entries, voiceDirectory, 71, 85, "EVENING");
            AddRange(entries, voiceDirectory, 86, 100, "NIGHT");

            return entries;
        }

        internal static List<AnnouncementEntry> GetCandidates(
            List<AnnouncementEntry> entries,
            string period,
            Queue<string> recentIds)
        {
            List<AnnouncementEntry> candidates = new List<AnnouncementEntry>();

            foreach (AnnouncementEntry entry in entries)
            {
                if (!string.Equals(entry.Period, "ANY", StringComparison.Ordinal)
                    && !string.Equals(entry.Period, period, StringComparison.Ordinal))
                {
                    continue;
                }

                if (ContainsRecent(recentIds, entry.Id))
                {
                    continue;
                }

                candidates.Add(entry);
            }

            if (candidates.Count > 0)
            {
                return candidates;
            }

            foreach (AnnouncementEntry entry in entries)
            {
                if (string.Equals(entry.Period, "ANY", StringComparison.Ordinal)
                    || string.Equals(entry.Period, period, StringComparison.Ordinal))
                {
                    candidates.Add(entry);
                }
            }

            return candidates;
        }

        private static void AddRange(
            List<AnnouncementEntry> entries,
            string voiceDirectory,
            int first,
            int last,
            string period)
        {
            for (int number = first; number <= last; number++)
            {
                string suffix = number.ToString("000");
                string id = "HR_" + suffix;
                string fileName = "HR_V" + suffix + ".ogg";
                string filePath = Path.Combine(voiceDirectory, fileName);

                if (!File.Exists(filePath))
                {
                    Plugin.Log.LogWarning(
                        "Announcement audio file is missing for " + id + ": " + fileName + ".");
                    continue;
                }

                AnnouncementEntry entry = new AnnouncementEntry();
                entry.Id = id;
                entry.Period = period;
                entry.FilePath = filePath;
                entries.Add(entry);
            }
        }

        private static bool ContainsRecent(Queue<string> recentIds, string id)
        {
            foreach (string recentId in recentIds)
            {
                if (string.Equals(recentId, id, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
