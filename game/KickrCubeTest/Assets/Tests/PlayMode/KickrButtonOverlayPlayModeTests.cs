using System.Collections;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Kickr.Tests
{
    /// <summary>Button map overlay fed by real UDP datagrams on a test port (fake bridge, no controller).</summary>
    public class KickrButtonOverlayPlayModeTests
    {
        const int TestButtonPort = 5093;
        GameObject _go;
        ZwiftRideUdpReceiver _rx;
        ZwiftRideButtonOverlay _overlay;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _go = new GameObject("TestRideButtonsOverlay");
            _go.SetActive(false);
            _rx = _go.AddComponent<ZwiftRideUdpReceiver>();
            _rx.port = TestButtonPort;
            _overlay = _go.AddComponent<ZwiftRideButtonOverlay>();
            _overlay.buttons = _rx;
            _go.SetActive(true);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_go != null) Object.Destroy(_go);
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

        static string Ev(string btn, string unit, bool pressed, string code = "") =>
            "{\"kind\":\"" + (pressed ? "press" : "release") + "\",\"btn\":\"" + btn + "\",\"button\":\"" + btn +
            "\",\"side\":\"" + ZwiftButtonMessage.PhysicalSide(btn) + "\",\"unit\":\"" + unit + "\",\"pressed\":" +
            (pressed ? "true" : "false") + ",\"code\":\"" + code + "\",\"raw\":\"23 08\",\"device\":\"Ride\"}";

        IEnumerator WaitFor(System.Func<bool> cond, float timeout = 2f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (!cond() && Time.realtimeSinceStartup < end) yield return null;
        }

        List<string> Lit() => ZwiftRideOverlayModel.LitNames(_rx.State);

        [UnityTest]
        public IEnumerator UdpPressesLightTheOverlayAndReleasesClearIt()
        {
            Assert.IsTrue(_rx.IsListening, _rx.LastError);
            Send(Ev("A", "LEFT", true, "0x00010"));
            Send(Ev("A", "RIGHT", true, "0x00010"));
            Send(Ev("DPAD_UP", "LEFT", true, "0x00002"));
            Send(Ev("UNKNOWN_0x10000", "LEFT", true, "0x10000"));
            yield return WaitFor(() => _rx.ButtonEvents >= 4);
            yield return null;   // let OnGUI draw the lit state
            CollectionAssert.AreEquivalent(new[] { "A", "DPAD_UP" }, Lit());
            CollectionAssert.AreEqual(new[] { "0x10000" }, ZwiftRideOverlayModel.UnknownHeld(_rx.State));
            StringAssert.StartsWith("0x10000", _rx.LastUnknown);

            Send(Ev("A", "LEFT", false));
            Send(Ev("A", "RIGHT", false));
            Send(Ev("DPAD_UP", "LEFT", false));
            Send(Ev("UNKNOWN_0x10000", "LEFT", false));
            yield return WaitFor(() => _rx.ButtonEvents >= 8);
            yield return null;
            CollectionAssert.IsEmpty(Lit());
            CollectionAssert.IsEmpty(ZwiftRideOverlayModel.UnknownHeld(_rx.State));
            LogAssert.NoUnexpectedReceived();
        }
    }
}
