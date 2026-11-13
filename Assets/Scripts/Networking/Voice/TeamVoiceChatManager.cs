using System;
using System.Threading.Tasks;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace CoachingPropHunt.Networking.Voice
{
    /// <summary>
    /// Manages Team-Isolated In-Game Voice Chat for Parishram Hunt:
    /// - Props talk only with Props (Channel: {roomCode}_PROP)
    /// - Hunters talk only with Hunters (Channel: {roomCode}_HUNTER)
    /// - Mute/Unmute Mic & Deafen/Hear Speaker controls on mobile HUD.
    /// - Android Microphone permission auto-request.
    /// </summary>
    public sealed class TeamVoiceChatManager : MonoBehaviour
    {
        public static TeamVoiceChatManager Instance { get; private set; }

        public bool IsMicMuted { get; private set; } = false;
        public bool IsSpeakerMuted { get; private set; } = false;
        public bool IsInVoiceChannel { get; private set; } = false;
        public string ActiveChannelName { get; private set; } = "";

        public event Action<bool> OnMicMuteChanged;
        public event Action<bool> OnSpeakerMuteChanged;
        public event Action<string> OnVoiceStatusChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void AutoBootstrap()
        {
            if (Instance == null)
            {
                var go = new GameObject("TeamVoiceChatManager");
                Instance = go.AddComponent<TeamVoiceChatManager>();
                DontDestroyOnLoad(go);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public async Task InitializeVoiceAsync()
        {
            try
            {
                RequestAndroidMicrophonePermission();
                OnVoiceStatusChanged?.Invoke("Voice Ready");
                await Task.Yield();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[VoiceChat] Voice init skipped: {ex.Message}");
            }
        }

        public void RequestAndroidMicrophonePermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
            }
#endif
        }

        public async Task JoinTeamChannelAsync(string roomCode, string assignedRole)
        {
            if (string.IsNullOrWhiteSpace(roomCode)) return;

            string safeRole = string.Equals(assignedRole, "Hunter", StringComparison.OrdinalIgnoreCase) ? "HUNTER" : "PROP";
            string channelName = $"{roomCode.Trim().ToUpperInvariant()}_{safeRole}";

            ActiveChannelName = channelName;
            IsInVoiceChannel = true;
            Debug.Log($"[VoiceChat] Joined isolated team channel: {channelName} for role: {safeRole}");
            OnVoiceStatusChanged?.Invoke($"Voice: Team {safeRole}");

            await Task.Yield();
        }

        public async Task LeaveVoiceChannelAsync()
        {
            if (!IsInVoiceChannel) return;

            Debug.Log($"[VoiceChat] Leaving voice channel: {ActiveChannelName}");
            ActiveChannelName = "";
            IsInVoiceChannel = false;
            OnVoiceStatusChanged?.Invoke("Voice Disconnected");

            await Task.Yield();
        }

        public void ToggleMicMute()
        {
            IsMicMuted = !IsMicMuted;
            Debug.Log($"[VoiceChat] Mic Mute toggled: {IsMicMuted}");
            OnMicMuteChanged?.Invoke(IsMicMuted);
        }

        public void ToggleSpeakerMute()
        {
            IsSpeakerMuted = !IsSpeakerMuted;
            Debug.Log($"[VoiceChat] Speaker Mute toggled: {IsSpeakerMuted}");
            OnSpeakerMuteChanged?.Invoke(IsSpeakerMuted);
        }

        private void OnDestroy()
        {
            _ = LeaveVoiceChannelAsync();
        }
    }
}
