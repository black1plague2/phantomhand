import 'dart:async';

import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:opus_app/core/theme/opus_tokens.dart';
import 'package:opus_app/data/models/phantom_demo.dart';
import 'package:opus_app/shared/clinical/phantom_demo_strings.dart';

/// The one "Run Phantom Hand demo" button (EN + HI by the app language), with a
/// one-line hint under it. One tap opens the demo screen, which plays the whole
/// run, the audience results mirror and the demo participant's report with no
/// headset, sleeve or hub. On the login screen [onPressed] signs in first.
class PhantomDemoButton extends StatelessWidget {
  const new({this.onPressed, super.key});

  /// Replaces opening the demo screen.
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) {
    final s = PhantomDemoStrings.forLang(Localizations.localeOf(context).languageCode);
    final theme = Theme.of(context);
    final t = theme.extension<OpusTokens>();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        FilledButton.icon(
          key: const ValueKey('ph-run-demo'),
          // The one light button among the dark ones: the first thing to tap.
          style: FilledButton.styleFrom(minimumSize: const Size.fromHeight(56), backgroundColor: t?.ink, foregroundColor: t?.mist),
          onPressed: onPressed ?? () => unawaited(context.push(demoPhantomRoute)),
          icon: const Icon(Icons.play_arrow),
          label: Text(s.t('button')),
        ),
        const SizedBox(height: 6),
        Text(
          s.t('buttonHint'),
          textAlign: TextAlign.center,
          style: theme.textTheme.bodySmall?.copyWith(color: t?.slate),
        ),
      ],
    );
  }
}
