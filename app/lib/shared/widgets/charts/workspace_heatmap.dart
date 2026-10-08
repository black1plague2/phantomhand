import 'package:flutter/material.dart';

/// One reach trial's location + outcome, enough to bin it into the heatmap.
class HeatmapTrial {
  const new({required this.azimuthDeg, required this.elevationDeg, required this.success, this.hand});
  final double azimuthDeg;
  final double elevationDeg;
  final bool success;
  final String? hand;
}

/// Azimuth x elevation success-rate grid, split left/right hand -- brief A5
/// "workspace heatmap (L/R)". Bins the raw per-trial target positions
/// (`events.ndjson` / `metrics.json#trials[].target`) client-side rather than
/// depending on `metrics.json#session.workspace_heatmap` directly, since that
/// field's exact binning isn't finalized in the contract yet (kept generic on
/// purpose -- documented as an open question in the session log).
class WorkspaceHeatmap extends StatelessWidget {
  const new({required this.trials, this.azimuthBins = 6, this.elevationBins = 4, super.key});

  final List<HeatmapTrial> trials;
  final int azimuthBins;
  final int elevationBins;

  @override
  Widget build(BuildContext context) {
    final left = trials.where((t) => t.hand == 'left').toList();
    final right = trials.where((t) => t.hand == 'right').toList();
    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Expanded(child: _SideGrid(label: 'Left', trials: left, azimuthBins: azimuthBins, elevationBins: elevationBins)),
        const SizedBox(width: 16),
        Expanded(child: _SideGrid(label: 'Right', trials: right, azimuthBins: azimuthBins, elevationBins: elevationBins)),
      ],
    );
  }
}

class _SideGrid extends StatelessWidget {
  const new({required this.label, required this.trials, required this.azimuthBins, required this.elevationBins});
  final String label;
  final List<HeatmapTrial> trials;
  final int azimuthBins;
  final int elevationBins;

  @override
  Widget build(BuildContext context) {
    if (trials.isEmpty) {
      return Column(children: [Text(label), const SizedBox(height: 8), const Text('No trials', style: TextStyle(color: Colors.grey))]);
    }
    final minAz = trials.map((t) => t.azimuthDeg).reduce((a, b) => a < b ? a : b);
    final maxAz = trials.map((t) => t.azimuthDeg).reduce((a, b) => a > b ? a : b);
    final minEl = trials.map((t) => t.elevationDeg).reduce((a, b) => a < b ? a : b);
    final maxEl = trials.map((t) => t.elevationDeg).reduce((a, b) => a > b ? a : b);
    final azSpan = (maxAz - minAz).abs() < 1e-6 ? 1.0 : maxAz - minAz;
    final elSpan = (maxEl - minEl).abs() < 1e-6 ? 1.0 : maxEl - minEl;

    // successCount/total per bin, bins indexed [elevation][azimuth] so row 0 is
    // the top (highest elevation) visually.
    final counts = List.generate(elevationBins, (_) => List.filled(azimuthBins, 0));
    final successes = List.generate(elevationBins, (_) => List.filled(azimuthBins, 0));
    for (final t in trials) {
      final azBin = ((t.azimuthDeg - minAz) / azSpan * azimuthBins).floor().clamp(0, azimuthBins - 1);
      var elBin = ((t.elevationDeg - minEl) / elSpan * elevationBins).floor().clamp(0, elevationBins - 1);
      elBin = elevationBins - 1 - elBin; // flip so higher elevation renders higher on screen
      counts[elBin][azBin]++;
      if (t.success) successes[elBin][azBin]++;
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(label, textAlign: TextAlign.center, style: Theme.of(context).textTheme.labelLarge),
        const SizedBox(height: 8),
        AspectRatio(
          aspectRatio: azimuthBins / elevationBins,
          child: Column(
            children: [
              for (var r = 0; r < elevationBins; r++)
                Expanded(
                  child: Row(
                    children: [
                      for (var c = 0; c < azimuthBins; c++)
                        Expanded(
                          child: _Cell(count: counts[r][c], successRate: counts[r][c] == 0 ? null : successes[r][c] / counts[r][c]),
                        ),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ],
    );
  }
}

class _Cell extends StatelessWidget {
  const new({required this.count, required this.successRate});
  final int count;
  final double? successRate;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final color = successRate == null
        ? scheme.surfaceContainerHighest
        : Color.lerp(scheme.errorContainer, scheme.primary, successRate!);
    return Tooltip(
      message: successRate == null ? 'No trials' : '${(successRate! * 100).toStringAsFixed(0)}% success ($count trials)',
      child: Container(
        margin: const EdgeInsets.all(1),
        color: color,
      ),
    );
  }
}
