// Hand written stubs of the UnityEngine API surface Kordon.Game compiles against.
//
// Unity cannot run in CI (no editor licence, no download host), but Roslyn can
// still type check every line of Unity side code: the compile check project
// builds Assets/_Project/Scripts/{Core,Game,Editor} together with these stubs.
// Signatures mirror Unity 6000.0 exactly - a member that is wrong here would be
// wrong in the editor too - so "the game code compiles" is verified for real on
// every push, and the editor import then only has to resolve assets.
//
// Keep this file additive: when new game code needs a Unity member, add it here
// with the real signature instead of changing the game code to fit a stub.

using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x;
        public float y;

        public Vector2(float x, float y)
        {
            this.x = x;
            this.y = y;
        }

        public static Vector2 zero => new Vector2(0f, 0f);
        public float magnitude => (float)Math.Sqrt(x * x + y * y);
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;

        public Vector3(float x, float y, float z)
        {
            this.x = x;
            this.y = y;
            this.z = z;
        }

        public Vector3(float x, float y) : this(x, y, 0f)
        {
        }

        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 back => new Vector3(0f, 0f, -1f);
        public static Vector3 left => new Vector3(-1f, 0f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);

        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;

        public Vector3 normalized
        {
            get
            {
                float m = magnitude;
                return m > 1e-6f ? this * (1f / m) : zero;
            }
        }

        public static Vector3 operator +(Vector3 a, Vector3 b) =>
            new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);

        public static Vector3 operator -(Vector3 a, Vector3 b) =>
            new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);

        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);

        public static Vector3 operator *(Vector3 a, float d) =>
            new Vector3(a.x * d, a.y * d, a.z * d);

        public static Vector3 operator *(float d, Vector3 a) => a * d;

        public static bool operator ==(Vector3 a, Vector3 b) =>
            Math.Abs(a.x - b.x) < 1e-5 && Math.Abs(a.y - b.y) < 1e-5 &&
            Math.Abs(a.z - b.z) < 1e-5;

        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);

        public override bool Equals(object obj) => obj is Vector3 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";

        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) =>
            new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) =>
            a + (b - a) * Mathf.Clamp01(t);
        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDelta)
        {
            Vector3 delta = target - current;
            return delta.magnitude <= maxDelta ? target : current + delta.normalized * maxDelta;
        }
    }

    public struct Quaternion
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Quaternion(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }

        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);

        public static Quaternion Euler(float x, float y, float z) =>
            new Quaternion(x, y, z, 0f); // structural stub: angles only

        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) =>
            new Quaternion(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t,
                           a.z + (b.z - a.z) * t, a.w + (b.w - a.w) * t);

        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => Slerp(a, b, t);

        public static Quaternion AngleAxis(float angle, Vector3 axis) =>
            new Quaternion(axis.x, axis.y, axis.z, angle);

        public Vector3 eulerAngles => new Vector3(x, y, z);

        public static Quaternion operator *(Quaternion a, Quaternion b) =>
            new Quaternion(a.x + b.x, a.y + b.y, a.z + b.z, a.w * b.w);

        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
    }

    public struct Color
    {
        public float r;
        public float g;
        public float b;
        public float a;

        public Color(float r, float g, float b, float a = 1f)
        {
            this.r = r;
            this.g = g;
            this.b = b;
            this.a = a;
        }

        public static Color white => new Color(1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);
        public static Color red => new Color(1f, 0f, 0f);
        public static Color green => new Color(0f, 1f, 0f);
        public static Color yellow => new Color(1f, 0.92f, 0.016f);
    }

    public struct Bounds
    {
        public Vector3 center;
        public Vector3 size;

        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
        }

        public Vector3 extents => size * 0.5f;
        public bool Contains(Vector3 point) => true;
    }

    public static class Mathf
    {
        public const float PI = (float)Math.PI;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 180f / PI;
        public const float Infinity = float.PositiveInfinity;
        public const float Epsilon = 1.401298E-45f;

        public static float Abs(float f) => Math.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Min(float a, float b) => Math.Min(a, b);
        public static int Min(int a, int b) => Math.Min(a, b);
        public static float Max(float a, float b) => Math.Max(a, b);
        public static int Max(int a, int b) => Math.Max(a, b);
        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float LerpUnclamped(float a, float b, float t) => a + (b - a) * t;
        public static float InverseLerp(float a, float b, float v) =>
            Math.Abs(b - a) < 1e-6f ? 0f : Clamp01((v - a) / (b - a));
        public static float MoveTowards(float current, float target, float maxDelta) =>
            Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
        public static float Repeat(float t, float length) => t - (float)Math.Floor(t / length) * length;
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Ceil(float f) => (float)Math.Ceiling(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-6f;
        public static float Sign(float f) => Math.Sign(f);
        public static float SmoothStep(float a, float b, float t)
        {
            t = Clamp01((t - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }

    public static class Time
    {
        public static float deltaTime = 1f / 60f;
        public static float fixedDeltaTime = 1f / 50f;
        public static float time;
        public static float unscaledTime;
        public static float unscaledDeltaTime = 1f / 60f;
        public static int frameCount;
        public static float realtimeSinceStartup;
    }

    public static class Application
    {
        public static string streamingAssetsPath = "./Assets/StreamingAssets";
        public static string dataPath = "./Assets";
        public static string persistentDataPath = "./persist";
        public static bool isEditor;
        public static string version = "6000.0.32f1";
        public static void Quit()
        {
        }
    }

    public static class Debug
    {
        public static void Log(object message) => Console.WriteLine(message);
        public static void LogWarning(object message) => Console.WriteLine("WARN " + message);
        public static void LogError(object message) => Console.Error.WriteLine("ERROR " + message);
        public static void LogException(Exception exception) => Console.Error.WriteLine(exception);
        public static void Assert(bool condition, string message = "")
        {
            if (!condition)
            {
                throw new InvalidOperationException("assert failed: " + message);
            }
        }
    }

    public class Object
    {
        public string name { get; set; } = "";
        public HideFlags hideFlags { get; set; }

        public static void Destroy(Object obj)
        {
        }

        public static void DestroyImmediate(Object obj)
        {
        }

        public static void DontDestroyOnLoad(Object obj)
        {
        }

        public static T FindObjectOfType<T>() where T : Object => null;
        public static Object FindObjectOfType(Type type) => null;

        public static T Instantiate<T>(T original) where T : Object => original;
        public static T Instantiate<T>(T original, Transform parent) where T : Object => original;
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation)
            where T : Object => original;
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation,
            Transform parent) where T : Object => original;

        public static implicit operator bool(Object obj) => !ReferenceEquals(obj, null);
        public override string ToString() => name;
    }

    public enum HideFlags
    {
        None = 0,
        HideInHierarchy = 1,
    }

    public class Component : Object
    {
        public GameObject gameObject { get; internal set; }
        public Transform transform => gameObject == null ? null : gameObject.transform;

        public T GetComponent<T>() where T : class =>
            gameObject == null ? null : gameObject.GetComponent<T>();

        public T GetComponentInChildren<T>() where T : class =>
            gameObject == null ? null : gameObject.GetComponentInChildren<T>();

        public T[] GetComponentsInChildren<T>(bool includeInactive = false) =>
            gameObject == null ? new T[0] : gameObject.GetComponentsInChildren<T>(includeInactive);

        public T GetComponentInParent<T>() where T : class => GetComponent<T>();
        public bool TryGetComponent<T>(out T component) where T : class
        {
            component = GetComponent<T>();
            return component != null;
        }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; } = true;
        public bool isActiveAndEnabled => enabled;
    }

    public class MonoBehaviour : Behaviour
    {
        public static void print(object message) => Debug.Log(message);

        public Coroutine StartCoroutine(System.Collections.IEnumerator routine) => new Coroutine();
        public void StopCoroutine(Coroutine routine)
        {
        }

        public void Invoke(string methodName, float time)
        {
        }
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; } = Quaternion.identity;
        public Quaternion localRotation { get; set; } = Quaternion.identity;
        public Vector3 localScale { get; set; } = Vector3.one;
        public Vector3 lossyScale => Vector3.one;
        public Vector3 eulerAngles { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Transform parent { get; private set; }
        public int childCount => _children.Count;

        private readonly List<Transform> _children = new List<Transform>();

        public void SetParent(Transform parent, bool worldPositionStays = true)
        {
            this.parent = parent;
            parent?._children.Add(this);
        }

        public Transform GetChild(int index) => _children[index];
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            this.rotation = rotation;
        }

        public Vector3 TransformDirection(Vector3 direction) => direction;
        public Vector3 InverseTransformDirection(Vector3 direction) => direction;
        public Vector3 TransformPoint(Vector3 point) => point;

        public IEnumerator GetEnumerator() => _children.GetEnumerator();
    }

    public class GameObject : Object
    {
        public Transform transform { get; } = new Transform();
        public string tag { get; set; } = "Untagged";
        public int layer { get; set; }
        public bool activeSelf { get; private set; } = true;
        public bool activeInHierarchy => activeSelf;

        private readonly List<Component> _components = new List<Component>();

        public GameObject()
        {
            transform.gameObject = this;
        }

        public GameObject(string name) : this()
        {
            this.name = name;
        }

        public GameObject(string name, params Type[] components) : this(name)
        {
            foreach (Type type in components)
            {
                if (type == typeof(RectTransform))
                {
                    AddComponent<RectTransform>();
                }
            }
        }

        public void SetActive(bool value) => activeSelf = value;

        public T AddComponent<T>() where T : Component, new()
        {
            T component = new T { gameObject = this };
            _components.Add(component);
            return component;
        }

        public T GetComponent<T>() where T : class
        {
            foreach (Component component in _components)
            {
                if (component is T match)
                {
                    return match;
                }
            }

            return null;
        }

        public T GetComponentInChildren<T>(bool includeInactive = false) where T : class =>
            GetComponent<T>();

        public T[] GetComponentsInChildren<T>(bool includeInactive = false)
        {
            List<T> found = new List<T>();
            if (this is T self)
            {
                found.Add(self);
            }

            foreach (Component component in _components)
            {
                if (component is T match)
                {
                    found.Add(match);
                }
            }

            return found.ToArray();
        }

        public Component[] GetComponentsInChildren(Type type, bool includeInactive = false) =>
            new Component[0];
    }

    public class Mesh : Object
    {
        public Bounds bounds { get; set; }
        public int vertexCount { get; set; }
        public int triangles => vertexCount;
    }

    public class Material : Object
    {
        public Color color { get; set; } = Color.white;
        public Texture mainTexture { get; set; }
        public Shader shader { get; set; }

        public Material()
        {
        }

        public Material(Shader shader)
        {
            this.shader = shader;
        }

        public void SetFloat(string name, float value)
        {
        }

        public void SetColor(string name, Color value)
        {
        }

        public void SetVector(string name, Vector4 value)
        {
        }

        public void SetTexture(string name, Texture value)
        {
        }

        public float GetFloat(string name) => 0f;
        public Color GetColor(string name) => Color.white;
        public bool HasProperty(string name) => true;
        public void EnableKeyword(string keyword)
        {
        }

        public void DisableKeyword(string keyword)
        {
        }
    }

    public class RectOffset
    {
        public int left { get; set; }
        public int right { get; set; }
        public int top { get; set; }
        public int bottom { get; set; }

        public RectOffset()
        {
        }

        public RectOffset(int left, int right, int top, int bottom)
        {
            this.left = left;
            this.right = right;
            this.top = top;
            this.bottom = bottom;
        }
    }

    public struct Vector4
    {
        public float x;
        public float y;
        public float z;
        public float w;

        public Vector4(float x, float y, float z, float w)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.w = w;
        }
    }

    public class Texture : Object
    {
        public int width { get; set; }
        public int height { get; set; }
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth)
        {
            this.width = width;
            this.height = height;
        }

        public static RenderTexture active { get; set; }
        public void Release()
        {
        }
    }

    public class Light : Component
    {
        public Color color { get; set; } = Color.white;
        public float intensity { get; set; } = 1f;
        public float range { get; set; } = 10f;
        public float spotAngle { get; set; } = 30f;
    }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height)
        {
            this.width = width;
            this.height = height;
        }

        public void SetPixels(Color[] colors)
        {
        }

        public void Apply()
        {
        }

        public byte[] EncodeToPNG() => new byte[0];
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => new Shader { name = name };
    }

    public class Renderer : Component
    {
        public Material sharedMaterial { get; set; }
        public Material[] sharedMaterials { get; set; } = new Material[0];
        public Material material => sharedMaterial;
        public bool enabled { get; set; } = true;
        public Bounds bounds { get; set; }
    }

    public class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
        public Mesh mesh { get; set; }
    }

    public class MeshRenderer : Renderer
    {
    }

    public class Collider : Component
    {
        public bool enabled { get; set; } = true;
        public bool isTrigger { get; set; }
        public Bounds bounds { get; set; }
    }

    public class MeshCollider : Collider
    {
        public Mesh sharedMesh { get; set; }
        public bool convex { get; set; }
    }

    public class BoxCollider : Collider
    {
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
    }

    public class CapsuleCollider : Collider
    {
        public float height { get; set; }
        public float radius { get; set; }
        public Vector3 center { get; set; }
    }

    public class Rigidbody : Component
    {
        public Vector3 velocity { get; set; }
        public Vector3 angularVelocity { get; set; }
        public float mass { get; set; } = 1f;
        public bool isKinematic { get; set; }
        public bool useGravity { get; set; } = true;

        public void AddForce(Vector3 force)
        {
        }

        public void MovePosition(Vector3 position)
        {
        }

        public void MoveRotation(Quaternion rotation)
        {
        }
    }

    public class CharacterController : Collider
    {
        public float height { get; set; } = 2f;
        public float radius { get; set; } = 0.4f;
        public Vector3 velocity { get; set; }
        public bool isGrounded { get; set; } = true;
        public Vector3 center { get; set; }

        public CollisionFlags Move(Vector3 motion) => CollisionFlags.None;
    }

    [Flags]
    public enum CollisionFlags
    {
        None = 0,
        Sides = 1,
        Above = 2,
        Below = 4,
    }

    public static class Graphics
    {
        public static void Blit(Texture source, RenderTexture destination)
        {
        }

        public static void Blit(Texture source, RenderTexture destination, Material material,
            int pass = -1)
        {
        }
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => null;
        public static Object Load(string path) => null;
        public static T[] LoadAll<T>(string path) where T : Object => new T[0];
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
        public static void UnloadUnusedAssets()
        {
        }
    }

    public class Coroutine
    {
    }

    public class AudioClip : Object
    {
        public float length { get; set; }
        public int samples { get; set; }
        public int channels { get; set; }
        public int frequency { get; set; }
    }

    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; } = 1f;
        public float pitch { get; set; } = 1f;
        public bool loop { get; set; }
        public bool playOnAwake { get; set; }
        public float minDistance { get; set; } = 1f;
        public float maxDistance { get; set; } = 500f;
        public bool isPlaying { get; private set; }

        public void Play() => isPlaying = true;
        public void Stop() => isPlaying = false;
        public void Pause()
        {
        }

        public void PlayOneShot(AudioClip clip, float volumeScale = 1f)
        {
        }
    }

    public class Camera : Behaviour
    {
        public static Camera main { get; set; }
        public float fieldOfView { get; set; } = 60f;
        public float nearClipPlane { get; set; } = 0.05f;
        public float farClipPlane { get; set; } = 1000f;
        public Transform transform => base.transform;

        public float depth { get; set; }
        public int cullingMask { get; set; } = -1;
        public RenderTexture targetTexture { get; set; }
        public Vector3 ScreenPointToRay(Vector3 pos) => Vector3.zero;
        public Vector3 WorldToScreenPoint(Vector3 position) => position;
        public Vector3 ScreenToWorldPoint(Vector3 position) => position;
        public bool Raycast(float distance) => false;
    }

    public struct Ray
    {
        public Vector3 origin;
        public Vector3 direction;

        public Ray(Vector3 origin, Vector3 direction)
        {
            this.origin = origin;
            this.direction = direction;
        }

        public Vector3 GetPoint(float distance) => origin + direction * distance;
    }

    public struct RaycastHit
    {
        public Vector3 point;
        public Vector3 normal;
        public float distance;
        public Collider collider;
    }

    public static class Physics
    {
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit,
            float maxDistance = Mathf.Infinity, int layerMask = -1)
        {
            hit = default;
            return false;
        }

        public static bool Raycast(Ray ray, out RaycastHit hit,
            float maxDistance = Mathf.Infinity, int layerMask = -1)
        {
            hit = default;
            return false;
        }

        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction,
            float maxDistance = Mathf.Infinity, int layerMask = -1) => new RaycastHit[0];

        public static Collider[] OverlapSphere(Vector3 position, float radius,
            int layerMask = -1) => new Collider[0];

        public static bool CheckSphere(Vector3 position, float radius, int layerMask = -1) => false;
        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hit,
            int layerMask = -1)
        {
            hit = default;
            return false;
        }
    }

    public enum KeyCode
    {
        None = 0,
        Space = 32,
        Tab = 9,
        Escape = 27,
        Return = 13,
        Backspace = 8,
        Alpha1 = 49,
        Alpha2 = 50,
        Alpha3 = 51,
        Alpha4 = 52,
        Alpha5 = 53,
        A = 97,
        B = 98,
        C = 99,
        D = 100,
        E = 101,
        F = 102,
        G = 103,
        H = 104,
        I = 105,
        J = 106,
        K = 107,
        L = 108,
        M = 109,
        N = 110,
        O = 111,
        P = 112,
        Q = 113,
        R = 114,
        S = 115,
        T = 116,
        U = 117,
        V = 118,
        W = 119,
        X = 120,
        Y = 121,
        Z = 122,
        UpArrow = 273,
        DownArrow = 274,
        LeftArrow = 275,
        RightArrow = 276,
        LeftShift = 304,
        RightShift = 303,
        LeftControl = 306,
        RightControl = 305,
        LeftAlt = 308,
        RightAlt = 307,
        Mouse0 = 323,
        Mouse1 = 324,
        Mouse2 = 325,
    }

    public static class Input
    {
        public static bool GetKey(string name) => false;
        public static bool GetKeyDown(string name) => false;
        public static bool GetKeyUp(string name) => false;
        public static bool GetKey(KeyCode key) => false;
        public static bool GetKeyDown(KeyCode key) => false;
        public static bool GetKeyUp(KeyCode key) => false;
        public static bool GetButton(string name) => false;
        public static bool GetButtonDown(string name) => false;
        public static bool GetButtonUp(string name) => false;
        public static bool GetMouseButton(int button) => false;
        public static bool GetMouseButtonDown(int button) => false;
        public static bool GetMouseButtonUp(int button) => false;
        public static float GetAxis(string name) => 0f;
        public static float GetAxisRaw(string name) => 0f;
        public static Vector3 mousePosition => Vector3.zero;
        public static Vector2 mouseScrollDelta => Vector2.zero;
    }

    public static class QualitySettings
    {
        public static int GetQualityLevel() => 2;
        public static void SetQualityLevel(int level, bool applyExpensiveChanges = true)
        {
        }

        public static string[] names => new[] { "Low", "Medium", "High", "Ultra" };
    }

    public enum CursorLockMode
    {
        None = 0,
        Locked = 1,
        Confined = 2,
    }

    public static class Cursor
    {
        public static bool visible { get; set; } = true;
        public static CursorLockMode lockState { get; set; } = CursorLockMode.None;
    }

    public static class Screen
    {
        public static int width => 1920;
        public static int height => 1080;
        public static void SetResolution(int width, int height, bool fullscreen)
        {
        }
    }

    public static class PlayerPrefs
    {
        private static readonly Dictionary<string, string> Store = new Dictionary<string, string>();

        public static void SetString(string key, string value) => Store[key] = value;
        public static string GetString(string key, string fallback = "") =>
            Store.TryGetValue(key, out string value) ? value : fallback;
        public static void SetInt(string key, int value) => Store[key] = value.ToString();
        public static int GetInt(string key, int fallback = 0) =>
            int.TryParse(Store.TryGetValue(key, out string v) ? v : "", out int parsed) ? parsed : fallback;
        public static void SetFloat(string key, float value) => Store[key] = value.ToString("R");
        public static float GetFloat(string key, float fallback = 0f) =>
            float.TryParse(Store.TryGetValue(key, out string v) ? v : "", out float parsed) ? parsed : fallback;
        public static bool HasKey(string key) => Store.ContainsKey(key);
        public static void DeleteKey(string key) => Store.Remove(key);
        public static void Save()
        {
        }
    }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject, new() => new T();
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class RequireComponent : Attribute
    {
        public RequireComponent(Type type)
        {
        }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class DisallowMultipleComponent : Attribute
    {
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name { get; set; }
        public string path { get; set; }
        public bool isLoaded { get; set; }
        public int rootCount => 0;
    }

    public static class SceneManager
    {
        public static Scene GetActiveScene() => default;
        public static void LoadScene(string name)
        {
        }

        public static event Action<Scene, LoadSceneMode> sceneLoaded;

        internal static void RaiseSceneLoaded(Scene scene, LoadSceneMode mode) =>
            sceneLoaded?.Invoke(scene, mode);
    }

    public enum LoadSceneMode
    {
        Single = 0,
        Additive = 1,
    }
}
