using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kickr
{
    /// <summary>Tunable boost settings (Inspector on the <b>BoostGems</b> object).</summary>
    [Serializable]
    public class BoostSettings
    {
        [Tooltip("Boosts in hand at the start of every Play session.")]
        [Min(0)] public int startingBoosts = 2;
        [Tooltip("Boosts added per gem picked up.")]
        [Min(0)] public int gemValue = 1;
        [Tooltip("How long one boost lasts, in seconds.")]
        [Min(0.1f)] public float boostDuration = 10f;
        [Tooltip("Resistance levels taken off the trainer while a boost is active (added to terrain and zone offsets, result clamped 0-100).")]
        [Range(0, 100)] public int boostResistanceReduction = 15;
        [Tooltip("Cube speed multiplier while a boost is active.")]
        [Min(0.1f)] public float speedMultiplier = 1.3f;

        [Header("Gem layout")]
        [Tooltip("Gems per 500 m lap (about one every 50 m at 10).")]
        [Min(0)] public int gemsPerLap = 10;
        [Tooltip("Gems are kept at least this many metres clear of the red/green zones.")]
        [Min(0f)] public float zoneClearance = 4f;
        [Tooltip("Climbs count this many times their length when spreading gems, so climbs get more of them.")]
        [Min(1f)] public float climbWeight = 2f;
    }

    /// <summary>
    /// Where the gems sit on one lap. Gems are spread evenly over a "weighted" lap where climbs
    /// count <c>climbWeight</c> times their length (so the V1 climbs get two gems each), then any gem
    /// within <c>clearance</c> metres of a zone is nudged to the nearer clear side. Plain C#.
    /// </summary>
    public static class BoostGemLayout
    {
        public static List<float> Generate(TrackProfile profile, IList<Vector2> zoneRanges, int count,
            float clearance = 4f, float climbWeight = 2f)
        {
            var result = new List<float>();
            if (profile == null || count <= 0 || profile.Length <= 0f) return result;
            float length = profile.Length;
            climbWeight = Mathf.Max(1f, climbWeight);

            float weighted = 0f;
            foreach (var s in profile.sections) weighted += s.length * (s.gradePercent > 0f ? climbWeight : 1f);

            for (int i = 0; i < count; i++)
            {
                float z = WeightedToLap(profile, (i + 0.5f) * weighted / count, climbWeight);
                result.Add(Nudge(z, zoneRanges, clearance, length));
            }
            result.Sort();
            return result;
        }

        static float WeightedToLap(TrackProfile profile, float w, float climbWeight)
        {
            float z = 0f;
            foreach (var s in profile.sections)
            {
                float k = s.gradePercent > 0f ? climbWeight : 1f;
                float sw = s.length * k;
                if (w <= sw) return z + w / k;
                w -= sw;
                z += s.length;
            }
            return z;
        }

        /// <summary>Moves z out of every zone range widened by clearance (to the nearer edge that stays on the lap).</summary>
        public static float Nudge(float z, IList<Vector2> zoneRanges, float clearance, float lapLength)
        {
            if (zoneRanges == null) return z;
            for (int pass = 0; pass < 8; pass++)
            {
                bool moved = false;
                foreach (var r in zoneRanges)
                {
                    float lo = Mathf.Min(r.x, r.y) - clearance, hi = Mathf.Max(r.x, r.y) + clearance;
                    if (z > lo && z < hi)
                    {
                        bool loOk = lo >= 0f, hiOk = hi <= lapLength;
                        z = (loOk && (!hiOk || z - lo <= hi - z)) ? lo : hi;
                        moved = true;
                    }
                }
                if (!moved) break;
            }
            return Mathf.Clamp(z, 0f, lapLength);
        }

        /// <summary>True if z is at least clearance metres outside every zone range.</summary>
        public static bool IsClear(float z, IList<Vector2> zoneRanges, float clearance)
        {
            if (zoneRanges == null) return true;
            foreach (var r in zoneRanges)
                if (z > Mathf.Min(r.x, r.y) - clearance + 1e-3f && z < Mathf.Max(r.x, r.y) + clearance - 1e-3f) return false;
            return true;
        }
    }

    public struct GemPickup
    {
        public int index;          // gem index on the lap
        public int lap;            // 0-based lap it was collected on
        public double distance;    // course distance of the gem (lap x length + gem position)
    }

    /// <summary>
    /// Collects gems by course distance: a gem is collected when the total course distance goes
    /// past lap x lapLength + gem position between two samples, so a big step (high speed, a
    /// frame hitch) or a lap wrap never skips one. Each gem can be collected once per lap; all
    /// gems respawn when a new lap starts. Going backwards (cube reset) re-baselines and respawns. Plain C#.
    /// </summary>
    public class BoostGemTracker
    {
        readonly float[] _gems;
        readonly bool[] _collected;
        double _last;
        bool _hasLast;

        public float LapLength { get; }
        public int Count => _gems.Length;
        public int CurrentLap { get; private set; }
        public int CollectedThisLap { get; private set; }
        public int TotalCollected { get; private set; }

        public BoostGemTracker(IList<float> gemLapPositions, float lapLength)
        {
            _gems = gemLapPositions != null ? new List<float>(gemLapPositions).ToArray() : new float[0];
            Array.Sort(_gems);
            _collected = new bool[_gems.Length];
            LapLength = lapLength;
        }

        public float GemPosition(int i) => _gems[i];
        public bool IsCollected(int i) => _collected[i];

        int LapOf(double d) => LapLength > 0f ? (int)Math.Floor(d / LapLength + 1e-9) : 0;

        /// <summary>Start from this course distance with every gem in place.</summary>
        public void Reset(double startDistance)
        {
            Array.Clear(_collected, 0, _collected.Length);
            CollectedThisLap = 0;
            CurrentLap = LapOf(startDistance);
            _last = startDistance;
            _hasLast = true;
        }

        /// <summary>Feed the current total course distance. Returns how many gems were collected; fills pickups if given.</summary>
        public int Advance(double courseDistance, List<GemPickup> pickups = null)
        {
            if (!_hasLast || courseDistance < _last - 1e-6) { Reset(courseDistance); return 0; }
            if (LapLength <= 0f || _gems.Length == 0) { _last = courseDistance; return 0; }

            int n = 0;
            int fromLap = LapOf(_last), toLap = LapOf(courseDistance);
            if (toLap - fromLap > 1000) fromLap = toLap - 1000;
            for (int lap = fromLap; lap <= toLap; lap++)
            {
                if (lap > CurrentLap)
                {
                    Array.Clear(_collected, 0, _collected.Length);   // new lap: gems respawn
                    CollectedThisLap = 0;
                    CurrentLap = lap;
                }
                if (lap < CurrentLap) continue;
                for (int i = 0; i < _gems.Length; i++)
                {
                    double at = (double)lap * LapLength + _gems[i];
                    if (at > _last && at <= courseDistance && !_collected[i])
                    {
                        _collected[i] = true;
                        CollectedThisLap++;
                        TotalCollected++;
                        n++;
                        pickups?.Add(new GemPickup { index = i, lap = lap, distance = at });
                    }
                }
            }
            _last = courseDistance;
            return n;
        }
    }

    /// <summary>
    /// The boost count: starts at startingBoosts, +gemValue per gem, -1 per use. A boost can only
    /// start when the count is at least 1 and no boost is running. Time is passed in. Plain C#.
    /// </summary>
    public class BoostInventory
    {
        public BoostSettings Settings { get; }
        public int Count { get; private set; }
        public int Used { get; private set; }
        public int GemsCollected { get; private set; }
        public double ActiveUntil { get; private set; } = double.NegativeInfinity;

        public BoostInventory(BoostSettings settings)
        {
            Settings = settings ?? new BoostSettings();
            Reset();
        }

        public void Reset()
        {
            Count = Mathf.Max(0, Settings.startingBoosts);
            Used = GemsCollected = 0;
            ActiveUntil = double.NegativeInfinity;
        }

        public void AddGems(int gems = 1)
        {
            if (gems <= 0) return;
            GemsCollected += gems;
            Count += gems * Mathf.Max(0, Settings.gemValue);
        }

        public bool IsActive(double now) => now < ActiveUntil;
        public float Remaining(double now) => IsActive(now) ? (float)(ActiveUntil - now) : 0f;

        /// <summary>Uses one boost. False (and nothing changes) if the count is 0 or a boost is already active.</summary>
        public bool TryActivate(double now)
        {
            if (Count < 1 || IsActive(now)) return false;
            Count--;
            Used++;
            ActiveUntil = now + Mathf.Max(0.1f, Settings.boostDuration);
            return true;
        }

        /// <summary>Resistance levels to take off right now (0 when no boost is active).</summary>
        public int ResistanceReduction(double now) => IsActive(now) ? Mathf.Clamp(Settings.boostResistanceReduction, 0, 100) : 0;
    }

    /// <summary>Rising-edge detector: true only on the update where the button goes from up to held.</summary>
    public class ButtonEdge
    {
        public bool Held { get; private set; }

        public bool Update(bool held)
        {
            bool rising = held && !Held;
            Held = held;
            return rising;
        }
    }
}
