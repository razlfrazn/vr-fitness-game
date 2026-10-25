using System.Collections.Generic;

namespace Kickr
{
    /// <summary>
    /// Temporary speed multipliers (power-ups / power-downs). Each effect has a key; triggering
    /// the same key again replaces its multiplier and restarts its timer. Different keys stack by
    /// multiplying. Time is passed in, so this is a plain, unit-testable class.
    /// </summary>
    public class SpeedEffects
    {
        struct Effect { public float multiplier; public float until; }
        readonly Dictionary<string, Effect> _effects = new Dictionary<string, Effect>();

        public void Trigger(string key, float multiplier, float durationSeconds, float now)
        {
            _effects[key] = new Effect { multiplier = multiplier, until = now + durationSeconds };
        }

        public float GetMultiplier(float now)
        {
            float m = 1f;
            foreach (var e in _effects.Values)
                if (now < e.until) m *= e.multiplier;
            return m;
        }

        public bool IsActive(string key, float now) => _effects.TryGetValue(key, out var e) && now < e.until;

        public float Remaining(string key, float now)
        {
            if (!_effects.TryGetValue(key, out var e)) return 0f;
            return e.until > now ? e.until - now : 0f;
        }

        public void Clear() => _effects.Clear();

        /// <summary>True when a move from prevZ to z crosses the zone's start (also if it jumps past the whole zone).</summary>
        public static bool CrossedInto(float prevZ, float z, float zoneStartZ) => prevZ < zoneStartZ && z >= zoneStartZ;
    }
}
