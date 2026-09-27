using System;
using System.Reflection;
using HarmonyLib;

namespace HospitalRadio
{
    internal static class AudioEventFactory
    {
        private static readonly PropertyInfo AudioClipsProperty = AccessTools.Property(typeof(GameDBSoundEvent), "AudioClips");
        private static readonly PropertyInfo SoundBusRefProperty = AccessTools.Property(typeof(GameDBSoundEvent), "SoundBusRef");
        private static readonly PropertyInfo VolumeDbProperty = AccessTools.Property(typeof(GameDBSoundEvent), "VolumeDB");

        internal static GameDBSoundEvent Create(string databaseId, string audioClipKey, float volumeDb)
        {
            GameDBSoundBus bus = Database.Instance.GetEntry<GameDBSoundBus>("SOUND_BUS_UI");
            if (bus == null)
            {
                throw new InvalidOperationException("SOUND_BUS_UI is not available.");
            }

            if (AudioClipsProperty == null || SoundBusRefProperty == null || VolumeDbProperty == null)
            {
                throw new MissingMemberException("GameDBSoundEvent properties could not be resolved.");
            }

            GameDBSoundEvent soundEvent = new GameDBSoundEvent();
            soundEvent.DatabaseID = ID.CreateID(databaseId);

            AudioClipsProperty.SetValue(soundEvent, new string[] { audioClipKey }, null);
            SoundBusRefProperty.SetValue(soundEvent, new DatabaseEntryRef<GameDBSoundBus>(bus), null);
            VolumeDbProperty.SetValue(soundEvent, volumeDb, null);

            return soundEvent;
        }
    }
}
