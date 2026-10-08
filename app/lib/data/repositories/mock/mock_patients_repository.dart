import 'dart:convert';

import 'package:flutter/services.dart' show rootBundle;
import 'package:opus_app/data/models/patient.dart';
import 'package:opus_app/data/repositories/mock/fixture_loader.dart';
import 'package:opus_app/data/repositories/patients_repository.dart';

/// Backed by the hand-written `assets/fixtures/patients.json` roster (small,
/// curated -- see brief A2), whose ids line up 1:1 with the `patient_ref`
/// values inside the real synthetic sessions under
/// `contracts/fixtures/sessions/*` so a patient's session/progress screens
/// show real generated data.
class MockPatientsRepository implements PatientsRepository {
  new({FixtureLoader? loader, this.errorInjector})
      : _loader = loader ?? const FixtureLoader();

  final FixtureLoader _loader;
  final MockErrorInjector? errorInjector;
  List<Patient>? _cache;

  Future<List<Patient>> _all() async {
    if (_cache != null) return _cache!;
    final raw = await rootBundle.loadString('assets/fixtures/patients.json');
    final list = (jsonDecode(raw) as List).cast<Map<String, dynamic>>();
    _cache = list
        .map(
          (j) => Patient(
            id: j['id'] as String,
            displayName: j['display_name'] as String,
            age: j['age'] as int,
            gender: j['gender'] as String,
            affectedSide: AffectedSide.values.byName(j['affected_side'] as String),
            diagnosis: j['diagnosis'] as String,
            onsetDate: DateTime.parse(j['onset_date'] as String),
          ),
        )
        .toList();
    return _cache!;
  }

  @override
  Future<List<Patient>> listPatients([PatientFilter filter = const PatientFilter()]) async {
    errorInjector?.maybeThrow('listPatients');
    await _loader.latency();
    final all = await _all();
    return all.where((p) {
      final matchesSearch = filter.search.isEmpty ||
          p.displayName.toLowerCase().contains(filter.search.toLowerCase()) ||
          p.id.toLowerCase().contains(filter.search.toLowerCase());
      final matchesSide = filter.affectedSide == null || p.affectedSide == filter.affectedSide;
      final matchesDiagnosis = filter.diagnosisContains == null ||
          filter.diagnosisContains!.isEmpty ||
          p.diagnosis.toLowerCase().contains(filter.diagnosisContains!.toLowerCase());
      return matchesSearch && matchesSide && matchesDiagnosis;
    }).toList();
  }

  @override
  Future<Patient?> getPatient(String id) async {
    errorInjector?.maybeThrow('getPatient');
    await _loader.latency();
    final all = await _all();
    for (final p in all) {
      if (p.id == id) return p;
    }
    return null;
  }
}
