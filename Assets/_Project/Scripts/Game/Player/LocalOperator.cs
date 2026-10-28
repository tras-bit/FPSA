using Kordon.Core.Data;
using Kordon.Core.Loadout;
using Kordon.Game.Camera;
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
        public HudScreen Hud;
    }

    /// <summary>
    /// Builds the local operator exactly one way, so practice and the networked
    /// match cannot drift apart: same body, same bodycam, same weapon stack, same HUD.
    /// </summary>
    public static class LocalOperatorFactory
    {
        public static LocalOperator Build(Transform parent, MonoBehaviour host,
            DefinitionLibrary library, LoadoutSelection loadout, ulong seed, bool serverAuthority)
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

            op.Hud = host.gameObject.AddComponent<HudScreen>();
            op.Hud.Weapons = op.Weapons;
            return op;
        }
    }
}
