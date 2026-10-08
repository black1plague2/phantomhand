import 'package:flutter/material.dart';

/// Shown whenever a mock repository's error-injection toggle (Settings ->
/// "Simulate errors") is on, so every screen has real, testable error/retry
/// UI instead of only ever seeing the happy path -- brief A2.
class ErrorRetryView extends StatelessWidget {
  const new({required this.message, required this.onRetry, super.key});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(24),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.error_outline, size: 40, color: Theme.of(context).colorScheme.error),
            const SizedBox(height: 12),
            Text(message, textAlign: TextAlign.center),
            const SizedBox(height: 16),
            FilledButton.tonal(
              style: FilledButton.styleFrom(minimumSize: const Size(120, 48)),
              onPressed: onRetry,
              child: const Text('Retry'),
            ),
          ],
        ),
      ),
    );
  }
}
