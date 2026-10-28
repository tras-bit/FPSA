using Kordon.Core.Settings;
using Kordon.Core.Utility;
using Kordon.Core.Voice;
using Kordon.Game.Input;
using Kordon.Game.Maps;
using Kordon.Game.Player;
using UnityEngine;
using UnityInput = UnityEngine.Input;

namespace Kordon.Game.Audio
{
    /// <summary>
    /// Runtime side of voice chat.  Kordon.Core owns the rules (who can hear whom,
    /// proximity falloff, occlusion, the squad radio) and the transport interface;
    /// this component only reads the keys, drives the HUD indicator and hands the
    /// listener gains to whatever transport is installed.
    ///
    /// The shipped transport is <see cref="UnconfiguredVoiceTransport"/>: the game is
    /// honest about voice not being connected rather than pretending, and no key,
    /// account or paid relay is ever requested.  Dropping in a real IVoiceTransport
    /// is the single line that changes.
    /// </summary>
    public sealed class VoiceRuntime : MonoBehaviour
    {
        public PlayerController Listener { get; set; }
        public IVoiceTransport Transport { get; private set; } = new UnconfiguredVoiceTransport();
        public VoiceProximityModel Model { get; } = new VoiceProximityModel();
        public VoiceChannel Channel { get; private set; } = VoiceChannel.LocalProximity;
        public bool Talking { get; private set; }
        public bool SquadAllowed { get; private set; }
        public bool Configured => Transport != null && Transport.IsAvailable;

        public event System.Action<VoiceChannel, bool, bool> StateChanged;

        public void Install(IVoiceTransport transport)
        {
            Transport = transport ?? new UnconfiguredVoiceTransport();
            Transport.Initialize(string.Empty);
            Raise();
        }

        public void AllowSquadChannel(bool allowed)
        {
            SquadAllowed = allowed;
            if (!allowed)
            {
                Channel = VoiceChannel.LocalProximity;
            }

            Raise();
        }

        private void Update()
        {
            KeyBindings bindings = KordonInput.Bindings;
            bool local = UnityInput.GetKey((KeyCode)bindings.GetKey(InputAction.VoiceLocal));
            bool team = SquadAllowed && Configured &&
                        UnityInput.GetKey((KeyCode)bindings.GetKey(InputAction.VoiceTeam));

            VoiceChannel wanted = team ? VoiceChannel.TeamRadio
                : local ? VoiceChannel.LocalProximity
                : VoiceChannel.None;

            if (wanted != Channel && Configured)
            {
                Channel = wanted;
                Transport.SetChannel(Channel);
                Transport.SetLocalCaptureEnabled(Channel != VoiceChannel.None);
            }
            else if (wanted != Channel)
            {
                // No transport: the channel indicator still reflects the request so the
                // player can see it is not wired, but nothing is transmitted.
                Channel = wanted;
            }

            bool talking = Configured && Channel != VoiceChannel.None;
            if (talking != Talking)
            {
                Talking = talking;
                Raise();
            }
        }

        /// <summary>Gain for one speaker, from the Core proximity model.</summary>
        public float GainFor(ulong speakerId, Vector3 speakerPosition, bool occluded)
        {
            if (Listener == null)
            {
                return 0f;
            }

            VoiceAudibility audibility = Model.Evaluate(Channel,
                MapSpace.ToCore(speakerPosition), MapSpace.ToCore(Listener.transform.position),
                occluded);
            if (Configured)
            {
                Transport.SetListenerGain(speakerId, audibility.Gain);
            }

            return audibility.Audible ? audibility.Gain : 0f;
        }

        public string ChannelLabel =>
            Configured ? VoiceProximityModel.ChannelLabel(Channel) : "VOICE   not configured";

        private void Raise() => StateChanged?.Invoke(Channel, Talking, Configured);
    }
}
