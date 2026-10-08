// Shared by the witness mirror tests and goldens (B11): fonts like the A1
// goldens, a themed app shell, and the sample witness summaries.
import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart' show FontLoader;
import 'package:opus_app/core/theme/app_theme.dart';
import 'package:opus_app/data/models/phantom_witness.dart';
import 'package:path/path.dart' as p;

Future<void> _loadFont(String family, File? file) async {
  if (file == null) return;
  final data = await file.readAsBytes();
  final loader = FontLoader(family)..addFont(Future.value(ByteData.view(data.buffer)));
  await loader.load();
}

File? _materialIconsFont() {
  try {
    var dir = File(Platform.resolvedExecutable).absolute.parent;
    for (var i = 0; i < 8; i++) {
      final candidate = File('${dir.path}/bin/cache/artifacts/material_fonts/MaterialIcons-Regular.otf');
      if (candidate.existsSync()) return candidate;
      if (dir.parent.path == dir.path) break;
      dir = dir.parent;
    }
  } on FileSystemException catch (_) {}
  return null;
}

/// The app font and the icon font, as the A1 goldens load them. Call in `setUpAll`.
Future<void> loadWitnessTestFonts() async {
  await _loadFont('AtkinsonHyperlegibleNext', File('assets/fonts/AtkinsonHyperlegibleNext-Variable.ttf'));
  await _loadFont('MaterialIcons', _materialIconsFont());
}

/// A themed shell around [child] (no app localizations: the mirror has its own strings).
Widget witnessApp(Widget child, {Brightness brightness = Brightness.dark, double textScale = 1}) => MaterialApp(
      debugShowCheckedModeBanner: false,
      theme: brightness == Brightness.light ? AppTheme.light() : AppTheme.dark(),
      builder: (context, c) => MediaQuery(
        data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(textScale)),
        child: c!,
      ),
      home: Scaffold(body: child),
    );

/// A path inside the repo (tests run with `app/` as the working directory).
String repoPath(String rel) => p.normalize(p.join(Directory.current.path, '..', rel));

String contractsPath(String rel) => repoPath('contracts/$rel');

/// The `witness_summary` event line of the real fixture session
/// `contracts/fixtures/sessions/phantom_hand_min/events.ndjson`.
Map<String, dynamic> fixtureWitnessEvent() {
  final line = File(contractsPath('fixtures/sessions/phantom_hand_min/events.ndjson'))
      .readAsLinesSync()
      .firstWhere((l) => l.contains('"witness_summary"'));
  return jsonDecode(line) as Map<String, dynamic>;
}

/// The fixture exactly as it is: it predates `flinch_latency_ms`,
/// `flinch_strength` and `agency_q5`, so those rows read "No data".
PhantomWitness fixtureWitness() => PhantomWitness.tryParseEvent(fixtureWitnessEvent())!;

/// The fixture's numbers completed with the keys it lacks (what a full run with
/// the agency phase sends): flinch latency + strength on both cards, q5 in the
/// last condition only (sync ran last: `condition_order` is async, sync).
PhantomWitness fullWitness() {
  final data = Map<String, dynamic>.from(fixtureWitnessEvent()['data'] as Map<String, dynamic>);
  final sync = Map<String, dynamic>.from(data['sync'] as Map<String, dynamic>)
    ..['flinch_latency_ms'] = 96
    ..['flinch_strength'] = 'strong'
    ..['agency_q5'] = 6.5;
  final async = Map<String, dynamic>.from(data['async'] as Map<String, dynamic>)
    ..['flinch_latency_ms'] = 140
    ..['flinch_strength'] = 'weak';
  return PhantomWitness.tryParse({...data, 'sync': sync, 'async': async})!;
}
