// What the hub does today when a headset reconnects, pinned so that nobody
// changes it without seeing who relies on it.
//
// contracts/LIVE_PROTOCOL.md, "Resume": each side keeps an outbox; on reconnect
// `hello.resume_from_seq` tells the peer what arrived, the peer replays anything
// after it, and receivers de-dup by `id`. HubServer._onHello deviates on
// purpose for now (the deviation belongs under the "Known deviation" note of
// that section): on a reconnect it builds a NEW HubConnection, carries over only
// the sent history (seedHistory), echoes the headset's own number in
// hello_ack.resume_from_seq, and does not carry over the ids already seen
// (`_seenIds`), so the headset's replay is handled as new.
//
// The Phantom Hand live card leans on that: phantom/phantom_live_reconnect_test.dart.
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/core/hub/live_message.dart';

import 'reconnecting_headset.dart';

void main() {
  late Directory dataDir;
  late HubServer server;
  late ReconnectingHeadset headset;

  setUp(() async {
    dataDir = Directory.systemTemp.createTempSync('opus_hub_reconnect_');
    server = HubServer(sessionsDir: dataDir.path, port: await freePort());
    await server.start();
    headset = ReconnectingHeadset(port: server.port);
  });

  tearDown(() async {
    await headset.close();
    await server.stop();
    dataDir.deleteSync(recursive: true);
  });

  // TODAY'S BEHAVIOUR, not the contract's. See contracts/LIVE_PROTOCOL.md:
  // Resume, Known deviation. Receivers de-dup by id; the hub does, but only
  // within one HubConnection (`_seenIds`), and a reconnect builds a new one. So
  // every consumer of HubConnection.trialEventStream (HubPhantomLiveRepository,
  // HubLiveRepository, the live monitor screen, the last_status relay in
  // HubServer) must be ready for a replayed event. If this test fails because
  // the replay no longer reaches the listeners, the hub has changed: read
  // phantom/phantom_live_reconnect_test.dart before shipping that.
  test('replays after a reconnect reach listeners again: consumers must be idempotent', () async {
    await headset.connect();
    final first = server.connections.single;
    // The hub has said something by now, so the headset has a number to resume from.
    for (var i = 0; i < 3; i++) {
      first.send(LiveMessage.ping(first.nextSeq));
    }
    await eventually(() => headset.lastRecvSeqFromHub >= 3, "the headset to receive the hub's pings");
    final resumeFromSeq = headset.lastRecvSeqFromHub;

    final events = [demoEvent('threat_impact'), demoEvent('emg_burst'), demoEvent('witness_summary')];
    final deliveredOnFirst = <Map<String, dynamic>>[];
    final sub1 = first.trialEventStream.listen(deliveredOnFirst.add);
    addTearDown(sub1.cancel);
    events.forEach(headset.event);
    await headset.roundTrip();
    await pumpEventQueue();
    expect(deliveredOnFirst, events, reason: 'the first link delivers each event once');

    // The link is lost; the same device dials in again and names what it got.
    headset.drop();
    final helloAck = await headset.connect(resumeFromSeq: resumeFromSeq);
    final second = server.connections.single;
    expect(second, isNot(same(first)), reason: 'a reconnect builds a new HubConnection');
    expect((helloAck['payload'] as Map)['resume_from_seq'], resumeFromSeq, reason: "hello_ack repeats the headset's own number");

    final deliveredOnSecond = <Map<String, dynamic>>[];
    final sub2 = second.trialEventStream.listen(deliveredOnSecond.add);
    addTearDown(sub2.cancel);
    headset.replay(); // the same three envelopes, same ids
    await headset.roundTrip();
    await pumpEventQueue();

    expect(
      deliveredOnSecond,
      events,
      reason: 'the replayed ids reach the listeners a second time, once each, in the same order',
    );
  });
}
