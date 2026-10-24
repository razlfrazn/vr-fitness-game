using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Kickr.Tests
{
    public class KickrSceneEditModeTests
    {
        const string ScenePath = "Assets/Scenes/KickrCubeTest.unity";

        [Test]
        public void SceneIsFirstInBuildSettings()
        {
            Assert.IsTrue(EditorBuildSettings.scenes.Length > 0, "no scenes in Build Settings");
            Assert.AreEqual(ScenePath, EditorBuildSettings.scenes[0].path);
            Assert.IsTrue(EditorBuildSettings.scenes[0].enabled);
        }

        [Test]
        public void SceneIsWiredUp()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Assert.IsTrue(scene.IsValid());
            var receiver = Object.FindObjectsByType<KickrUdpReceiver>().Single();
            var mover = Object.FindObjectsByType<KickrCubeMover>().Single();
            var cam = Object.FindObjectsByType<FollowCamera>().Single();
            var track = Object.FindObjectsByType<TrackMarkers>().Single();

            Assert.AreEqual(5005, receiver.port);
            Assert.AreSame(receiver, mover.receiver);
            Assert.AreSame(mover.transform, cam.target);
            Assert.AreSame(mover.transform, track.target);
            Assert.IsNotNull(track.ground);
            Assert.Greater(track.markers.Count, 0);
            Assert.IsNotNull(cam.GetComponent<Camera>());
        }

        [Test]
        public void ParsesBridgeJson()
        {
            var go = new GameObject("r");
            go.SetActive(false); // never opens a socket
            var r = go.AddComponent<KickrUdpReceiver>();
            Assert.IsTrue(r.HandleDatagram("{\"t\": 1761330000.123, \"speed_kmh\": 7.5, \"cadence_rpm\": 0.0, \"power_w\": 0, \"distance_m\": 3}"));
            Assert.AreEqual(7.5f, r.SpeedKmh, 1e-4f);
            Assert.AreEqual(0, r.PowerW);
            Assert.AreEqual(3, r.DistanceM);
            Assert.AreEqual(1761330000.123, r.Latest.t, 1e-3);
            Assert.AreEqual(1, r.PacketsReceived);
            Object.DestroyImmediate(go);
        }
    }
}
