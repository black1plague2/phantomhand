import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'package:opus_app/core/providers/hub_providers.dart';
import 'package:opus_app/core/providers/repository_providers.dart';
import 'package:opus_app/core/providers/settings_providers.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/game_manifest.dart';
import 'package:opus_app/data/models/program.dart';
import 'package:opus_app/shared/widgets/dynamic_form/dynamic_form.dart';
import 'package:opus_app/shared/widgets/dynamic_form/dynamic_form_controller.dart';
import 'package:opus_app/shared/design/v2_colors.dart';
import 'package:opus_app/shared/widgets/error_retry_view.dart';
import 'package:opus_app/shared/widgets/section.dart';

/// Design v2 §5 "Programs": title "Programs"; a builder form **grouped into
/// sections by param group** (Task, Difficulty, Feedback, Safety -- whatever
/// the manifest declares), each field a plain label + control, and **one**
/// primary action, "Send to headset".
///
/// A3 run1 deleted the three-pane workstation layout (game library / block
/// list / form), the live workspace preview, the preset chips and the
/// separate Save button. What is left is one scrolling column at every width.
/// The form itself is still driven entirely by the manifest's `paramSchema`
/// through [DynamicForm] -- zero game-specific widgets (GOAL.md G1): adding a
/// game to `MockManifestsRepository.knownGameIds` is still enough for it to
/// appear here, with all of its own param groups.
class ProgramBuilderScreen extends ConsumerStatefulWidget {
  const new({required this.patientId, super.key});
  final String patientId;

  @override
  ConsumerState<ProgramBuilderScreen> createState() => _ProgramBuilderScreenState();
}

class _ProgramBuilderScreenState extends ConsumerState<ProgramBuilderScreen> {
  GameManifest? _manifest;
  DynamicFormController? _controller;
  int _sessionsPerWeek = 3;
  int _weeks = 6;
  String? _selectedDeviceId;
  bool _sending = false;

  /// RUN 18 leftover (Opus): the manifest's `easy`/`medium`/`hard` presets
  /// were never surfaced in the UI. Tracks which tile is highlighted; `null`
  /// until the clinician taps one (no level implied until chosen).
  String? _selectedPresetId;

  @override
  void dispose() {
    _controller?.dispose();
    super.dispose();
  }

  void _selectManifest(GameManifest m) {
    _controller?.dispose();
    _manifest = m;
    _controller = DynamicFormController(paramSchema: m.paramSchema);
    _selectedPresetId = null;
  }

  void _applyPreset(GamePreset preset) {
    setState(() {
      _controller!.applyPreset(preset.params);
      _selectedPresetId = preset.id;
    });
  }

  @override
  Widget build(BuildContext context) {
    final manifestsAsync = ref.watch(manifestsListProvider);

    return Scaffold(
      backgroundColor: V2Colors.black,
      appBar: AppBar(
        backgroundColor: V2Colors.black,
        foregroundColor: V2Colors.text,
        title: const Text('Programs'),
      ),
      body: manifestsAsync.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (e, _) => ErrorRetryView(
          message: e.toString(),
          onRetry: () => ref.invalidate(manifestsListProvider),
        ),
        data: (manifests) {
          if (manifests.isEmpty) {
            return const Center(
              child: Text('No games available.', style: TextStyle(color: V2Colors.textDim)),
            );
          }
          if (_manifest == null) _selectManifest(manifests.first);
          final controller = _controller!;
          return ListView(
            padding: const EdgeInsets.all(16),
            children: [
              Section(
                title: 'Game',
                child: DropdownButtonFormField<String>(
                  initialValue: _manifest!.id,
                  isExpanded: true,
                  decoration: const InputDecoration(isDense: true),
                  items: [
                    for (final m in manifests)
                      DropdownMenuItem(value: m.id, child: Text(m.nameFor('en'), overflow: TextOverflow.ellipsis)),
                  ],
                  onChanged: (id) {
                    if (id == null) return;
                    setState(() => _selectManifest(manifests.firstWhere((m) => m.id == id)));
                  },
                ),
              ),
              if (_manifest!.presets.isNotEmpty) ...[
                const SizedBox(height: 16),
                Section(
                  title: 'Level',
                  child: Row(
                    children: [
                      for (final preset in _manifest!.presets) ...[
                        if (preset != _manifest!.presets.first) const SizedBox(width: 8),
                        Expanded(
                          child: _LevelTile(
                            label: preset.label['en'] as String? ?? preset.id,
                            selected: preset.id == _selectedPresetId,
                            onTap: () => _applyPreset(preset),
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
              const SizedBox(height: 16),
              // One Section per manifest param group, rendered by the
              // schema-driven form engine.
              DynamicForm(
                key: ValueKey(_manifest!.id),
                controller: controller,
                locale: ref.watch(appLocaleProvider).languageCode,
              ),
              const SizedBox(height: 16),
              Section(
                title: 'Schedule',
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    _PlainStepper(
                      label: 'Sessions per week',
                      value: _sessionsPerWeek,
                      min: 1,
                      max: 7,
                      onChanged: (v) => setState(() => _sessionsPerWeek = v),
                    ),
                    _PlainStepper(
                      label: 'Weeks',
                      value: _weeks,
                      min: 1,
                      max: 12,
                      onChanged: (v) => setState(() => _weeks = v),
                    ),
                  ],
                ),
              ),
              if (hubCapable) ...[
                const SizedBox(height: 16),
                _HeadsetSection(
                  selectedDeviceId: _selectedDeviceId,
                  onDeviceChanged: (id) => setState(() => _selectedDeviceId = id),
                ),
              ],
              const SizedBox(height: 16),
              // §4: ONE primary action, filled oxblood, full width.
              FilledButton(
                style: FilledButton.styleFrom(
                  backgroundColor: V2Colors.oxblood,
                  foregroundColor: V2Colors.text,
                  minimumSize: const Size.fromHeight(48),
                ),
                onPressed: _sending ? null : () => _sendToHeadset(context),
                child: Text(_sending ? 'Sending...' : 'Send to headset'),
              ),
            ],
          );
        },
      ),
    );
  }

  Program _buildProgram() => Program(
        programId: '',
        patientRef: widget.patientId,
        createdBy: 'mock-clinician',
        title: 'New program',
        schedule: ProgramSchedule(startDate: DateTime.now(), sessionsPerWeek: _sessionsPerWeek),
        blocks: [
          ProgramBlock(
            gameId: _manifest!.id,
            gameVersionRange: '^${_manifest!.version}',
            params: _controller!.values,
            durationSec: 300,
            restAfterSec: 60,
          ),
        ],
        status: ProgramStatus.active,
      );

  /// The screen's single primary action. Saves the program, then sends it to
  /// the selected headset when one is connected. One action, one result line.
  Future<void> _sendToHeadset(BuildContext context) async {
    if (!_controller!.isValid) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Fix the highlighted fields first.')),
      );
      return;
    }
    setState(() => _sending = true);
    final program = _buildProgram();
    await ref.read(programsRepositoryProvider).saveProgram(program);

    final deviceId = _selectedDeviceId;
    if (deviceId == null) {
      if (!context.mounted) return;
      setState(() => _sending = false);
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Program saved. No headset connected.')),
      );
      return;
    }
    final ok = await ref.read(hubControllerProvider.notifier).assignProgram(
          deviceId,
          program: program.toJson(),
          patientRef: widget.patientId,
          manifests: [_manifest!.toJson()],
        );
    if (!context.mounted) return;
    setState(() => _sending = false);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(content: Text(ok ? 'Program sent to headset' : "Headset didn't confirm. Try again.")),
    );
  }
}

/// One "Level" tile (Easy / Medium / Hard, from the manifest's own
/// `presets` -- never hardcoded per-game, per GOAL.md G1). Tapping applies
/// `controller.applyPreset(preset.params)`. Selected = filled oxblood
/// (matches the single-primary-action fill elsewhere), unselected =
/// `panelRaised` with a plain border.
class _LevelTile extends StatelessWidget {
  const _LevelTile({required this.label, required this.selected, required this.onTap});

  final String label;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: selected ? V2Colors.oxblood : V2Colors.panelRaised,
      borderRadius: BorderRadius.circular(8),
      child: InkWell(
        borderRadius: BorderRadius.circular(8),
        onTap: onTap,
        child: Container(
          constraints: const BoxConstraints(minHeight: 48),
          alignment: Alignment.center,
          padding: const EdgeInsets.symmetric(vertical: 12),
          child: Text(
            label,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                  color: V2Colors.text,
                  fontWeight: selected ? FontWeight.w700 : FontWeight.w400,
                ),
          ),
        ),
      ),
    );
  }
}

/// §5 "each field a plain label + control" -- a label and a -/value/+ control,
/// the same shape the dynamic form's own stepper fields use, so Schedule
/// doesn't look like a different kind of form from the manifest's own groups.
class _PlainStepper extends StatelessWidget {
  const new({
    required this.label,
    required this.value,
    required this.min,
    required this.max,
    required this.onChanged,
  });

  final String label;
  final int value;
  final int min;
  final int max;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: Theme.of(context).textTheme.bodyMedium),
          Row(
            children: [
              IconButton(
                constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
                icon: const Icon(Icons.remove_circle_outline),
                onPressed: value <= min ? null : () => onChanged(value - 1),
              ),
              SizedBox(width: 48, child: Text('$value', textAlign: TextAlign.center)),
              IconButton(
                constraints: const BoxConstraints(minWidth: 48, minHeight: 48),
                icon: const Icon(Icons.add_circle_outline),
                onPressed: value >= max ? null : () => onChanged(value + 1),
              ),
            ],
          ),
        ],
      ),
    );
  }
}

/// A headset picker limited to currently-connected devices (one that isn't
/// reachable can't ack, so it isn't offered). Empty state is one short line.
class _HeadsetSection extends ConsumerWidget {
  const new({required this.selectedDeviceId, required this.onDeviceChanged});

  final String? selectedDeviceId;
  final ValueChanged<String?> onDeviceChanged;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final hub = ref.watch(hubControllerProvider);
    final headsets = hub is HubRunning ? hub.headsets.where((h) => h.connected).toList() : const <HeadsetInfo>[];
    return Section(
      title: 'Headset',
      child: headsets.isEmpty
          ? const Text('No headset connected.', style: TextStyle(color: V2Colors.textDim))
          : DropdownButtonFormField<String>(
              initialValue: headsets.any((h) => h.deviceId == selectedDeviceId) ? selectedDeviceId : null,
              isExpanded: true,
              decoration: const InputDecoration(isDense: true),
              items: [
                for (final h in headsets)
                  DropdownMenuItem(value: h.deviceId, child: Text(h.deviceId, overflow: TextOverflow.ellipsis)),
              ],
              onChanged: onDeviceChanged,
            ),
    );
  }
}

/// The program builder's "live preview of the target workspace (the same
/// top-down view as the reach trace) that updates as ranges change"
/// (`docs/APP_DESIGN.md`). Draws the workspace arc plus a wedge for the
/// param schema's `reachPercent`/`azimuthRangeDeg` ranges when the selected
/// block's manifest exposes them (`orchard_reach`-shaped params, per
/// `contracts/fixtures/orchard_reach.manifest.json`'s actual field names --
/// see the fix note below), plus `elevationRangeDeg`/`neglectBias` as text
/// annotations; otherwise shows a neutral placeholder rather than guessing
/// at an unknown game's geometry.
///
/// **Bug fix (`docs/IMPROVEMENT_BRIEF.md` §3 item 10):** this previously
/// read `params['azimuth_range_deg']` and `params['reach_percent_range']` --
/// snake_case keys that don't exist anywhere in the real manifest's
/// `paramSchema` (verified directly against
/// `contracts/fixtures/orchard_reach.manifest.json`: the actual property
/// names are camelCase `azimuthRangeDeg` and `reachPercent`). That meant the
/// preview was *always* stuck on the "no workspace geometry" placeholder for
/// the one real game manifest that has this geometry, silently never
/// updating no matter what the clinician set the ranges to. Fixed to read
/// the real keys, and extended to also read `elevationRangeDeg` and
/// `neglectBias` (the other two fields item 10 names) as text annotations
/// below the top-down wedge -- elevation is a vertical (up/down) range that
/// a top-down 2D view can't honestly depict as geometry without implying a
/// 3D projection the drawing doesn't actually do, and neglect bias is a
/// scalar skew, not a shape, so both are stated in words instead.
class WorkspacePreview extends StatelessWidget {
  const new({required this.params, super.key});
  final Map<String, dynamic> params;

  @override
  Widget build(BuildContext context) {
    final t = Theme.of(context).extension<OpusTokens>() ?? OpusTokens.light;
    final azimuthRange = (params['azimuthRangeDeg'] as List?)?.cast<num>();
    final reachRange = (params['reachPercent'] as List?)?.cast<num>();
    final elevationRange = (params['elevationRangeDeg'] as List?)?.cast<num>();
    final neglectBias = (params['neglectBias'] as num?)?.toDouble();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          height: 200,
          width: double.infinity,
          decoration: BoxDecoration(border: Border.all(color: t.rule), borderRadius: BorderRadius.circular(16)),
          child: azimuthRange == null || reachRange == null
              ? Center(
                  child: Text(
                    "No workspace geometry in this game's parameters.",
                    style: TextStyle(color: t.slate),
                    textAlign: TextAlign.center,
                  ),
                )
              : CustomPaint(
                  painter: WorkspaceWedgePainter(
                    azimuthRange: (azimuthRange[0].toDouble(), azimuthRange[1].toDouble()),
                    // `reachPercent` is a min/max BAND of the patient's arm
                    // length (e.g. [50, 85], not "0 to 85") -- drawn as an
                    // annulus between the two radii, not a wedge from the
                    // center, so a preview with a raised minimum honestly
                    // shows the near targets as excluded.
                    reachRange: (reachRange[0].toDouble() / 110, reachRange[1].toDouble() / 110),
                    rule: t.rule,
                    lake: t.lake,
                    neglectBias: neglectBias,
                    ochre: t.ochre,
                  ),
                  child: const SizedBox.expand(),
                ),
        ),
        if (elevationRange != null || neglectBias != null) ...[
          const SizedBox(height: 8),
          if (elevationRange != null)
            Text(
              'Elevation ${elevationRange[0]}° to ${elevationRange[1]}° (not shown in this top-down view)',
              style: TextStyle(color: t.slate, fontSize: 12),
            ),
          if (neglectBias != null)
            Text(
              neglectBias == 0
                  ? 'Neglect bias: none (even left/right target mix)'
                  : 'Neglect bias: ${neglectBias.toStringAsFixed(2)} '
                      '(more targets on the ${neglectBias < 0 ? 'left' : 'right'})',
              style: TextStyle(color: t.slate, fontSize: 12),
            ),
        ],
      ],
    );
  }
}

class WorkspaceWedgePainter extends CustomPainter {
  new({
    required this.azimuthRange,
    required this.reachRange,
    required this.rule,
    required this.lake,
    required this.ochre,
    this.neglectBias,
  });
  final (double, double) azimuthRange;
  final (double, double) reachRange;
  final Color rule;
  final Color lake;
  final Color ochre;
  final double? neglectBias;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height); // patient at bottom-centre
    final radius = size.height * 0.9;

    final arcPaint = Paint()
      ..color = rule
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    canvas.drawArc(Rect.fromCircle(center: center, radius: radius), -math.pi, math.pi, false, arcPaint);

    // Azimuth: 0 = straight ahead, negative = left, positive = right (same
    // convention as the reach-trace/target model elsewhere in the app).
    final startAngle = -math.pi / 2 + azimuthRange.$1 * math.pi / 180;
    final sweep = (azimuthRange.$2 - azimuthRange.$1) * math.pi / 180;
    // The reachable band is an ANNULUS between the min and max reach radii
    // (see `WorkspacePreview`'s doc), not a wedge from the center -- built
    // as outer-arc-forward + inner-arc-backward so the inner radius is
    // genuinely excluded, not just visually implied.
    final outerRadius = radius * reachRange.$2;
    final innerRadius = radius * reachRange.$1;
    final bandPaint = Paint()
      ..color = lake.withValues(alpha: 0.18)
      ..style = PaintingStyle.fill;
    final path = Path()
      ..arcTo(Rect.fromCircle(center: center, radius: outerRadius), startAngle, sweep, true)
      ..arcTo(Rect.fromCircle(center: center, radius: innerRadius), startAngle + sweep, -sweep, false)
      ..close();
    canvas.drawPath(path, bandPaint);

    // Neglect bias: a translucent tint over the half of the wedge on the
    // biased side, in addition to (never instead of) the text label above --
    // color is never the only carrier of meaning here.
    final bias = neglectBias;
    if (bias != null && bias != 0) {
      final biasPaint = Paint()..color = ochre.withValues(alpha: 0.12 * bias.abs().clamp(0, 1));
      final mid = startAngle + sweep / 2;
      final biasStart = bias < 0 ? startAngle : mid;
      final biasSweep = sweep / 2;
      final biasPath = Path()
        ..arcTo(Rect.fromCircle(center: center, radius: outerRadius), biasStart, biasSweep, true)
        ..arcTo(Rect.fromCircle(center: center, radius: innerRadius), biasStart + biasSweep, -biasSweep, false)
        ..close();
      canvas.drawPath(biasPath, biasPaint);
    }
  }

  @override
  bool shouldRepaint(covariant WorkspaceWedgePainter oldDelegate) =>
      oldDelegate.azimuthRange != azimuthRange ||
      oldDelegate.reachRange != reachRange ||
      oldDelegate.neglectBias != neglectBias;
}

final manifestsListProvider = FutureProvider<List<GameManifest>>(
  (ref) => ref.watch(manifestsRepositoryProvider).listManifests(),
);
