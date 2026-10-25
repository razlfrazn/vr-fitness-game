using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Kickr.Tests
{
    /// <summary>Zwift Ride button map overlay: mapping, press/release, simultaneous presses, unknown codes, parsing. No hardware.</summary>
    public class ButtonMapTests
    {
        [Test]
        public void MapHasEveryRideInputNinePerSide()
        {
            Assert.AreEqual(18, ZwiftRideButtonMap.All.Count);
            CollectionAssert.AreEqual(
                new[] { "DPAD_UP", "DPAD_DOWN", "DPAD_LEFT", "DPAD_RIGHT", "LEFT_SHIFT_UP", "LEFT_SHIFT_DOWN", "LEFT_POWERUP", "LEFT_ON_OFF", "LEFT_PADDLE" },
                ZwiftRideButtonMap.ForSide("left").Select(b => b.Name).ToArray());
            CollectionAssert.AreEqual(
                new[] { "A", "B", "Y", "Z", "RIGHT_SHIFT_UP", "RIGHT_SHIFT_DOWN", "RIGHT_POWERUP", "RIGHT_ON_OFF", "RIGHT_PADDLE" },
                ZwiftRideButtonMap.ForSide("right").Select(b => b.Name).ToArray());
        }

        [Test]
        public void MasksMatchTheProtocolAndCoverAllSixteenBits()
        {
            var expected = new Dictionary<string, uint>
            {
                { "DPAD_LEFT", 0x1 }, { "DPAD_UP", 0x2 }, { "DPAD_RIGHT", 0x4 }, { "DPAD_DOWN", 0x8 },
                { "A", 0x10 }, { "B", 0x20 }, { "Y", 0x40 }, { "Z", 0x80 },
                { "LEFT_SHIFT_UP", 0x100 }, { "LEFT_SHIFT_DOWN", 0x200 }, { "LEFT_POWERUP", 0x400 }, { "LEFT_ON_OFF", 0x800 },
                { "RIGHT_SHIFT_UP", 0x1000 }, { "RIGHT_SHIFT_DOWN", 0x2000 }, { "RIGHT_POWERUP", 0x4000 }, { "RIGHT_ON_OFF", 0x8000 },
            };
            uint all = 0;
            foreach (var kv in expected)
            {
                var b = ZwiftRideButtonMap.Find(kv.Key);
                Assert.IsNotNull(b, kv.Key);
                Assert.AreEqual(kv.Value, b.Mask, kv.Key);
                Assert.AreEqual(-1, b.AnalogLocation, kv.Key);
                Assert.AreEqual(0u, all & b.Mask, "duplicate mask " + kv.Key);
                all |= b.Mask;
            }
            Assert.AreEqual(0xFFFFu, all);
            Assert.AreEqual(0, ZwiftRideButtonMap.Find("LEFT_PADDLE").AnalogLocation);
            Assert.AreEqual(1, ZwiftRideButtonMap.Find("RIGHT_PADDLE").AnalogLocation);
            Assert.AreEqual("0x00010", ZwiftRideButtonMap.Find("A").Code);
            Assert.AreEqual("0x08000", ZwiftRideButtonMap.Find("RIGHT_ON_OFF").Code);
            Assert.AreEqual("analog 1", ZwiftRideButtonMap.Find("RIGHT_PADDLE").Code);
        }

        [Test]
        public void EverySideAgreesWithPhysicalSideAndLookupIgnoresCase()
        {
            foreach (var b in ZwiftRideButtonMap.All)
                Assert.AreEqual(b.Side, ZwiftButtonMessage.PhysicalSide(b.Name), b.Name);
            Assert.AreSame(ZwiftRideButtonMap.Find("A"), ZwiftRideButtonMap.Find("a"));
            Assert.IsTrue(ZwiftRideButtonMap.IsKnown("dpad_up"));
            Assert.IsFalse(ZwiftRideButtonMap.IsKnown("UNKNOWN_0x10000"));
            Assert.IsFalse(ZwiftRideButtonMap.IsKnown(""));
            Assert.IsNull(ZwiftRideButtonMap.Find(null));
        }

        [Test]
        public void BitmapDecodingMatchesTheListener()
        {
            CollectionAssert.AreEquivalent(new[] { "A" }, ZwiftRideButtonMap.PressedInBitmap(0xFFFFFFEF));   // events.log sample
            CollectionAssert.IsEmpty(ZwiftRideButtonMap.PressedInBitmap(0xFFFFFFFF));
            CollectionAssert.AreEquivalent(new[] { "A", "DPAD_UP", "RIGHT_SHIFT_UP" },
                ZwiftRideButtonMap.PressedInBitmap(0xFFFFFFFF & ~0x10u & ~0x2u & ~0x1000u));
            CollectionAssert.AreEquivalent(new[] { "UNKNOWN_0x10000" }, ZwiftRideButtonMap.PressedInBitmap(0xFFFEFFFF));
            CollectionAssert.IsEmpty(ZwiftRideButtonMap.PressedInBitmap(0xFFFF), "bits above the highest sent bit aren't presses");
            Assert.AreEqual("UNKNOWN_0x80000", ZwiftRideButtonMap.UnknownName(0x80000));
        }
    }

    public class ButtonOverlayStateTests
    {
        GameObject _go;
        ZwiftRideUdpReceiver _rx;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("TestRideButtons");
            _rx = _go.AddComponent<ZwiftRideUdpReceiver>();   // edit mode: OnEnable doesn't run, no socket
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        static string Ev(string btn, string unit, bool pressed, string code = null, string raw = null) =>
            "{\"kind\":\"" + (pressed ? "press" : "release") + "\",\"btn\":\"" + btn + "\",\"button\":\"" + btn +
            "\",\"side\":\"" + ZwiftButtonMessage.PhysicalSide(btn) + "\",\"unit\":\"" + unit + "\",\"pressed\":" + (pressed ? "true" : "false") +
            (code != null ? ",\"code\":\"" + code + "\"" : "") + (raw != null ? ",\"raw\":\"" + raw + "\"" : "") +
            ",\"address\":\"CC:A3:D1:BA:46:D6\",\"device\":\"Ride\"}";

        List<string> Lit() => ZwiftRideOverlayModel.LitNames(_rx.State);

        [Test]
        public void EveryButtonLightsWhilePressedAndGoesOutOnRelease()
        {
            foreach (var b in ZwiftRideButtonMap.All)
            {
                Assert.IsTrue(_rx.HandleDatagram(Ev(b.Name, "LEFT", true, b.Code)));
                CollectionAssert.AreEqual(new[] { b.Name }, Lit(), b.Name);
                var cell = ZwiftRideOverlayModel.Cells(_rx.State, b.Side).Single(c => c.Button == b);
                Assert.IsTrue(cell.Lit, b.Name);
                Assert.IsTrue(_rx.HandleDatagram(Ev(b.Name, "LEFT", false, b.Code)));
                CollectionAssert.IsEmpty(Lit(), b.Name + " released");
            }
            Assert.AreEqual("", _rx.LastUnknown, "known buttons are never 'unknown'");
        }

        [Test]
        public void SimultaneousPressesLightTogetherAndReleaseIndependently()
        {
            _rx.HandleDatagram(Ev("A", "LEFT", true));
            _rx.HandleDatagram(Ev("DPAD_UP", "LEFT", true));
            _rx.HandleDatagram(Ev("LEFT_SHIFT_DOWN", "LEFT", true));
            _rx.HandleDatagram(Ev("RIGHT_PADDLE", "LEFT", true));
            CollectionAssert.AreEquivalent(new[] { "A", "DPAD_UP", "LEFT_SHIFT_DOWN", "RIGHT_PADDLE" }, Lit());
            Assert.AreEqual(2, ZwiftRideOverlayModel.Cells(_rx.State, "left").Count(c => c.Lit));
            Assert.AreEqual(2, ZwiftRideOverlayModel.Cells(_rx.State, "right").Count(c => c.Lit));

            _rx.HandleDatagram(Ev("DPAD_UP", "LEFT", false));
            CollectionAssert.AreEquivalent(new[] { "A", "LEFT_SHIFT_DOWN", "RIGHT_PADDLE" }, Lit());
        }

        [Test]
        public void RelayedPressFromBothUnitsStaysLitUntilBothRelease()
        {
            _rx.HandleDatagram(Ev("A", "LEFT", true));     // left unit relays the right unit's A
            _rx.HandleDatagram(Ev("A", "RIGHT", true));    // without --left-only the right unit sends it too
            _rx.HandleDatagram(Ev("A", "RIGHT", false));
            CollectionAssert.AreEqual(new[] { "A" }, Lit());
            _rx.HandleDatagram(Ev("A", "LEFT", false));
            CollectionAssert.IsEmpty(Lit());
        }

        [Test]
        public void DisconnectClearsThatUnitsLitButtons()
        {
            _rx.HandleDatagram(Ev("B", "LEFT", true));
            _rx.HandleDatagram("{\"kind\":\"disconnected\",\"side\":\"LEFT\",\"unit\":\"LEFT\"}");
            CollectionAssert.IsEmpty(Lit());
        }

        [Test]
        public void UnknownButtonCodeIsShownRaw()
        {
            Assert.IsTrue(_rx.HandleDatagram(Ev("UNKNOWN_0x10000", "LEFT", true, "0x10000", "23 08 ff ff fb ff 0f")));
            CollectionAssert.IsEmpty(Lit(), "unknown codes don't light a known button");
            CollectionAssert.AreEqual(new[] { "0x10000" }, ZwiftRideOverlayModel.UnknownHeld(_rx.State));
            StringAssert.Contains("0x10000", _rx.LastUnknown);
            StringAssert.Contains("raw 23 08 ff ff fb ff 0f", _rx.LastUnknown);
            Assert.AreEqual(1, _rx.UnknownEvents);

            _rx.HandleDatagram(Ev("UNKNOWN_0x10000", "LEFT", false, "0x10000"));
            CollectionAssert.IsEmpty(ZwiftRideOverlayModel.UnknownHeld(_rx.State));
            StringAssert.Contains("0x10000", _rx.LastUnknown, "last unknown stays visible after release");
        }

        [Test]
        public void UnknownNameWithoutCodeShowsTheNameAndOtherMessagesShowOpcodeAndBytes()
        {
            _rx.HandleDatagram(Ev("ANALOG_7", "LEFT", true));
            CollectionAssert.AreEqual(new[] { "ANALOG_7" }, ZwiftRideOverlayModel.UnknownHeld(_rx.State));

            Assert.IsFalse(_rx.HandleDatagram("{\"kind\":\"message\",\"op\":42,\"name\":\"LOG_DATA\",\"raw\":\"2a 00\",\"unit\":\"LEFT\"}"));
            Assert.AreEqual(1, _rx.UnknownEvents, "known opcodes aren't 'unknown'");
            Assert.IsFalse(_rx.HandleDatagram("{\"kind\":\"message\",\"op\":119,\"name\":\"UNKNOWN\",\"raw\":\"77 01 02\",\"unit\":\"LEFT\"}"));
            Assert.AreEqual("op 0x77  raw 77 01 02", _rx.LastUnknown);
            _rx.HandleDatagram("{\"kind\":\"decode_error\",\"raw\":\"23 ff\",\"unit\":\"LEFT\"}");
            Assert.AreEqual("undecodable  raw 23 ff", _rx.LastUnknown);
            Assert.AreEqual(3, _rx.UnknownEvents);
        }
    }

    public class ButtonMessageParsingTests
    {
        [Test]
        public void ParsesCodeAndRawFromTheListenerJson()
        {
            string json = "{\"kind\":\"press\",\"btn\":\"Y\",\"button\":\"Y\",\"side\":\"right\",\"unit\":\"LEFT\",\"pressed\":true," +
                          "\"code\":\"0x00040\",\"raw\":\"23 08 bf ff ff ff 0f\",\"address\":\"x\",\"device\":\"Ride\",\"analog\":{\"LEFT_PADDLE\":0}}";
            Assert.IsTrue(ZwiftButtonMessage.TryParse(json, out var ev));
            Assert.AreEqual("Y", ev.Button);
            Assert.AreEqual("right", ev.Side);
            Assert.AreEqual("LEFT", ev.Unit);
            Assert.IsTrue(ev.Pressed);
            Assert.AreEqual("0x00040", ev.Code);
            Assert.AreEqual("23 08 bf ff ff ff 0f", ev.Raw);
        }

        [Test]
        public void OlderJsonWithoutCodeStillParses()
        {
            Assert.IsTrue(ZwiftButtonMessage.TryParse("{\"btn\":\"DPAD_DOWN\",\"side\":\"left\",\"pressed\":false}", out var ev));
            Assert.AreEqual("DPAD_DOWN", ev.Button);
            Assert.IsFalse(ev.Pressed);
            Assert.AreEqual("", ev.Code);
            Assert.AreEqual("", ev.Raw);
        }

        [Test]
        public void NonButtonAndBrokenMessagesAreNotButtonEvents()
        {
            Assert.IsFalse(ZwiftButtonMessage.TryParse("{\"kind\":\"battery\",\"percent\":80}", out _));
            Assert.IsFalse(ZwiftButtonMessage.TryParse("{\"kind\":\"state\",\"held\":[\"A\"]}", out _));
            Assert.IsFalse(ZwiftButtonMessage.TryParse("not json", out _));
            Assert.IsFalse(ZwiftButtonMessage.TryParse("", out _));
        }
    }

    public class ButtonOverlayLayoutTests
    {
        [Test]
        public void NoHudBoxesOverlapAndTheButtonMapFitsTheEditorGameView()
        {
            var boxes = KickrHud.AllBoxes;
            for (int i = 0; i < boxes.Length; i++)
                for (int j = i + 1; j < boxes.Length; j++)
                    Assert.IsFalse(boxes[i].Overlaps(boxes[j]), $"{boxes[i]} overlaps {boxes[j]}");
            var r = KickrHud.ButtonMapBox;
            Assert.GreaterOrEqual(r.x, 0f);
            Assert.GreaterOrEqual(r.y, 0f);
            Assert.LessOrEqual(r.xMax, 1102f, "ProjectX Game view (Free Aspect) is 1102 x 505");
            Assert.LessOrEqual(r.yMax, 505f);
            Assert.GreaterOrEqual(r.x, KickrHud.DistanceBox.xMax);
            Assert.GreaterOrEqual(r.x, KickrHud.BoostBox.xMax);
        }

        [Test]
        public void SceneHasTheOverlayWiredToTheRideButtons()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/KickrCubeTest.unity", OpenSceneMode.Single);
            var overlay = Object.FindObjectsByType<ZwiftRideButtonOverlay>().Single();
            var buttons = Object.FindObjectsByType<ZwiftRideUdpReceiver>().Single();
            Assert.AreSame(buttons, overlay.buttons);
            Assert.IsTrue(overlay.show);
            Assert.AreEqual(5006, buttons.port);
        }
    }
}
