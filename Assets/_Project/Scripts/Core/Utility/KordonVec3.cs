using System;
using System.Runtime.InteropServices;

namespace Kordon.Core.Utility
{
    /// <summary>
    /// Engine independent 3D vector used by every deterministic rule in Kordon.Core.
    /// Keeping the maths out of UnityEngine means the whole rule set (damage,
    /// ballistics, movement, match logic, navigation) can be compiled and unit
    /// tested by plain `dotnet test` outside of the Unity editor.
    /// Kordon.Game provides implicit conversion to/from UnityEngine.Vector3.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct KordonVec3 : IEquatable<KordonVec3>
    {
        public float X;
        public float Y;
        public float Z;

        public KordonVec3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly KordonVec3 Zero = new KordonVec3(0f, 0f, 0f);
        public static readonly KordonVec3 One = new KordonVec3(1f, 1f, 1f);
        public static readonly KordonVec3 Up = new KordonVec3(0f, 1f, 0f);
        public static readonly KordonVec3 Down = new KordonVec3(0f, -1f, 0f);
        public static readonly KordonVec3 Forward = new KordonVec3(0f, 0f, 1f);
        public static readonly KordonVec3 Back = new KordonVec3(0f, 0f, -1f);
        public static readonly KordonVec3 Right = new KordonVec3(1f, 0f, 0f);
        public static readonly KordonVec3 Left = new KordonVec3(-1f, 0f, 0f);

        public static KordonVec3 operator +(KordonVec3 a, KordonVec3 b) => new KordonVec3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static KordonVec3 operator -(KordonVec3 a, KordonVec3 b) => new KordonVec3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static KordonVec3 operator -(KordonVec3 a) => new KordonVec3(-a.X, -a.Y, -a.Z);
        public static KordonVec3 operator *(KordonVec3 a, float s) => new KordonVec3(a.X * s, a.Y * s, a.Z * s);
        public static KordonVec3 operator *(float s, KordonVec3 a) => a * s;
        public static KordonVec3 operator /(KordonVec3 a, float s) => new KordonVec3(a.X / s, a.Y / s, a.Z / s);
        public static bool operator ==(KordonVec3 a, KordonVec3 b) => a.Equals(b);
        public static bool operator !=(KordonVec3 a, KordonVec3 b) => !a.Equals(b);

        public float SqrMagnitude => X * X + Y * Y + Z * Z;
        public float Magnitude => (float)Math.Sqrt(SqrMagnitude);

        public static float Dot(KordonVec3 a, KordonVec3 b) => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        public static KordonVec3 Cross(KordonVec3 a, KordonVec3 b) => new KordonVec3(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);

        public static float Distance(KordonVec3 a, KordonVec3 b) => (a - b).Magnitude;
        public static float SqrDistance(KordonVec3 a, KordonVec3 b) => (a - b).SqrMagnitude;

        public KordonVec3 Normalized()
        {
            float m = Magnitude;
            if (m < 1e-6f)
            {
                return Zero;
            }

            return new KordonVec3(X / m, Y / m, Z / m);
        }

        /// <summary>Unsigned angle between two directions, in degrees.</summary>
        public static float Angle(KordonVec3 a, KordonVec3 b)
        {
            float d = (float)Math.Sqrt(a.SqrMagnitude * b.SqrMagnitude);
            if (d < 1e-6f)
            {
                return 0f;
            }

            float cos = MathUtil.Clamp(Dot(a, b) / d, -1f, 1f);
            return (float)(Math.Acos(cos) * MathUtil.Rad2Deg);
        }

        public static KordonVec3 Lerp(KordonVec3 a, KordonVec3 b, float t)
        {
            t = MathUtil.Clamp01(t);
            return new KordonVec3(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t, a.Z + (b.Z - a.Z) * t);
        }

        public static KordonVec3 Min(KordonVec3 a, KordonVec3 b) =>
            new KordonVec3(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Min(a.Z, b.Z));

        public static KordonVec3 Max(KordonVec3 a, KordonVec3 b) =>
            new KordonVec3(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Max(a.Z, b.Z));

        public bool Equals(KordonVec3 other) =>
            Math.Abs(X - other.X) < 1e-5f && Math.Abs(Y - other.Y) < 1e-5f && Math.Abs(Z - other.Z) < 1e-5f;

        public override bool Equals(object obj) => obj is KordonVec3 v && Equals(v);

        public override int GetHashCode() => X.GetHashCode() ^ (Y.GetHashCode() << 2) ^ (Z.GetHashCode() >> 2);

        public override string ToString() => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "({0:F3}, {1:F3}, {2:F3})", X, Y, Z);

        public string ToString(string format) => string.Format(System.Globalization.CultureInfo.InvariantCulture,
            "(" + format + ", " + format + ", " + format + ")", X, Y, Z);
    }
}
