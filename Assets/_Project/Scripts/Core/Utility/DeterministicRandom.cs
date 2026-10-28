using System;

namespace Kordon.Core.Utility
{
    /// <summary>
    /// Small deterministic PRNG (xorshift128+) so spread, recoil jitter and AI
    /// decisions are reproducible in tests and identical on server and client
    /// when the same seed is used.  Deliberately not System.Random: its
    /// implementation may change between .NET runtimes, which would break
    /// lockstep validation between the Unity editor (Mono) and CI (CoreCLR).
    /// </summary>
    public sealed class DeterministicRandom
    {
        private ulong _s0;
        private ulong _s1;

        public DeterministicRandom(ulong seed)
        {
            Seed = seed;
            // SplitMix64 seeding avoids degenerate all-zero states.
            _s0 = SplitMix(seed);
            _s1 = SplitMix(seed + 0x9E3779B97F4A7C15UL);
            if (_s0 == 0UL && _s1 == 0UL)
            {
                _s0 = 0x243F6A8885A308D3UL;
            }
        }

        public ulong Seed { get; private set; }

        private static ulong SplitMix(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            ulong z = x;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public ulong NextULong()
        {
            ulong s1 = _s0;
            ulong s0 = _s1;
            ulong result = s0 + s1;
            _s0 = s0;
            s1 ^= s1 << 23;
            _s1 = s1 ^ s0 ^ (s1 >> 17) ^ (s0 >> 26);
            return result;
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Next01() => (NextULong() >> 40) * (1f / 16777216f);

        /// <summary>Uniform float in [-1, 1).</summary>
        public float NextSigned() => Next01() * 2f - 1f;

        public float Range(float minInclusive, float maxExclusive) =>
            minInclusive + (maxExclusive - minInclusive) * Next01();

        public int Range(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            return minInclusive + (int)(NextULong() % (ulong)(maxExclusive - minInclusive));
        }

        /// <summary>Approximately normal sample in [-1, 1] (average of 3 uniforms).</summary>
        public float NextBell() => (NextSigned() + NextSigned() + NextSigned()) / 3f;

        public bool Chance(float probability01) => Next01() < probability01;

        public DeterministicRandom Fork(ulong salt) => new DeterministicRandom(NextULong() ^ salt);
    }
}
