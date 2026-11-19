using System;
using System.Collections.Generic;

namespace GOA.Sim
{
    /// <summary>
    /// Context-keyed deterministic randomness.
    /// Every random decision in the simulation is derived from (world seed, context values, purpose salt)
    /// rather than from a shared stream. This matters for honest causality: an intervention can only change
    /// outcomes that actually depend on the state it altered. Unrelated rolls (weather, other villagers'
    /// moods) stay identical across timelines, so differences between branches are real consequences.
    /// </summary>
    public static class Rand
    {
        public static ulong Mix(ulong z)
        {
            z += 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public static ulong Hash(ulong seed, long a, long b = 0, long c = 0, int salt = 0)
        {
            ulong h = Mix(seed ^ 0xA0761D6478BD642FUL);
            h = Mix(h ^ (ulong)a);
            h = Mix(h ^ (ulong)b ^ 0xE7037ED1A0B428DBUL);
            h = Mix(h ^ (ulong)c ^ 0x8EBC6AF09C88C6E3UL);
            h = Mix(h ^ (ulong)(uint)salt ^ 0x589965CC75374CC3UL);
            return h;
        }

        /// <summary>Uniform double in [0,1).</summary>
        public static double U01(ulong seed, long a, long b = 0, long c = 0, int salt = 0)
        {
            return (Hash(seed, a, b, c, salt) >> 11) * (1.0 / 9007199254740992.0);
        }

        public static bool Chance(double p, ulong seed, long a, long b = 0, long c = 0, int salt = 0)
        {
            if (p <= 0) return false;
            if (p >= 1) return true;
            return U01(seed, a, b, c, salt) < p;
        }

        public static int Range(int minInclusive, int maxExclusive, ulong seed, long a, long b = 0, long c = 0, int salt = 0)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            return minInclusive + (int)(Hash(seed, a, b, c, salt) % (ulong)(maxExclusive - minInclusive));
        }

        public static float RangeF(float min, float max, ulong seed, long a, long b = 0, long c = 0, int salt = 0)
        {
            return min + (float)U01(seed, a, b, c, salt) * (max - min);
        }
    }

    /// <summary>Sequential seeded generator used only for world generation (never mid-simulation).</summary>
    public sealed class SeqRandom
    {
        ulong _state;
        public SeqRandom(ulong seed) { _state = Rand.Mix(seed ^ 0x2545F4914F6CDD1DUL); }
        public ulong Next() { _state = Rand.Mix(_state); return _state; }
        public double NextDouble() => (Next() >> 11) * (1.0 / 9007199254740992.0);
        public int Range(int min, int maxExclusive) => maxExclusive <= min ? min : min + (int)(Next() % (ulong)(maxExclusive - min));
        public float RangeF(float min, float max) => min + (float)NextDouble() * (max - min);
        public bool Chance(double p) => NextDouble() < p;
        public T Pick<T>(IReadOnlyList<T> list) => list[Range(0, list.Count)];
        public void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Range(0, i + 1);
                T t = list[i]; list[i] = list[j]; list[j] = t;
            }
        }
    }

    [Serializable]
    public struct Vec2 : IEquatable<Vec2>
    {
        public float x, z;
        public Vec2(float x, float z) { this.x = x; this.z = z; }
        public static Vec2 operator +(Vec2 a, Vec2 b) => new Vec2(a.x + b.x, a.z + b.z);
        public static Vec2 operator -(Vec2 a, Vec2 b) => new Vec2(a.x - b.x, a.z - b.z);
        public static Vec2 operator *(Vec2 a, float s) => new Vec2(a.x * s, a.z * s);
        public float Length => (float)Math.Sqrt(x * x + z * z);
        public float SqrLength => x * x + z * z;
        public Vec2 Normalized { get { float l = Length; return l > 1e-6f ? new Vec2(x / l, z / l) : new Vec2(0, 0); } }
        public static float Distance(Vec2 a, Vec2 b) => (a - b).Length;
        public static float Dot(Vec2 a, Vec2 b) => a.x * b.x + a.z * b.z;
        public static Vec2 Lerp(Vec2 a, Vec2 b, float t) => new Vec2(a.x + (b.x - a.x) * t, a.z + (b.z - a.z) * t);
        public static Vec2 FromAngle(float radians) => new Vec2((float)Math.Cos(radians), (float)Math.Sin(radians));
        public float Angle => (float)Math.Atan2(z, x);
        public bool Equals(Vec2 o) => x == o.x && z == o.z;
        public override bool Equals(object obj) => obj is Vec2 v && Equals(v);
        public override int GetHashCode() => x.GetHashCode() * 397 ^ z.GetHashCode();
        public override string ToString() => $"({x:0.0},{z:0.0})";

        /// <summary>Distance from p to segment ab and the parameter t of the closest point.</summary>
        public static float DistanceToSegment(Vec2 p, Vec2 a, Vec2 b, out float t)
        {
            Vec2 ab = b - a;
            float len2 = ab.SqrLength;
            if (len2 < 1e-6f) { t = 0; return Distance(p, a); }
            t = Dot(p - a, ab) / len2;
            if (t < 0) t = 0; else if (t > 1) t = 1;
            return Distance(p, a + ab * t);
        }
    }

    public enum Season { Spring, Summer, Autumn, Winter }

    /// <summary>Simulation time is measured in whole simulated seconds.</summary>
    public static class SimTime
    {
        public const long Minute = 60;
        public const long Hour = 3600;
        public const long Day = 86400;
        public const int DaysPerSeason = 7;
        public const int DaysPerYear = 28;
        public const long Year = Day * DaysPerYear;
        /// <summary>Walking pace in metres per simulated hour (stylised miniature pace).</summary>
        public const float WalkSpeedPerHour = 48f;
        public const float WalkSpeedPerSecond = WalkSpeedPerHour / 3600f;

        public static long DayIndex(long t) => t / Day;
        public static int Year1(long t) => (int)(t / Year) + 1;
        public static int DayOfYear(long t) => (int)((t / Day) % DaysPerYear);
        public static Season SeasonOf(long t) => (Season)(DayOfYear(t) / DaysPerSeason);
        public static int DayOfSeason(long t) => DayOfYear(t) % DaysPerSeason + 1;
        public static double HourOfDay(long t) => (t % Day) / 3600.0;
        public static double HourOfDay(double t) => (t % Day) / 3600.0;
        public static bool IsHolyDay(long t) => DayOfSeason(t) == DaysPerSeason;
        public static long StartOfDay(long t) => t - t % Day;
        public static long At(long dayStart, double hour) => dayStart + (long)(hour * Hour);

        public static string Format(long t)
        {
            int h = (int)HourOfDay(t);
            int m = (int)((t % Hour) / Minute);
            return $"Year {Year1(t)}, {SeasonOf(t)} {DayOfSeason(t)}, {h:00}:{m:00}";
        }

        public static string FormatShort(long t) => $"Y{Year1(t)} {SeasonOf(t)} {DayOfSeason(t)}";
    }
}
