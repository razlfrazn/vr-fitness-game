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
    public class KickrReceiverPlayModeTests
    {
        // Not 5005, so the test never fights a running bridge/editor for the real port.
        const int TestPort = 5098;

        GameObject _receiverGo;
        GameObject _cubeGo;
        KickrUdpReceiver _receiver;
        KickrCubeMover _mover;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _receiverGo = new GameObject("TestReceiver");
            _receiverGo.SetActive(false);            // set the port before OnEnable runs
            _receiver = _receiverGo.AddComponent<KickrUdpReceiver>();
            _receiver.port = TestPort;
            _receiverGo.SetActive(true);

            _cubeGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _cubeGo.SetActive(false);
            _mover = _cubeGo.AddComponent<KickrCubeMover>();
            _mover.receiver = _receiver;
            _mover.showReadout = false;
            _mover.smoothing = 0f;                   // follow speed instantly for the test
            _cubeGo.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_cubeGo != null) Object.Destroy(_cubeGo);
            if (_receiverGo != null) Object.Destroy(_receiverGo);
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

        IEnumerator WaitForPackets(int count, float timeout = 5f)
        {
            var sw = Stopwatch.StartNew();
            while (_receiver.PacketsReceived < count && sw.Elapsed.TotalSeconds < timeout)
                yield return null;
        }

        [UnityTest]
        public IEnumerator ReceivesParsesAndMovesCube()
        {
            Assert.IsTrue(_receiver.IsListening, "receiver should be listening: " + _receiver.LastError);
            Assert.AreEqual(KickrConnectionState.WaitingForData, _receiver.State);

            float startZ = _cubeGo.transform.position.z;
            Quaternion startRot = _cubeGo.transform.rotation;

            Send("{\"t\": 1761330000.1, \"speed_kmh\": 18.0, \"cadence_rpm\": 80.0, \"power_w\": 150, \"distance_m\": 10}");
            Send("{\"t\": 1761330000.4, \"speed_kmh\": 20.5, \"cadence_rpm\": 85.5, \"power_w\": 175, \"distance_m\": 12}");
            Send("{\"t\": 1761330000.7, \"speed_kmh\": 25.2, \"cadence_rpm\": 90.0, \"power_w\": 210, \"distance_m\": 15, \"elapsed_s\": 42}");
            yield return WaitForPackets(3);

            Assert.AreEqual(3, _receiver.PacketsReceived, "packets received");
            Assert.AreEqual(25.2f, _receiver.SpeedKmh, 0.001f, "speed_kmh");
            Assert.AreEqual(90.0f, _receiver.CadenceRpm, 0.001f, "cadence_rpm");
            Assert.AreEqual(210, _receiver.PowerW, "power_w");
            Assert.AreEqual(15, _receiver.DistanceM, "distance_m");
            Assert.AreEqual(42, _receiver.Latest.elapsed_s, "elapsed_s");
            Assert.Less(_receiver.SecondsSinceLastPacket, 2f, "time since last packet");
            Assert.AreEqual(KickrConnectionState.Live, _receiver.State);

            // Keep data fresh while the cube moves for ~0.5 s.
            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < 0.6)
            {
                Send("{\"t\": 1761330001.0, \"speed_kmh\": 25.2, \"cadence_rpm\": 90.0, \"power_w\": 210}");
                yield return new WaitForSeconds(0.1f);
            }

            float moved = _cubeGo.transform.position.z - startZ;
            UnityEngine.Debug.Log($"[KickrTest] cube moved {moved:0.000} m along +Z, speed {_mover.CurrentSpeedMps:0.00} m/s, packets {_receiver.PacketsReceived}");
            Assert.Greater(moved, 0.5f, "cube should have moved forward along +Z");
            Assert.AreEqual(25.2f / 3.6f, _mover.CurrentSpeedMps, 0.05f, "km/h -> m/s");
            Assert.AreNotEqual(startRot, _cubeGo.transform.rotation, "cube should spin");
            Assert.AreEqual(0f, _cubeGo.transform.position.x, 1e-4f, "no sideways drift");
        }

        [UnityTest]
        public IEnumerator PartialPacketKeepsPreviousSpeed()
        {
            // The bridge omits keys that weren't in an FTMS notification; that must not zero them.
            Send("{\"t\": 1.0, \"speed_kmh\": 12.3, \"cadence_rpm\": 0.0, \"power_w\": 0}");
            Send("{\"t\": 1.1, \"distance_m\": 99}");
            yield return WaitForPackets(2);
            Assert.AreEqual(2, _receiver.PacketsReceived);
            Assert.AreEqual(12.3f, _receiver.SpeedKmh, 0.001f);
            Assert.AreEqual(99, _receiver.DistanceM);
        }

        [UnityTest]
        public IEnumerator BadJsonIsCountedNotCrashing()
        {
            Send("this is not json");
            Send("{\"speed_kmh\": 5.0}");
            var sw = Stopwatch.StartNew();
            while ((_receiver.PacketsReceived < 1 || _receiver.ParseErrors < 1) && sw.Elapsed.TotalSeconds < 5)
                yield return null;
            Assert.AreEqual(1, _receiver.ParseErrors);
            Assert.AreEqual(1, _receiver.PacketsReceived);
            Assert.AreEqual(5.0f, _receiver.SpeedKmh, 0.001f);
        }

        [UnityTest]
        public IEnumerator NoDataMeansCubeStaysPut()
        {
            float startZ = _cubeGo.transform.position.z;
            yield return new WaitForSeconds(0.3f);
            Assert.AreEqual(startZ, _cubeGo.transform.position.z, 1e-5f);
        }

        [UnityTest]
        public IEnumerator DisablingReleasesThePort()
        {
            Assert.IsTrue(_receiver.IsListening);
            _receiverGo.SetActive(false);   // OnDisable -> StopListening
            yield return null;
            Assert.IsFalse(_receiver.IsListening);

            // If the port were still bound this would throw "address already in use".
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Any, TestPort))) { }

            _receiverGo.SetActive(true);    // and it can listen again afterwards
            yield return null;
            Assert.IsTrue(_receiver.IsListening, _receiver.LastError);
        }
    }
}
