using System;
using System.Collections.Generic;
using UnityEngine;

namespace Kickr
{
    /// <summary>Anything that can deliver a resistance level to the trainer (real UDP bridge or a test mock).</summary>
    public interface IResistanceCommandSender
    {
        void SendResistance(int level);
    }

    /// <summary>
    /// Zone resistance relative to the current slope: inside a red (slowdown) zone the level is
    /// terrain + <see cref="slowdownOffset"/>, inside a green (boost) zone it is terrain -
    /// <see cref="boostOffset"/>, clamped 0-100. Overlapping zones of the same colour don't stack;
    /// overlapping red and green add both offsets. Plain C# so it is unit-testable.
    /// </summary>
    [Serializable]
    public class ZoneResistance
    {
        [Tooltip("Resistance levels added to the terrain level while inside a red slowdown zone.")]
        [Range(0, 100)] public int slowdownOffset = 20;
        [Tooltip("Resistance levels taken off the terrain level while inside a green boost zone.")]
        [Range(0, 100)] public int boostOffset = 20;

        public int Apply(int terrainLevel, bool inSlowdownZone, bool inBoostZone = false)
        {
            int level = terrainLevel;
            if (inSlowdownZone) level += Mathf.Clamp(slowdownOffset, 0, 100);
            if (inBoostZone) level -= Mathf.Clamp(boostOffset, 0, 100);
            return Mathf.Clamp(level, 0, 100);
        }

        /// <summary>True if z is inside [startZ, endZ).</summary>
        public static bool IsInside(float z, float startZ, float endZ) => z >= startZ && z < endZ;

        /// <summary>True if z is inside any enabled zone of the given kind.</summary>
        public static bool InsideAny(IEnumerable<SpeedZone> zones, float z, SpeedZoneKind kind)
        {
            if (zones == null) return false;
            foreach (var zone in zones)
                if (zone != null && zone.isActiveAndEnabled && zone.kind == kind && IsInside(z, zone.StartZ, zone.EndZ))
                    return true;
            return false;
        }

        public static bool InsideAnySlowdown(IEnumerable<SpeedZone> zones, float z) => InsideAny(zones, z, SpeedZoneKind.Slowdown);
        public static bool InsideAnyBoost(IEnumerable<SpeedZone> zones, float z) => InsideAny(zones, z, SpeedZoneKind.Boost);
    }

    /// <summary>
    /// Decides what resistance to send each frame from (grade, inside red / green zone, time):
    /// target = terrain level (+ red offset, - green offset while inside a zone).
    /// Normal changes go through the rate limiter (change >= minChange, at most 2 Hz).
    /// Zone edges are never lost:
    /// - entering a zone (or switching colour): the zone level is held as pending and goes out on
    ///   the next allowed send (as soon as the interval allows), even if it differs by less than minChange;
    /// - leaving all zones: the terrain level is recomputed (grade may have changed) and sent
    ///   immediately, even inside the 0.5 s window. If the zone level never went out, there is
    ///   nothing to undo and the pending zone level is dropped, so a stale level is never replayed.
    /// </summary>
    public class ResistanceController
    {
        public TerrainResistanceMapper Mapping;
        public ResistanceRateLimiter RateLimit;
        public ZoneResistance Zone;
        public IResistanceCommandSender Sender;

        bool _inSlowdown, _inBoost;
        bool _enterPending;

        public int TerrainLevel { get; private set; } = -1;
        public int TargetLevel { get; private set; } = -1;
        public bool InSlowdownZone => _inSlowdown;
        public bool InBoostZone => _inBoost;
        public bool EnterPending => _enterPending;

        public ResistanceController(TerrainResistanceMapper mapping, ResistanceRateLimiter rateLimit, ZoneResistance zone, IResistanceCommandSender sender)
        {
            Mapping = mapping ?? new TerrainResistanceMapper();
            RateLimit = rateLimit ?? new ResistanceRateLimiter();
            Zone = zone ?? new ZoneResistance();
            Sender = sender;
        }

        public void Reset()
        {
            RateLimit.Reset();
            _inSlowdown = _inBoost = false;
            _enterPending = false;
            TerrainLevel = TargetLevel = -1;
        }

        /// <summary>Call every frame (red zones only). Returns true if a level was sent this call.</summary>
        public bool Tick(float gradePercent, bool inSlowdownZone, double now) => Tick(gradePercent, inSlowdownZone, false, now);

        /// <summary>Call every frame. Returns true if a level was sent this call.</summary>
        public bool Tick(float gradePercent, bool inSlowdownZone, bool inBoostZone, double now)
        {
            bool wasInZone = _inSlowdown || _inBoost;
            bool inZone = inSlowdownZone || inBoostZone;
            bool changed = inSlowdownZone != _inSlowdown || inBoostZone != _inBoost;
            bool entered = changed && inZone;        // into a zone, or switched colour
            bool left = changed && !inZone && wasInZone;
            _inSlowdown = inSlowdownZone;
            _inBoost = inBoostZone;

            TerrainLevel = Mapping.LevelForGrade(gradePercent);
            TargetLevel = Zone.Apply(TerrainLevel, inSlowdownZone, inBoostZone);

            if (entered) _enterPending = true;

            if (left)
            {
                _enterPending = false;
                if (RateLimit.HasSent && RateLimit.LastSentLevel != TargetLevel)
                    return Force(TargetLevel, now);   // restore: bypasses the interval
                return false;
            }

            if (_enterPending)
            {
                if (!RateLimit.IntervalElapsed(now)) return false;   // wait for the next allowed send
                _enterPending = false;
                if (RateLimit.LastSentLevel != TargetLevel)
                    return Force(TargetLevel, now);
                return false;
            }

            if (RateLimit.ShouldSend(TargetLevel, now))
            {
                Sender?.SendResistance(TargetLevel);
                return true;
            }
            return false;
        }

        bool Force(int level, double now)
        {
            RateLimit.Record(level, now);
            Sender?.SendResistance(level);
            return true;
        }
    }
}
