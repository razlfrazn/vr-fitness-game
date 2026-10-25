using System.Collections.Generic;
using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Moves this object along +Z at the trainer's speed (km/h -> m/s, times speedMultiplier,
    /// times any active power-up / power-down), follows the track's height profile if a
    /// <see cref="KickrTrack"/> is assigned, rolls it like a wheel as the flywheel turns, and
    /// draws a small on-screen readout.
    /// </summary>
    public class KickrCubeMover : MonoBehaviour
    {
        public KickrUdpReceiver receiver;

        [Tooltip("1 = real speed. Raise it to exaggerate motion while hand-turning the flywheel.")]
        public float speedMultiplier = 1f;

        [Tooltip("How quickly the displayed/used speed follows the trainer (higher = snappier).")]
        public float smoothing = 5f;

        [Tooltip("Treat the trainer as stopped if no packet arrives for this long (seconds).")]
        public float stopAfterSeconds = 2f;

        [Tooltip("Radius used to turn forward speed into a rolling spin (metres).")]
        public float spinRadius = 0.5f;

        [Header("Terrain")]
        [Tooltip("Optional. When set, the cube rides the track's height profile.")]
        public KickrTrack track;
        [Tooltip("Height of the cube's centre above the track surface (metres).")]
        public float heightAboveTrack = 0.5f;
        [Tooltip("Jump back to the start line after the finish, so you can keep riding.")]
        public bool loopAtEnd = true;

        [Header("Power-ups")]
        [Tooltip("Boost/slowdown zones to check. Empty = find all SpeedZones in the scene on Start.")]
        public List<SpeedZone> zones = new List<SpeedZone>();

        public bool showReadout = true;

        public float CurrentSpeedMps { get; private set; }
        public float DistanceTravelled { get; private set; }
        public float CurrentGradePercent { get; private set; }
        public int Laps { get; private set; }
        public SpeedEffects Effects { get; } = new SpeedEffects();
        /// <summary>Product of all active power-up / power-down multipliers (1 = none).</summary>
        public float EffectMultiplier => Effects.GetMultiplier(Time.time);

        Vector3 _startPosition;
        GUIStyle _style;

        void Start()
        {
            if (zones.Count == 0)
                zones.AddRange(FindObjectsByType<SpeedZone>());
            if (track != null)
            {
                var p = transform.position;
                p.y = track.HeightAtWorldZ(p.z) + heightAboveTrack;
                transform.position = p;
            }
            _startPosition = transform.position;
        }

        void Update()
        {
            float targetKmh = 0f;
            if (receiver != null && receiver.SecondsSinceLastPacket <= stopAfterSeconds)
                targetKmh = receiver.SpeedKmh;

            float targetMps = targetKmh / 3.6f * speedMultiplier * EffectMultiplier;
            float k = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            CurrentSpeedMps = Mathf.Lerp(CurrentSpeedMps, targetMps, k);
            if (targetMps == 0f && CurrentSpeedMps < 0.001f) CurrentSpeedMps = 0f;

            float step = CurrentSpeedMps * Time.deltaTime;
            DistanceTravelled += step;

            var p = transform.position;
            float prevZ = p.z;
            if (track != null)
            {
                // Speed is along the slope, so the forward (Z) part is a little shorter on hills.
                float g = track.GradePercentAtWorldZ(p.z) / 100f;
                p.z += step / Mathf.Sqrt(1f + g * g);
                if (loopAtEnd && p.z >= track.EndWorldZ)
                {
                    p.z = track.StartWorldZ + (p.z - track.EndWorldZ);
                    prevZ = p.z;
                    Laps++;
                }
                p.y = track.HeightAtWorldZ(p.z) + heightAboveTrack;
                CurrentGradePercent = track.GradePercentAtWorldZ(p.z);
            }
            else
            {
                p.z += step;
            }
            transform.position = p;

            CheckZones(prevZ, p.z);

            // Roll around X like a wheel of radius spinRadius (world space so it doesn't wobble).
            float degrees = step / Mathf.Max(0.01f, spinRadius) * Mathf.Rad2Deg;
            transform.Rotate(Vector3.right, degrees, Space.World);
        }

        void CheckZones(float prevZ, float z)
        {
            foreach (var zone in zones)
            {
                if (zone == null || !zone.isActiveAndEnabled) continue;
                if (SpeedEffects.CrossedInto(prevZ, z, zone.StartZ))
                    TriggerZone(zone);
            }
        }

        /// <summary>Apply a zone's effect now (re-triggering the same kind refreshes its timer).</summary>
        public void TriggerZone(SpeedZone zone)
        {
            Effects.Trigger(zone.EffectKey, zone.multiplier, zone.durationSeconds, Time.time);
            Debug.Log($"[KickrCubeMover] {zone.kind} x{zone.multiplier:0.##} for {zone.durationSeconds:0.#} s ({zone.name})");
        }

        public void ResetPosition()
        {
            transform.position = _startPosition;
            transform.rotation = Quaternion.identity;
            DistanceTravelled = 0f;
            Effects.Clear();
        }

        void OnGUI()
        {
            if (!showReadout) return;
            if (_style == null)
                _style = new GUIStyle(GUI.skin.box) { fontSize = 20, alignment = TextAnchor.UpperLeft, richText = true };

            string text;
            if (receiver == null)
            {
                text = "<b>No KickrUdpReceiver assigned</b>";
            }
            else
            {
                float age = receiver.SecondsSinceLastPacket;
                string ageText = float.IsInfinity(age) ? "never" : $"{age:0.0}s ago";
                string err = string.IsNullOrEmpty(receiver.LastError) ? "" : $"\n<color=orange>{receiver.LastError}</color>";
                text =
                    $"<b>KICKR</b>  state: {StateLabel(receiver.State)}  (UDP {receiver.port})\n" +
                    $"Speed:   {receiver.SpeedKmh:0.0} km/h  (cube {CurrentSpeedMps:0.00} m/s)\n" +
                    $"Cadence: {receiver.CadenceRpm:0} rpm\n" +
                    $"Power:   {receiver.PowerW} W\n" +
                    $"Packets: {receiver.PacketsReceived}  last: {ageText}\n" +
                    $"Cube distance: {DistanceTravelled:0.0} m" + (track != null ? $"  (z {transform.position.z:0} / {track.EndWorldZ:0} m, lap {Laps + 1})" : "") + "\n" +
                    EffectsLabel() + err;
            }
            GUI.Box(new Rect(10, 10, 460, 250), text, _style);
        }

        string EffectsLabel()
        {
            float now = Time.time;
            string s = $"Multiplier: x{EffectMultiplier:0.00}";
            string boost = SpeedZoneKind.Boost.ToString(), slow = SpeedZoneKind.Slowdown.ToString();
            if (Effects.IsActive(boost, now)) s += $"  <color=lime>BOOST {Effects.Remaining(boost, now):0.0}s</color>";
            if (Effects.IsActive(slow, now)) s += $"  <color=red>SLOW {Effects.Remaining(slow, now):0.0}s</color>";
            return s;
        }

        static string StateLabel(KickrConnectionState s)
        {
            switch (s)
            {
                case KickrConnectionState.Live: return "<color=lime>LIVE</color>";
                case KickrConnectionState.Stale: return "<color=yellow>STALE (no recent data)</color>";
                case KickrConnectionState.WaitingForData: return "<color=yellow>waiting for bridge…</color>";
                default: return "<color=red>NOT LISTENING</color>";
            }
        }
    }
}
