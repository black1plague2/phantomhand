import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'package:opus_app/data/models/user.dart';
import 'package:opus_app/features/auth/auth_controller.dart';

/// Rail on tablet/web (width >= 700), bottom bar on phone -- brief A1
/// "App shell with adaptive nav (rail on tablet/web, bottom bar on phone)".
///
/// Bottom nav is the 4 top-level tabs Opus decided on 2026-09-19: Patients /
/// Monitor / Programs / Reports. Session/outcome screens are still reached by
/// drilling into a patient rather than as their own tabs, since neither makes
/// sense without a patient already selected -- but Monitor and Reports (the
/// live-now view and the cross-patient session list) are top-level because a
/// clinician needs to reach "what's happening right now" and "every session"
/// without first picking a patient. Devices, Settings and Sign out move
/// behind the top-right avatar menu (same Opus decision) since none of them
/// are a clinician's everyday destination.
class AdaptiveShell extends ConsumerWidget {
  const new({required this.child, super.key});

  final Widget child;

  static const _destinations = [
    NavigationRailDestination(icon: Icon(Icons.people_outline), selectedIcon: Icon(Icons.people), label: Text('Patients')),
    NavigationRailDestination(icon: Icon(Icons.monitor_heart_outlined), selectedIcon: Icon(Icons.monitor_heart), label: Text('Monitor')),
    NavigationRailDestination(icon: Icon(Icons.fitness_center_outlined), selectedIcon: Icon(Icons.fitness_center), label: Text('Programs')),
    NavigationRailDestination(icon: Icon(Icons.assessment_outlined), selectedIcon: Icon(Icons.assessment), label: Text('Reports')),
  ];

  static const _bottomDestinations = [
    NavigationDestination(icon: Icon(Icons.people_outline), selectedIcon: Icon(Icons.people), label: 'Patients'),
    NavigationDestination(icon: Icon(Icons.monitor_heart_outlined), selectedIcon: Icon(Icons.monitor_heart), label: 'Monitor'),
    NavigationDestination(icon: Icon(Icons.fitness_center_outlined), selectedIcon: Icon(Icons.fitness_center), label: 'Programs'),
    NavigationDestination(icon: Icon(Icons.assessment_outlined), selectedIcon: Icon(Icons.assessment), label: 'Reports'),
  ];

  static const _routes = ['/patients', '/monitor', '/programs', '/reports'];

  int _indexFor(String location) {
    final i = _routes.indexWhere(location.startsWith);
    return i == -1 ? 0 : i;
  }

  void _onSelect(BuildContext context, int index) => context.go(_routes[index]);

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final location = GoRouterState.of(context).matchedLocation;
    final index = _indexFor(location);
    final isWide = MediaQuery.sizeOf(context).width >= 700;
    final user = ref.watch(authControllerProvider);

    if (isWide) {
      return Scaffold(
        body: Row(
          children: [
            NavigationRail(
              selectedIndex: index,
              onDestinationSelected: (i) => _onSelect(context, i),
              labelType: NavigationRailLabelType.all,
              destinations: _destinations,
              trailing: Expanded(
                child: Align(
                  alignment: Alignment.bottomCenter,
                  child: Padding(
                    padding: const EdgeInsets.only(bottom: 16),
                    child: _AvatarMenu(user: user, ref: ref),
                  ),
                ),
              ),
            ),
            const VerticalDivider(width: 1),
            Expanded(
              child: Column(
                children: [
                  _TopStrip(user: user, ref: ref, showAvatar: false),
                  Expanded(child: child),
                ],
              ),
            ),
          ],
        ),
      );
    }

    return Scaffold(
      body: Column(
        children: [
          _TopStrip(user: user, ref: ref, showAvatar: true),
          Expanded(child: child),
        ],
      ),
      bottomNavigationBar: NavigationBar(
        selectedIndex: index,
        onDestinationSelected: (i) => _onSelect(context, i),
        destinations: _bottomDestinations,
      ),
    );
  }
}

/// Thin shell-level strip carrying only the avatar menu, sitting above
/// whatever [AppBar] the routed [child] renders itself -- keeps Devices /
/// Settings / Sign out reachable from every tab without this shell having to
/// reach into (or remove) the per-screen `AppBar`s that `features/patients`,
/// `features/programs` and `features/sessions` (out of this track's scope)
/// already own. On wide layouts the avatar lives in the rail's trailing area
/// instead ([showAvatar] false), so this strip renders as an empty 0-height
/// SizedBox there rather than showing the menu twice.
class _TopStrip extends StatelessWidget {
  const _TopStrip({required this.user, required this.ref, required this.showAvatar});
  final AppUser? user;
  final WidgetRef ref;
  final bool showAvatar;

  @override
  Widget build(BuildContext context) {
    if (!showAvatar) return const SizedBox.shrink();
    return SafeArea(
      bottom: false,
      child: Container(
        height: 44,
        color: Theme.of(context).colorScheme.surface,
        padding: const EdgeInsets.only(right: 8),
        alignment: Alignment.centerRight,
        child: _AvatarMenu(user: user, ref: ref),
      ),
    );
  }
}

/// The avatar button (top-right on phone, rail trailing on tablet/desktop)
/// that opens Devices / Settings / Sign out -- Opus decision 2026-09-19:
/// "Devices + Settings + Sign out behind the top-right avatar".
class _AvatarMenu extends StatelessWidget {
  const _AvatarMenu({required this.user, required this.ref});
  final AppUser? user;
  final WidgetRef ref;

  @override
  Widget build(BuildContext context) {
    final name = user?.displayName;
    final initials = name == null || name.trim().isEmpty
        ? '?'
        : name.trim().split(RegExp(r'\s+')).where((s) => s.isNotEmpty).take(2).map((s) => s[0].toUpperCase()).join();
    return Semantics(
      button: true,
      label: 'Account menu${name != null ? ', signed in as $name' : ''}',
      child: PopupMenuButton<String>(
        tooltip: 'Account',
        icon: CircleAvatar(radius: 16, child: Text(initials, style: const TextStyle(fontSize: 13))),
        itemBuilder: (context) => [
          const PopupMenuItem(value: 'devices', child: ListTile(leading: Icon(Icons.devices_outlined), title: Text('Devices'))),
          const PopupMenuItem(value: 'settings', child: ListTile(leading: Icon(Icons.settings_outlined), title: Text('Settings'))),
          const PopupMenuDivider(),
          PopupMenuItem(
            value: 'signout',
            child: ListTile(
              leading: const Icon(Icons.logout),
              title: Text(name != null ? 'Sign out ($name)' : 'Sign out'),
            ),
          ),
        ],
        onSelected: (value) {
          switch (value) {
            case 'devices':
              context.go('/devices');
            case 'settings':
              context.go('/settings');
            case 'signout':
              ref.read(authControllerProvider.notifier).signOut();
          }
        },
      ),
    );
  }
}
