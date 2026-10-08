import 'package:flutter/material.dart';

import 'package:opus_app/shared/design/v2_colors.dart';

/// Design v2 §4 "Structure": a screen is a title + a vertical stack of
/// sections. A section = `panel` background, 1 px `line` border, radius 12,
/// 16 px padding, section title top-left, optional single icon action
/// top-right. Title only -- no subtitle/description slot exists on purpose.
class Section extends StatelessWidget {
  const Section({required this.title, required this.child, this.action, super.key});

  final String title;
  final Widget child;

  /// At most one icon action in the top-right corner (§4: "optional single
  /// icon action"). Never a second button -- callers must not stack more
  /// than one here.
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    return Container(
      width: double.infinity,
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: V2Colors.panel,
        border: Border.all(color: V2Colors.line, width: 1),
        borderRadius: BorderRadius.circular(12),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  title,
                  // A3 run1: no `fontSize` override. The size comes from the
                  // central v2 type scale in `core/theme/opus_tokens.dart`
                  // (section title 14/600) -- hardcoding 17 here was exactly
                  // why Opus's type-scale reduction had no visible effect on
                  // these screens.
                  style: textTheme.titleMedium?.copyWith(color: V2Colors.text),
                ),
              ),
              if (action != null) action!,
            ],
          ),
          const SizedBox(height: 12),
          child,
        ],
      ),
    );
  }
}

/// A `panelRaised` tile inside a section (§4), radius 8, no border. Shows one
/// value -- big value 34/700 + a `textDim` unit beside it (§2 Type) -- and
/// nothing else: no subtitle slot, title only, per the copy rules.
///
/// v3 amendment (BINDING): "KPI tiles get a 3 px left colour bar in their
/// metric colour + the value in that colour." Pass [color] (typically
/// `V2Colors.metricColorV3('reactionTime')` etc., or `V2Colors.good`/`bad`
/// for a pass/fail tile) to opt a tile into this; omitting it keeps the
/// v2 neutral-`text` look for tiles with no single fixed metric meaning
/// (e.g. a plain trial count).
class StatTile extends StatelessWidget {
  const StatTile({required this.label, required this.value, this.unit, this.color, super.key});

  final String label;
  final String value;
  final String? unit;

  /// v3: the metric's fixed semantic colour. Colours the left bar and the
  /// value text; `null` keeps the plain v2 look.
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final textTheme = Theme.of(context).textTheme;
    final valueColor = color ?? V2Colors.text;
    final tile = Container(
      padding: const EdgeInsets.fromLTRB(13, 16, 16, 16),
      decoration: BoxDecoration(
        color: V2Colors.panelRaised,
        borderRadius: BorderRadius.circular(8),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
          ),
          const SizedBox(height: 6),
          // Design v2 §7: "text scales to 2.0 without clipping". A tile in a
          // fixed-height grid cell can't grow tall enough for the 34 px
          // value at 2x text scale (found via `--update-goldens`'s real
          // RenderFlex overflow) -- `Expanded` gives `FittedBox` a bounded
          // box to shrink the value+unit row into instead of clipping it
          // (a bare `FittedBox` under an unbounded `Column` has nothing to
          // shrink against and does nothing).
          Expanded(
            child: Align(
              alignment: Alignment.centerLeft,
              child: FittedBox(
                fit: BoxFit.scaleDown,
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.baseline,
                  textBaseline: TextBaseline.alphabetic,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      value,
                      // v2 §2 big value = 26/700 from the central scale
                      // (`headlineSmall`); v3: coloured by [color] when the
                      // tile has a fixed metric meaning.
                      style: textTheme.headlineSmall?.copyWith(color: valueColor),
                    ),
                    if (unit != null) ...[
                      const SizedBox(width: 4),
                      Text(
                        unit!,
                        style: textTheme.bodySmall?.copyWith(color: V2Colors.textDim),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
    if (color == null) return tile;
    // v3: "3 px left colour bar in their metric colour".
    return ClipRRect(
      borderRadius: BorderRadius.circular(8),
      child: Stack(
        children: [
          tile,
          Positioned(
            left: 0,
            top: 0,
            bottom: 0,
            child: Container(width: 3, color: color),
          ),
        ],
      ),
    );
  }
}
