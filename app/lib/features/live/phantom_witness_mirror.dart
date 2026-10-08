/// The audience "witness mirror" (B11): what changed for the person who just
/// did the Phantom Hand run, shown big on the observer screen during the
/// witness phase. Two cards, "In sync" (info blue) and "Delayed" (warn amber),
/// each with three groups: Body (where the hand felt to be, the flinch),
/// Mind (it felt like my hand, I caused that movement) and The one who noticed
/// (q4, "a pointer, not proof"). The closing lines fade in 3 s after the
/// mirror appears.
///
/// Pure view: it takes a parsed [PhantomWitness] and a language code, nothing
/// else. Missing values read "No data", never 0. Questionnaire values are
/// drawn the way the headset draws them, -3..+3 (wire value minus 4) on a bar.
///
/// Wording (`docs/agent-briefs/ph/03-SPEC.md` D11, `docs/PH_JUDGE_SHEET.md`
/// §2): nothing here says the demo measures, creates or proves consciousness,
/// or that "the witness did not change". The strings live in
/// [PhantomWitnessStrings] (EN + HI) so this file does not touch the arb files.
library;

import 'package:flutter/material.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/phantom_witness.dart';

/// The closing lines appear this long after the mirror does, then fade in over
/// [phantomWitnessClosingFade] (the headset panel does the same).
const phantomWitnessClosingDelay = Duration(seconds: 3);
const phantomWitnessClosingFade = Duration(seconds: 1);

/// Two cards side by side from this width (dp), stacked below it.
const phantomWitnessWideFrom = 900.0;

/// EN + HI texts of the mirror. Row labels, direction words and strength words
/// are the headset's own (`PhStrings.cs`, `WitnessSummary.cs`, `Questionnaire.cs`).
class PhantomWitnessStrings {
  const new({
    required this.title,
    required this.preliminary,
    required this.inSync,
    required this.delayed,
    required this.body,
    required this.mind,
    required this.noticer,
    required this.drift,
    required this.toward,
    required this.away,
    required this.noShift,
    required this.flinch,
    required this.strong,
    required this.weak,
    required this.none,
    required this.ownership,
    required this.agency,
    required this.awareness,
    required this.pointer,
    required this.noData,
    required this.closing,
  });

  final String title;
  final String preliminary;
  final String inSync;
  final String delayed;
  final String body;
  final String mind;
  final String noticer;
  final String drift;
  final String toward;
  final String away;
  final String noShift;
  final String flinch;
  final String strong;
  final String weak;
  final String none;
  final String ownership;
  final String agency;
  final String awareness;
  final String pointer;
  final String noData;

  /// Shown when the event carries no closing lines of its own.
  final List<String> closing;

  static const en = PhantomWitnessStrings(
    title: 'What changed?',
    preliminary: 'Preliminary',
    inSync: 'In sync',
    delayed: 'Delayed',
    body: 'Body',
    mind: 'Mind',
    noticer: 'The one who noticed',
    drift: 'Where your hand felt to be',
    toward: 'toward the virtual hand',
    away: 'away from the virtual hand',
    noShift: 'no shift',
    flinch: 'Flinch after the stone',
    strong: 'Strong',
    weak: 'Weak',
    none: 'None',
    ownership: 'It felt like my hand',
    agency: 'I caused that movement',
    awareness: 'The awareness that noticed was the same',
    pointer: 'A pointer, not proof',
    noData: 'No data',
    closing: [
      'The body changed. The touch changed. The feeling of "mine" changed.',
      'You noticed every change.',
      'Tattva 5: consciousness is beyond the body and mind.',
    ],
  );

  // Hindi: everything except the three default closing lines is the headset's
  // own Hindi (or the brief's). The closing lines are new and need a native check.
  static const hi = PhantomWitnessStrings(
    title: 'क्या बदला?',
    preliminary: 'प्रारंभिक',
    inSync: 'साथ-साथ',
    delayed: 'देरी से',
    body: 'शरीर',
    mind: 'मन',
    noticer: 'देखने वाला',
    drift: 'आपका हाथ कहाँ महसूस हुआ',
    toward: 'वर्चुअल हाथ की ओर',
    away: 'वर्चुअल हाथ से दूर',
    noShift: 'कोई खिसकाव नहीं',
    flinch: 'पत्थर के बाद झटका',
    strong: 'तेज़',
    weak: 'हल्का',
    none: 'कोई नहीं',
    ownership: 'यह मेरा हाथ लगा',
    agency: 'वह गति मैंने ही कराई',
    awareness: 'देखने वाली जागरूकता वही रही',
    pointer: 'एक संकेत, प्रमाण नहीं',
    noData: 'कोई डेटा नहीं',
    closing: [
      'शरीर बदला। स्पर्श बदला। "मेरा" होने का एहसास बदला।',
      'आपने हर बदलाव को देखा।',
      'तत्त्व 5: चेतना शरीर और मन से परे है।',
    ],
  );

  /// `hi` gives Hindi; anything else gives English.
  static PhantomWitnessStrings forLang(String lang) => lang == 'hi' ? hi : en;
}

/// A closing text from an older headset build that says the witness "did not
/// change" (pre-D11 wording). It is never shown; the default lines replace it.
final _staleClosing = RegExp('did not change|नहीं बदला', caseSensitive: false);

List<String> _closingLines(PhantomWitness w, PhantomWitnessStrings s, {required bool hindi}) {
  final sent = hindi ? w.closingHi : w.closingEn;
  if (sent == null || _staleClosing.hasMatch(sent)) return s.closing;
  final lines = [
    for (final l in sent.split('\n'))
      if (l.trim().isNotEmpty) l.trim(),
  ];
  return lines.isEmpty ? s.closing : lines;
}

/// Sizes (sp / dp) of one layout. "Wide" is the audience screen on a laptop
/// (read from 3 m): the type is as large as two cards side by side allow.
class _Sizes {
  const new({
    required this.page,
    required this.cardTitle,
    required this.group,
    required this.label,
    required this.value,
    required this.chip,
    required this.closing,
    required this.pad,
    required this.gap,
    required this.bar,
  });

  final double page;
  final double cardTitle;
  final double group;
  final double label;
  final double value;
  final double chip;
  final double closing;
  final double pad;
  final double gap;
  final double bar;
}

const _wide = _Sizes(page: 40, cardTitle: 40, group: 20, label: 24, value: 56, chip: 30, closing: 24, pad: 20, gap: 12, bar: 22);
const _compact = _Sizes(page: 30, cardTitle: 30, group: 18, label: 18, value: 40, chip: 22, closing: 20, pad: 16, gap: 12, bar: 18);

/// Strings, sizes and tokens of one build, so the small widgets below take one argument.
class _Look {
  const new({required this.s, required this.z, required this.t, required this.base});

  final PhantomWitnessStrings s;
  final _Sizes z;
  final OpusTokens t;
  final TextStyle base;

  TextStyle style(double size, {FontWeight weight = FontWeight.w500, Color? color, bool italic = false, double height = 1.25}) =>
      base.copyWith(
        fontSize: size,
        fontWeight: weight,
        color: color ?? t.ink,
        fontStyle: italic ? FontStyle.italic : FontStyle.normal,
        height: height,
      );

  /// "+2.0" with a small unit after it ("cm", "ms"). Digits only, so the line is tight.
  Widget bigNumber(String number, {String unit = ''}) => Text.rich(
        TextSpan(
          children: [
            TextSpan(text: number, style: style(z.value, weight: FontWeight.w800, height: 1)),
            if (unit.isNotEmpty) TextSpan(text: ' $unit', style: style(z.value * 0.5, weight: FontWeight.w600, color: t.slate, height: 1)),
          ],
        ),
      );

  /// "No data" in the place of a value; [minHeight] keeps the rows of the two cards level.
  Widget noData({required double minHeight}) => ConstrainedBox(
        constraints: BoxConstraints(minHeight: minHeight),
        child: Align(
          alignment: Alignment.centerLeft,
          widthFactor: 1,
          child: Text(s.noData, style: style(z.label + 4, color: t.slate, italic: true)),
        ),
      );
}

/// The mirror. [summary] null (no event yet) draws every value as "No data".
/// Pass the same [PhantomWitness] object while it is unchanged: a new object
/// restarts the closing-line fade.
class PhantomWitnessMirror extends StatefulWidget {
  const new({required this.summary, required this.lang, super.key});

  final PhantomWitness? summary;

  /// `en` or `hi`.
  final String lang;

  @override
  State<PhantomWitnessMirror> createState() => _PhantomWitnessMirrorState();
}

class _PhantomWitnessMirrorState extends State<PhantomWitnessMirror> with SingleTickerProviderStateMixin {
  static final Duration _total = phantomWitnessClosingDelay + phantomWitnessClosingFade;

  late final AnimationController _clock = AnimationController(vsync: this, duration: _total)..forward();
  late final CurvedAnimation _closingOpacity = CurvedAnimation(
    parent: _clock,
    curve: Interval(phantomWitnessClosingDelay.inMilliseconds / _total.inMilliseconds, 1),
  );

  @override
  void didUpdateWidget(covariant PhantomWitnessMirror old) {
    super.didUpdateWidget(old);
    if (!identical(old.summary, widget.summary)) _clock.forward(from: 0);
  }

  @override
  void dispose() {
    _closingOpacity.dispose();
    _clock.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final hindi = widget.lang == 'hi';
    final s = PhantomWitnessStrings.forLang(widget.lang);
    final w = widget.summary ?? PhantomWitness.empty;
    return LayoutBuilder(
      builder: (context, box) {
        final wide = box.maxWidth >= phantomWitnessWideFrom;
        final look = _Look(
          s: s,
          z: wide ? _wide : _compact,
          t: theme.extension<OpusTokens>()!,
          base: theme.textTheme.bodyMedium ?? const TextStyle(),
        );
        final z = look.z;
        // The agency row appears on both cards as soon as either condition has a q5.
        final showAgency = w.sync.agencyQ5 != null || w.async.agencyQ5 != null;
        final syncCard = _ConditionCard(
          key: const ValueKey('ph-witness-card-sync'),
          look: look,
          tag: 'sync',
          title: s.inSync,
          accent: OpusTokens.info,
          solid: true,
          data: w.sync,
          showAgency: showAgency,
        );
        final asyncCard = _ConditionCard(
          key: const ValueKey('ph-witness-card-async'),
          look: look,
          tag: 'async',
          title: s.delayed,
          accent: OpusTokens.warn,
          solid: false,
          data: w.async,
          showAgency: showAgency,
        );
        final content = Padding(
          key: const ValueKey('ph-witness-content'),
          padding: EdgeInsets.all(z.pad),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              _Header(look: look),
              SizedBox(height: z.gap),
              if (wide)
                IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [Expanded(child: syncCard), SizedBox(width: z.gap), Expanded(child: asyncCard)],
                  ),
                )
              else ...[syncCard, SizedBox(height: z.gap), asyncCard],
              SizedBox(height: z.gap + 6),
              FadeTransition(
                key: const ValueKey('ph-witness-closing'),
                opacity: _closingOpacity,
                child: Column(
                  children: [
                    for (final line in _closingLines(w, s, hindi: hindi))
                      Padding(
                        padding: const EdgeInsets.only(bottom: 4),
                        child: Text(line, textAlign: TextAlign.center, style: look.style(z.closing)),
                      ),
                  ],
                ),
              ),
            ],
          ),
        );
        // Audience screen: shrink a little rather than scroll when the window is
        // short (and centre what is left). Phones scroll.
        if (wide) {
          return SizedBox(
            width: box.maxWidth,
            height: box.hasBoundedHeight ? box.maxHeight : null,
            child: FittedBox(
              fit: BoxFit.scaleDown,
              child: SizedBox(width: box.maxWidth, child: content),
            ),
          );
        }
        return SingleChildScrollView(child: content);
      },
    );
  }
}

class _Header extends StatelessWidget {
  const new({required this.look});
  final _Look look;

  @override
  Widget build(BuildContext context) {
    final z = look.z;
    final t = look.t;
    return Wrap(
      alignment: WrapAlignment.spaceBetween,
      crossAxisAlignment: WrapCrossAlignment.center,
      spacing: 16,
      runSpacing: 8,
      children: [
        Text(look.s.title, key: const ValueKey('ph-witness-title'), style: look.style(z.page, weight: FontWeight.w800)),
        Container(
          key: const ValueKey('ph-witness-preliminary'),
          padding: EdgeInsets.symmetric(horizontal: z.label * 0.7, vertical: z.label * 0.25),
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(OpusTokens.radiusChip),
            border: Border.all(color: t.slate, width: 2),
          ),
          child: Text(look.s.preliminary, style: look.style(z.label, weight: FontWeight.w700, color: t.slate)),
        ),
      ],
    );
  }
}

class _ConditionCard extends StatelessWidget {
  const new({
    required this.look,
    required this.tag,
    required this.title,
    required this.accent,
    required this.solid,
    required this.data,
    required this.showAgency,
    super.key,
  });

  final _Look look;

  /// `sync` / `async`, used in the row keys.
  final String tag;
  final String title;
  final Color accent;

  /// Filled dot for In sync, ring for Delayed (colour is never the only cue).
  final bool solid;
  final PhantomWitnessCondition data;
  final bool showAgency;

  @override
  Widget build(BuildContext context) {
    final z = look.z;
    final t = look.t;
    final s = look.s;
    final dot = z.cardTitle * 0.55;
    return Semantics(
      container: true,
      label: title,
      child: Container(
        decoration: BoxDecoration(
          color: t.paper,
          borderRadius: BorderRadius.circular(OpusTokens.radiusPane),
          border: Border.all(color: t.rule),
        ),
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Container(height: 8, color: accent),
            Padding(
              padding: EdgeInsets.all(z.pad),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Container(
                        width: dot,
                        height: dot,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          color: solid ? accent : null,
                          border: Border.all(color: accent, width: 4),
                        ),
                      ),
                      const SizedBox(width: 12),
                      Flexible(child: Text(title, style: look.style(z.cardTitle, weight: FontWeight.w800))),
                    ],
                  ),
                  _Group(
                    look: look,
                    header: s.body,
                    children: [
                      _DriftRow(key: ValueKey('ph-witness-$tag-drift'), look: look, cm: data.driftChangeCm),
                      _FlinchRow(
                        key: ValueKey('ph-witness-$tag-flinch'),
                        look: look,
                        ms: data.flinchLatencyMs,
                        strength: data.flinchStrength,
                      ),
                    ],
                  ),
                  _Group(
                    look: look,
                    header: s.mind,
                    children: [
                      _BarRow(
                        key: ValueKey('ph-witness-$tag-ownership'),
                        look: look,
                        label: s.ownership,
                        wire: data.ownership,
                        fill: accent,
                      ),
                      if (showAgency)
                        _BarRow(
                          key: ValueKey('ph-witness-$tag-agency'),
                          look: look,
                          label: s.agency,
                          wire: data.agencyQ5,
                          fill: accent,
                        ),
                    ],
                  ),
                  _Group(
                    look: look,
                    header: s.noticer,
                    children: [
                      _BarRow(
                        key: ValueKey('ph-witness-$tag-awareness'),
                        look: look,
                        label: s.awareness,
                        wire: data.witnessQ4,
                        fill: t.slate,
                        pointer: s.pointer,
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// A small header with a rule under it, then the rows of that group.
class _Group extends StatelessWidget {
  const new({required this.look, required this.header, required this.children});

  final _Look look;
  final String header;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final z = look.z;
    return Padding(
      padding: EdgeInsets.only(top: z.gap),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SizedBox(
            width: double.infinity,
            child: DecoratedBox(
              decoration: BoxDecoration(border: Border(bottom: BorderSide(color: look.t.rule))),
              child: Padding(
                padding: const EdgeInsets.only(bottom: 2),
                child: Text(header, style: look.style(z.group, weight: FontWeight.w700, color: look.t.slate)),
              ),
            ),
          ),
          for (final c in children) Padding(padding: EdgeInsets.only(top: z.gap * 0.7), child: c),
        ],
      ),
    );
  }
}

/// "-2.4" / "+2.0" / "0.0" (never "-0.0").
String _signed(double v, int dp) {
  final text = v.toStringAsFixed(dp);
  if (double.parse(text) == 0) return 0.0.toStringAsFixed(dp);
  return v > 0 ? '+$text' : text;
}

/// A shift of at most this many cm reads as "no shift" (the headset's rule).
const _noShiftCm = 0.25;

class _DriftRow extends StatelessWidget {
  const new({required this.look, required this.cm, super.key});

  final _Look look;
  final double? cm;

  @override
  Widget build(BuildContext context) {
    final s = look.s;
    final z = look.z;
    final v = cm;
    final direction = v == null || v.abs() <= _noShiftCm ? 0 : (v > 0 ? 1 : -1);
    final detail = v == null ? null : (direction > 0 ? s.toward : (direction < 0 ? s.away : s.noShift));
    // The virtual arm is to the left of the real one: toward = left arrow. No shift = a dot (a dash would read as a minus sign).
    final icon = direction == 0 ? Icons.circle : (direction > 0 ? Icons.arrow_back_rounded : Icons.arrow_forward_rounded);
    return Semantics(
      container: true,
      excludeSemantics: true,
      label: s.drift,
      value: v == null ? s.noData : '${_signed(v, 1)} cm, $detail',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(s.drift, style: look.style(z.label, color: look.t.slate)),
          const SizedBox(height: 2),
          if (v == null)
            look.noData(minHeight: z.value)
          else
            Wrap(
              crossAxisAlignment: WrapCrossAlignment.center,
              spacing: 12,
              children: [
                Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    SizedBox(
                      width: z.value * 0.8,
                      child: Center(child: Icon(icon, size: z.value * (direction == 0 ? 0.3 : 0.8), color: look.t.ink)),
                    ),
                    const SizedBox(width: 6),
                    look.bigNumber(_signed(v, 1), unit: 'cm'),
                  ],
                ),
                Text(detail!, style: look.style(z.label)),
              ],
            ),
        ],
      ),
    );
  }
}

class _FlinchRow extends StatelessWidget {
  const new({required this.look, required this.ms, required this.strength, super.key});

  final _Look look;
  final double? ms;
  final PhantomFlinchStrength? strength;

  @override
  Widget build(BuildContext context) {
    final s = look.s;
    final z = look.z;
    final t = look.t;
    final latency = ms;
    final word = switch (strength) {
      PhantomFlinchStrength.strong => s.strong,
      PhantomFlinchStrength.weak => s.weak,
      PhantomFlinchStrength.none => s.none,
      null => null,
    };
    return Semantics(
      container: true,
      excludeSemantics: true,
      label: s.flinch,
      value: [if (latency != null) '${latency.round()} ms' else s.noData, ?word].join(', '),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(s.flinch, style: look.style(z.label, color: t.slate)),
          const SizedBox(height: 2),
          Wrap(
            crossAxisAlignment: WrapCrossAlignment.center,
            spacing: 14,
            runSpacing: 4,
            children: [
              if (latency != null) look.bigNumber('${latency.round()}', unit: 'ms') else look.noData(minHeight: z.value),
              if (word != null)
                Container(
                  padding: EdgeInsets.symmetric(horizontal: z.chip * 0.5, vertical: z.chip * 0.15),
                  decoration: BoxDecoration(
                    color: t.panelRaised,
                    borderRadius: BorderRadius.circular(OpusTokens.radiusChip),
                    border: Border.all(color: t.ink, width: 2),
                  ),
                  child: Text(word, style: look.style(z.chip, weight: FontWeight.w800)),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

/// A questionnaire answer as the headset draws it: -3..+3 on a bar with a mark
/// in the middle, the number at the right. [wire] is the 1..7 mean.
class _BarRow extends StatelessWidget {
  const new({required this.look, required this.label, required this.wire, required this.fill, this.pointer, super.key});

  final _Look look;
  final String label;
  final double? wire;
  final Color fill;

  /// Caption under the bar (q4: "A pointer, not proof").
  final String? pointer;

  @override
  Widget build(BuildContext context) {
    final z = look.z;
    final t = look.t;
    final signed = phantomSignedAnswer(wire);
    final text = signed == null ? look.s.noData : _signed(signed, 1);
    return Semantics(
      container: true,
      excludeSemantics: true,
      label: pointer == null ? label : '$label, $pointer',
      value: text,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: look.style(z.label, color: t.slate)),
          const SizedBox(height: 4),
          if (signed == null)
            look.noData(minHeight: z.value * 0.75)
          else
            Row(
              children: [
                Expanded(
                  child: _AnswerBar(
                    fraction: ((signed + 3) / 6).clamp(0.0, 1.0),
                    height: z.bar,
                    fill: fill,
                    track: t.rule,
                    edge: t.slate.withValues(alpha: 0.5),
                    mark: t.ink,
                  ),
                ),
                const SizedBox(width: 14),
                Text(text, style: look.style(z.value * 0.75, weight: FontWeight.w800, height: 1)),
              ],
            ),
          if (pointer != null) ...[
            const SizedBox(height: 4),
            Text(pointer!, style: look.style(z.label, color: t.slate, italic: true)),
          ],
        ],
      ),
    );
  }
}

/// Track, fill from the left up to [fraction] (0..1), mark in the middle (the
/// neutral answer).
class _AnswerBar extends StatelessWidget {
  const new({
    required this.fraction,
    required this.height,
    required this.fill,
    required this.track,
    required this.edge,
    required this.mark,
  });

  final double fraction;
  final double height;
  final Color fill;
  final Color track;
  final Color edge;
  final Color mark;

  @override
  Widget build(BuildContext context) {
    return CustomPaint(
      // The mark sticks out 4 dp above and below the track.
      size: Size.fromHeight(height + 8),
      painter: _BarPainter(fraction: fraction, barHeight: height, fill: fill, track: track, edge: edge, mark: mark),
    );
  }
}

class _BarPainter extends CustomPainter {
  const new({
    required this.fraction,
    required this.barHeight,
    required this.fill,
    required this.track,
    required this.edge,
    required this.mark,
  });

  final double fraction;
  final double barHeight;
  final Color fill;
  final Color track;
  final Color edge;
  final Color mark;

  @override
  void paint(Canvas canvas, Size size) {
    final top = (size.height - barHeight) / 2;
    final radius = Radius.circular(barHeight / 2);
    final trackBox = RRect.fromRectAndRadius(Rect.fromLTWH(0, top, size.width, barHeight), radius);
    // A answer at the bottom of the scale still shows a sliver of fill.
    final width = size.width * (fraction < 0.04 ? 0.04 : fraction);
    canvas
      ..drawRRect(trackBox, Paint()..color = track)
      ..drawRRect(RRect.fromRectAndRadius(Rect.fromLTWH(0, top, width, barHeight), radius), Paint()..color = fill)
      ..drawRRect(
        trackBox,
        Paint()
          ..color = edge
          ..style = PaintingStyle.stroke
          ..strokeWidth = 1,
      )
      ..drawRect(Rect.fromCenter(center: size.center(Offset.zero), width: 3, height: size.height), Paint()..color = mark);
  }

  @override
  bool shouldRepaint(_BarPainter old) =>
      old.fraction != fraction ||
      old.barHeight != barHeight ||
      old.fill != fill ||
      old.track != track ||
      old.edge != edge ||
      old.mark != mark;
}
