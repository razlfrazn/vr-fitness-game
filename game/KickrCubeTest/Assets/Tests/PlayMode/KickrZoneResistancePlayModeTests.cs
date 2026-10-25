using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kickr.Tests
{
    /// <summary>
    /// Drives the real KickrResistanceSender component through red and green zones with a mock
    /// bridge sender. Nothing is sent over UDP, so the real trainer is never touched.
    /// </summary>
    public class KickrZoneResistancePlayModeTests
    {
        class RecordingSender : IResistanceCommandSender
        {
            public readonly List<int> Sent = new List<int>();
            public void SendResistance(int level) => Sent.Add(level);
        }

        readonly List<GameObject> _objects = new List<GameObject>();
        KickrResistanceSender _sender;
        Transform _cube;
        RecordingSender _bridge;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // 20 m flat, then a 100 m climb at +10 %.
            var trackGo = Track();
            var track = trackGo.AddComponent<KickrTrack>();
            track.profile = new TrackProfile();
            track.profile.sections.Add(new TrackSection("flat", 20f, 0f));
            track.profile.sections.Add(new TrackSection("climb", 100f, 10f));

            var red = Zone(SpeedZoneKind.Slowdown, 10f);    // 8-12 m, on the flat
            var green = Zone(SpeedZoneKind.Boost, 40f);     // 38-42 m, on the climb

            var cubeGo = Track("TestCube");
            var mover = cubeGo.AddComponent<KickrCubeMover>();
            mover.enabled = false;      // the test moves the cube by hand
            mover.showReadout = false;
            _cube = cubeGo.transform;

            var senderGo = Track("TestResistance");
            senderGo.SetActive(false);
            _sender = senderGo.AddComponent<KickrResistanceSender>();
            _sender.mover = mover;
            _sender.track = track;
            _sender.port = 5096;        // never used: the mock replaces UDP
            _sender.showReadout = false;
            _sender.zones.Add(red);
            _sender.zones.Add(green);
            _bridge = new RecordingSender();
            _sender.CommandSender = _bridge;
            senderGo.SetActive(true);
            yield return null;
        }

        GameObject Track(string name = "TestTrack")
        {
            var go = new GameObject(name);
            _objects.Add(go);
            return go;
        }

        SpeedZone Zone(SpeedZoneKind kind, float centreZ)
        {
            var go = Track("TestZone_" + kind);
            go.transform.position = new Vector3(0f, 0f, centreZ);
            var z = go.AddComponent<SpeedZone>();
            z.kind = kind;
            z.multiplier = SpeedZone.DefaultMultiplier(kind);
            z.length = 4f;
            return z;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.Destroy(go);
            _objects.Clear();
            yield return null;
        }

        IEnumerator RideTo(float z, float holdSeconds = 0.7f)
        {
            _cube.position = new Vector3(0f, 0.5f, z);
            yield return new WaitForSecondsRealtime(holdSeconds);
        }

        [UnityTest]
        public IEnumerator RedAndGreenZonesChangeResistanceThroughTheMockBridge()
        {
            Assert.AreSame(_bridge, _sender.CommandSender, "mock bridge in place, no UDP");

            yield return RideTo(0f);    // flat: 30
            yield return RideTo(10f);   // red: 30 + 20
            Assert.IsTrue(_sender.InSlowdownZone);
            yield return RideTo(15f);   // back to flat
            yield return RideTo(30f);   // climb +10 %: 60
            yield return RideTo(40f);   // green on the climb: 60 - 20
            Assert.IsTrue(_sender.InBoostZone);
            yield return RideTo(45f, 0.1f);   // leave: restore right away
            CollectionAssert.AreEqual(new[] { 30, 50, 30, 60, 40, 60 }, _bridge.Sent);

            _sender.enabled = false;    // like leaving Play mode
            Assert.AreEqual(30, _bridge.Sent[_bridge.Sent.Count - 1], "baseline sent on disable");
            Assert.AreEqual(7, _sender.CommandsSent);
            Assert.AreEqual("", _sender.LastError);
        }
    }
}
