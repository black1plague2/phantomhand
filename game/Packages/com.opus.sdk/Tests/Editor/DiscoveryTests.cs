using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using Opus.Sdk;

namespace Opus.Sdk.Tests
{
    /// <summary>Beacon parsing (both dialects), the latch filter, the shared DiscoveryHub socket and the
    /// transport's manual-host path. Fan-out tests call DiscoveryHub.Dispatch directly (synchronous, no timing);
    /// the one real-socket test waits for readiness (IsListening) and polls with a deadline, never a fixed sleep.
    /// Ports are offsets so a running game or simulator on 8790/8791 is not touched.</summary>
    public class DiscoveryTests
    {
        private const string NodeAPrd = "{\"type\":\"device_discovery\",\"device_id\":\"SLEEVE_001\",\"device_kind\":\"haptic\"," +
            "\"firmware_version\":\"0.5.0\",\"command_port\":8790,\"status\":\"available\",\"motor_count\":2,\"timestamp_ms\":1000}";
        private const string NodeAMerged = "{\"type\":\"device_discovery\",\"device_id\":\"SLEEVE_001\",\"device_kind\":\"haptic\"," +
            "\"firmware_version\":\"0.5.0\",\"command_port\":8790,\"status\":\"available\",\"motor_count\":2,\"timestamp_ms\":1000," +
            "\"opus_haptic\":1,\"port\":8790,\"fw\":\"0.5.0\"}";
        private const string NodeBPrd = "{\"type\":\"device_discovery\",\"device_id\":\"CHETNA_BIO_001\",\"device_kind\":\"bio\"," +
            "\"firmware_version\":\"0.5.0\",\"command_port\":8790,\"status\":\"available\",\"motor_count\":0,\"timestamp_ms\":2000}";
        private const string LegacyHello = "{\"opus_haptic\":1,\"device_id\":\"sleeve-01\",\"port\":8790,\"fw\":\"0.1.0\"}";

        private static readonly IPAddress HostA = IPAddress.Parse("192.168.43.20");
        private static readonly IPAddress HostB = IPAddress.Parse("192.168.43.21");

        [Test]
        public void Parse_PrdForm_ReadsKindIdPortAndFirmware()
        {
            var i = DiscoveryHub.Parse(NodeBPrd, HostB);
            Assert.AreEqual("bio", i.DeviceKind);
            Assert.AreEqual("CHETNA_BIO_001", i.DeviceId);
            Assert.AreEqual(8790, i.CommandPort);
            Assert.AreEqual("0.5.0", i.Firmware);
            Assert.AreEqual(HostB, i.Host);
            Assert.IsFalse(i.Legacy);
        }

        [Test]
        public void Parse_LegacyHello_DefaultsToHapticKind()
        {
            var i = DiscoveryHub.Parse(LegacyHello, HostA);
            Assert.AreEqual("haptic", i.DeviceKind, "missing device_kind means haptic");
            Assert.AreEqual("sleeve-01", i.DeviceId);
            Assert.AreEqual(8790, i.CommandPort);
            Assert.AreEqual("0.1.0", i.Firmware);
            Assert.IsTrue(i.Legacy);
        }

        [Test]
        public void Parse_NodeAMergedDatagram_IsReadAsThePrdForm()
        {
            var i = DiscoveryHub.Parse(NodeAMerged, HostA);
            Assert.IsFalse(i.Legacy);
            Assert.AreEqual("haptic", i.DeviceKind);
            Assert.AreEqual("SLEEVE_001", i.DeviceId);
        }

        [Test]
        public void Parse_CustomCommandPort_IsHonoured()
        {
            var i = DiscoveryHub.Parse(NodeAPrd.Replace("\"command_port\":8790", "\"command_port\":9001"), HostA);
            Assert.AreEqual(9001, i.CommandPort);
        }

        [Test]
        public void Parse_NotADiscoveryMessage_ReturnsNull()
        {
            Assert.IsNull(DiscoveryHub.Parse("{\"type\":\"sensor_data\",\"device_id\":\"x\"}", HostA));
            Assert.IsNull(DiscoveryHub.Parse("not json", HostA));
            Assert.IsNull(DiscoveryHub.Parse("[1,2]", HostA));
            Assert.IsNull(DiscoveryHub.Parse("{\"type\":\"device_discovery\",\"device_id\":\"x\",\"command_port\":99999}", HostA), "port out of range");
        }

        [Test]
        public void Filter_KindAndId_ForBothDialects()
        {
            var a = DiscoveryHub.Parse(NodeAPrd, HostA);
            var aLegacy = DiscoveryHub.Parse(LegacyHello, HostA);
            var b = DiscoveryHub.Parse(NodeBPrd, HostB);

            Assert.IsTrue(DiscoveryFilter.Haptic.Matches(a));
            Assert.IsTrue(DiscoveryFilter.Haptic.Matches(aLegacy), "legacy hello is a haptic node");
            Assert.IsFalse(DiscoveryFilter.Haptic.Matches(b));
            Assert.IsTrue(DiscoveryFilter.Bio.Matches(b));
            Assert.IsFalse(DiscoveryFilter.Bio.Matches(a));
            Assert.IsFalse(DiscoveryFilter.Bio.Matches(aLegacy));
            Assert.IsTrue(DiscoveryFilter.Any.Matches(a) && DiscoveryFilter.Any.Matches(b));
            Assert.IsTrue(new DiscoveryFilter(deviceId: "SLEEVE_001").Matches(a));
            Assert.IsFalse(new DiscoveryFilter(deviceId: "SLEEVE_001").Matches(b));
            Assert.IsFalse(new DiscoveryFilter("haptic", "SLEEVE_002").Matches(a));
        }

        [Test]
        public void Transport_DefaultFilter_LatchesFirstHaptic_IgnoresBioAndLaterBeacons()
        {
            const int port = 28811;
            using (var t = new UdpHapticTransport(discoveryPort: port))
            {
                t.Start();
                Assert.AreEqual(1, DiscoveryHub.ListenerCount(port));

                Assert.AreEqual(1, DiscoveryHub.Dispatch(port, NodeBPrd, HostB));
                Assert.IsFalse(t.HasDevice, "a bio node is not a haptic node");

                DiscoveryHub.Dispatch(port, NodeAMerged, HostA);
                Assert.IsTrue(t.HasDevice);
                Assert.AreEqual(new IPEndPoint(HostA, 8790), t.DeviceEndpoint);
                Assert.AreEqual("SLEEVE_001", t.LatchedDeviceId);

                DiscoveryHub.Dispatch(port, LegacyHello, HostB);   // nobody is listening any more
                Assert.AreEqual(new IPEndPoint(HostA, 8790), t.DeviceEndpoint, "first match stays latched");
                Assert.AreEqual(0, DiscoveryHub.ListenerCount(port), "a latched transport leaves the hub");
            }
        }

        [Test]
        public void Hub_FansOutOneBeaconToEveryRegisteredTransport_EachLatchesItsOwnKind()
        {
            const int port = 28812;
            using (var haptic = new UdpHapticTransport(filter: DiscoveryFilter.Haptic, discoveryPort: port))
            using (var bio = new UdpHapticTransport(filter: DiscoveryFilter.Bio, discoveryPort: port))
            {
                int known = 0;
                haptic.OnDeviceKnown += () => known++;
                bio.OnDeviceKnown += () => known++;
                haptic.Start();
                bio.Start();
                Assert.AreEqual(2, DiscoveryHub.ListenerCount(port), "two transports share one hub entry (one socket)");

                Assert.AreEqual(2, DiscoveryHub.Dispatch(port, NodeBPrd, HostB), "both are notified of every beacon");
                Assert.IsFalse(haptic.HasDevice);
                Assert.IsTrue(bio.HasDevice);
                Assert.AreEqual(1, DiscoveryHub.ListenerCount(port));

                Assert.AreEqual(1, DiscoveryHub.Dispatch(port, NodeAPrd, HostA));
                Assert.IsTrue(haptic.HasDevice);
                Assert.AreEqual(HostA, haptic.DeviceEndpoint.Address);
                Assert.AreEqual(HostB, bio.DeviceEndpoint.Address);
                Assert.AreEqual(2, known);
                Assert.AreEqual(0, DiscoveryHub.ListenerCount(port));
            }
        }

        [Test]
        public void Transport_DeviceIdFilter_PicksTheNamedNode()
        {
            const int port = 28813;
            using (var t = new UdpHapticTransport(filter: new DiscoveryFilter(deviceId: "SLEEVE_002"), discoveryPort: port))
            {
                t.Start();
                DiscoveryHub.Dispatch(port, NodeAPrd, HostA);
                Assert.IsFalse(t.HasDevice);
                DiscoveryHub.Dispatch(port, NodeAPrd.Replace("SLEEVE_001", "SLEEVE_002"), HostB);
                Assert.IsTrue(t.HasDevice);
                Assert.AreEqual(HostB, t.DeviceEndpoint.Address);
            }
        }

        [Test]
        public void Transport_ManualHost_SkipsDiscoveryAndUsesTheGivenPort()
        {
            const int port = 28814;
            using (var t = new UdpHapticTransport("127.0.0.1", commandPort: 28790, discoveryPort: port))
            {
                int known = 0;
                t.OnDeviceKnown += () => known++;
                t.Start();
                Assert.IsTrue(t.HasDevice);
                Assert.AreEqual(new IPEndPoint(IPAddress.Loopback, 28790), t.DeviceEndpoint);
                Assert.AreEqual(1, known);
                Assert.AreEqual(0, DiscoveryHub.ListenerCount(port), "a manual host never registers for beacons");
            }
        }

        [Test]
        public void Hub_ClosesWhenTheLastTransportStops()
        {
            const int port = 28815;
            var t = new UdpHapticTransport(discoveryPort: port);
            t.Start();
            Assert.AreEqual(1, DiscoveryHub.ListenerCount(port));
            t.Dispose();
            Assert.AreEqual(0, DiscoveryHub.ListenerCount(port));
            Assert.IsFalse(DiscoveryHub.IsListening(port));
            Assert.AreEqual(0, DiscoveryHub.Dispatch(port, NodeAPrd, HostA));
        }

        [Test]
        public void Hub_RealSocket_BeaconOverLoopbackLatchesTheTransport()
        {
            const int port = 28816;
            using (var t = new UdpHapticTransport(discoveryPort: port))
            using (var sender = new UdpClient())
            {
                t.Start();
                var sw = Stopwatch.StartNew();
                while (!DiscoveryHub.IsListening(port) && sw.ElapsedMilliseconds < 5000) Thread.Sleep(10);   // readiness, not a guess
                Assert.IsTrue(DiscoveryHub.IsListening(port), "hub socket bound");

                var beacon = Encoding.UTF8.GetBytes(NodeAPrd.Replace("\"command_port\":8790", "\"command_port\":28790"));
                sw.Restart();
                while (!t.HasDevice && sw.ElapsedMilliseconds < 5000)
                {
                    sender.Send(beacon, beacon.Length, new IPEndPoint(IPAddress.Loopback, port));   // nodes repeat each second
                    Thread.Sleep(20);
                }
                Assert.IsTrue(t.HasDevice, "beacon received through the shared socket");
                Assert.AreEqual(new IPEndPoint(IPAddress.Loopback, 28790), t.DeviceEndpoint);
            }
        }
    }
}
