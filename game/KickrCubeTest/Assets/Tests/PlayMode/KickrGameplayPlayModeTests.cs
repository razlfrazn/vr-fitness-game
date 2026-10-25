using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kickr.Tests
{
    public class KickrGameplayPlayModeTests
    {
        // Separate from 5005 (bridge) and 5098 (receiver tests).
        const int TestPort = 5097;

        GameObject _receiverGo, _trackGo, _cubeGo, _zoneGo;
        KickrUdpReceiver _receiver;
        KickrTrack _track;
        KickrCubeMover _mover;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _receiverGo = new GameObject("TestReceiver");
            _receiverGo.SetActive(false);
            _receiver = _receiverGo.AddComponent<KickrUdpReceiver>();
            _receiver.port = TestPort;
            _receiverGo.SetActive(true);

            // A short course: 5 m flat, then a 20% ramp, so movement shows up quickly.
            _trackGo = new GameObject("TestTrack");
            _track = _trackGo.AddComponent<KickrTrack>();
            _track.profile = new TrackProfile();
            _track.profile.sections.Add(new TrackSection("flat", 5f, 0f));
            _track.profile.sections.Add(new TrackSection("ramp", 200f, 20f));

            _zoneGo = new GameObject("TestBoost");
            _zoneGo.transform.position = new Vector3(0f, 0f, 3f);   // starts at z = 1
            var zone = _zoneGo.AddComponent<SpeedZone>();
            zone.kind = SpeedZoneKind.Boost;
            zone.multiplier = 1.5f;
            zone.durationSeconds = 30f;
            zone.length = 4f;

            _cubeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeGo.SetActive(false);
            _mover = _cubeGo.AddComponent<KickrCubeMover>();
            _mover.receiver = _receiver;
            _mover.track = _track;
            _mover.zones.Add(zone);
            _mover.showReadout = false;
            _mover.smoothing = 0f;
            _cubeGo.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in new[] { _cubeGo, _zoneGo, _trackGo, _receiverGo })
                if (go != null) Object.Destroy(go);
            yield return null;
        }

        static void Send(string json)
        {
            using (var udp = new UdpClient())
            {
                byte[] bytes = Encoding.UTF8.GetBytes(json);
                udp.Send(bytes, bytes.Length, new IPEndPoint(IPAddress.Loopback, TestPort));
            }
        }

        [UnityTest]
        public IEnumerator CubeFollowsTerrainAndGetsBoosted()
        {
            Assert.AreEqual(0.5f, _cubeGo.transform.position.y, 1e-4f, "snapped onto the track at start");

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 1.5 && _cubeGo.transform.position.z < 8f)
            {
                Send("{\"t\": 1.0, \"speed_kmh\": 36.0, \"cadence_rpm\": 90.0, \"power_w\": 200}");
                yield return new WaitForSeconds(0.05f);
            }

            var p = _cubeGo.transform.position;
            UnityEngine.Debug.Log($"[KickrTest] terrain: z {p.z:0.00} y {p.y:0.000} speed {_mover.CurrentSpeedMps:0.00} m/s mult {_mover.EffectMultiplier:0.00}");
            Assert.Greater(p.z, 5.5f, "should be on the ramp");
            Assert.AreEqual(_track.HeightAtWorldZ(p.z) + 0.5f, p.y, 0.01f, "rides the height profile");
            Assert.Greater(p.y, 0.5f, "climbed");
            Assert.AreEqual(20f, _mover.CurrentGradePercent, 1e-4f);
            Assert.AreEqual(1.5f, _mover.EffectMultiplier, 1e-4f, "boost zone triggered");
            Assert.AreEqual(36f / 3.6f * 1.5f, _mover.CurrentSpeedMps, 0.05f, "effective speed = speed x multiplier");
            Assert.AreEqual(0f, p.x, 1e-4f, "no sideways drift");
        }
    }
}
