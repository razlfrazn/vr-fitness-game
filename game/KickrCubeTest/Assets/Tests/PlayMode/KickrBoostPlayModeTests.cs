using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kickr.Tests
{
    /// <summary>
    /// Boost end to end with a fake Zwift Ride bridge: UDP JSON to a test port (not 5006) like
    /// zwift_play_listen.py sends, and a mock resistance sender, so neither the controllers nor the trainer are touched.
    /// </summary>
    public class KickrBoostPlayModeTests
    {
        const int TestButtonPort = 5094;

        class RecordingSender : IResistanceCommandSender
        {
            public readonly List<int> Sent = new List<int>();
            public void SendResistance(int level) => Sent.Add(level);
        }

        readonly List<GameObject> _objects = new List<GameObject>();
        KickrCubeMover _mover;
        BoostController _boost;
        ZwiftRideUdpReceiver _buttons;
        KickrResistanceSender _sender;
        RecordingSender _bridge;
        KickrTrack _track;

        GameObject Make(string name, bool active = false)
        {
            var go = new GameObject(name);
            go.SetActive(active);
            _objects.Add(go);
            return go;
        }

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            var trackGo = Make("TestTrack", true);
            _track = trackGo.AddComponent<KickrTrack>();
            _track.profile = new TrackProfile();
            _track.profile.sections.Add(new TrackSection("flat", 200f, 0f));

            var cubeGo = Make("TestCube", true);
            _mover = cubeGo.AddComponent<KickrCubeMover>();
            _mover.enabled = false;      // the test moves the cube by hand
            _mover.track = _track;
            _mover.showReadout = false;
            _mover.showDistanceReadout = false;
            cubeGo.transform.position = new Vector3(0f, 0.5f, 0f);

            var buttonsGo = Make("TestButtons");
            _buttons = buttonsGo.AddComponent<ZwiftRideUdpReceiver>();
            _buttons.port = TestButtonPort;

            var boostGo = Make("TestBoost");
            _boost = boostGo.AddComponent<BoostController>();
            _boost.mover = _mover;
            _boost.track = _track;
            _boost.buttons = _buttons;
            _boost.keyboardFallback = false;
            _boost.showReadout = false;
            _boost.settings.gemsPerLap = 4;
            _boost.settings.boostDuration = 1.5f;

            var senderGo = Make("TestResistance");
            _sender = senderGo.AddComponent<KickrResistanceSender>();
            _sender.mover = _mover;
            _sender.track = _track;
            _sender.port = 5095;         // never used: the mock replaces UDP
            _sender.showReadout = false;
            _sender.boost = _boost;
            _bridge = new RecordingSender();
            _sender.CommandSender = _bridge;

            buttonsGo.SetActive(true);
            boostGo.SetActive(true);
            senderGo.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            foreach (var go in _objects) if (go != null) Object.Destroy(go);
            _objects.Clear();
            yield return null;
        }

        static void Send(string json)
        {
            using (var udp = new UdpClient())
            {
                byte[] b = Encoding.UTF8.GetBytes(json);
                udp.Send(b, b.Length, "127.0.0.1", TestButtonPort);
            }
        }

        static string A(string unit, bool pressed) =>
            "{\"kind\":\"" + (pressed ? "press" : "release") + "\",\"btn\":\"A\",\"button\":\"A\",\"side\":\"right\",\"unit\":\"" + unit +
            "\",\"device\":\"Ride\",\"pressed\":" + (pressed ? "true" : "false") + "}";

        IEnumerator WaitFor(System.Func<bool> cond, float timeout = 2f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!cond() && Time.realtimeSinceStartup < end) yield return null;
        }

        [UnityTest]
        public IEnumerator FakeBridgeAPressStartsOneBoostDropsResistanceAndRestoresIt()
        {
            Assert.IsTrue(_buttons.IsListening, _buttons.LastError);
            Assert.AreEqual(2, _boost.Inventory.Count);
            yield return new WaitForSecondsRealtime(0.7f);            // flat: 30
            CollectionAssert.AreEqual(new[] { 30 }, _bridge.Sent);

            Send(A("LEFT", true));                                     // the left unit relays the right A ...
            Send(A("RIGHT", true));                                    // ... and the right unit sends it too
            yield return WaitFor(() => _boost.Activations > 0);
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.AreEqual(1, _boost.Activations, "one press from both units = one boost");
            Assert.AreEqual(1, _boost.Inventory.Count);
            Assert.IsTrue(_mover.Effects.IsActive(BoostController.EffectKey, Time.time));
            Assert.AreEqual(1.3f, _mover.EffectMultiplier, 1e-4f, "cube speed x1.3");

            yield return new WaitForSecondsRealtime(0.6f);
            CollectionAssert.AreEqual(new[] { 30, 15 }, _bridge.Sent, "resistance - 15");

            yield return WaitFor(() => !_boost.IsActive, 3f);           // still holding A
            yield return null;
            yield return null;
            CollectionAssert.AreEqual(new[] { 30, 15, 30 }, _bridge.Sent, "restored when the boost ends");
            Assert.AreEqual(1, _boost.Activations, "held press used once");

            Send(A("LEFT", false));
            Send(A("RIGHT", false));
            yield return new WaitForSecondsRealtime(0.2f);
            Send(A("LEFT", true));
            yield return WaitFor(() => _boost.Activations > 1);
            Assert.AreEqual(2, _boost.Activations);
            Assert.AreEqual(0, _boost.Inventory.Count);
            Send(A("LEFT", false));
            yield return WaitFor(() => !_boost.IsActive, 3f);
            yield return new WaitForSecondsRealtime(0.1f);

            Send(A("LEFT", true));                                     // count is 0: nothing happens
            yield return new WaitForSecondsRealtime(0.3f);
            Assert.AreEqual(2, _boost.Activations);
            Assert.IsFalse(_boost.IsActive);
            Assert.AreEqual("", _sender.LastError);
        }

        [UnityTest]
        public IEnumerator GemsFloatAboveTheRoadAndAreCollectedOnABigStep()
        {
            Assert.AreEqual(4, _boost.GemObjects.Count);
            CollectionAssert.AreEqual(new[] { 25f, 75f, 125f, 175f }, _boost.GemPositions);
            foreach (var g in _boost.GemObjects)
            {
                Assert.IsTrue(g.activeSelf);
                Assert.That(g.transform.position.y, Is.InRange(0.9f - 0.13f, 0.9f + 0.13f), "just above the road");
            }

            _mover.transform.position = new Vector3(0f, 0.5f, 190f);   // one huge step past all four
            yield return null;
            yield return null;
            Assert.AreEqual(4, _boost.Gems.CollectedThisLap);
            Assert.AreEqual(6, _boost.Inventory.Count, "2 + 4 gems");
            yield return new WaitForSeconds(_boost.flashSeconds + 0.1f);
            foreach (var g in _boost.GemObjects) Assert.IsFalse(g.activeSelf, "hidden after the flash");
        }
    }
}
