// Run 8: regression coverage for the "Trunk lean said 'Worse' while the line
// fell" bug (Opus's review of `logs/sessions/screens/app/run8/
// patient_profile_desktop_light_1.0x.png`). `mdcChangeDirection`/
// `metricIsImprovement` used to default every metric except
// `reaction_time_ms`/`movement_time_ms` to "higher is better" -- these tests
// pin down every metric's real clinical direction so a future change can't
// silently regress one, and specifically cover the magnitude-based metrics
// (`neglect_index`, `fatigue_slope`) where a plain "lower raw value" rule
// would be wrong.
import 'package:flutter_test/flutter_test.dart';
import 'package:opus_app/shared/metrics/metric_format.dart';

void main() {
  group('metricDirectionFor', () {
    const lowerIsBetter = {
      'reaction_time_ms',
      'movement_time_ms',
      'time_to_peak_speed_pct',
      'tracking_loss_pct',
      'n_submovements',
      'path_length_ratio',
      'endpoint_error_cm',
      'trunk_displacement_cm',
      'trunk_lean_cm',
    };
    const higherIsBetter = {
      'peak_speed_mps',
      'sparc',
      'ldlj',
      'success_rate',
      'reach_envelope_area_m2',
    };
    const lowerMagnitudeIsBetter = {'neglect_index', 'fatigue_slope'};

    for (final id in lowerIsBetter) {
      test('$id is lowerIsBetter', () {
        expect(metricDirectionFor(id), MetricDirection.lowerIsBetter);
      });
    }
    for (final id in higherIsBetter) {
      test('$id is higherIsBetter', () {
        expect(metricDirectionFor(id), MetricDirection.higherIsBetter);
      });
    }
    for (final id in lowerMagnitudeIsBetter) {
      test('$id is lowerMagnitudeIsBetter', () {
        expect(metricDirectionFor(id), MetricDirection.lowerMagnitudeIsBetter);
      });
    }
  });

  group('mdcChangeDirection (the patient-profile "Trunk lean" bug)', () {
    test('trunk_lean_cm falling by more than the MDC is Improved, not Worse', () {
      // Reproduces run7/8's actual fixture shape: 1.2 -> 0.4 across sessions.
      final direction = mdcChangeDirection(metricId: 'trunk_lean_cm', first: 1.2, last: 0.4, mdc: 0.1);
      expect(direction, ChangeDirection.improved);
      expect(changeLabel(direction), 'Improved beyond normal variation');
    });

    test('trunk_lean_cm rising by more than the MDC is Worse', () {
      final direction = mdcChangeDirection(metricId: 'trunk_lean_cm', first: 0.4, last: 1.2, mdc: 0.1);
      expect(direction, ChangeDirection.worse);
    });

    test('reaction_time_ms falling (getting faster) is Improved', () {
      expect(
        mdcChangeDirection(metricId: 'reaction_time_ms', first: 420, last: 300, mdc: 10),
        ChangeDirection.improved,
      );
    });

    test('sparc rising toward zero (less negative, smoother) is Improved', () {
      expect(
        mdcChangeDirection(metricId: 'sparc', first: -3, last: -1.5, mdc: 0.1),
        ChangeDirection.improved,
      );
    });

    test('reach_envelope_area_m2 rising is Improved', () {
      expect(
        mdcChangeDirection(metricId: 'reach_envelope_area_m2', first: 0.10, last: 0.14, mdc: 0.01),
        ChangeDirection.improved,
      );
    });

    test('success_rate rising is Improved', () {
      expect(
        mdcChangeDirection(metricId: 'success_rate', first: 0.5, last: 0.9, mdc: 0.05),
        ChangeDirection.improved,
      );
    });

    test('neglect_index: growing more negative (bigger magnitude, worse neglect) is Worse, not Improved', () {
      // A naive "lower raw value is better" rule would call -0.1 -> -0.6 an
      // improvement (the number went down); it's actually more severe
      // neglect on the same side, so it must read as Worse.
      final direction = mdcChangeDirection(metricId: 'neglect_index', first: -0.1, last: -0.6, mdc: 0.05);
      expect(direction, ChangeDirection.worse);
    });

    test('neglect_index: magnitude shrinking toward zero from either sign is Improved', () {
      expect(
        mdcChangeDirection(metricId: 'neglect_index', first: 0.6, last: 0.1, mdc: 0.05),
        ChangeDirection.improved,
      );
      expect(
        mdcChangeDirection(metricId: 'neglect_index', first: -0.6, last: -0.1, mdc: 0.05),
        ChangeDirection.improved,
      );
    });

    test('a change within the MDC band is Within, regardless of direction', () {
      expect(
        mdcChangeDirection(metricId: 'trunk_lean_cm', first: 1, last: 0.95, mdc: 0.5),
        ChangeDirection.within,
      );
    });

    test('a null or zero mdc is always Within (never claims an unmeasured change)', () {
      expect(mdcChangeDirection(metricId: 'trunk_lean_cm', first: 1.2, last: 0.1, mdc: null), ChangeDirection.within);
      expect(mdcChangeDirection(metricId: 'trunk_lean_cm', first: 1.2, last: 0.1, mdc: 0), ChangeDirection.within);
    });
  });

  group("metricIsImprovement (progress screen's Improving/Declining chip)", () {
    test('trunk_lean_cm falling counts as improvement', () {
      expect(metricIsImprovement('trunk_lean_cm', 1.2, 0.4), isTrue);
    });

    test('reaction_time_ms rising counts as decline', () {
      expect(metricIsImprovement('reaction_time_ms', 300, 420), isFalse);
    });

    test('neglect_index uses magnitude, not raw sign', () {
      expect(metricIsImprovement('neglect_index', -0.1, -0.6), isFalse);
      expect(metricIsImprovement('neglect_index', -0.6, -0.1), isTrue);
    });
  });
}
