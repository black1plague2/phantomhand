// B2 (R3 D14): the screen must not dim while the operator watches the live card.
// This is a source guard, not a behaviour test: the flag only acts on a real
// Android window, which `flutter test` cannot show. The Kotlin file is also
// compile-checked against android.jar (see the session log); what a phone does
// with it is verified by hand (screen stays on while the app is in front).
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

void main() {
  final activity = File('android/app/src/main/kotlin/com/opus/opus_app/MainActivity.kt').readAsStringSync();

  test('MainActivity sets FLAG_KEEP_SCREEN_ON in onCreate, after super.onCreate', () {
    expect(activity, contains('import android.view.WindowManager'));
    expect(activity, contains('override fun onCreate(savedInstanceState: Bundle?)'));
    final superCall = activity.indexOf('super.onCreate(savedInstanceState)');
    final flag = activity.indexOf('window.addFlags(WindowManager.LayoutParams.FLAG_KEEP_SCREEN_ON)');
    expect(superCall, isNonNegative);
    expect(flag, greaterThan(superCall));
  });

  test('no wakelock plugin: a new plugin breaks pub get on this PC (Developer Mode off)', () {
    expect(File('pubspec.yaml').readAsStringSync(), isNot(contains('wakelock')));
  });
}
