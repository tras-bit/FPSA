using Kordon.Game.Maps;
using UnityEngine;

namespace Kordon.Net
{
    /// <summary>
    /// The visible body of another operator.  Clients build one per remote client
    /// id and push the server's snapshot into it; the server builds a body-only
    /// version to have a hittable collider for its own authoritative raycasts.
    ///
    /// The proportions come from the same operator contract as the local body
    /// (1.82 m tall, 0.62 m shoulders) so what the server can hit is what the
    /// client can see.
    /// </summary>
    public sealed class RemoteOperatorView : MonoBehaviour
    {
        public ulong ClientId { get; private set; }
        public string DisplayName { get; private set; } = "operator";

        private Transform _visualRoot;
        private Renderer _torso;
        private Renderer _head;
        private Collider _collider;
        private Vector3 _targetPosition;
        private float _targetYaw;
        private bool _hasTarget;

        public static RemoteOperatorView Create(Transform parent, ulong clientId, string name,
            bool visuals)
        {
            GameObject go = new GameObject("Operator_" + clientId);
            go.transform.SetParent(parent, false);
            RemoteOperatorView view = go.AddComponent<RemoteOperatorView>();
            view.ClientId = clientId;
            view.DisplayName = name ?? "operator";
            view.Build(visuals);
            return view;
        }

        private void Build(bool visuals)
        {
            GameObject root = new GameObject("Visual");
            root.transform.SetParent(transform, false);
            _visualRoot = root.transform;

            GameObject torso = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            torso.name = "Torso";
            torso.transform.SetParent(_visualRoot, false);
            torso.transform.localPosition = new Vector3(0f, 0.9f, 0f);
            torso.transform.localScale = new Vector3(0.62f, 0.9f, 0.42f);
            _collider = torso.GetComponent<Collider>();
            _torso = torso.GetComponent<Renderer>();
            ApplyLook(_torso, 1f);

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(_visualRoot, false);
            head.transform.localPosition = new Vector3(0f, 1.66f, 0f);
            head.transform.localScale = new Vector3(0.26f, 0.3f, 0.26f);
            _head = head.GetComponent<Renderer>();
            ApplyLook(_head, 0.82f);
            Collider headCollider = head.GetComponent<Collider>();
            if (headCollider != null)
            {
                Object.Destroy(headCollider);
            }

            if (!visuals)
            {
                if (_torso != null)
                {
                    _torso.enabled = false;
                }

                if (_head != null)
                {
                    _head.enabled = false;
                }
            }
        }

        private void ApplyLook(Renderer renderer, float brightness)
        {
            if (renderer == null)
            {
                return;
            }

            Material baseMaterial = SurfaceMaterialLibrary.ForSurface("steel_painted");
            if (baseMaterial == null)
            {
                return;
            }

            Material instance = new Material(baseMaterial.shader);
            instance.CopyPropertiesFromMaterial(baseMaterial);
            float tint = (ClientId % 7) / 7f;
            instance.color = Color.Lerp(new Color(0.32f, 0.35f, 0.29f, 1f),
                new Color(0.44f, 0.32f, 0.2f, 1f), tint) * brightness;
            renderer.sharedMaterial = instance;
        }

        /// <summary>Server snapshot for this operator: smooth on the client, exact on the server.</summary>
        public void Apply(Vector3 position, float yaw, bool dead, bool immediate)
        {
            if (immediate)
            {
                transform.position = position;
                transform.rotation = MapSpace.ToUnityYaw(yaw);
                _hasTarget = true;
            }
            else
            {
                _targetPosition = position;
                _targetYaw = yaw;
                _hasTarget = true;
            }

            if (_visualRoot != null)
            {
                _visualRoot.gameObject.SetActive(!dead);
            }

            if (_collider != null)
            {
                _collider.enabled = !dead;
            }
        }

        private void Update()
        {
            if (!_hasTarget || _visualRoot == null)
            {
                return;
            }

            float dt = Mathf.Clamp(Time.deltaTime, 0f, 0.1f);
            transform.position = Vector3.Lerp(transform.position, _targetPosition,
                Mathf.Clamp01(dt * 16f));
            Quaternion target = MapSpace.ToUnityYaw(_targetYaw);
            transform.rotation = Quaternion.Slerp(transform.rotation, target,
                Mathf.Clamp01(dt * 18f));
        }
    }
}
