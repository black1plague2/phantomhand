// What happens to the Phantom Hand live card when the headset reconnects, with
// the REAL hub (HubServer on a loopback socket) and a headset that loses its
// link, says hello again with `resume_from_seq` and replays its trial_events
// (same ids), as LiveClient.cs does on hello_ack.
//
// The witness numbers and the trace markers only ever arrive as trial_events
// (HubPhantomLiveRepository._onEvent), and the card is rebuilt on every reconnect:
//  - the hub builds a new HubConnection (HubServer._onHello), which has no status
//    yet, so the Monitor tab shows "No headset connected" until its first status;
//  - phantomLiveRepositoryProvider keeps one repository per HubConnection OBJECT
//    and PhantomLiveScreen starts a new PhantomLiveModel for a new repository;
//  - the hub republishes its headset list only every 250 ms
//    (HubServer._publishSnapshotThrottled), so the card is back a fraction of a
//    second after the new link's first status, and HubConnection.trialEventStream
//    is a broadcast stream: an event nobody listens to yet is gone.
// So a replay sent right after hello_ack (LiveClient.cs) reaches the hub long before
// the card is back and the card never sees it. The card therefore starts from what
// the previous card of that headset showed: phantomLiveRepositoryProvider hands it
// over as the first snapshot (_RememberingRepository), and whatever the headset
// replays afterwards is taken once (a marker of the same kind within 1 ms, a witness
// that replaces the witness). Both orders of the replay are tested.
//
// What is handed over must never be a previous participant's: the group "what a card on
// a new connection is handed". The memory behind it is bounded (the resume window, the
// session, at most 8 headsets): the last test. Not covered anywhere: an event that reaches
// the hub while no card is on screen and that no card showed before (a replay-only event
// raised during the outage); a headset that replays an earlier participant's events once
// the new card is back (the hub gives no per-event session id); and the gap itself: the
// card is still gone from the hello until the hub's next publish (0.25 to 0.3 s).
//
// The card is reached the way the Monitor tab reaches it: hub state ->
// phantomDeviceId -> phantomLiveRepositoryProvider(phantomDeviceKey) -> a
// PhantomLiveModel fed by repository.watch() (see _Card).
import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/hub/hub_server.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:opus_app/features/live/phantom_live_providers.dart';

import '../hub/reconnecting_headset.dart';
import 'witness_test_support.dart';

/// `HubController` over a real [HubServer], published the way
/// `HubController.start` / `_emit` publish it (every `knownHeadsetsStream`
/// event becomes the running state), without the path_provider and the UDP
/// beacon of `start()`.
class _ServerHub extends HubController {
  new(this.server);

  final HubServer server;
  StreamSubscription<List<HeadsetSnapshot>>? _sub;

  HubControllerState _snapshot() => HubControllerState.running(
        hubId: server.hubId,
        port: server.port,
        headsets: [
          for (final h in server.knownHeadsets)
            HeadsetInfo(
              deviceId: h.deviceId,
              rttMs: h.rtt?.inMilliseconds,
              status: h.status,
              connected: h.connected,
              disconnectedAt: h.disconnectedAt,
            ),
        ],
      );

  @override
  HubControllerState build() {
    ref.onDispose(() => unawaited(_sub?.cancel()));
    _sub = server.knownHeadsetsStream.listen((_) => state = _snapshot());
    return _snapshot();
  }

  @override
  HubConnection? connectionFor(String deviceId) => server.connections.where((c) => c.deviceId == deviceId).firstOrNull;
}

/// The operator card as the Monitor tab puts it on screen, reduced to its
/// decisions: monitor_home_screen.dart picks `phantomDeviceId(hub)` and watches
/// `phantomLiveRepositoryProvider(phantomDeviceKey(id))`; PhantomLiveScreen
/// subscribes to that repository with a PhantomLiveModel of its own and starts
/// a new one when the repository is another. [model] is null while the tab
/// shows something else ("No headset connected").
class _Card {
  new(this.container) {
    _hub = container.listen(hubControllerProvider, (_, _) => _sync(), fireImmediately: true);
  }

  final ProviderContainer container;
  late final ProviderSubscription<HubControllerState> _hub;
  Object? _repository;
  StreamSubscription<PhantomLiveSnapshot>? _snapshots;
  PhantomLiveModel? model;

  /// Every snapshot [model] has taken, as it came. The model de-dups a marker that comes
  /// twice (and a witness replaces the witness), so only this list shows an event that
  /// came out of the repository twice.
  final List<PhantomLiveSnapshot> received = [];

  /// The card has taken its first snapshot: it shows the headset.
  bool get isUp => model?.latest != null;

  void _sync() {
    final id = phantomDeviceId(container.read(hubControllerProvider));
    final repository = id == null ? null : container.read(phantomLiveRepositoryProvider(phantomDeviceKey(id)));
    if (identical(repository, _repository)) return;
    unawaited(_snapshots?.cancel());
    _repository = repository;
    _snapshots = null;
    model = null;
    received.clear();
    if (repository != null) {
      final fresh = PhantomLiveModel();
      model = fresh;
      _snapshots = repository.watch().listen((s) {
        received.add(s);
        fresh.apply(s);
      });
    }
  }

  void dispose() {
    _hub.close();
    unawaited(_snapshots?.cancel());
  }
}

/// When the headset replays, relative to the card coming back after the hello.
enum _Replay {
  /// Straight after hello_ack, before its first status on the new link: what
  /// LiveClient.cs does (the replay is its answer to hello_ack).
  rightAfterHelloAck('replay right after hello_ack'),

  /// Once the card is on screen again.
  onceTheCardIsBack('replay once the card is back');

  new(this.label);
  final String label;
}

void main() {
  late Directory dataDir;
  late HubServer server;
  late ReconnectingHeadset headset;
  late ProviderContainer container;
  late _Card card;

  /// A fresh provider scope (nothing remembered) over the hub, and the card on it.
  void startCard({Duration? resumeWindow}) {
    container = ProviderContainer(
      overrides: [
        hubControllerProvider.overrideWith(() => _ServerHub(server)),
        if (resumeWindow != null) phantomResumeWindowProvider.overrideWithValue(resumeWindow),
      ],
    );
    card = _Card(container);
  }

  setUp(() async {
    dataDir = Directory.systemTemp.createTempSync('opus_ph_reconnect_');
    server = HubServer(sessionsDir: dataDir.path, port: await freePort());
    await server.start();
    headset = ReconnectingHeadset(port: server.port);
    startCard();
  });

  tearDown(() async {
    card.dispose();
    container.dispose();
    await headset.close();
    await server.stop();
    dataDir.deleteSync(recursive: true);
  });

  /// First link: the card comes up and [events] reach it ([shown] says it shows
  /// them). Then the link is lost without the hub noticing, the device says
  /// hello again with `resume_from_seq`, its statuses go on and it replays
  /// [events] at [replay]. Returns the card's model on the first link and the
  /// one it has once the card is back and the hub has handled the replay.
  Future<({PhantomLiveModel before, PhantomLiveModel after})> reconnect(
    _Replay replay, {
    required String phase,
    required List<Map<String, dynamic>> events,
    required bool Function(PhantomLiveModel) shown,
  }) async {
    void statuses() => headset.startStatuses((n) => phantomStatus(n: n, phase: phase));

    await headset.connect();
    statuses();
    await eventually(() => card.isUp, 'the card on the first link');
    final before = card.model!;
    events.forEach(headset.event);
    await eventually(() => shown(before), 'the card to show the events of the first link');

    headset.drop();
    await headset.connect(resumeFromSeq: headset.lastRecvSeqFromHub);
    switch (replay) {
      case _Replay.rightAfterHelloAck:
        headset.replay();
        await headset.roundTrip();
        statuses();
        await eventually(() => card.isUp, 'the card on the new link');
      case _Replay.onceTheCardIsBack:
        statuses();
        await eventually(() => card.isUp, 'the card on the new link');
        headset.replay();
        await headset.roundTrip();
    }
    await pumpEventQueue();
    return (before: before, after: card.model!);
  }

  for (final replay in _Replay.values) {
    group(replay.label, () {
      test('witness_summary: the card shows the witness numbers and the closing lines again', () async {
        final event = demoEvent('witness_summary');
        final run = await reconnect(replay, phase: 'witness', events: [event], shown: (m) => m.witness != null);
        _expectWitness(run.before.witness, event, 'on the first link');
        expect(run.after, isNot(same(run.before)), reason: 'the card is rebuilt on the new connection, with a new model');
        _expectWitness(run.after.witness, event, 'after the reconnect (${replay.label})');
      });

      test('threat_impact and emg_burst: both markers are on the trace again, once each', () async {
        final events = [demoEvent('threat_impact'), demoEvent('emg_burst')];
        final run = await reconnect(replay, phase: 'threat', events: events, shown: (m) => m.buffer.markers.length == 2);
        _expectMarkers(run.before, events, 'on the first link');
        expect(run.after, isNot(same(run.before)), reason: 'the card is rebuilt on the new connection, with a new model');
        _expectMarkers(run.after, events, 'after the reconnect (${replay.label})');
      });
    });
  }

  /// Participant A, session A, on the first link: the card shows A's stone, burst and
  /// witness numbers. Returns the card's model.
  Future<PhantomLiveModel> participantA() async {
    headset.sessionId = 'session-A';
    await headset.connect();
    headset.startStatuses((n) => phantomStatus(n: n, phase: 'witness'));
    await eventually(() => card.isUp, 'the card on the first link');
    final model = card.model!;
    [demoEvent('threat_impact'), demoEvent('emg_burst'), demoEvent('witness_summary')].forEach(headset.event);
    await eventually(() => model.witness != null && model.buffer.markers.length == 2, "the card to show A's events");
    return model;
  }

  /// The link is lost without the hub noticing; the device says hello again and, right
  /// after hello_ack, replays everything in its outbox (LiveClient.cs: all the
  /// trial_events since the last hello_ack, those of earlier participants too).
  Future<void> blip() async {
    headset.drop();
    await headset.connect(resumeFromSeq: headset.lastRecvSeqFromHub);
    headset.replay();
    await headset.roundTrip();
  }

  /// The trial_event payloads the hub itself still holds (GET /opus/v1/live/last_status:
  /// the last 60 of every headset, mixed, replays included).
  Future<List<Map<String, dynamic>>> heldByHub() async {
    final client = HttpClient();
    try {
      final response = await (await client.getUrl(Uri.parse('http://127.0.0.1:${server.port}/opus/v1/live/last_status'))).close();
      final body = jsonDecode(await utf8.decodeStream(response)) as Map<String, dynamic>;
      return (body['events'] as List).cast<Map<String, dynamic>>();
    } finally {
      client.close(force: true);
    }
  }

  // Handed on to the same participant (the same session, within the window: the first test
  // here, and the two groups above, where the headset keeps its session id across the
  // reconnect), and to a card that meets a finished run, where the hub has no session id at
  // all (a `finished` status clears it); never to the next participant, whatever the hub or
  // the headset still hold.
  group('what a card on a new connection is handed', () {
    test('in the same session, within the window, the witness and the markers are handed on', () async {
      final events = [demoEvent('threat_impact'), demoEvent('emg_burst')];
      await participantA();
      final onOldLink = card.model!;

      await blip();
      headset.startStatuses((n) => phantomStatus(n: n, phase: 'witness'));
      await eventually(() => card.isUp && !identical(card.model, onOldLink), 'the card on the new link');
      await pumpEventQueue();

      expect(server.connections.single.activeSessionId, 'session-A', reason: "premise: the new link is in A's session too");
      _expectWitness(card.model!.witness, demoEvent('witness_summary'), 'in the same session');
      _expectMarkers(card.model!, events, 'in the same session');
    });

    test('after a finished run, with no session id left, the witness and the markers are still handed on within the window', () async {
      final events = [demoEvent('threat_impact'), demoEvent('emg_burst')];
      await participantA();
      headset.startStatuses((n) => phantomStatus(n: n, phase: 'done', state: 'finished'));
      await eventually(() => card.model!.latest!.runState == PhantomRunState.finished, 'A to finish');
      final onOldLink = card.model!;

      await blip();
      headset.startStatuses((n) => phantomStatus(n: n, phase: 'done', state: 'finished'));
      await eventually(() => card.isUp && !identical(card.model, onOldLink), 'the card on the new link');
      await pumpEventQueue();

      expect(server.connections.single.activeSessionId, isNull, reason: 'premise: a finished run leaves the hub with no session id');
      expect(card.model!.latest!.runState, PhantomRunState.finished);
      _expectWitness(card.model!.witness, demoEvent('witness_summary'), 'after a finished run');
      _expectMarkers(card.model!, events, 'after a finished run');
    });

    test(
      'the hub still holds the previous witness_summary and markers, yet after a reconnect early in the next session the card has none of them',
      () async {
        await participantA();
        headset.startStatuses((n) => phantomStatus(n: n, phase: 'done', state: 'finished'));
        await eventually(() => card.model!.latest!.runState == PhantomRunState.finished, 'A to finish');

        // The operator moves on: the headset waits for the next participant (session B), then runs.
        headset
          ..sessionId = 'session-B'
          ..startStatuses((n) => phantomStatus(n: n, phase: 'idle', state: 'ready', condition: null));
        await eventually(() => card.model!.latest!.runState == PhantomRunState.ready, 'the headset to wait again');
        expect(card.model!.witness, isNull, reason: "the card's own rule: a headset waiting again is a new participant");
        headset
          ..startStatuses((n) => phantomStatus(n: n, phase: 'calibrate'))
          ..event(demoEvent('phase_start'));
        await eventually(() => card.model!.latest!.game?.phase == 'calibrate', 'B to run');
        final earlyInB = card.model!;

        await blip();
        headset.startStatuses((n) => phantomStatus(n: n, phase: 'calibrate'));
        await eventually(() => card.isUp && !identical(card.model, earlyInB), 'the card on the new link');
        await pumpEventQueue();

        final held = await heldByHub();
        expect(held.where((e) => e['type'] == 'witness_summary'), isNotEmpty, reason: "premise: the hub holds A's witness_summary");
        expect(held.where((e) => e['type'] == 'threat_impact'), isNotEmpty, reason: "premise: the hub holds A's stone");
        final stone = demoEvent('threat_impact')['t_ms'] as num;
        expect(card.model!.buffer.windowStartMs, lessThan(stone), reason: "A's stone is inside the 20 s window: its absence is not the window");
        expect(card.model!.witness, isNull, reason: "A's witness_summary on B's card");
        expect(card.model!.buffer.markers, isEmpty, reason: "A's markers on B's card");

        // B's own events are shown, and replace nothing of A's: there is nothing of A's.
        final stoneOfB = <String, dynamic>{...demoEvent('threat_impact'), 't_ms': 67000.0};
        final witnessOfB = fixtureWitnessEvent();
        headset
          ..event(stoneOfB)
          ..event(witnessOfB);
        await eventually(() => card.model!.witness != null && card.model!.buffer.markers.isNotEmpty, "the card to show B's events");
        _expectWitness(card.model!.witness, witnessOfB, "B's numbers, not A's");
        expect(card.model!.buffer.markers.map((m) => m.tMs), [67000.0], reason: "only B's stone");
      },
    );

    test("the next session's id was already seen on the old link before the drop: nothing of the old session is handed on", () async {
      await participantA();
      // The headset is in the next participant's session on the same link, with no waiting state
      // in between, so the card's own rule does not clear anything; its id reaches the memory.
      headset
        ..sessionId = 'session-B'
        ..startStatuses((n) => phantomStatus(n: n, phase: 'calibrate'));
      await eventually(() => card.model!.latest!.game?.phase == 'calibrate', 'B to run');
      final onOldLink = card.model!;

      await blip();
      headset.startStatuses((n) => phantomStatus(n: n, phase: 'calibrate'));
      await eventually(() => card.isUp && !identical(card.model, onOldLink), 'the card on the new link');
      await pumpEventQueue();

      expect(card.model!.witness, isNull, reason: "A's witness_summary on B's card");
      expect(card.model!.buffer.markers, isEmpty, reason: "A's markers on B's card");
    });

    test('the next session began while the link was down: its id differs on the new link, nothing of the old session is handed on', () async {
      await participantA();

      headset.sessionId = 'session-B';
      await blip();
      headset.startStatuses((n) => phantomStatus(n: n, phase: 'calibrate'));
      await eventually(() => card.isUp, 'the card on the new link');
      await pumpEventQueue();

      expect(card.model!.witness, isNull, reason: "A's witness_summary on B's card");
      expect(card.model!.buffer.markers, isEmpty, reason: "A's markers on B's card");
    });

    test('a resume window of zero hands nothing on, not even to the same participant', () async {
      card.dispose();
      container.dispose();
      startCard(resumeWindow: Duration.zero);
      await participantA();

      await blip();
      headset.startStatuses((n) => phantomStatus(n: n, phase: 'witness'));
      await eventually(() => card.isUp, 'the card on the new link');
      await pumpEventQueue();

      expect(card.model!.witness, isNull, reason: 'window zero: the card starts empty, as it always did');
      expect(card.model!.buffer.markers, isEmpty);
    });
  });

  test('two cards on one repository: each event reaches both once, and the first goes on when the second leaves', () async {
    await participantA();
    final second = PhantomLiveModel();
    final secondReceived = <PhantomLiveSnapshot>[];
    final repository = container.read(phantomLiveRepositoryProvider(phantomDeviceKey('quest-1')))!;
    // The live screen next to the Monitor tab card.
    final secondCard = repository.watch().listen((s) {
      secondReceived.add(s);
      second.apply(s);
    });
    addTearDown(secondCard.cancel);
    final firstReceivedBefore = card.received.length;

    final burst = <String, dynamic>{...demoEvent('emg_burst'), 't_ms': 66900.0};
    final witness = fixtureWitnessEvent();
    headset
      ..event(burst)
      ..event(witness);
    await eventually(() => second.witness != null && card.model!.buffer.markers.length == 3, 'both cards to show the new events');
    await pumpEventQueue();

    // The models would not show an event that came out twice (markers de-dup, a witness replaces
    // the witness): count what each card was handed. Two cards on one repository get each event once.
    for (final (name, taken) in [('first', card.received.skip(firstReceivedBefore).toList()), ('second', secondReceived)]) {
      expect(taken.where((s) => s.markers.isNotEmpty), hasLength(1), reason: 'the burst came out once on the $name card');
      expect(taken.where((s) => s.witness != null), hasLength(1), reason: 'the witness came out once on the $name card');
    }
    expect(second.buffer.markers.map((m) => m.tMs), [66900.0], reason: 'the second card saw the burst once');
    expect(card.model!.buffer.markers.where((m) => m.kind == TraceMarkerKind.emgBurst).map((m) => m.tMs), [demoEvent('emg_burst')['t_ms'], 66900.0]);
    _expectWitness(second.witness, witness, 'on the second card');
    _expectWitness(card.model!.witness, witness, 'on the first card');

    await secondCard.cancel();
    headset.event(<String, dynamic>{...demoEvent('threat_impact'), 't_ms': 67100.0});
    await eventually(() => card.model!.buffer.markers.length == 4, 'the first card to go on');
    expect(second.buffer.markers, hasLength(1), reason: 'the second card is no longer listening');
  });

  test('the memory keeps at most 8 headsets: the one heard from longest ago is forgotten, the others still hand theirs on', () async {
    card.dispose(); // no card follows the first headset here: the models below are the cards
    final headsets = [
      for (var i = 0; i < 9; i++) ReconnectingHeadset(port: server.port, deviceId: 'quest-$i', sessionId: 'session-$i'),
    ];
    addTearDown(() => Future.wait(headsets.map((h) => h.close())));
    PhantomLiveRepository? repositoryOf(ReconnectingHeadset h) => container.read(phantomLiveRepositoryProvider(phantomDeviceKey(h.deviceId)));

    for (final h in headsets) {
      await h.connect();
      h.startStatuses((n) => phantomStatus(n: n, phase: 'witness'));
    }
    await eventually(() => headsets.every((h) => repositoryOf(h) != null), 'a repository for every headset');

    // Heard from one after the other, in this order: each card shows its headset's witness, then goes quiet.
    for (final h in headsets) {
      final model = PhantomLiveModel();
      final cardOfHeadset = repositoryOf(h)!.watch().listen(model.apply);
      await eventually(() => model.latest != null, 'the card of ${h.deviceId}');
      h.event(demoEvent('witness_summary'));
      await eventually(() => model.witness != null, "the witness on ${h.deviceId}'s card");
      h.stopStatuses();
      await cardOfHeadset.cancel();
    }

    /// The headset loses its link and dials in again: the model of its new card.
    Future<PhantomLiveModel> reconnected(ReconnectingHeadset h) async {
      final before = repositoryOf(h);
      h.drop();
      await h.connect(resumeFromSeq: h.lastRecvSeqFromHub);
      h.startStatuses((n) => phantomStatus(n: n, phase: 'witness'));
      await eventually(() => repositoryOf(h) != null && !identical(repositoryOf(h), before), 'a repository on the new link of ${h.deviceId}');
      final model = PhantomLiveModel();
      addTearDown(repositoryOf(h)!.watch().listen(model.apply).cancel);
      await pumpEventQueue();
      return model;
    }

    // The forgotten one comes last: its new card puts an entry in, which would push out the next-oldest.
    expect((await reconnected(headsets[1])).witness, isNotNull, reason: 'the second-oldest of nine was kept');
    expect((await reconnected(headsets[8])).witness, isNotNull, reason: 'the newest of nine was kept');
    expect((await reconnected(headsets[0])).witness, isNull, reason: 'the oldest of nine was forgotten');
  });
}

/// [witness] holds the numbers and closing lines of the recorded `witness_summary` [event].
void _expectWitness(PhantomWitness? witness, Map<String, dynamic> event, String when) {
  expect(witness, isNotNull, reason: 'no witness numbers on the card $when');
  final data = event['data'] as Map<String, dynamic>;
  final sync = data['sync'] as Map<String, dynamic>;
  final async = data['async'] as Map<String, dynamic>;
  expect(witness!.conditionOrder, ['async', 'sync'], reason: when);
  expect(witness.sync.driftChangeCm, sync['drift_change_cm'], reason: when);
  expect(witness.async.driftChangeCm, async['drift_change_cm'], reason: when);
  expect(witness.sync.flinchLatencyMs, sync['flinch_latency_ms'], reason: when);
  expect(witness.async.flinchLatencyMs, async['flinch_latency_ms'], reason: when);
  expect(witness.sync.ownership, sync['ownership'], reason: when);
  expect(witness.async.ownership, async['ownership'], reason: when);
  expect(witness.closingEn, data['closing_en'], reason: 'closing lines (English) $when');
  expect(witness.closingHi, data['closing_hi'], reason: 'closing lines (Hindi) $when');
}

/// The card's trace has each recorded stone / burst of [events] once, at its time.
void _expectMarkers(PhantomLiveModel model, List<Map<String, dynamic>> events, String when) {
  final markers = model.buffer.markers;
  final kinds = {'threat_impact': TraceMarkerKind.threatImpact, 'emg_burst': TraceMarkerKind.emgBurst};
  for (final event in events) {
    final kind = kinds[event['type']]!;
    final ofKind = markers.where((m) => m.kind == kind).toList();
    expect(ofKind, hasLength(1), reason: '${kind.name} markers on the card $when (all: ${markers.map((m) => m.kind.name).toList()})');
    expect(ofKind.single.tMs, event['t_ms'], reason: '${kind.name} time $when');
  }
  expect(markers, hasLength(events.length), reason: 'markers on the card $when');
}
