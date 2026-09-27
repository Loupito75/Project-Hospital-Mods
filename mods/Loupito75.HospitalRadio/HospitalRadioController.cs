using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using GLib;
using Lopital;
using UnityEngine;

namespace HospitalRadio
{
    internal sealed class HospitalRadioController : MonoBehaviour
    {
        private sealed class MusicTrack
        {
            internal string Id;
            internal string DisplayName;
            internal string FilePath;
            internal GameDBSoundEvent VanillaEvent;
            internal string VanillaEventId;
            internal float LocalVolumeDb;

            internal bool IsLocalFile
            {
                get { return !string.IsNullOrEmpty(FilePath); }
            }
        }

        private const float VoiceDuckLevel = 0.25f;
        private const float DuckDownSpeed = 4f;
        private const float DuckUpSpeed = 5f;
        private const float VoiceLeadingSilenceSeconds = 1f;
        private const float VoiceDuckLeadSeconds = 0.25f;
        private const float VoiceTrailingSilenceSeconds = 1f;
        private const float TrackFadeDuration = 0.75f;
        private const float RadioFadeInDuration = 5f;
        private const float RadioFadeOutDuration = 4f;

        private static readonly string[] VanillaGameplayMusicEventIds =
        {
            "MUS_GAMEPLAY",
            "MUS_GAMEPLAY_NIGHT"
        };

        private static readonly string[] DiagnosticMusicIds =
        {
            "HR_M045",
            "HR_M040",
            "HR_M039",
            "HR_M007",
            "HR_M003",
            "HR_M031",
            "HR_M025",
            "HR_M027"
        };

        private static string s_pluginDirectory;

        private readonly List<string> _registeredClipKeys = new List<string>();
        private readonly List<AudioClip> _loadedClips = new List<AudioClip>();
        private readonly List<MusicTrack> _musicTracks = new List<MusicTrack>();
        private readonly Queue<int> _musicQueue = new Queue<int>();
        private readonly List<AnnouncementEntry> _announcementEntries = new List<AnnouncementEntry>();
        private readonly Queue<string> _recentAnnouncementIds = new Queue<string>();

        private SoundSourceComponent _musicSource;
        private SoundSourceComponent _voiceSource;
        private SoundEvent _musicPlayback;
        private SoundEvent _voicePlayback;

        private AudioClip _currentMusicClip;
        private string _currentMusicClipKey;
        private AudioClip _currentVoiceClip;
        private string _currentVoiceClipKey;
        private string _voiceDirectory;
        private int _currentTrackIndex = -1;
        private int _nextTrackIndex = -1;
        private int _preparedTrackIndex = -1;
        private AudioClip _preparedMusicClip;
        private bool _preparingNextTrack;
        private bool _trackTransitionInProgress;
        private int _preloadGeneration;
        private int _audioKeySequence;

        private bool _initializing;
        private bool _active;
        private bool _stopping;
        private float _musicDuck = 1f;
        private float _musicFade = 1f;
        private float _stopFadeTimeRemaining;
        private float _initializationRetryDelay;
        private bool _nativeEventConditionActive;
        private bool _nativeEventSkipped;
        private bool _nextTrackRequested;
        private bool _announcementLoading;
        private bool _announcementPending;
        private int _songsUntilAnnouncement;

        private bool _nativeMusicCaptured;
        private float _nativeIngameMultiplier = 1f;
        private float _nativeEventsMultiplier = 1f;
        private float _nativeNightMultiplier = 1f;

        internal static void Configure(string pluginDirectory)
        {
            s_pluginDirectory = pluginDirectory;
        }

        private void Update()
        {
            if (!_active)
            {
                if (_initializationRetryDelay > 0f)
                {
                    _initializationRetryDelay = Math.Max(0f, _initializationRetryDelay - Time.deltaTime);
                    return;
                }

                if (!_initializing && CanInitialize())
                {
                    StartCoroutine(InitializeRadio());
                }
                return;
            }

            if (_stopping)
            {
                _stopFadeTimeRemaining = Math.Max(0f, _stopFadeTimeRemaining - Time.deltaTime);
                MuteNativeMusic();

                if (_stopFadeTimeRemaining <= 0f)
                {
                    StopRadio();
                }
                return;
            }

            if (!IsGameplayMusicState())
            {
                BeginFadeOut();
                return;
            }

            MuteNativeMusic();

            if (_voicePlayback != null
                && !_voicePlayback.IsPlaying()
                && !_announcementLoading)
            {
                CleanupCurrentVoiceClip();
            }

            float nativeEventMusicMix = GetNativeEventMusicMix();
            UpdateNativeEventState(IsNativeEventConditionActive(), nativeEventMusicMix);

            _musicFade = Mathf.MoveTowards(
                _musicFade,
                1f,
                Time.deltaTime / RadioFadeInDuration);

            bool voicePlaying = IsVoiceActivelySpeaking();
            float target = voicePlaying ? VoiceDuckLevel : 1f;
            float speed = voicePlaying ? DuckDownSpeed : DuckUpSpeed;
            _musicDuck = Mathf.MoveTowards(_musicDuck, target, Time.deltaTime * speed);

            bool eventMusicLeading = IsNativeEventMusicLeading(nativeEventMusicMix);
            ApplyMusicVolume(eventMusicLeading ? nativeEventMusicMix : 0f);
            ApplyNativeEventMusicVolume(eventMusicLeading);
            ApplyVoiceVolume();

            if (HospitalRadioConfig.DiagnosticsEnabled
                && Input.GetKeyDown(KeyCode.F11))
            {
                PlayTestVoice();
            }

            if (Input.GetKeyDown(KeyCode.F9))
            {
                SkipCurrentTrack();
                return;
            }

            if (_announcementPending
                && !_announcementLoading
                && (_voicePlayback == null || !_voicePlayback.IsPlaying())
                && _musicPlayback != null
                && _musicPlayback.IsPlaying())
            {
                StartCoroutine(PlayAnnouncement(true));
            }

            if (_nextTrackRequested && !_trackTransitionInProgress)
            {
                _nextTrackRequested = false;
                StartCoroutine(StartNextTrack());
                return;
            }

            if (!eventMusicLeading && !_trackTransitionInProgress)
            {
                if (_musicPlayback != null && !_musicPlayback.IsPlaying())
                {
                    HandleNaturalTrackCompletion();
                    StartCoroutine(StartNextTrack());
                    return;
                }

                if (_musicPlayback == null)
                {
                    StartCoroutine(StartNextTrack());
                }
            }
        }

        private bool CanInitialize()
        {
            StreamingAssetManager assetManager = StreamingAssetManager.GetInstance();

            return HospitalRadioConfig.RadioEnabled
                && !string.IsNullOrEmpty(s_pluginDirectory)
                && Database.Instance.Loaded
                && assetManager != null
                && assetManager.m_audioReady
                && UISoundManager.sm_instance != null
                && IsGameplayMusicState();
        }

        private static bool IsGameplayMusicState()
        {
            UISoundManager manager = UISoundManager.sm_instance;
            if (manager == null)
            {
                return false;
            }

            UISoundManager.MenuMusicState state = manager.m_titleScreenMusicState;
            return state == UISoundManager.MenuMusicState.INGAME_MUSIC_PLAYING
                || state == UISoundManager.MenuMusicState.INGAME_MUSIC_FADEOUT
                || state == UISoundManager.MenuMusicState.NIGHT_MUSIC_PLAYING
                || state == UISoundManager.MenuMusicState.NIGHT_MUSIC_FADEOUT;
        }

        private IEnumerator InitializeRadio()
        {
            _initializing = true;

            string ourMusicDirectory = Path.Combine(Path.Combine(s_pluginDirectory, "Audio"), "Music");
            string myMusicDirectory = Path.Combine(Path.Combine(s_pluginDirectory, "Audio"), "MyMusic");
            _voiceDirectory = Path.Combine(Path.Combine(s_pluginDirectory, "Audio"), "Voice");

            BuildMusicTrackList(ourMusicDirectory, myMusicDirectory);

            if (_musicTracks.Count == 0)
            {
                Plugin.Log.LogWarning(
                    "No playable Hospital Radio music source was found. Vanilla music remains active.");
                _initializationRetryDelay = 30f;
                _initializing = false;
                yield break;
            }

            _announcementEntries.Clear();
            _recentAnnouncementIds.Clear();
            _announcementPending = false;
            _announcementLoading = false;
            _songsUntilAnnouncement = 0;

            if (HospitalRadioConfig.EffectiveAnnouncementsEnabled)
            {
                _announcementEntries.AddRange(AnnouncementCatalog.Load(_voiceDirectory));
                if (_announcementEntries.Count > 0)
                {
                    ResetAnnouncementCountdown();
                    LogDiagnostic(
                        "Announcements ready: " + _announcementEntries.Count +
                        "; first voice in " + _songsUntilAnnouncement + " completed song(s).");
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "Announcements are enabled, but no valid Hospital Radio announcement audio file was found. Music will continue without announcements.");
                }
            }

            _musicSource = new SoundSourceComponent(null, SoundPositioning.UI2D);
            _musicSource.m_persistent = true;
            _voiceSource = new SoundSourceComponent(null, SoundPositioning.UI2D);
            _voiceSource.m_persistent = true;

            _musicDuck = 1f;
            _musicFade = 0f;
            _active = true;
            _initializing = false;

            MuteNativeMusic();

            yield return StartCoroutine(StartNextTrack());

            if (!_active || _musicPlayback == null)
            {
                yield break;
            }

            Plugin.Log.LogInfo(
                "Hospital Radio playlist started with " + _musicTracks.Count + " available track(s).");

            LogDiagnostic("F9 skips music; F11 plays a period-aware announcement.");
        }

        private void BuildMusicTrackList(string ourMusicDirectory, string myMusicDirectory)
        {
            _musicTracks.Clear();
            _musicQueue.Clear();
            _currentTrackIndex = -1;
            _nextTrackIndex = -1;

            if (HospitalRadioConfig.DiagnosticsEnabled)
            {
                AddDiagnosticMusicTracks(GetAudioFiles(ourMusicDirectory));
                LogDiagnostic(
                    "Short-track pool active: " + _musicTracks.Count + "/" +
                    DiagnosticMusicIds.Length + " track(s).");
                return;
            }

            if (HospitalRadioConfig.OurMusicEnabled)
            {
                AddLocalMusicTracks(GetAudioFiles(ourMusicDirectory), "Hospital Radio");
            }

            if (HospitalRadioConfig.MyMusicEnabled)
            {
                AddLocalMusicTracks(GetAudioFiles(myMusicDirectory), "MyMusic");
            }

            if (HospitalRadioConfig.VanillaMusicEnabled)
            {
                AddVanillaMusicTracks();
            }
        }

        private void AddDiagnosticMusicTracks(string[] files)
        {
            foreach (string diagnosticId in DiagnosticMusicIds)
            {
                string matchedFile = null;

                foreach (string file in files)
                {
                    string fileName = Path.GetFileName(file);
                    if (fileName.StartsWith(
                        diagnosticId + "_",
                        StringComparison.OrdinalIgnoreCase))
                    {
                        matchedFile = file;
                        break;
                    }
                }

                if (matchedFile == null)
                {
                    Plugin.Log.LogWarning(
                        "Diagnostic music track " + diagnosticId + " was not found.");
                    continue;
                }

                AddLocalMusicTrack(matchedFile, "Hospital Radio");
            }
        }

        private void AddLocalMusicTracks(string[] files, string sourceName)
        {
            foreach (string file in files)
            {
                AddLocalMusicTrack(file, sourceName);
            }
        }

        private void AddLocalMusicTrack(string file, string sourceName)
        {
            MusicTrack track = new MusicTrack();
            track.Id = GetMusicId(file);
            track.DisplayName = sourceName + "/" + Path.GetFileName(file);
            track.FilePath = file;
            track.LocalVolumeDb = string.Equals(sourceName, "MyMusic", StringComparison.Ordinal)
                ? HospitalRadioConfig.MyMusicVolumeDb
                : HospitalRadioConfig.OurMusicVolumeDb;
            _musicTracks.Add(track);
        }

        private static string GetMusicId(string file)
        {
            string fileName = Path.GetFileNameWithoutExtension(file);
            if (fileName != null
                && fileName.StartsWith("HR_M", StringComparison.OrdinalIgnoreCase)
                && fileName.Length >= 7)
            {
                return fileName.Substring(0, 7).ToUpperInvariant();
            }

            return "LOCAL";
        }

        private void AddVanillaMusicTracks()
        {
            foreach (string eventId in VanillaGameplayMusicEventIds)
            {
                GameDBSoundEvent soundEvent = Database.Instance.GetEntry<GameDBSoundEvent>(eventId);
                if (soundEvent == null)
                {
                    Plugin.Log.LogWarning("Vanilla music event " + eventId + " was not found.");
                    continue;
                }

                if (soundEvent.AudioClips == null || soundEvent.AudioClips.Length == 0)
                {
                    Plugin.Log.LogWarning("Vanilla music event " + eventId + " has no audio clip.");
                    continue;
                }

                MusicTrack track = new MusicTrack();
                track.Id = eventId;
                track.DisplayName = "Vanilla/" + eventId;
                track.VanillaEvent = soundEvent;
                track.VanillaEventId = eventId;
                _musicTracks.Add(track);
            }
        }

        private IEnumerator StartNextTrack()
        {
            if (_trackTransitionInProgress || _musicTracks.Count == 0)
            {
                yield break;
            }

            _trackTransitionInProgress = true;

            if (_musicSource != null)
            {
                _musicSource.Stop();
            }

            CleanupCurrentMusicClip();

            int attemptsRemaining = _musicTracks.Count;
            while (attemptsRemaining-- > 0)
            {
                int trackIndex = TakeNextTrackIndex();
                MusicTrack track = _musicTracks[trackIndex];
                if (!IsTrackEligibleForCurrentShift(track))
                {
                    continue;
                }

                AudioClip localClip = null;
                string localClipKey = null;
                GameDBSoundEvent soundEvent = null;

                if (track.IsLocalFile)
                {
                    if (_preparedTrackIndex == trackIndex)
                    {
                        while (_preparingNextTrack)
                        {
                            yield return null;
                        }

                        localClip = _preparedMusicClip;
                        _preparedMusicClip = null;
                        _preparedTrackIndex = -1;
                    }

                    if (localClip == null)
                    {
                        yield return StartCoroutine(
                            LoadAudioFile(track.FilePath, "music", true, delegate(AudioClip clip) { localClip = clip; }));
                    }

                    if (!_active || _stopping)
                    {
                        ReleaseUnregisteredClip(localClip);
                        _trackTransitionInProgress = false;
                        yield break;
                    }

                    if (localClip == null)
                    {
                        Plugin.Log.LogWarning(
                            "Skipping unreadable Hospital Radio track " + Path.GetFileName(track.FilePath) + ".");
                        continue;
                    }

                    localClipKey = RegisterClip("Music", track.FilePath, localClip);
                    soundEvent = AudioEventFactory.Create(
                        "LOUPITO75_HR_MUSIC_" + _audioKeySequence,
                        localClipKey,
                        track.LocalVolumeDb);
                }
                else
                {
                    soundEvent = track.VanillaEvent;
                }

                _musicPlayback = _musicSource.PlaySoundEvent(
                    soundEvent,
                    SoundEventCategory.MUSIC,
                    1f,
                    0f,
                    false,
                    false);

                if (_musicPlayback == null)
                {
                    Plugin.Log.LogWarning("Could not start track " + track.DisplayName + ".");
                    ReleaseLocalMusicClip(localClipKey, localClip);
                    continue;
                }

                _currentTrackIndex = trackIndex;
                _currentMusicClip = localClip;
                _currentMusicClipKey = localClipKey;
                ApplyMusicVolumeImmediate();

                LogCurrentTrack(track);

                _trackTransitionInProgress = false;
                PrepareFollowingTrack();
                yield break;
            }

            _trackTransitionInProgress = false;
            Plugin.Log.LogError("No Hospital Radio playlist track could be started. Restoring vanilla music.");
            _initializationRetryDelay = 30f;
            StopRadio();
        }

        private void SkipCurrentTrack()
        {
            if (_trackTransitionInProgress || _stopping)
            {
                return;
            }

            float nativeEventMusicMix = GetNativeEventMusicMix();
            if (IsNativeEventMusicLeading(nativeEventMusicMix))
            {
                _nativeEventSkipped = true;
                ApplyNativeEventMusicVolume(false);
                LogDiagnostic("Skip MUS_EVENTS.");
            }
            else
            {
                string currentId = _currentTrackIndex >= 0 && _currentTrackIndex < _musicTracks.Count
                    ? _musicTracks[_currentTrackIndex].Id
                    : "music";
                LogDiagnostic("Skip " + currentId + ".");
            }

            StartCoroutine(StartNextTrack());
        }

        private int TakeNextTrackIndex()
        {
            if (_nextTrackIndex >= 0)
            {
                int preparedIndex = _nextTrackIndex;
                _nextTrackIndex = -1;
                return preparedIndex;
            }

            if (_musicQueue.Count == 0)
            {
                FillMusicQueue();
            }

            return _musicQueue.Dequeue();
        }

        private void FillMusicQueue()
        {
            List<int> indexes = new List<int>();
            for (int index = 0; index < _musicTracks.Count; index++)
            {
                indexes.Add(index);
            }

            for (int index = indexes.Count - 1; index > 0; index--)
            {
                int swapIndex = UnityEngine.Random.Range(0, index + 1);
                int value = indexes[index];
                indexes[index] = indexes[swapIndex];
                indexes[swapIndex] = value;
            }

            if (indexes.Count > 1 && indexes[0] == _currentTrackIndex)
            {
                int value = indexes[0];
                indexes[0] = indexes[1];
                indexes[1] = value;
            }

            foreach (int index in indexes)
            {
                _musicQueue.Enqueue(index);
            }
        }

        private void PrepareFollowingTrack()
        {
            if (_musicTracks.Count == 0 || _stopping)
            {
                return;
            }

            if (_musicQueue.Count == 0)
            {
                FillMusicQueue();
            }

            _nextTrackIndex = _musicQueue.Dequeue();
            MusicTrack nextTrack = _musicTracks[_nextTrackIndex];

            _preloadGeneration++;
            int generation = _preloadGeneration;

            ReleasePreparedMusicClip();

            if (nextTrack.IsLocalFile)
            {
                _preparedTrackIndex = _nextTrackIndex;
                StartCoroutine(PrepareLocalTrack(nextTrack, generation));
            }
        }

        private IEnumerator PrepareLocalTrack(MusicTrack track, int generation)
        {
            _preparingNextTrack = true;

            AudioClip clip = null;
            yield return StartCoroutine(
                LoadAudioFile(track.FilePath, "music", true, delegate(AudioClip loadedClip) { clip = loadedClip; }));

            if (generation != _preloadGeneration || !_active || _stopping)
            {
                ReleaseUnregisteredClip(clip);
                if (generation == _preloadGeneration)
                {
                    _preparingNextTrack = false;
                }
                yield break;
            }

            _preparedMusicClip = clip;
            _preparingNextTrack = false;
        }

        private static string[] GetAudioFiles(string directory)
        {
            if (!Directory.Exists(directory))
            {
                return new string[0];
            }

            string[] allFiles = Directory.GetFiles(directory);
            List<string> audioFiles = new List<string>();

            foreach (string file in allFiles)
            {
                string extension = Path.GetExtension(file);

                if (string.Equals(extension, ".ogg", StringComparison.OrdinalIgnoreCase))
                {
                    audioFiles.Add(file);
                    continue;
                }

                if (string.Equals(extension, ".wav", StringComparison.OrdinalIgnoreCase))
                {
                    string validationError;
                    if (TryValidateWaveFile(file, out validationError))
                    {
                        audioFiles.Add(file);
                    }
                    else
                    {
                        Plugin.Log.LogWarning(
                            "Skipping invalid WAV '" + Path.GetFileName(file) + "': " +
                            validationError);
                    }
                    continue;
                }

                if (string.Equals(extension, ".mp3", StringComparison.OrdinalIgnoreCase))
                {
                    Plugin.Log.LogWarning(
                        "Skipping unsupported MP3 '" + Path.GetFileName(file) +
                        "'. Project Hospital's Unity standalone audio loader supports OGG or WAV for external music.");
                }
            }

            audioFiles.Sort(StringComparer.OrdinalIgnoreCase);
            return audioFiles.ToArray();
        }

        private static bool TryValidateWaveFile(string filePath, out string error)
        {
            error = null;

            try
            {
                using (FileStream stream = File.OpenRead(filePath))
                {
                    if (stream.Length < 12)
                    {
                        error = "file is too short to contain a RIFF/WAVE header.";
                        return false;
                    }

                    using (BinaryReader reader = new BinaryReader(stream, Encoding.ASCII))
                    {
                        string container = Encoding.ASCII.GetString(reader.ReadBytes(4));
                        uint declaredSize = reader.ReadUInt32();
                        string format = Encoding.ASCII.GetString(reader.ReadBytes(4));

                        if (!string.Equals(container, "RIFF", StringComparison.Ordinal)
                            || !string.Equals(format, "WAVE", StringComparison.Ordinal))
                        {
                            error = "missing a standard RIFF/WAVE header.";
                            return false;
                        }

                        if (declaredSize == uint.MaxValue)
                        {
                            error = "RIFF header contains an invalid 0xFFFFFFFF size; re-export the file as a standard WAV or OGG.";
                            return false;
                        }

                        long expectedLength = (long)declaredSize + 8L;
                        if (expectedLength > stream.Length)
                        {
                            error =
                                "RIFF header expects " + expectedLength +
                                " bytes but the file contains only " + stream.Length +
                                " bytes; the WAV is truncated or was not finalized correctly.";
                            return false;
                        }
                    }
                }

                return true;
            }
            catch (Exception exception)
            {
                error = exception.GetType().Name + ": " + exception.Message;
                return false;
            }
        }

        private IEnumerator LoadAudioFile(
            string filePath,
            string kind,
            bool keepOggCompressed,
            Action<AudioClip> completed)
        {
            string uri = new Uri(filePath).AbsoluteUri;
            WWW request = new WWW(uri);
            yield return request;

            if (!string.IsNullOrEmpty(request.error))
            {
                Plugin.Log.LogError(
                    "Failed to load " + kind + " file '" + Path.GetFileName(filePath) + "': " + request.error);
                request.Dispose();
                completed(null);
                yield break;
            }

            AudioClip clip = null;
            string extension = Path.GetExtension(filePath).ToLowerInvariant();

            try
            {
                if (extension == ".ogg")
                {
                    clip = keepOggCompressed
                        ? request.GetAudioClipCompressed(false, AudioType.OGGVORBIS)
                        : request.GetAudioClip(false, false, AudioType.OGGVORBIS);
                }
                else if (extension == ".wav")
                {
                    clip = request.GetAudioClip(false, false, AudioType.WAV);
                }
                else
                {
                    Plugin.Log.LogWarning(
                        "Unsupported audio format for " + Path.GetFileName(filePath) + ".");
                }
            }
            catch (Exception exception)
            {
                Plugin.Log.LogError(
                    "Could not decode " + Path.GetFileName(filePath) + ": " +
                    exception.GetType().Name + ": " + exception.Message);
            }
            finally
            {
                request.Dispose();
            }

            if (clip == null)
            {
                Plugin.Log.LogError(
                    "Unity returned no AudioClip for " + Path.GetFileName(filePath) +
                    ". OGG or WAV is recommended if this format is not supported by the game runtime.");
                completed(null);
                yield break;
            }

            clip.name = "HospitalRadio-" + Path.GetFileNameWithoutExtension(filePath);
            _loadedClips.Add(clip);
            completed(clip);
        }

        private string RegisterClip(string group, string filePath, AudioClip clip)
        {
            _audioKeySequence++;
            string key =
                "Loupito75/HospitalRadio/" + group + "/" +
                _audioKeySequence + "_" + Path.GetFileName(filePath);

            StreamingAssetManager manager = StreamingAssetManager.GetInstance();
            manager.m_audioClips[key] = clip;
            _registeredClipKeys.Add(key);
            return key;
        }

        private void ResetAnnouncementCountdown()
        {
            int minimum = HospitalRadioConfig.AnnouncementMinSongsBetween;
            int maximum = HospitalRadioConfig.AnnouncementMaxSongsBetween;
            _songsUntilAnnouncement = UnityEngine.Random.Range(minimum, maximum + 1);
        }

        private void HandleNaturalTrackCompletion()
        {
            if (!HospitalRadioConfig.EffectiveAnnouncementsEnabled
                || _announcementEntries.Count == 0
                || _announcementPending)
            {
                return;
            }

            _songsUntilAnnouncement--;
            if (_songsUntilAnnouncement <= 0)
            {
                _announcementPending = true;
                LogDiagnostic("Voice queued for next playing song.");
            }
            else
            {
                LogDiagnostic(
                    "Voice in " + _songsUntilAnnouncement + " completed song(s).");
            }
        }

        private string GetCurrentAnnouncementPeriod()
        {
            DayTime dayTime = DayTime.Instance;
            if (dayTime == null)
            {
                return "ANY";
            }

            float hour = dayTime.GetDayTimeHours();
            if (hour >= HospitalRadioConfig.NightStartHour
                || hour < HospitalRadioConfig.MorningStartHour)
            {
                return "NIGHT";
            }

            if (hour >= HospitalRadioConfig.EveningStartHour)
            {
                return "EVENING";
            }

            if (hour >= HospitalRadioConfig.AfternoonStartHour)
            {
                return "AFTERNOON";
            }

            return "MORNING";
        }

        private AnnouncementEntry SelectAnnouncement()
        {
            string period = GetCurrentAnnouncementPeriod();
            List<AnnouncementEntry> candidates = AnnouncementCatalog.GetCandidates(
                _announcementEntries,
                period,
                _recentAnnouncementIds);

            if (candidates.Count == 0)
            {
                return null;
            }

            return candidates[UnityEngine.Random.Range(0, candidates.Count)];
        }

        private IEnumerator PlayAnnouncement(bool automatic)
        {
            if (_announcementLoading
                || _voiceSource == null
                || _announcementEntries.Count == 0
                || (_voicePlayback != null && _voicePlayback.IsPlaying()))
            {
                yield break;
            }

            AnnouncementEntry entry = SelectAnnouncement();
            if (entry == null)
            {
                Plugin.Log.LogWarning(
                    "No announcement is available for the current period.");
                if (automatic)
                {
                    _announcementPending = false;
                    ResetAnnouncementCountdown();
                }
                yield break;
            }

            _announcementLoading = true;

            AudioClip voiceClip = null;
            yield return StartCoroutine(
                LoadAudioFile(
                    entry.FilePath,
                    "announcement",
                    false,
                    delegate(AudioClip clip) { voiceClip = clip; }));

            if (!_active || _stopping)
            {
                ReleaseUnregisteredClip(voiceClip);
                _announcementLoading = false;
                yield break;
            }

            if (voiceClip == null)
            {
                Plugin.Log.LogWarning(
                    "Announcement " + entry.Id + " could not be loaded.");
                _announcementLoading = false;

                if (automatic)
                {
                    _announcementPending = false;
                    ResetAnnouncementCountdown();
                }
                yield break;
            }

            string voiceKey = RegisterClip("Voice", entry.FilePath, voiceClip);
            GameDBSoundEvent voiceEvent = AudioEventFactory.Create(
                "LOUPITO75_HR_ANNOUNCEMENT_" + entry.Id + "_" + _audioKeySequence,
                voiceKey,
                HospitalRadioConfig.AnnouncementVolumeDb);

            SoundEvent playback = _voiceSource.PlaySoundEvent(
                voiceEvent,
                SoundEventCategory.MUSIC,
                1f,
                0f,
                false,
                false);

            if (playback == null)
            {
                ReleaseLocalMusicClip(voiceKey, voiceClip);
                _announcementLoading = false;

                if (automatic)
                {
                    _announcementPending = false;
                    ResetAnnouncementCountdown();
                }
                yield break;
            }

            _currentVoiceClip = voiceClip;
            _currentVoiceClipKey = voiceKey;
            _voicePlayback = playback;
            AddRecentAnnouncement(entry.Id);
            ApplyVoiceVolume();

            if (automatic)
            {
                _announcementPending = false;
                ResetAnnouncementCountdown();
            }

            string voiceId = Path.GetFileNameWithoutExtension(entry.FilePath);
            string mode = automatic ? "auto" : "F11";
            string next = automatic
                ? "; next in " + _songsUntilAnnouncement + " song(s)"
                : string.Empty;

            LogDiagnostic(
                "Voice " + voiceId + " [" + entry.Period + "] " + mode + next + ".");

            _announcementLoading = false;
        }

        private void AddRecentAnnouncement(string id)
        {
            int historyLimit = HospitalRadioConfig.AnnouncementRecentHistory;
            if (historyLimit <= 0)
            {
                return;
            }

            _recentAnnouncementIds.Enqueue(id);
            while (_recentAnnouncementIds.Count > historyLimit)
            {
                _recentAnnouncementIds.Dequeue();
            }
        }

        private bool IsVoiceActivelySpeaking()
        {
            if (_voicePlayback == null || !_voicePlayback.IsPlaying())
            {
                return false;
            }

            float duckStart = Math.Max(0f, VoiceLeadingSilenceSeconds - VoiceDuckLeadSeconds);
            float remaining = _voicePlayback.m_duration - _voicePlayback.m_playbackTime;

            return _voicePlayback.m_playbackTime >= duckStart
                && remaining > VoiceTrailingSilenceSeconds;
        }

        private void PlayTestVoice()
        {
            if (!HospitalRadioConfig.DiagnosticsEnabled)
            {
                return;
            }

            if (!HospitalRadioConfig.EffectiveAnnouncementsEnabled
                || _announcementEntries.Count == 0)
            {
                Plugin.Log.LogWarning("No Hospital Radio announcement catalog is available.");
                return;
            }

            if (_announcementLoading
                || (_voicePlayback != null && _voicePlayback.IsPlaying()))
            {
                return;
            }

            if (_announcementPending)
            {
                _announcementPending = false;
                ResetAnnouncementCountdown();
            }

            StartCoroutine(PlayAnnouncement(false));
        }

        private void ApplyMusicVolume(float nativeEventMusicMix)
        {
            if (_musicSource != null)
            {
                _musicSource.m_scriptedVolumeMultiplier =
                    _musicFade * GetTrackFadeMultiplier() * _musicDuck * (1f - nativeEventMusicMix);
                _musicSource.InterpolateState(true);
            }
        }

        private float GetTrackFadeMultiplier()
        {
            if (_musicPlayback == null || !_musicPlayback.IsPlaying() || TrackFadeDuration <= 0f)
            {
                return 1f;
            }

            float fadeIn = Mathf.Clamp01(_musicPlayback.m_playbackTime / TrackFadeDuration);
            float remaining = _musicPlayback.m_duration - _musicPlayback.m_playbackTime;
            float fadeOut = Mathf.Clamp01(remaining / TrackFadeDuration);
            return Math.Min(fadeIn, fadeOut);
        }

        private void ApplyVoiceVolume()
        {
            if (_voiceSource != null)
            {
                _voiceSource.m_scriptedVolumeMultiplier = 1f;
                _voiceSource.InterpolateState(true);
            }
        }

        private void ApplyNativeEventMusicVolume(bool eventMusicLeading)
        {
            UISoundManager manager = UISoundManager.sm_instance;
            if (manager == null || manager.m_musicSoundSourceEvents == null)
            {
                return;
            }

            manager.m_musicSoundSourceEvents.m_scriptedVolumeMultiplier =
                eventMusicLeading ? _nativeEventsMultiplier * _musicDuck : 0f;

            manager.m_musicSoundSourceEvents.InterpolateState(true);
        }

        private void ApplyMusicVolumeImmediate()
        {
            if (_musicSource == null)
            {
                return;
            }

            float nativeEventMusicMix = GetNativeEventMusicMix();
            bool eventMusicLeading = IsNativeEventMusicLeading(nativeEventMusicMix);
            _musicSource.m_scriptedVolumeMultiplier =
                _musicFade
                * GetTrackFadeMultiplier()
                * _musicDuck
                * (1f - (eventMusicLeading ? nativeEventMusicMix : 0f));
            _musicSource.InterpolateState(true);

            if (_musicPlayback != null && _musicPlayback.m_audioSource != null)
            {
                _musicPlayback.m_audioSource.volume =
                    _musicPlayback.m_volume
                    * _musicPlayback.m_categoryVolume
                    * _musicSource.m_volumeMultiplier
                    * _musicSource.m_gameStateVolumeMultiplier;
            }

            ApplyVoiceVolume();
            ApplyNativeEventMusicVolume(eventMusicLeading);
        }

        private static void LogDiagnostic(string message)
        {
            if (HospitalRadioConfig.DiagnosticsEnabled)
            {
                Plugin.Log.LogInfo("[Diag] " + message);
            }
        }

        private static void LogCurrentTrack(MusicTrack track)
        {
            if (!HospitalRadioConfig.DiagnosticsEnabled || track == null)
            {
                return;
            }

            string title = track.DisplayName;
            if (track.IsLocalFile)
            {
                title = Path.GetFileNameWithoutExtension(track.FilePath);
                string prefix = track.Id + "_";
                if (title.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    title = title.Substring(prefix.Length);
                }
            }

            Plugin.Log.LogInfo(
                "[Diag] Music " + track.Id + " | " + title + ".");
        }

        private static bool IsTrackEligibleForCurrentShift(MusicTrack track)
        {
            if (track == null || track.IsLocalFile || string.IsNullOrEmpty(track.VanillaEventId))
            {
                return true;
            }

            DayTime dayTime = DayTime.Instance;
            if (dayTime == null)
            {
                return true;
            }

            Shift shift = dayTime.GetShift();
            if (string.Equals(track.VanillaEventId, "MUS_GAMEPLAY_NIGHT", StringComparison.Ordinal))
            {
                return shift == Shift.NIGHT;
            }

            if (string.Equals(track.VanillaEventId, "MUS_GAMEPLAY", StringComparison.Ordinal))
            {
                return shift == Shift.DAY;
            }

            return true;
        }

        private void UpdateNativeEventState(bool conditionActive, float nativeEventMusicMix)
        {
            if (conditionActive && !_nativeEventConditionActive)
            {
                _nativeEventConditionActive = true;
                _nativeEventSkipped = false;
                LogDiagnostic("Music MUS_EVENTS | event started.");
            }
            else if (!conditionActive && _nativeEventConditionActive)
            {
                _nativeEventConditionActive = false;

                if (!_nativeEventSkipped)
                {
                    _nextTrackRequested = true;
                    LogDiagnostic("Music MUS_EVENTS | event ended; advancing.");
                }
                else
                {
                    LogDiagnostic("Music MUS_EVENTS | event ended after skip.");
                }
            }

            if (!_nativeEventConditionActive
                && _nativeEventSkipped
                && nativeEventMusicMix <= 0.01f)
            {
                _nativeEventSkipped = false;
            }
        }

        private static bool IsNativeEventConditionActive()
        {
            UISoundManager manager = UISoundManager.sm_instance;
            if (manager == null
                || manager.m_titleScreenMusicState != UISoundManager.MenuMusicState.INGAME_MUSIC_PLAYING)
            {
                return false;
            }

            GlobalScriptManager scriptManager = GlobalScriptManager.GetInstance();
            Hospital hospital = Hospital.Instance;

            return (scriptManager != null && scriptManager.GetEventCount() > 0)
                || (hospital != null && hospital.GetCollapsedPatientCount() > 0);
        }

        private bool IsNativeEventMusicLeading(float nativeEventMusicMix)
        {
            return !_nativeEventSkipped
                && (_nativeEventConditionActive || nativeEventMusicMix > 0.01f);
        }

        private static float GetNativeEventMusicMix()
        {
            UISoundManager manager = UISoundManager.sm_instance;
            if (manager == null
                || manager.m_titleScreenMusicState != UISoundManager.MenuMusicState.INGAME_MUSIC_PLAYING
                || manager.m_musicSoundSourceEvents == null)
            {
                return 0f;
            }

            return Mathf.Clamp01(manager.m_musicSoundSourceEvents.m_requestedVolumeMultiplier);
        }

        private void MuteNativeMusic()
        {
            UISoundManager manager = UISoundManager.sm_instance;
            if (manager == null)
            {
                return;
            }

            if (!_nativeMusicCaptured)
            {
                _nativeIngameMultiplier = manager.m_musicSoundSourceIngame != null
                    ? manager.m_musicSoundSourceIngame.m_scriptedVolumeMultiplier
                    : 1f;
                _nativeEventsMultiplier = manager.m_musicSoundSourceEvents != null
                    ? manager.m_musicSoundSourceEvents.m_scriptedVolumeMultiplier
                    : 1f;
                _nativeNightMultiplier = manager.m_musicSoundSourceNight != null
                    ? manager.m_musicSoundSourceNight.m_scriptedVolumeMultiplier
                    : 1f;
                _nativeMusicCaptured = true;
            }

            SetNativeMusicMultipliers(0f, _nativeEventsMultiplier, 0f);
        }

        private void RestoreNativeMusic()
        {
            if (!_nativeMusicCaptured)
            {
                return;
            }

            SetNativeMusicMultipliers(
                _nativeIngameMultiplier,
                _nativeEventsMultiplier,
                _nativeNightMultiplier);
            _nativeMusicCaptured = false;
        }

        private static void SetNativeMusicMultipliers(float ingame, float events, float night)
        {
            UISoundManager manager = UISoundManager.sm_instance;
            if (manager == null)
            {
                return;
            }

            if (manager.m_musicSoundSourceIngame != null)
            {
                manager.m_musicSoundSourceIngame.m_scriptedVolumeMultiplier = ingame;
            }
            if (manager.m_musicSoundSourceEvents != null)
            {
                manager.m_musicSoundSourceEvents.m_scriptedVolumeMultiplier = events;
            }
            if (manager.m_musicSoundSourceNight != null)
            {
                manager.m_musicSoundSourceNight.m_scriptedVolumeMultiplier = night;
            }
        }

        private void BeginFadeOut()
        {
            if (_stopping)
            {
                return;
            }

            _stopping = true;
            _preloadGeneration++;
            _stopFadeTimeRemaining = RadioFadeOutDuration;

            if (_musicSource != null)
            {
                _musicSource.FadeOutAll(RadioFadeOutDuration);
            }

            if (_voiceSource != null)
            {
                _voiceSource.FadeOutAll(RadioFadeOutDuration);
            }

            LogDiagnostic("Radio fade-out started.");
        }

        private void CleanupCurrentVoiceClip()
        {
            if (_voiceSource != null)
            {
                _voiceSource.Stop();
            }

            ReleaseLocalMusicClip(_currentVoiceClipKey, _currentVoiceClip);
            _currentVoiceClipKey = null;
            _currentVoiceClip = null;
            _voicePlayback = null;
        }

        private void CleanupCurrentMusicClip()
        {
            ReleaseLocalMusicClip(_currentMusicClipKey, _currentMusicClip);
            _currentMusicClipKey = null;
            _currentMusicClip = null;
            _musicPlayback = null;
        }

        private void ReleasePreparedMusicClip()
        {
            if (_preparedMusicClip != null)
            {
                ReleaseUnregisteredClip(_preparedMusicClip);
                _preparedMusicClip = null;
            }

            _preparedTrackIndex = -1;
            _preparingNextTrack = false;
        }

        private void ReleaseLocalMusicClip(string clipKey, AudioClip clip)
        {
            if (!string.IsNullOrEmpty(clipKey))
            {
                StreamingAssetManager manager = StreamingAssetManager.GetInstance();
                if (manager != null)
                {
                    manager.m_audioClips.Remove(clipKey);
                }
                _registeredClipKeys.Remove(clipKey);
            }

            ReleaseUnregisteredClip(clip);
        }

        private void ReleaseUnregisteredClip(AudioClip clip)
        {
            if (clip == null)
            {
                return;
            }

            _loadedClips.Remove(clip);
            Destroy(clip);
        }

        private void StopRadio()
        {
            _active = false;
            _stopping = false;
            _trackTransitionInProgress = false;
            _preloadGeneration++;
            _musicDuck = 1f;
            _musicFade = 1f;
            _stopFadeTimeRemaining = 0f;
            _nativeEventConditionActive = false;
            _nativeEventSkipped = false;
            _nextTrackRequested = false;
            _announcementLoading = false;
            _announcementPending = false;
            _songsUntilAnnouncement = 0;
            _nextTrackIndex = -1;
            _musicQueue.Clear();
            _recentAnnouncementIds.Clear();

            if (_musicSource != null)
            {
                _musicSource.Stop();
            }

            CleanupCurrentMusicClip();
            ReleasePreparedMusicClip();

            if (_musicSource != null)
            {
                _musicSource.Destroy();
                _musicSource = null;
            }

            CleanupCurrentVoiceClip();

            if (_voiceSource != null)
            {
                _voiceSource.Destroy();
                _voiceSource = null;
            }

            StreamingAssetManager manager = StreamingAssetManager.GetInstance();
            if (manager != null)
            {
                foreach (string key in _registeredClipKeys)
                {
                    manager.m_audioClips.Remove(key);
                }
            }
            _registeredClipKeys.Clear();

            foreach (AudioClip clip in _loadedClips)
            {
                if (clip != null)
                {
                    Destroy(clip);
                }
            }
            _loadedClips.Clear();
            _musicTracks.Clear();
            _announcementEntries.Clear();
            _voiceDirectory = null;

            RestoreNativeMusic();
        }

        private void OnDestroy()
        {
            StopRadio();
        }
    }
}
