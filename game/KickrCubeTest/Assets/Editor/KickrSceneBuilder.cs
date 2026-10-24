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
/// Builds Assets/Scenes/KickrCubeTest.unity from code so the scene is reproducible.
/// Menu: Kickr > Rebuild KickrCubeTest Scene.
/// Batch mode: Unity -batchmode -quit -projectPath . -executeMethod KickrSceneBuilder.BuildFromCommandLine
/// </summary>
public static class KickrSceneBuilder
{
    public const string ScenePath = "Assets/Scenes/KickrCubeTest.unity";
    const string MaterialFolder = "Assets/Materials";

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

        // Cube
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "KickrCube";
        cube.transform.position = new Vector3(0f, 0.5f, 0f);
        cube.GetComponent<Renderer>().sharedMaterial = cubeMat;
        var mover = cube.AddComponent<KickrCubeMover>();
        mover.receiver = receiver;
        // A dark stripe on the cube so the spin is easy to see.
        var stripe = GameObject.CreatePrimitive(PrimitiveType.Cube);
        stripe.name = "SpinStripe";
        UnityEngine.Object.DestroyImmediate(stripe.GetComponent<Collider>());
        stripe.transform.SetParent(cube.transform, false);
        stripe.transform.localScale = new Vector3(1.02f, 0.2f, 1.02f);
        stripe.GetComponent<Renderer>().sharedMaterial = stripeMat;

        // Ground (Plane is 10x10 m at scale 1) -> 40 m wide, 100 m long
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(4f, 1f, 10f);
        ground.GetComponent<Renderer>().sharedMaterial = groundMat;

        // Marker posts every 5 m on both sides of the track; TrackMarkers recycles them.
        var track = new GameObject("Track").AddComponent<TrackMarkers>();
        track.target = cube.transform;
        track.ground = ground.transform;
        track.spacing = 5f;
        track.keepBehind = 15f;
        var markers = new List<Transform>();
        const int perSide = 12;
        for (int i = 0; i < perSide; i++)
        {
            foreach (float x in new[] { -3f, 3f })
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = $"Marker_{(x < 0 ? "L" : "R")}_{i:00}";
                UnityEngine.Object.DestroyImmediate(post.GetComponent<Collider>());
                post.transform.SetParent(track.transform, false);
                post.transform.position = new Vector3(x, 0.75f, (i - 2) * track.spacing);
                post.transform.localScale = new Vector3(0.25f, 1.5f, 0.25f);
                post.GetComponent<Renderer>().sharedMaterial = markerMat;
                markers.Add(post.transform);
            }
        }
        track.markers = markers;

        // Camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.55f, 0.70f, 0.90f);
        cam.farClipPlane = 300f;
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
        Debug.Log($"[KickrSceneBuilder] Saved {ScenePath} with {markers.Count} markers; build scenes: {list.Count}");
    }

    static Material GetOrCreateMaterial(string name, Color color)
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
        EditorUtility.SetDirty(mat);
        return mat;
    }
}
