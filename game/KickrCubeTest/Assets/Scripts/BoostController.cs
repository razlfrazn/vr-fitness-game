using System.Collections.Generic;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Kickr
{
    /// <summary>
    /// Boost gems and the boost count. Spawns the gems for one lap along the road (respawned every
    /// lap), collects them from the mover's course distance (see <see cref="BoostGemTracker"/>), and
    /// starts a boost on the controller's A button (right handlebar of the Zwift Ride, through
    /// <see cref="ZwiftRideUdpReceiver"/> on UDP 5006). A boost speeds the cube up
    /// (<see cref="BoostSettings.speedMultiplier"/>) and makes <see cref="KickrResistanceSender"/>
    /// take <see cref="BoostSettings.boostResistanceReduction"/> off the trainer for
    /// <see cref="BoostSettings.boostDuration"/> seconds. Space bar is a hidden test fallback.
    /// </summary>
    public class BoostController : MonoBehaviour
    {
        public const string EffectKey = "GemBoost";

        public KickrCubeMover mover;
        public KickrTrack track;
        [Tooltip("Controller button source (zwift_play_listen.py -> UDP 5006). Optional.")]
        public ZwiftRideUdpReceiver buttons;

        public BoostSettings settings = new BoostSettings();

        [Header("Activation button")]
        [Tooltip("Button name as the listener sends it: A, B, Y, Z, RIGHT_POWERUP, RIGHT_SHIFT_UP, DPAD_UP, ...")]
        public string boostButton = "A";
        [Tooltip("Physical handlebar side the button must be on: right, left, or empty for either.")]
        public string boostButtonSide = "right";
        [Tooltip("Hidden testing fallback: Space bar also uses a boost.")]
        public bool keyboardFallback = true;

        [Header("Gems (gray-box)")]
        public bool spawnGems = true;
        public Material gemMaterial;
        public Color gemColor = new Color(0.2f, 0.85f, 1f);
        [Tooltip("Height of the gem centre above the road surface (m).")]
        public float gemHeight = 0.9f;
        public float gemSize = 0.45f;
        public float spinDegreesPerSecond = 90f;
        public float bobAmplitude = 0.12f;
        public float bobHz = 0.5f;
        public float flashSeconds = 0.3f;

        public bool showReadout = true;

        /// <summary>Tests can hold the button through this instead of UDP or the keyboard.</summary>
        public bool ExternalButtonHeld { get; set; }

        public BoostInventory Inventory { get; private set; }
        public BoostGemTracker Gems { get; private set; }
        public IReadOnlyList<float> GemPositions => _positions;
        public IReadOnlyList<GameObject> GemObjects => _gemObjects;
        public bool IsActive => Inventory != null && Inventory.IsActive(Time.time);
        public float Remaining => Inventory != null ? Inventory.Remaining(Time.time) : 0f;
        /// <summary>Resistance levels the trainer sender should take off right now.</summary>
        public int ResistanceReduction => Inventory != null ? Inventory.ResistanceReduction(Time.time) : 0;
        public int Activations { get; private set; }

        readonly ButtonEdge _edge = new ButtonEdge();
        readonly List<float> _positions = new List<float>();
        readonly List<GameObject> _gemObjects = new List<GameObject>();
        readonly List<Light> _gemLights = new List<Light>();
        readonly List<float> _popTimes = new List<float>();
        readonly List<GemPickup> _pickups = new List<GemPickup>();
        Transform _gemRoot;
        Material _runtimeMaterial;
        int _shownLap = int.MinValue;
        GUIStyle _style;

        void OnEnable()
        {
            Inventory = new BoostInventory(settings);
            BuildGems();
        }

        void OnDisable()
        {
            if (_gemRoot != null) Destroy(_gemRoot.gameObject);
            _gemRoot = null;
            _gemObjects.Clear(); _gemLights.Clear(); _popTimes.Clear();
            if (_runtimeMaterial != null) Destroy(_runtimeMaterial);
            _runtimeMaterial = null;
            Gems = null;
        }

        /// <summary>Lap length and red/green zone ranges (lap metres) used for the layout.</summary>
        void BuildGems()
        {
            _positions.Clear();
            var profile = track != null ? track.profile : null;
            if (profile == null || profile.Length <= 0f) { Gems = new BoostGemTracker(_positions, 0f); return; }

            var ranges = new List<Vector2>();
            if (mover != null && mover.zones != null)
                foreach (var z in mover.zones)
                    if (z != null) ranges.Add(new Vector2(z.StartZ - track.StartWorldZ, z.EndZ - track.StartWorldZ));
            _positions.AddRange(BoostGemLayout.Generate(profile, ranges, settings.gemsPerLap, settings.zoneClearance, settings.climbWeight));
            Gems = new BoostGemTracker(_positions, profile.Length);
            _shownLap = int.MinValue;
            if (spawnGems) SpawnGemObjects();
        }

        void SpawnGemObjects()
        {
            _gemRoot = new GameObject("Gems (runtime)").transform;
            _gemRoot.SetParent(transform, false);
            var mat = gemMaterial != null ? gemMaterial : RuntimeMaterial();
            for (int i = 0; i < _positions.Count; i++)
            {
                var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                go.name = $"Gem_{i:00}_z{_positions[i]:0}";
                var col = go.GetComponent<Collider>();
                if (col != null) Destroy(col);
                go.transform.SetParent(_gemRoot, false);
                var rend = go.GetComponent<Renderer>();
                if (mat != null) rend.sharedMaterial = mat;
                rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = gemColor;
                light.range = 6f;
                light.intensity = 0f;
                light.enabled = false;
                _gemObjects.Add(go);
                _gemLights.Add(light);
                _popTimes.Add(-1f);
            }
            AnimateGems(Time.time);
        }

        Material RuntimeMaterial()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            _runtimeMaterial = new Material(shader) { name = "BoostGem (runtime)" };
            ApplyGemLook(_runtimeMaterial, gemColor);
            return _runtimeMaterial;
        }

        /// <summary>Emissive gray-box look (also used by the scene builder for the BoostGem material).</summary>
        public static void ApplyGemLook(Material m, Color c)
        {
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Color")) m.SetColor("_Color", c);
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", c * 2f);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        void Update()
        {
            float now = Time.time;
            if (buttons != null) buttons.Pump();
            bool held = ExternalButtonHeld || (buttons != null && buttons.State.IsHeld(boostButton, boostButtonSide)) || KeyboardHeld();
            if (_edge.Update(held)) TryActivate();

            if (mover != null && Gems != null)
            {
                _pickups.Clear();
                if (Gems.Advance(mover.CourseDistanceMeters, _pickups) > 0)
                    foreach (var p in _pickups)
                    {
                        Inventory.AddGems(1);
                        if (p.index < _popTimes.Count) _popTimes[p.index] = now;
                    }
            }
            AnimateGems(now);
        }

        bool KeyboardHeld()
        {
            if (!keyboardFallback) return false;
#if ENABLE_INPUT_SYSTEM
            var kb = Keyboard.current;
            return kb != null && kb.spaceKey.isPressed;
#else
            return Input.GetKey(KeyCode.Space);
#endif
        }

        /// <summary>Uses one boost if possible (count >= 1 and none active). Returns true if a boost started.</summary>
        public bool TryActivate()
        {
            float now = Time.time;
            if (Inventory == null || !Inventory.TryActivate(now)) return false;
            Activations++;
            if (mover != null) mover.Effects.Trigger(EffectKey, settings.speedMultiplier, settings.boostDuration, now);
            Debug.Log($"[Boost] boost started: {settings.boostDuration:0.#} s, x{settings.speedMultiplier:0.##} speed, -{settings.boostResistanceReduction} resistance; {Inventory.Count} left");
            return true;
        }

        void AnimateGems(float now)
        {
            if (Gems == null || _gemObjects.Count == 0) return;
            bool newLap = Gems.CurrentLap != _shownLap;
            _shownLap = Gems.CurrentLap;
            float baseY = track != null ? track.transform.position.y : 0f;
            float startZ = track != null ? track.StartWorldZ : 0f;
            for (int i = 0; i < _gemObjects.Count; i++)
            {
                var go = _gemObjects[i];
                if (go == null) continue;
                float z = _positions[i];
                float popAge = _popTimes[i] >= 0f ? now - _popTimes[i] : -1f;
                if (newLap && !Gems.IsCollected(i) && (popAge < 0f || popAge >= flashSeconds)) _popTimes[i] = popAge = -1f;
                bool popping = popAge >= 0f && popAge < flashSeconds;
                bool visible = !Gems.IsCollected(i) || popping;
                if (go.activeSelf != visible) go.SetActive(visible);
                if (!visible) { _gemLights[i].enabled = false; continue; }

                float h = (track != null ? track.profile.HeightAt(z) : 0f) + baseY;
                float bob = bobAmplitude * Mathf.Sin((now * bobHz + i * 0.37f) * 2f * Mathf.PI);
                go.transform.position = new Vector3(0f, h + gemHeight + bob, startZ + z);
                go.transform.rotation = Quaternion.Euler(45f, now * spinDegreesPerSecond + i * 36f, 45f);
                if (popping)
                {
                    float k = popAge / Mathf.Max(0.01f, flashSeconds);
                    go.transform.localScale = Vector3.one * gemSize * (1f + 3f * k);
                    _gemLights[i].enabled = true;
                    _gemLights[i].intensity = 8f * (1f - k);
                }
                else
                {
                    go.transform.localScale = Vector3.one * gemSize;
                    _gemLights[i].enabled = false;
                }
            }
        }

        public string HudText()
        {
            if (Inventory == null) return "";
            string active = IsActive ? $"  <color=cyan>BOOST {Remaining:0.0} s</color>" : "";
            int gems = Gems != null ? Gems.Count : 0;
            int got = Gems != null ? Gems.CollectedThisLap : 0;
            return $"<b>Boosts: {Inventory.Count}</b>{active}\nGems this lap: {got} / {gems}";
        }

        void OnGUI()
        {
            if (!showReadout || Inventory == null) return;
            if (_style == null) _style = KickrHud.CreateBoxStyle();
            GUI.Box(KickrHud.BoostBox, HudText(), _style);
        }
    }
}
