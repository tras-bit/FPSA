using System;
using Kordon.Core.Utility;

namespace Kordon.Core.Voice
{
    /// <summary>
    /// Voice channels.  Local is proximity based (you only hear operators near you,
    /// friend or foe); the radio channel reaches the whole team and is explicitly
    /// distinguished in the UI by a different indicator and a filtered sound.
    /// </summary>
    public enum VoiceChannel
    {
        None = 0,
        LocalProximity = 1,
        TeamRadio = 2
    }

    /// <summary>What a listener should receive from one speaker.</summary>
    public struct VoiceAudibility
    {
        public bool Audible;
        public float Gain;
        public VoiceChannel Channel;
        public float DistanceM;
        public bool Occluded;

        public static VoiceAudibility Silent() => new VoiceAudibility { Audible = false, Gain = 0f };
    }

    /// <summary>
    /// Distance and occlusion model for local voice, plus the flat radio channel.
    /// The transport itself (capture, codec, network) is deliberately out of scope:
    /// <see cref="IVoiceTransport"/> is the integration point, and the shipped
    /// implementation is <see cref="UnconfiguredVoiceTransport"/>, which reports
    /// "not available" until an external provider is connected.  Nothing in the game
    /// pretends voice chat works when no provider account is configured.
    /// </summary>
    [Serializable]
    public sealed class VoiceProximityModel
    {
        /// <summary>Distance at which local voice starts to fall off.</summary>
        public float InnerRadiusM = 6f;

        /// <summary>Distance at which local voice becomes inaudible.</summary>
        public float OuterRadiusM = 28f;

        /// <summary>Gain multiplier applied when the speaker is behind cover/a wall.</summary>
        public float OccludedGain = 0.35f;

        /// <summary>Extra distance penalty per closed door between speaker and listener.</summary>
        public float OcclusionDistancePenaltyM = 6f;

        /// <summary>Radio channel is not distance limited, but is quieter and filtered.</summary>
        public float RadioGain = 0.8f;

        public VoiceAudibility Evaluate(VoiceChannel channel, KordonVec3 speaker, KordonVec3 listener,
            bool lineOfSight, int occludersBetween = 0)
        {
            if (channel == VoiceChannel.None)
            {
                return VoiceAudibility.Silent();
            }

            float distance = KordonVec3.Distance(speaker, listener);

            if (channel == VoiceChannel.TeamRadio)
            {
                return new VoiceAudibility
                {
                    Audible = true,
                    Gain = RadioGain,
                    Channel = channel,
                    Distance = distance,
                    Occluded = false
                };
            }

            if (distance >= OuterRadiusM)
            {
                return VoiceAudibility.Silent();
            }

            float gain = distance <= InnerRadiusM
                ? 1f
                : 1f - (distance - InnerRadiusM) / Math.Max(0.001f, OuterRadiusM - InnerRadiusM);

            bool occluded = !lineOfSight || occludersBetween > 0;
            if (occluded)
            {
                gain *= OccludedGain;
                gain *= Math.Max(0.15f, 1f - occludersBetween * 0.25f);
            }

            gain = MathUtil.Clamp01(gain);
            return new VoiceAudibility
            {
                Audible = gain > 0.01f,
                Gain = gain,
                Channel = channel,
                Distance = distance,
                Occluded = occluded
            };
        }

        /// <summary>Radius shown in the UI so players understand who can hear them.</summary>
        public float LocalHearRadiusM => OuterRadiusM;

        public static string ChannelLabel(VoiceChannel channel)
        {
            switch (channel)
            {
                case VoiceChannel.LocalProximity: return "LOCAL";
                case VoiceChannel.TeamRadio: return "RADIO";
                default: return "MUTED";
            }
        }
    }

    /// <summary>
    /// Integration point for an external voice provider.  Implement this against a
    /// provider SDK (Vivox, Photon Voice, Discord, a self hosted Mumble server, ...)
    /// and register it with Kordon.Net.Voice.VoiceService.
    ///
    /// No provider is bundled: every candidate requires an account, an API key or a
    /// self hosted service, and shipping one would mean committing credentials or
    /// signing the project up to a paid service.  See docs/12_VOICE_INTEGRATION.md.
    /// </summary>
    public interface IVoiceTransport
    {
        bool IsAvailable { get; }
        string ProviderName { get; }
        string StatusMessage { get; }

        bool Initialize(string configurationJson);
        void Shutdown();

        void SetLocalCaptureEnabled(bool enabled);
        void SetChannel(VoiceChannel channel);

        /// <summary>Called by the game with captured PCM data ready to be sent.</summary>
        void SendAudio(float[] samples, int sampleCount, int sampleRate, VoiceChannel channel);

        /// <summary>Raised when remote audio should be played through the given audibility.</summary>
        void SetListenerGain(ulong speakerId, float gain);
    }

    /// <summary>
    /// The shipped implementation: a clearly labelled no-op that keeps the UI honest.
    /// Text based team communication (radio messages / pings) works without it.
    /// </summary>
    public sealed class UnconfiguredVoiceTransport : IVoiceTransport
    {
        public bool IsAvailable => false;
        public string ProviderName => "none";

        public string StatusMessage =>
            "Voice chat is not connected. No provider is bundled with the game: connecting one " +
            "requires an account/API key for an external service. Until then use radio text " +
            "messages and the ping system.";

        public bool Initialize(string configurationJson) => false;

        public void Shutdown()
        {
        }

        public void SetLocalCaptureEnabled(bool enabled)
        {
        }

        public void SetChannel(VoiceChannel channel)
        {
        }

        public void SendAudio(float[] samples, int sampleCount, int sampleRate, VoiceChannel channel)
        {
        }

        public void SetListenerGain(ulong speakerId, float gain)
        {
        }
    }
}
