// B7 (R3 D1): the Monitor tab must show the Phantom Hand card whenever a
// connected headset reports Phantom Hand state, not only while it has an
// `activeSessionId`. The real runner (`OpusSessionRunner.cs`) sends no session
// id before the first Start and clears it when a run has finished, so the card
// (and with it Start and Next person) used to vanish exactly when needed.
//
// The headsets here are real `HubConnection`s over a `FakeLiveSocket`, so the
// session id follows the same envelope rules as on the wire
// (`HubConnection._onData`); only the `HubController` (which would bind port
// 8787 and need path_provider) is replaced.
import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/core/hub/hub_connection.dart';
import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/data/models/phantom_live.dart';
import 'package:opus_app/data/repositories/phantom_live_repository.dart';
import 'package:opus_app/features/live/live_monitor_screen.dart';
import 'package:opus_app/features/live/monitor_home_screen.dart';
import 'package:opus_app/features/live/phantom_live_providers.dart';
import 'package:opus_app/features/live/phantom_live_screen.dart';
import 'package:opus_app/l10n/app_localizations.dart';

import '../hub/fake_live_socket.dart';

/// One simulated headset: a real [HubConnection] fed through a fake socket.
class _Headset {
  new(this.deviceId) : socket = FakeLiveSocket() {
    connection = HubConnection(
      deviceId: deviceId,
      socket: socket,
      pairToken: '123456',
      incoming: socket.incoming,
      pingInterval: const Duration(hours: 1), // no heartbeat traffic in these tests
    );
  }

  final String deviceId;
  final FakeLiveSocket socket;
  late final HubConnection connection;

  /// False once the link is gone: the hub keeps only a "disconnected" row.
  bool up = true;
  int _n = 0;

  void status(Map<String, dynamic> payload, {String? sessionId}) {
    socket.receive(headsetMessage(type: 'status', id: '$deviceId-${_n++}', seq: _n, payload: payload, sessionId: sessionId));
  }

  /// Acknowledges a message the hub sent (an entry of `socket.sent`).
  void ack(Map<String, dynamic> sent) {
    socket.receive(headsetMessage(type: 'ack', id: '$deviceId-${_n++}', seq: _n, payload: {'ack_id': sent['id'], 'ok': true}));
  }
}

/// The Phantom Hand status as `OpusSessionRunner.BuildStatus` + `FillStatus`
/// build it: `game_id`, `game_state` always present, phase `idle` until the first Start.
Map<String, dynamic> _phStatus({
  String state = 'ready',
  String phase = 'idle',
  String? condition,
  num? remaining,
  bool haptic = true,
  bool bio = true,
}) =>
    {
      'state': state,
      'game_id': 'phantom_hand',
      'game_state': {
        'phase': phase,
        'condition': condition,
        'remaining_s': remaining,
        'nodes': {
          'haptic': {'connected': haptic},
          'bio': {'connected': bio, 'emg_level': null},
        },
      },
    };

/// A trial-based (Orchard) status: no `game_id`, no `game_state`.
Map<String, dynamic> _orchardStatus({String state = 'running'}) => {
      'state': state,
      'trial': 2,
      'trials_completed': 1,
      'trials_total': 10,
    };

/// Replaces the real controller: no server, no path_provider, no port. The
/// answers mirror what `HubController` derives from its connections.
class _FakeHub extends HubController {
  new(this.headsets);
  final List<_Headset> headsets;

  HubControllerState _snapshot() => HubControllerState.running(
        hubId: 'test-hub',
        port: 8797,
        headsets: [
          for (final h in headsets)
            HeadsetInfo(deviceId: h.deviceId, rttMs: null, status: h.connection.lastStatus, connected: h.up),
        ],
      );

  @override
  HubControllerState build() => _snapshot();

  /// What `HubController._emit` does after every status / trial event.
  void refresh() => state = _snapshot();

  @override
  Future<void> start({int port = 8787, int beaconPort = 8788}) async {}

  Iterable<HubConnection> get _up => [
        for (final h in headsets)
          if (h.up) h.connection,
      ];

  @override
  HubConnection? connectionFor(String deviceId) => _up.where((c) => c.deviceId == deviceId).firstOrNull;

  @override
  HubConnection? connectionForSession(String sessionId) => _up.where((c) => c.activeSessionId == sessionId).firstOrNull;

  @override
  Set<String> allRunningSessionIds() => {
        for (final c in _up)
          if (c.activeSessionId != null) c.activeSessionId!,
      };

  @override
  String? patientRefForSession(String sessionId) => null;
}

/// Headsets opened by the running test. Their ping timers are cancelled in the
/// test body (see [_hubTest]): a pending timer fails the invariant check that
/// runs before `addTearDown` callbacks.
final _opened = <_Headset>[];

_Headset _headset(String deviceId) {
  final h = _Headset(deviceId);
  _opened.add(h);
  return h;
}

void _hubTest(String description, Future<void> Function(WidgetTester tester) body) {
  testWidgets(description, (tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(() {
      tester.view.resetPhysicalSize();
      tester.view.resetDevicePixelRatio();
    });
    try {
      await body(tester);
    } finally {
      await tester.pumpWidget(const SizedBox());
      for (final h in _opened) {
        unawaited(h.connection.close());
      }
      _opened.clear();
      await tester.pump();
    }
  });
}

Widget _host(Widget home) => MaterialApp(
      theme: AppTheme.dark(),
      localizationsDelegates: const [
        ...AppLocalizations.localizationsDelegates,
        GlobalMaterialLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
      ],
      supportedLocales: AppLocalizations.supportedLocales,
      home: home,
    );

/// Pumps the real Monitor tab over [headsets] and returns the fake hub.
Future<_FakeHub> _pumpMonitor(WidgetTester tester, List<_Headset> headsets) async {
  final hub = _FakeHub(headsets);
  await tester.pumpWidget(
    ProviderScope(
      overrides: [hubControllerProvider.overrideWith(() => hub)],
      child: _host(const MonitorHomeScreen()),
    ),
  );
  await tester.pump();
  return hub;
}

/// Delivers [payload] from [headset] and lets the hub state republish, as the real hub does.
Future<void> _report(WidgetTester tester, _FakeHub hub, _Headset headset, Map<String, dynamic> payload, {String? sessionId}) async {
  headset.status(payload, sessionId: sessionId);
  await tester.pump();
  hub.refresh();
  await tester.pump();
  // A card mounted by that refresh gets its first snapshot (emitted from the
  // stream's onListen) one microtask later.
  await tester.pump();
}

bool _enabled(WidgetTester tester, PhantomCommand c) {
  final w = tester.widget(find.byKey(ValueKey('ph-cmd-${c.wire}')));
  if (w is ButtonStyleButton) return w.onPressed != null;
  throw StateError('not a button: $w');
}

void main() {
  _hubTest('D1: a connected Phantom Hand headset with NO session id still gets the card, with Start live', (tester) async {
    final quest = _headset('quest-1');
    final hub = await _pumpMonitor(tester, [quest]);
    await _report(tester, hub, quest, _phStatus());

    // Precondition = the defect: the headset has reported, but has no session id yet.
    expect(quest.connection.lastStatus, isNotNull);
    expect(quest.connection.activeSessionId, isNull);

    expect(find.text('No headset connected'), findsNothing);
    expect(find.byType(PhantomLiveScreen), findsOneWidget);
    expect(find.text('Waiting to start'), findsOneWidget);
    expect(_enabled(tester, PhantomCommand.start), isTrue);
  });

  _hubTest('Start from that card reaches the headset as a start command', (tester) async {
    final quest = _headset('quest-1');
    final hub = await _pumpMonitor(tester, [quest]);
    await _report(tester, hub, quest, _phStatus());

    await tester.tap(find.byKey(const ValueKey('ph-cmd-start')));
    await tester.pump();
    final commands = quest.socket.sent.where((m) => m['type'] == 'command').toList();
    expect(commands, hasLength(1));
    expect((commands.single['payload'] as Map)['command'], 'start');
    quest.ack(commands.single);
    await tester.pump(const Duration(seconds: 5)); // lets the ack-wait timers run out
    expect(find.text('Confirmed'), findsOneWidget);
  });

  _hubTest('the card stays through the whole run and after it has finished: Next person is reachable', (tester) async {
    final quest = _headset('quest-1');
    final hub = await _pumpMonitor(tester, [quest]);
    await _report(tester, hub, quest, _phStatus());
    expect(find.byType(PhantomLiveScreen), findsOneWidget);

    // Start: the runner now stamps the session id on its statuses.
    await _report(
      tester,
      hub,
      quest,
      _phStatus(state: 'running', phase: 'induction', condition: 'async', remaining: 40),
      sessionId: 'session-1',
    );
    expect(quest.connection.activeSessionId, 'session-1');
    expect(find.byType(PhantomLiveScreen), findsOneWidget);
    expect(find.text('Brush and touch'), findsOneWidget);

    // The run is over: the hub clears the session id (idle / finished / error).
    await _report(tester, hub, quest, _phStatus(state: 'finished', phase: 'done'), sessionId: 'session-1');
    expect(quest.connection.activeSessionId, isNull);
    expect(find.text('No headset connected'), findsNothing);
    expect(find.byType(PhantomLiveScreen), findsOneWidget);
    expect(find.text('Done'), findsOneWidget);
    expect(_enabled(tester, PhantomCommand.nextPerson), isTrue);

    await tester.tap(find.byKey(const ValueKey('ph-cmd-next_person')));
    await tester.pump();
    final last = quest.socket.sent.lastWhere((m) => m['type'] == 'command');
    expect((last['payload'] as Map)['command'], 'next_person');
    quest.ack(last);
    await tester.pump(const Duration(seconds: 5));
  });

  _hubTest('the card keeps its state (and so its traces) across the idle, running and finished flips', (tester) async {
    final quest = _headset('quest-1');
    final hub = await _pumpMonitor(tester, [quest]);
    await _report(tester, hub, quest, _phStatus());
    final first = tester.state(find.byType(PhantomLiveScreen));
    await _report(tester, hub, quest, _phStatus(state: 'running', phase: 'induction', condition: 'sync'), sessionId: 'session-1');
    await _report(tester, hub, quest, _phStatus(state: 'finished', phase: 'done'), sessionId: 'session-1');
    expect(tester.state(find.byType(PhantomLiveScreen)), same(first));
  });

  _hubTest('no status yet, or the headset is gone: still the empty state with the address', (tester) async {
    final quest = _headset('quest-1');
    final hub = await _pumpMonitor(tester, [quest]);
    expect(find.text('No headset connected'), findsOneWidget, reason: 'connected, but it has not reported a status');

    await _report(tester, hub, quest, _phStatus());
    expect(find.byType(PhantomLiveScreen), findsOneWidget);

    quest.up = false; // the link dropped; the hub keeps a "disconnected" row with the last status
    hub.refresh();
    await tester.pump();
    expect(find.byType(PhantomLiveScreen), findsNothing);
    expect(find.text('No headset connected'), findsOneWidget);
  });

  _hubTest('Orchard is unchanged: a trial headset with a session opens the trial monitor, one without shows the empty state', (tester) async {
    final orchard = _headset('orchard-1');
    final hub = await _pumpMonitor(tester, [orchard]);

    await _report(tester, hub, orchard, _orchardStatus(state: 'ready'));
    expect(find.byType(PhantomLiveScreen), findsNothing);
    expect(find.text('No headset connected'), findsOneWidget, reason: 'no session id, as before');

    await _report(tester, hub, orchard, _orchardStatus(), sessionId: 'orchard-session');
    expect(find.byType(PhantomLiveScreen), findsNothing);
    expect(find.byType(LiveMonitorScreen), findsOneWidget);
    expect(find.text('No headset connected'), findsNothing);
  });

  _hubTest('with an Orchard and a Phantom Hand headset connected, the Phantom Hand card is shown', (tester) async {
    final orchard = _headset('orchard-1');
    final quest = _headset('quest-1');
    final hub = await _pumpMonitor(tester, [orchard, quest]);
    await _report(tester, hub, orchard, _orchardStatus(), sessionId: 'orchard-session');
    await _report(tester, hub, quest, _phStatus());
    expect(find.byType(PhantomLiveScreen), findsOneWidget);
  });

  group('phantomLiveRepositoryProvider with a headset key', () {
    Future<(ProviderContainer, _FakeHub, _Headset)> boot(WidgetTester tester, Map<String, dynamic> payload) async {
      final quest = _headset('quest-1');
      final hub = _FakeHub([quest]);
      final container = ProviderContainer(overrides: [hubControllerProvider.overrideWith(() => hub)]);
      addTearDown(container.dispose);
      container.read(hubControllerProvider); // build the (fake) controller
      quest.status(payload);
      await tester.pump();
      hub.refresh();
      return (container, hub, quest);
    }

    _hubTest('one repository per connection, whatever the session id does', (tester) async {
      final (container, hub, quest) = await boot(tester, _phStatus());
      final key = phantomDeviceKey('quest-1');
      final repo = container.read(phantomLiveRepositoryProvider(key));
      expect(repo, isA<PhantomLiveRepository>());
      quest.status(_phStatus(state: 'running', phase: 'induction'), sessionId: 's');
      await tester.pump();
      hub.refresh();
      expect(container.read(phantomLiveRepositoryProvider(key)), same(repo));
      expect(container.read(phantomLiveRepositoryProvider('s')), same(repo), reason: 'the session key still resolves to it');
    });

    _hubTest('no repository for a trial-based status or an unknown headset', (tester) async {
      final (container, _, _) = await boot(tester, _orchardStatus());
      expect(container.read(phantomLiveRepositoryProvider(phantomDeviceKey('quest-1'))), isNull);
      expect(container.read(phantomLiveRepositoryProvider(phantomDeviceKey('nobody'))), isNull);
    });
  });
}
