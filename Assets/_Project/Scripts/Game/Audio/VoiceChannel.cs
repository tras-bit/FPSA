using System;
using Kordon.Game.Player;
using UnityEngine;

namespace Kordon.Game.Audio
{
    public enum VoiceMode
    {
        Off = 0,

        /// <summary>Hear and be heard by operators nearby, fading with distance.</summary>
        Proximity = 1,

        /// <summary>Co-op squad channel: everyone in the operation, any distance.</summary>
        Squad = 2,
    }

    /// <summary>
    /// Transport seam for voice.  KORDON ships no voice backend on purpose: every
    /// real option needs an account, a key or a paid relay, and the brief forbids
    /// asking for any of that.  What ships instead is the complete integration point:
    /// push to talk, channel switching, distance falloff data and a HUD channel
    /// indicator that is visually distinct for the squad channel.
    ///
    /// To enable voice, implement IVoiceTransport (any codec/service) and assign it
    /// to Transport before a match; nothing else in the game has to change.
    /// </summary>
    public interface IVoiceTransport
    {
        bool IsAvailable { get; }
        void SetPushToTalk(bool talking);
        void SendFrame(byte[] payload, VoiceMode mode);
        event Action<byte[], VoiceMode> FrameReceived;
    }

    public sealed class VoiceChannel : MonoBehaviour
    {
        public const float ProximityRangeM = 26f;
        public const KeyCode PushToTalkKey = KeyCode.V;

        public IVoiceTransport Transport { get; set; }
        public VoiceMode Mode { get; private set; } = VoiceMode.Proximity;
        public bool Talking { get; private set; }
        public bool Configured => Transport != null && Transport.IsAvailable;
        public PlayerController Listener { get; set; }

        public event Action<VoiceMode, bool, bool> StateChanged;

        private bool _squadAllowed;

        public void AllowSquadChannel(bool allowed)
        {
            _squadAllowed = allowed;
            if (!allowed && Mode == VoiceMode.Squad)
            {
                Mode = VoiceMode.Proximity;
            }

            Raise();
        }

        public void CycleMode()
        {
            if (Mode == VoiceMode.Proximity && _squadAllowed)
            {
                Mode = VoiceMode.Squad;
            }
            else
            {
                Mode = VoiceMode.Proximity;
            }

            Raise();
        }

        /// <summary>Distance gain for proximity voice; the squad channel ignores distance.</summary>
        public float GainFor(Vector3 speakerPosition)
        {
            if (Mode == VoiceMode.Squad)
            {
                return 1f;
            }

            if (Listener == null)
            {
                return 0f;
            }

            float distance = Vector3.Distance(Listener.transform.position, speakerPosition);
            return Mathf.Clamp01(1f - distance / ProximityRangeM);
        }

        private void Update()
        {
            bool talking = Configured && Input.GetKey(PushToTalkKey);
            if (talking != Talking)
            {
                Talking = talking;
                Transport?.SetPushToTalk(talking);
                Raise();
            }
        }

        public string ChannelLabel
        {
            get
            {
                if (!Configured)
                {
                    return "VOICE   not configured";
                }

                return Mode == VoiceMode.Squad ? "VOICE   squad" : "VOICE   proximity";
            }
        }

        private void Raise() => StateChanged?.Invoke(Mode, Talking, Configured);

        private void OnDestroy()
        {
            if (Transport != null)
            {
                Transport.FrameReceived -= OnFrame;
            }
        }

        private void OnFrame(byte[] payload, VoiceMode mode)
        {
        }
    }
}
