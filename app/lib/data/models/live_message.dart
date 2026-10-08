import 'package:freezed_annotation/freezed_annotation.dart';

part 'live_message.freezed.dart';
part 'live_message.g.dart';

enum LiveSessionStatus { running, paused, stopped }

/// Shape of ARCHITECTURE.md §7's low-rate live channel (`live-message.schema.json`
/// is not written yet -- this mirrors the described fields: status, current
/// trial, rolling metrics, commands). App-only until that contract lands;
/// `MockLiveRepository` emits these on a `Stream` to stand in for the WS feed.
@freezed
abstract class LiveMessage with _$LiveMessage {
  const factory({
    required LiveSessionStatus status,
    required int trialIndex,
    required int totalTrials,
    required Map<String, double> rollingMetrics,
    String? lastOutcome,
  }) = _LiveMessage;

  factory fromJson(Map<String, Object?> json) => _$LiveMessageFromJson(json);
}
