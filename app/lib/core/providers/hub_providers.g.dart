// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'hub_providers.dart';

// **************************************************************************
// RiverpodGenerator
// **************************************************************************

// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, type=warning
/// Owns the (optional) `HubServer` + `UdpBeacon` lifecycle behind a settings
/// toggle (brief M3: "Repository switch: Mock ↔ Hub data sources (settings
/// toggle)"). Nothing starts a server automatically -- the clinician turns
/// hub mode on from Settings/Devices.

@ProviderFor(HubController)
final hubControllerProvider = HubControllerProvider._();

/// Owns the (optional) `HubServer` + `UdpBeacon` lifecycle behind a settings
/// toggle (brief M3: "Repository switch: Mock ↔ Hub data sources (settings
/// toggle)"). Nothing starts a server automatically -- the clinician turns
/// hub mode on from Settings/Devices.
final class HubControllerProvider
    extends $NotifierProvider<HubController, HubControllerState> {
  /// Owns the (optional) `HubServer` + `UdpBeacon` lifecycle behind a settings
  /// toggle (brief M3: "Repository switch: Mock ↔ Hub data sources (settings
  /// toggle)"). Nothing starts a server automatically -- the clinician turns
  /// hub mode on from Settings/Devices.
  HubControllerProvider._()
    : super(
        from: null,
        argument: null,
        retry: null,
        name: r'hubControllerProvider',
        isAutoDispose: false,
        dependencies: null,
        $allTransitiveDependencies: null,
      );

  @override
  String debugGetCreateSourceHash() => _$hubControllerHash();

  @$internal
  @override
  HubController create() => HubController();

  /// {@macro riverpod.override_with_value}
  Override overrideWithValue(HubControllerState value) {
    return $ProviderOverride(
      origin: this,
      providerOverride: $SyncValueProvider<HubControllerState>(value),
    );
  }
}

String _$hubControllerHash() => r'5e992ab969d5b7ba1aaea98739409036ec8d66df';

/// Owns the (optional) `HubServer` + `UdpBeacon` lifecycle behind a settings
/// toggle (brief M3: "Repository switch: Mock ↔ Hub data sources (settings
/// toggle)"). Nothing starts a server automatically -- the clinician turns
/// hub mode on from Settings/Devices.

abstract class _$HubController extends $Notifier<HubControllerState> {
  HubControllerState build();
  @$mustCallSuper
  @override
  WhenComplete runBuild() {
    final ref = this.ref as $Ref<HubControllerState, HubControllerState>;
    final element =
        ref.element
            as $ClassProviderElement<
              AnyNotifier<HubControllerState, HubControllerState>,
              HubControllerState,
              Object?,
              Object?
            >;
    return element.handleCreate(ref, build);
  }
}
