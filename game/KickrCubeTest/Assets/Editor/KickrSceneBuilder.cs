using System;
using System.Collections.Generic;
using System.IO;
using Kickr;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// Builds Assets/Scenes/KickrCubeTest.unity from code so the scene is reproducible: the V1
/// gray-box course (hills and valleys from TrackProfile.CreateV1), boost/slowdown zones, the
/// KICKR receiver, the cube, the grade -> resistance sender and the chase camera.
/// Menu: Kickr > Rebuild KickrCubeTest Scene.
/// Batch mode: Unity -batchmode -quit -projectPath . -executeMethod KickrSceneBuilder.BuildFromCommandLine
/// </summary>
public static class KickrSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/KickrCubeTest.unity";
    const string MaterialFolder = "Assets/Materials";

    public const float RoadWidth = 4f;
    public const float RoadThickness = 0.2f;
    public const float EmbankmentWidth = 7f;
    public const float EmbankmentSlice = 2.5f;
    public const float GroundY = -3f;
    public const float MarkerSpacing = 10f;
    public const float ZoneLength = 4f;

    /// <summary>Power-up / power-down zones: (local z of the zone centre, kind).</summary>
    public static readonly (float z, SpeedZoneKind kind)[] ZoneLayout =
    {
        (70f, SpeedZoneKind.Boost),      // mid Climb 1
        (160f, SpeedZoneKind.Slowdown),  // mid Descent 1
        (260f, SpeedZoneKind.Boost),     // mid Climb 2 (steepest)
        (350f, SpeedZoneKind.Slowdown),  // mid Descent 2
        (470f, SpeedZoneKind.Boost),     // finish straight sprint
    };

    [MenuItem("Kickr/Rebuild KickrCubeTest Scene")]
    public static void BuildFromMenu()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        Build();
        EditorSceneManager.OpenScene(ScenePath);
    }

    public static void BuildFromCommandLine()
    {
        try
        {
            Build();
            Debug.Log("[KickrSceneBuilder] BUILD_SCENE_OK " + ScenePath);
        }
        catch (Exception e)
        {
            Debug.LogError("[KickrSceneBuilder] BUILD_SCENE_FAILED " + e);
            EditorApplication.Exit(1);
        }
    }

    public static void Build()
    {
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory(MaterialFolder);
        AssetDatabase.Refresh();

        var groundMat = GetOrCreateMaterial("Ground", new Color(0.30f, 0.36f, 0.30f));
        var cubeMat = GetOrCreateMaterial("Cube", new Color(1.00f, 0.45f, 0.10f));
        var markerMat = GetOrCreateMaterial("Marker", new Color(0.95f, 0.95f, 0.95f));
        var stripeMat = GetOrCreateMaterial("Stripe", new Color(0.15f, 0.15f, 0.18f));
        var roadFlatMat = GetOrCreateMaterial("RoadFlat", new Color(0.55f, 0.55f, 0.55f));
        var roadUpMat = GetOrCreateMaterial("RoadUphill", new Color(0.62f, 0.50f, 0.46f));
        var roadDownMat = GetOrCreateMaterial("RoadDownhill", new Color(0.46f, 0.52f, 0.62f));
        var embankMat = GetOrCreateMaterial("Embankment", new Color(0.38f, 0.38f, 0.40f));
        var lineMat = GetOrCreateMaterial("Line", new Color(0.97f, 0.97f, 0.97f));
        var boostMat = GetOrCreateMaterial("ZoneBoost", new Color(0.10f, 0.90f, 0.20f, 0.35f), transparent: true);
        var slowMat = GetOrCreateMaterial("ZoneSlowdown", new Color(0.95f, 0.15f, 0.10f, 0.35f), transparent: true);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Light
        var lightGo = new GameObject("Directional Light");
        var light = lightGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.2f;
        light.shadows = LightShadows.Soft;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.55f, 0.65f, 0.80f);
        RenderSettings.ambientEquatorColor = new Color(0.40f, 0.42f, 0.45f);
        RenderSettings.ambientGroundColor = new Color(0.20f, 0.20f, 0.20f);

        // Receiver (owns the UDP socket)
        var kickrGo = new GameObject("KickrReceiver");
        var receiver = kickrGo.AddComponent<KickrUdpReceiver>();
        receiver.port = 5005;

        // Course: profile + gray-box geometry
        var trackGo = new GameObject("Track");
        var kickrTrack = trackGo.AddComponent<KickrTrack>();
        kickrTrack.profile = TrackProfile.CreateV1();
        var profile = kickrTrack.profile;
        float length = profile.Length;
        BuildRoad(trackGo.transform, profile, roadFlatMat, roadUpMat, roadDownMat);
        BuildEmbankment(trackGo.transform, profile, embankMat);
        AddLine(trackGo.transform, "StartLine", 0f, profile, lineMat);
        AddLine(trackGo.transform, "FinishLine", length, profile, lineMat);

        // Ground plane under the whole course (Plane is 10x10 m at scale 1).
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.position = new Vector3(0f, GroundY, length * 0.5f);
        ground.transform.localScale = new Vector3(10f, 1f, (length + 200f) / 10f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;

        // Cube, sitting on the start line
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "KickrCube";
        cube.transform.position = new Vector3(0f, profile.HeightAt(0f) + 0.5f, 0f);
        cube.GetComponent<Renderer>().sharedMaterial = cubeMat;
        var mover = cube.AddComponent<KickrCubeMover>();
        mover.receiver = receiver;
        mover.track = kickrTrack;
        mover.heightAboveTrack = 0.5f;
        // A dark stripe on the cube so the spin is easy to see.
        var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stripe.name = "SpinStripe";
        UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
        stripe.transform.SetParent(cube.transform, false);
        stripe.transform.localScale = new Vector3(1.02f, 0.2f, 1.02f);
        stripe.GetComponent<Renderer>().sharedMaterial = stripeMat;

        // Marker posts every 10 m on both sides, standing on the embankment (fixed, not recycled).
        var markersComp = new GameObject("TrackMarkers").AddComponent<TrackMarkers>();
        markersComp.target = cube.transform;
        markersComp.ground = ground.transform;
        markersComp.spacing = MarkerSpacing;
        markersComp.recycle = false;
        var markers = new List<Transform>();
        for (float z = 0f; z <= length + 0.01f; z += MarkerSpacing)
        {
            bool major = Mathf.Abs(z % 50f) < 0.01f;
            float postH = major ? 2.5f : 1.5f;
            foreach (float x in new[] { -3f, 3f })
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = $"Marker_{(x < 0 ? "L" : "R")}_{z:000}";
                UnityEngine.Object.DestroyImmediate(post.GetComponent<Collider>());
                post.transform.SetParent(markersComp.transform, false);
                float baseY = profile.HeightAt(z) - 0.25f;
                post.transform.position = new Vector3(x, baseY + postH * 0.5f, z);
                post.transform.localScale = new Vector3(0.25f, postH, 0.25f);
                post.GetComponent<Renderer>().sharedMaterial = markerMat;
                markers.Add(post.transform);
            }
        }
        markersComp.markers = markers;

        // Power-up / power-down zones (translucent boxes; the mover checks z, no physics).
        var zonesRoot = new GameObject("SpeedZones").transform;
        var zones = new List<SpeedZone>();
        int boostN = 0, slowN = 0;
        foreach (var (z, kind) in ZoneLayout)
        {
            var zoneGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            zoneGo.name = kind == SpeedZoneKind.Boost ? $"Boost_{++boostN}_z{z:0}" : $"Slowdown_{++slowN}_z{z:0}";
            UnityEngine.Object.DestroyImmediate(zoneGo.GetComponent<Collider>());
            zoneGo.transform.SetParent(zonesRoot, false);
            float pitch = profile.PitchDegreesAt(z);
            zoneGo.transform.rotation = Quaternion.Euler(-pitch, 0f, 0f);
            zoneGo.transform.position = new Vector3(0f, profile.HeightAt(z) + 1.25f, z);
            zoneGo.transform.localScale = new Vector3(RoadWidth + 0.2f, 2.5f, ZoneLength);
            var rend = zoneGo.GetComponent<Renderer>();
            rend.sharedMaterial = kind == SpeedZoneKind.Boost ? boostMat : slowMat;
            rend.shadowCastingMode = ShadowCastingMode.Off;
            var zone = zoneGo.AddComponent<SpeedZone>();
            zone.kind = kind;
            zone.multiplier = SpeedZone.DefaultMultiplier(kind);
            zone.durationSeconds = 3f;
            zone.length = ZoneLength;
            zones.Add(zone);
        }
        mover.zones = zones;

        // Grade -> KICKR resistance over UDP 5007 to kickr_bridge.py
        var resGo = new GameObject("TrainerResistance");
        var sender = resGo.AddComponent<KickrResistanceSender>();
        sender.mover = mover;
        sender.track = kickrTrack;
        sender.host = "127.0.0.1";
        sender.port = 5007;

        // Camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.70f, 0.90f);
        cam.farClipPlane = 600f;
        camGo.AddComponent<AudioListener>();
        var follow = camGo.AddComponent<FollowCamera>();
        follow.target = cube.transform;
        camGo.transform.position = cube.transform.position + follow.offset;
        camGo.transform.LookAt(cube.transform.position + Vector3.up * 0.5f);

        EditorSceneManager.MarkSceneDirty(scene);
        if (!EditorSceneManager.SaveScene(scene, ScenePath))
            throw new Exception("SaveScene failed for " + ScenePath);

        // Build Settings: KickrCubeTest first, keep any other existing scenes after it.
        var list = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(ScenePath, true) };
        foreach (var s in EditorBuildSettings.scenes)
            if (s.path != ScenePath) list.Add(s);
        EditorBuildSettings.scenes = list.ToArray();

        AssetDatabase.SaveAssets();
        Debug.Log($"[KickrSceneBuilder] Saved {ScenePath}: {length:0} m course, {profile.sections.Count} sections, " +
                  $"{zones.Count} zones, {markers.Count} markers; build scenes: {list.Count}");
    }

    // One slab per section, tilted to the section's pitch, top surface exactly on the profile.
    static void BuildRoad(Transform parent, TrackProfile profile, Material flat, Material up, Material down)
    {
        var road = new GameObject("Road").transform;
        road.SetParent(parent, false);
        for (int i = 0; i < profile.sections.Count; i++)
        {
            var s = profile.sections[i];
            float z0 = profile.SectionStart(i), h0 = profile.SectionStartHeight(i);
            float rise = s.length * s.gradePercent / 100f;
            float theta = Mathf.Atan2(rise, s.length);
            float slant = Mathf.Sqrt(s.length * s.length + rise * rise);
            var slab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            slab.name = $"Road_{i:00}_{s.name.Replace(' ', '_')}";
            slab.transform.SetParent(road, false);
            slab.transform.rotation = Quaternion.Euler(-theta * Mathf.Rad2Deg, 0f, 0f);
            slab.transform.position = new Vector3(0f,
                h0 + rise * 0.5f - RoadThickness * 0.5f * Mathf.Cos(theta),
                z0 + s.length * 0.5f + RoadThickness * 0.5f * Mathf.Sin(theta));
            // Slightly longer than the slant so the joints between pitches don't show gaps.
            slab.transform.localScale = new Vector3(RoadWidth, RoadThickness, slant + 0.3f);
            slab.GetComponent<Renderer>().sharedMaterial = s.gradePercent > 0f ? up : s.gradePercent < 0f ? down : flat;
        }
    }

    // Stepped "terrain" under the road from the ground up, so hills read as solid.
    static void BuildEmbankment(Transform parent, TrackProfile profile, Material mat)
    {
        var root = new GameObject("Embankment").transform;
        root.SetParent(parent, false);
        float length = profile.Length;
        for (float z = 0f; z < length - 0.01f; z += EmbankmentSlice)
        {
            float zm = z + EmbankmentSlice * 0.5f;
            float top = profile.HeightAt(zm) - 0.25f;
            float h = top - GroundY;
            var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = $"Embank_{z:000.0}";
            UnityEngine.Object.DestroyImmediate(block.GetComponent<Collider>());
            block.transform.SetParent(root, false);
            block.transform.position = new Vector3(0f, GroundY + h * 0.5f, zm);
            block.transform.localScale = new Vector3(EmbankmentWidth, h, EmbankmentSlice);
            block.GetComponent<Renderer>().sharedMaterial = mat;
        }
    }

    static void AddLine(Transform parent, string name, float z, TrackProfile profile, Material mat)
    {
        var line = GameObject.CreatePrimitive(PrimitiveType.Cube);
        line.name = name;
        UnityEngine.Object.DestroyImmediate(line.GetComponent<Collider>());
        line.transform.SetParent(parent, false);
        line.transform.position = new Vector3(0f, profile.HeightAt(z) + 0.01f, z);
        line.transform.localScale = new Vector3(RoadWidth, 0.02f, 0.5f);
        line.GetComponent<Renderer>().sharedMaterial = mat;
    }

    static Material GetOrCreateMaterial(string name, Color color, bool transparent = false)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            mat = new Material(shader);
            AssetDatabase.CreateAsset(mat, path);
        }
        if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
        if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
        if (transparent) MakeTransparent(mat);
        EditorUtility.SetDirty(mat);
        return mat;
    }

    // URP Lit: Surface Type = Transparent, Blend = Alpha.
    static void MakeTransparent(Material mat)
    {
        if (mat.HasProperty("_Surface")) mat.SetFloat("_Surface", 1f);
        if (mat.HasProperty("_Blend")) mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_SrcBlend")) mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        if (mat.HasProperty("_DstBlend")) mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_ZWrite")) mat.SetFloat("_ZWrite", 0f);
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.renderQueue = (int)RenderQueue.Transparent;
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
    }
}
