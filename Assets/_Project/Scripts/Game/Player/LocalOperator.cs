using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Core.Movement;
using Kordon.Game.Audio;
using Kordon.Game.Camera;
using Kordon.Game.Maps;
using Kordon.Game.Ui;
using Kordon.Game.Weapons;
using UnityEngine;

namespace Kordon.Game.Player
{
    /// <summary>The assembled local operator: body, bodycam, weapon and the HUD that reads them.</summary>
    public sealed class LocalOperator
    {
        public PlayerController Body;
        public BodycamRig Rig;
        public WeaponController Weapons;
        public WeaponViewmodel Viewmodel;
        public GrenadeThrower Grenades;
        public HudScreen Hud;
        public AudioDirector Audio;
        public VoiceRuntime Voice;
    }

    /// <summary>
    /// Builds the local operator exactly one way, so practice and the networked
    /// match cannot drift apart: same body, same bodycam, same weapon stack, same HUD.
    /// </summary>
    public static class LocalOperatorFactory
    {
        public static LocalOperator Build(Transform parent, MonoBehaviour host,
            DefinitionLibrary library, LoadoutSelection loadout, ulong seed, bool serverAuthority,
            MapLayout layout = null, bool squadVoice = false)
        {
            LocalOperator op = new LocalOperator();

            GameObject go = new GameObject("LocalOperator");
            go.transform.SetParent(parent, false);
            op.Body = go.AddComponent<PlayerController>();

            GameObject cam = new GameObject("Bodycam");
            cam.transform.SetParent(op.Body.CameraAnchor, false);
            op.Rig = cam.AddComponent<BodycamRig>();
            op.Rig.Body = op.Body;

            GameObject viewObject = new GameObject("Viewmodel");
            viewObject.transform.SetParent(cam.transform, false);
            op.Viewmodel = viewObject.AddComponent<WeaponViewmodel>();
            op.Viewmodel.Body = op.Body;
            op.Viewmodel.Rig = op.Rig;

            op.Weapons = go.AddComponent<WeaponController>();
            op.Weapons.Body = op.Body;
            op.Weapons.Rig = op.Rig;
            op.Weapons.Viewmodel = op.Viewmodel;
            op.Weapons.ServerAuthoritativeHits = serverAuthority;
            op.Weapons.Equip(loadout, library, seed);
            op.Body.Weapons = op.Weapons;

            // Grenades: slot 3, hold to cook, release to throw.  Offline this
            // resolves the blast itself; online the session swaps in a server intent.
            op.Grenades = go.AddComponent<GrenadeThrower>();
            op.Grenades.Weapons = op.Weapons;
            op.Grenades.Body = op.Body;
            op.Grenades.Rig = op.Rig;
            op.Viewmodel.Grenades = op.Grenades;

            op.Hud = host.gameObject.AddComponent<HudScreen>();
            op.Hud.Weapons = op.Weapons;
            op.Hud.Grenades = op.Grenades;

            // Audio: the director hears the same noise events the AI and the server do.
            GameObject audioObject = new GameObject("Audio");
            audioObject.transform.SetParent(parent, false);
            op.Audio = audioObject.AddComponent<AudioDirector>();
            op.Audio.Listener = op.Body;
            op.Audio.Layout = layout;
            op.Grenades.Audio = op.Audio;
            op.Body.NoiseEmitted += noise => OnNoise(op, noise);
            op.Weapons.ShotFired += shot => op.Audio.PlayShot(shot.Origin, shot.WeaponId, false);
            op.Weapons.AmmoEventRaised += ammoEvent =>
                op.Audio.PlayAmmoEvent(op.Body.transform.position, ammoEvent.Kind);
            op.Weapons.PlayerHit += (target, result) =>
                op.Audio.PlayHit(target.transform.position, result.BlockedByArmor);

            op.Voice = host.gameObject.AddComponent<VoiceRuntime>();
            op.Voice.Listener = op.Body;
            op.Voice.AllowSquadChannel(squadVoice);
            op.Voice.StateChanged += (channel, talking, configured) =>
                op.Hud.SetVoiceState(channel, talking, configured);
            return op;
        }

        private static void OnNoise(LocalOperator op, NoiseEvent noise)
        {
            switch (noise.Kind)
            {
                case NoiseKind.Footstep:
                    op.Audio.PlayFootstep(op.Body.transform.position, noise.SurfaceId,
                        op.Body.Gait, op.Body.GearMassKg);
                    break;
                case NoiseKind.Landing:
                    op.Audio.PlayLanding(op.Body.transform.position, 1.5f);
                    break;
            }
        }
    }
}
