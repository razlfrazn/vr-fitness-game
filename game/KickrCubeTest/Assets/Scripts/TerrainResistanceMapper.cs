using System;
using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Maps course grade to a KICKR FTMS resistance level (0-100): flat = baseline, linear in
    /// grade, separate gains for up and down, clamped. Plain C# so it is unit-testable.
    /// </summary>
    [Serializable]
    public class TerrainResistanceMapper
    {
        [Tooltip("Resistance on flat ground.")]
        [Range(0, 100)] public int baselineLevel = 30;
        [Tooltip("Levels added per +1% grade when going uphill.")]
        public float uphillLevelsPerGradePercent = 3f;
        [Tooltip("Levels removed per -1% grade when going downhill.")]
        public float downhillLevelsPerGradePercent = 3f;
        [Range(0, 100)] public int minLevel = 0;
        [Range(0, 100)] public int maxLevel = 100;

        public int LevelForGrade(float gradePercent)
        {
            float gain = gradePercent >= 0f ? uphillLevelsPerGradePercent : downhillLevelsPerGradePercent;
            float level = baselineLevel + gradePercent * gain;
            int lo = Mathf.Clamp(Mathf.Min(minLevel, maxLevel), 0, 100);
            int hi = Mathf.Clamp(Mathf.Max(minLevel, maxLevel), 0, 100);
            return Mathf.Clamp(Mathf.RoundToInt(level), lo, hi);
        }
    }

    /// <summary>
    /// Decides when a new resistance level may be sent: only if it differs from the last sent
    /// level by at least <see cref="minChange"/> and at least <see cref="minIntervalSeconds"/>
    /// have passed since the last send. The caller re-asks every frame, so a change that was held
    /// back goes out as soon as the interval allows.
    /// </summary>
    [Serializable]
    public class ResistanceRateLimiter
    {
        [Tooltip("Only send when the level changes by at least this much.")]
        public int minChange = 1;
        [Tooltip("Never send more often than this (0.5 s = 2 Hz).")]
        public float minIntervalSeconds = 0.5f;

        [NonSerialized] bool _hasSent;
        [NonSerialized] int _lastLevel;
        [NonSerialized] double _lastTime;

        public bool HasSent => _hasSent;
        public int LastSentLevel => _hasSent ? _lastLevel : -1;

        public bool ShouldSend(int level, double now)
        {
            if (_hasSent)
            {
                if (Math.Abs(level - _lastLevel) < Math.Max(1, minChange)) return false;
                if (now - _lastTime < minIntervalSeconds) return false;
            }
            _hasSent = true;
            _lastLevel = level;
            _lastTime = now;
            return true;
        }

        public void Reset() => _hasSent = false;
    }
}
