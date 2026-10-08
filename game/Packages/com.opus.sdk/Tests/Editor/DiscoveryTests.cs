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
        // Verbatim from docs/PH_ELECTRONICS_HANDOFF_FROM_TEAM.md section A (the firmware as flashed 2026-10-08): the real Node A id is
        // CHETNA_HAPTIC_001, the contract and the fixtures say SLEEVE_001. Both must latch; the node KIND decides, never the id.
        private const string RealNodeA = "{\"type\":\"device_discovery\",\"device_id\":\"CHETNA_HAPTIC_001\",\"device_kind\":\"haptic\"," +
            "\"firmware_version\":\"0.5.0\",\"command_port\":8790,\"status\":\"available\",\"motor_count\":2,\"timestamp_ms\":123456}";
        private const string RealNodeB = "{\"type\":\"device_discovery\",\"device_id\":\"CHETNA_BIO_001\",\"device_kind\":\"bio\"," +
            "\"firmware_version\":\"0.5.0\",\"command_port\":8790,\"status\":\"available\",\"motor_count\":0,\"timestamp_ms\":123456}";

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

        // ------------------------------------------------------------------ node matching: by kind, not by id

        [Test]
        public void Parse_RealFirmwareBeacons_AreMatchedByKind()
        {
            var a = DiscoveryHub.Parse(RealNodeA, HostA);
            Assert.AreEqual("haptic", a.DeviceKind);
            Assert.AreEqual("CHETNA_HAPTIC_001", a.DeviceId);
            Assert.AreEqual(8790, a.CommandPort);
            Assert.AreEqual("0.5.0", a.Firmware);
            Assert.IsFalse(a.Legacy);

            var b = DiscoveryHub.Parse(RealNodeB, HostB);
            Assert.AreEqual("bio", b.DeviceKind);
            Assert.AreEqual("CHETNA_BIO_001", b.DeviceId);

            Assert.IsTrue(DiscoveryFilter.Haptic.Matches(a));
            Assert.IsFalse(DiscoveryFilter.Haptic.Matches(b));
            Assert.IsTrue(DiscoveryFilter.Bio.Matches(b));
            Assert.IsFalse(DiscoveryFilter.Bio.Matches(a));
        }

        [Test]
        public void Filter_TheKindDecides_TheIdNever()
        {
            // a haptic node whatever it is called, and a bio node even if its id looks like a haptic one
            foreach (string id in new[] { "SLEEVE_001", "CHETNA_HAPTIC_001", "CHETNA_BIO_001", "anything-else", "" })
            {
                var haptic = DiscoveryHub.Parse(RealNodeA.Replace("CHETNA_HAPTIC_001", id), HostA);
                Assert.IsTrue(DiscoveryFilter.Haptic.Matches(haptic), "haptic id '" + id + "'");
                Assert.IsFalse(DiscoveryFilter.Bio.Matches(haptic), "haptic id '" + id + "'");
            }
            foreach (string id in new[] { "CHETNA_BIO_001", "CHETNA_HAPTIC_001", "SLEEVE_001" })
            {
                var bio = DiscoveryHub.Parse(RealNodeB.Replace("CHETNA_BIO_001", id), HostB);
                Assert.IsFalse(DiscoveryFilter.Haptic.Matches(bio), "bio id '" + id + "'");
                Assert.IsTrue(DiscoveryFilter.Bio.Matches(bio), "bio id '" + id + "'");
            }
        }

        [Test]
        public void Parse_LegacyHello_WithoutAKind_StaysHaptic_WhateverItsId()
        {
            foreach (string id in new[] { "sleeve-01", "SLEEVE_001", "CHETNA_HAPTIC_001" })
            {
                var i = DiscoveryHub.Parse("{\"opus_haptic\":1,\"device_id\":\"" + id + "\",\"port\":8790,\"fw\":\"0.5.0\"}", HostA);
                Assert.AreEqual("haptic", i.DeviceKind, id);
                Assert.IsTrue(DiscoveryFilter.Haptic.Matches(i), id);
                Assert.IsFalse(DiscoveryFilter.Bio.Matches(i), id);
            }
            var nullKind = DiscoveryHub.Parse("{\"opus_haptic\":1,\"device_id\":\"x\",\"device_kind\":null,\"port\":8790}", HostA);
            Assert.AreEqual("haptic", nullKind.DeviceKind, "a null kind is no kind");
        }

        [Test]
        public void Transport_DefaultFilter_LatchesNodeA_UnderTheContractIdAndTheRealId()
        {
            int port = 28821;
            foreach (string id in new[] { "SLEEVE_001", "CHETNA_HAPTIC_001", "anything-else" })
            {
                using (var t = new UdpHapticTransport(discoveryPort: port))
                {
                    t.Start();
                    DiscoveryHub.Dispatch(port, RealNodeB, HostB);
                    Assert.IsFalse(t.HasDevice, "the bio node is never taken for Node A (" + id + ")");
                    DiscoveryHub.Dispatch(port, RealNodeA.Replace("CHETNA_HAPTIC_001", id), HostA);
                    Assert.IsTrue(t.HasDevice, id);
                    Assert.AreEqual(id, t.LatchedDeviceId);
                    Assert.AreEqual(new IPEndPoint(HostA, 8790), t.DeviceEndpoint);
                }
                port++;
            }
        }

        [Test]
        public void Hub_RealTwoNodeSession_EachTransportLatchesItsKind_InEitherArrivalOrder()
        {
            int port = 28825;
            foreach (bool bioFirst in new[] { true, false })
            {
                using (var haptic = new UdpHapticTransport(filter: DiscoveryFilter.Haptic, discoveryPort: port))
                using (var bio = new UdpHapticTransport(filter: DiscoveryFilter.Bio, discoveryPort: port))
                {
                    haptic.Start();
                    bio.Start();
                    string first = bioFirst ? RealNodeB : RealNodeA, second = bioFirst ? RealNodeA : RealNodeB;
                    IPAddress firstHost = bioFirst ? HostB : HostA, secondHost = bioFirst ? HostA : HostB;
                    DiscoveryHub.Dispatch(port, first, firstHost);
                    DiscoveryHub.Dispatch(port, second, secondHost);

                    Assert.AreEqual("CHETNA_HAPTIC_001", haptic.LatchedDeviceId);
                    Assert.AreEqual(HostA, haptic.DeviceEndpoint.Address);
                    Assert.AreEqual("CHETNA_BIO_001", bio.LatchedDeviceId);
                    Assert.AreEqual(HostB, bio.DeviceEndpoint.Address);
                }
                port++;
            }
        }
    }

    /// <summary>Windows reports an ICMP "port unreachable" from an earlier send as SocketException 10054 (WSAECONNRESET) on the
    /// NEXT receive of a UDP socket, which used to end the receive loop for good (U5 log, open issue 2). Every SDK UDP receive
    /// loop must swallow exactly that error and keep receiving; any other socket error keeps ending it.</summary>
    public class UdpConnResetTests
    {
        [Test]
        public void IsConnReset_IsTrueFor10054Only()
        {
            Assert.IsTrue(UdpConnReset.IsConnReset(new SocketException(10054)));
            Assert.IsTrue(UdpConnReset.IsConnReset(new SocketException((int)SocketError.ConnectionReset)));
            foreach (var other in new[] { SocketError.ConnectionRefused, SocketError.ConnectionAborted, SocketError.OperationAborted,
                                          SocketError.Interrupted, SocketError.TimedOut, SocketError.NetworkUnreachable, SocketError.HostUnreachable,
                                          SocketError.MessageSize, SocketError.AccessDenied, SocketError.Shutdown })
                Assert.IsFalse(UdpConnReset.IsConnReset(new SocketException((int)other)), other.ToString());
            Assert.IsFalse(UdpConnReset.IsConnReset(null));
            Assert.AreEqual(-1744830452, UdpConnReset.SioUdpConnReset, "SIO_UDP_CONNRESET");
        }

        private static int ClosedUdpPort()
        {
            using (var probe = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0))) return ((IPEndPoint)probe.Client.LocalEndPoint).Port;
        }

        [Test]
        public void Disable_NeverThrows_IsWindowsOnly_AndWhereItWorksTheOsStopsReportingTheReset()
        {
            Assert.IsFalse(UdpConnReset.Disable(null));
            using (var udp = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0)))
            {
                bool disabled = UdpConnReset.Disable(udp.Client);
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    Assert.IsFalse(disabled, "SIO_UDP_CONNRESET is a Windows ioctl; elsewhere the loops rely on IsConnReset alone");
                    return;
                }
                if (!disabled) return;   // this runtime cannot issue the ioctl: the exception handler is the fallback (Transport_KeepsReceiving_... covers it)

                // a send to a closed port: Windows would answer the next receive with 10054; with the report switched off it just waits
                udp.Send(new byte[] { 1 }, 1, new IPEndPoint(IPAddress.Loopback, ClosedUdpPort()));
                udp.Client.ReceiveTimeout = 700;
                var ex = Assert.Throws<SocketException>(() => udp.Client.Receive(new byte[16]));
                Assert.AreEqual(SocketError.TimedOut, ex.SocketErrorCode, "a timeout, not WSAECONNRESET");
            }
        }

        [Test]
        public void Transport_KeepsReceiving_AfterSendsToAClosedPort()
        {
            int closedPort = ClosedUdpPort();   // a UDP port nothing listens on (bound once, then released)

            using (var t = new UdpHapticTransport("127.0.0.1", commandPort: closedPort, discoveryPort: 28830))
            {
                var got = new System.Collections.Concurrent.ConcurrentQueue<string>();
                t.OnMessage += got.Enqueue;
                t.Start();

                // each send to the closed port makes a Windows socket throw 10054 from its next receive; the old loop ended there.
                // There is no signal for "the ICMP has come back", so give it a moment: too short only makes the test less
                // sensitive, it can never make it fail.
                for (int i = 0; i < 5; i++) { t.Send("{\"type\":\"ping\"}"); Thread.Sleep(30); }

                // now the node comes up on that very port and talks to the transport's command socket
                using (var node = new UdpClient(new IPEndPoint(IPAddress.Loopback, closedPort)))
                {
                    var reply = Encoding.UTF8.GetBytes("{\"type\":\"status\",\"device_id\":\"CHETNA_HAPTIC_001\"}");
                    var sw = Stopwatch.StartNew();
                    while (got.IsEmpty && sw.ElapsedMilliseconds < 5000)
                    {
                        node.Send(reply, reply.Length, new IPEndPoint(IPAddress.Loopback, t.LocalPort));
                        Thread.Sleep(20);
                    }
                }
                Assert.IsFalse(got.IsEmpty, "the receive loop must survive WSAECONNRESET and still deliver the node's datagram");
            }
        }
    }
}
