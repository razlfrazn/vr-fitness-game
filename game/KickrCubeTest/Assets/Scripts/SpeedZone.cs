using UnityEngine;

namespace Kickr
{
    public enum SpeedZoneKind { Boost, Slowdown }

    /// <summary>
    /// A gray-box trigger zone on the track. When the cube's z crosses <see cref="StartZ"/> the
    /// mover applies <see cref="multiplier"/> for <see cref="durationSeconds"/>. Riding through
    /// another zone of the same kind refreshes the timer. No physics needed.
    /// </summary>
    public class SpeedZone : MonoBehaviour
    {
        public SpeedZoneKind kind = SpeedZoneKind.Boost;
        [Tooltip("Speed multiplier while active (1.5 = 50% faster, 0.6 = 40% slower).")]
        public float multiplier = 1.5f;
        [Tooltip("How long the effect lasts after entering (seconds).")]
        public float durationSeconds = 3f;
        [Tooltip("Zone length along Z (metres), centred on this object.")]
        public float length = 4f;

        public float StartZ => transform.position.z - length * 0.5f;
        public float EndZ => transform.position.z + length * 0.5f;
        public string EffectKey => kind.ToString();

        public static float DefaultMultiplier(SpeedZoneKind k) => k == SpeedZoneKind.Boost ? 1.5f : 0.6f;
    }
}
