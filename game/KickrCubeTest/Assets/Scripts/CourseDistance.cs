using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Distance ridden, worked out from the cube's progress along the known course (laps x course
    /// length + position on the current lap), not from the trainer (the KICKR reports no distance).
    /// Plain C# so it is unit-testable.
    /// </summary>
    public static class CourseDistance
    {
        /// <summary>Position on the current lap (metres from the start line, 0..lapLength).</summary>
        public static float LapPosition(float worldZ, float startWorldZ, float lapLength)
        {
            float d = worldZ - startWorldZ;
            return lapLength > 0f ? Mathf.Clamp(d, 0f, lapLength) : Mathf.Max(0f, d);
        }

        /// <summary>Total distance over all laps: completed laps x lapLength + position on this lap.</summary>
        public static float Total(int completedLaps, float lapPosition, float lapLength)
        {
            if (lapLength <= 0f) return Mathf.Max(0f, lapPosition);
            return Mathf.Max(0, completedLaps) * lapLength + Mathf.Clamp(lapPosition, 0f, lapLength);
        }

        /// <summary>Split a total distance into completed laps and position on the current lap (wraps at lapLength).</summary>
        public static void Split(float total, float lapLength, out int completedLaps, out float lapPosition)
        {
            total = Mathf.Max(0f, total);
            if (lapLength <= 0f) { completedLaps = 0; lapPosition = total; return; }
            completedLaps = Mathf.FloorToInt(total / lapLength);
            lapPosition = total - completedLaps * lapLength;
            if (lapPosition >= lapLength) { completedLaps++; lapPosition = 0f; } // float rounding at the line
            if (lapPosition < 0f) lapPosition = 0f;
        }

        /// <summary>HUD text: "Total: 1.23 km  (1234 m)" and "Lap 3: 234 / 500 m".</summary>
        public static string Label(int completedLaps, float lapPosition, float lapLength)
        {
            float total = Total(completedLaps, lapPosition, lapLength);
            string lap = lapLength > 0f
                ? $"Lap {Mathf.Max(0, completedLaps) + 1}: {Mathf.Clamp(lapPosition, 0f, lapLength):0} / {lapLength:0} m"
                : "Lap: - (no track)";
            return $"Total: {total / 1000f:0.00} km  ({total:0} m)\n" + lap;
        }
    }
}
