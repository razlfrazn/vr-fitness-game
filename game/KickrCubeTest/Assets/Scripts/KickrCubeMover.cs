using UnityEngine;

namespace Kickr
{
    /// <summary>
    /// Moves this object along +Z at the trainer's speed (km/h -> m/s, times speedMultiplier),
    /// rolls it like a wheel as the flywheel turns, and draws a small on-screen readout.
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

        public bool showReadout = true;

        public float CurrentSpeedMps { get; private set; }
        public float DistanceTravelled { get; private set; }

        Vector3 _startPosition;
        GUIStyle _style;

        void Start() => _startPosition = transform.position;

        void Update()
        {
            float targetKmh = 0f;
            if (receiver != null && receiver.SecondsSinceLastPacket <= stopAfterSeconds)
                targetKmh = receiver.SpeedKmh;

            float targetMps = targetKmh / 3.6f * speedMultiplier;
            float k = smoothing <= 0f ? 1f : 1f - Mathf.Exp(-smoothing * Time.deltaTime);
            CurrentSpeedMps = Mathf.Lerp(CurrentSpeedMps, targetMps, k);
            if (targetMps == 0f && CurrentSpeedMps < 0.001f) CurrentSpeedMps = 0f;

            float step = CurrentSpeedMps * Time.deltaTime;
            transform.position += Vector3.forward * step;
            DistanceTravelled += step;

            // Roll around X like a wheel of radius spinRadius (world space so it doesn't wobble).
            float degrees = step / Mathf.Max(0.01f, spinRadius) * Mathf.Rad2Deg;
            transform.Rotate(Vector3.right, degrees, Space.World);
        }

        public void ResetPosition()
        {
            transform.position = _startPosition;
            transform.rotation = Quaternion.identity;
            DistanceTravelled = 0f;
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
                    $"Cube distance: {DistanceTravelled:0.0} m" + err;
            }
            GUI.Box(new Rect(10, 10, 460, 190), text, _style);
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
