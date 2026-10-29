using System;

namespace Kordon.Core.Utility
{
    /// <summary>Deterministic scalar maths shared by every rule module.</summary>
    public static class MathUtil
    {
        public const float Pi = (float)Math.PI;
        public const float TwoPi = (float)(Math.PI * 2.0);
        public const float HalfPi = (float)(Math.PI * 0.5);
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public const float Rad2Deg = (float)(180.0 / Math.PI);
        public const float Epsilon = 1e-6f;

        public static float Clamp(float v, float min, float max) => v < min ? min : (v > max ? max : v);

        public static int Clamp(int v, int min, int max) => v < min ? min : (v > max ? max : v);

        public static float Clamp01(float v) => Clamp(v, 0f, 1f);

        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);

        public static float InverseLerp(float a, float b, float v)
        {
            if (Math.Abs(b - a) < Epsilon)
            {
                return 0f;
            }

            return Clamp01((v - a) / (b - a));
        }

        public static float Remap(float v, float fromMin, float fromMax, float toMin, float toMax) =>
            Lerp(toMin, toMax, InverseLerp(fromMin, fromMax, v));

        public static float SmoothStep(float t)
        {
            t = Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        public static float MoveTowards(float current, float target, float maxDelta)
        {
            if (Math.Abs(target - current) <= maxDelta)
            {
                return target;
            }

            return current + Math.Sign(target - current) * maxDelta;
        }

        /// <summary>Frame rate independent exponential approach.</summary>
        public static float Damp(float current, float target, float smoothing, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return current;
            }

            float t = 1f - (float)Math.Pow(smoothing, deltaTime);
            return Lerp(current, target, t);
        }

        /// <summary>Fractional part folded into [0, length); the loop of a cycle.</summary>
        public static float Repeat(float value, float length)
        {
            if (length <= Epsilon)
            {
                return 0f;
            }

            float wrapped = value - (float)Math.Floor(value / length) * length;
            return wrapped < 0f ? wrapped + length : wrapped;
        }

        public static float Round(float value) => (float)Math.Round(value);

        public static float Sin(float radians) => (float)Math.Sin(radians);

        public static float Cos(float radians) => (float)Math.Cos(radians);

        /// <summary>atan2 in degrees, so map/orientation code never repeats the conversion.</summary>
        public static float Atan2Degrees(float y, float x) =>
            (float)Math.Atan2(y, x) * Rad2Deg;

        public static float WrapAngle(float degrees)
        {
            degrees %= 360f;
            if (degrees > 180f)
            {
                degrees -= 360f;
            }
            else if (degrees < -180f)
            {
                degrees += 360f;
            }

            return degrees;
        }
    }
}
