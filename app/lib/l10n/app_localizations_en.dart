// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for English (`en`).
class AppLocalizationsEn extends AppLocalizations {
  AppLocalizationsEn([String locale = 'en']) : super(locale);

  @override
  String get appTitle => 'OPUS Clinician';

  @override
  String get navPatients => 'Patients';

  @override
  String get navPrograms => 'Programs';

  @override
  String get navSessions => 'Sessions';

  @override
  String get navProgress => 'Progress';

  @override
  String get navLive => 'Live';

  @override
  String get navSettings => 'Settings';

  @override
  String get loginTitle => 'Sign in';

  @override
  String get loginSubtitle => 'Mock authentication -- pick a role to continue';

  @override
  String get loginRoleAdmin => 'Administrator';

  @override
  String get loginRoleClinician => 'Clinician';

  @override
  String get loginRoleTherapist => 'Therapist';

  @override
  String get loginRoleNurse => 'Nurse';

  @override
  String get loginRolePatient => 'Patient';

  @override
  String get loginButton => 'Continue';

  @override
  String get signOut => 'Sign out';

  @override
  String signedInAs(String name, String role) {
    return 'Signed in as $name, $role';
  }

  @override
  String get patientListTitle => 'Patients';

  @override
  String get patientSearchHint => 'Search by name or ID';

  @override
  String get patientFilterAffectedSide => 'Affected side';

  @override
  String get patientFilterDiagnosis => 'Diagnosis';

  @override
  String get patientFilterAll => 'All';

  @override
  String get patientNoResults => 'No patients match these filters.';

  @override
  String get patientProfileTitle => 'Patient profile';

  @override
  String patientAge(int age) {
    return 'Age $age';
  }

  @override
  String patientAffectedSide(String side) {
    return 'Affected side: $side';
  }

  @override
  String patientDiagnosis(String diagnosis) {
    return 'Diagnosis: $diagnosis';
  }

  @override
  String get patientTabOverview => 'Overview';

  @override
  String get patientTabPrograms => 'Programs';

  @override
  String get patientTabSessions => 'Sessions';

  @override
  String get patientTabProgress => 'Progress';

  @override
  String get patientTabOutcomes => 'Outcome measures';

  @override
  String get programBuilderTitle => 'Build program';

  @override
  String get programBuilderNewBlock => 'Add block';

  @override
  String get programBuilderSelectGame => 'Select a game';

  @override
  String get programBuilderSchedule => 'Schedule';

  @override
  String programBuilderFrequency(int count, int weeks) {
    return '$count times / week for $weeks weeks';
  }

  @override
  String get programBuilderSupervision => 'Requires supervision';

  @override
  String get programBuilderSave => 'Save program';

  @override
  String get programBuilderPresets => 'Presets';

  @override
  String get programBuilderNoPreset => 'Custom';

  @override
  String get programBuilderRemoveBlock => 'Remove block';

  @override
  String get programBuilderReorderHint => 'Drag to reorder blocks';

  @override
  String get formHelp => 'What is this?';

  @override
  String get formResetToDefault => 'Reset to default';

  @override
  String get formValidationRequired => 'This value is required.';

  @override
  String formValidationRange(String min, String max) {
    return 'Enter a value between $min and $max.';
  }

  @override
  String get sessionListTitle => 'Sessions';

  @override
  String get sessionReportTitle => 'Session report';

  @override
  String get sessionReportTrials => 'Trials';

  @override
  String get sessionReportTrialTable => 'Trial table';

  @override
  String get sessionReportSpeedProfile => 'Speed profile';

  @override
  String get sessionReportSparcTrend => 'SPARC trend';

  @override
  String get sessionReportRtTrend => 'Reaction time trend';

  @override
  String get sessionReportWorkspaceHeatmap => 'Workspace heatmap';

  @override
  String get sessionReportLeftHand => 'Left';

  @override
  String get sessionReportRightHand => 'Right';

  @override
  String get sessionReportPatientReported => 'Patient-reported';

  @override
  String sessionReportPain(int value) {
    return 'Pain $value/10';
  }

  @override
  String sessionReportFatigue(int value) {
    return 'Fatigue $value/10';
  }

  @override
  String sessionReportEnjoyment(int value) {
    return 'Enjoyment $value/5';
  }

  @override
  String get qualityOk => 'Good quality';

  @override
  String get qualityDegraded => 'Degraded';

  @override
  String get qualityInvalid => 'Invalid';

  @override
  String get progressTitle => 'Progress';

  @override
  String get progressMdcBand => 'Minimal detectable change band';

  @override
  String get progressNoChange =>
      'Within measurement noise (no reliable change yet)';

  @override
  String get progressImproved => 'Reliable improvement';

  @override
  String get progressDeclined => 'Reliable decline';

  @override
  String get liveTitle => 'Live session';

  @override
  String get liveWaitingForStream => 'Waiting for the session to start…';

  @override
  String liveCurrentTrial(int index, int total) {
    return 'Trial $index of $total';
  }

  @override
  String get livePause => 'Pause';

  @override
  String get liveResume => 'Resume';

  @override
  String get liveStop => 'Stop session';

  @override
  String get liveStopConfirm => 'Stop this session now? This cannot be undone.';

  @override
  String get liveStatusRunning => 'Running';

  @override
  String get liveStatusPaused => 'Paused';

  @override
  String get liveStatusStopped => 'Stopped';

  @override
  String get outcomesTitle => 'Outcome measures';

  @override
  String get outcomesNewEntry => 'New entry';

  @override
  String get outcomesFmaUe => 'Fugl-Meyer Assessment (Upper Extremity)';

  @override
  String get outcomesArat => 'Action Research Arm Test';

  @override
  String get outcomesBoxBlock => 'Box and Block Test';

  @override
  String get outcomesMas => 'Modified Ashworth Scale';

  @override
  String get outcomesDate => 'Date';

  @override
  String get outcomesScore => 'Score';

  @override
  String get outcomesSave => 'Save entry';

  @override
  String get outcomesHistory => 'History';

  @override
  String get commonSave => 'Save';

  @override
  String get commonCancel => 'Cancel';

  @override
  String get commonRetry => 'Retry';

  @override
  String get commonLoading => 'Loading…';

  @override
  String get commonErrorGeneric => 'Something went wrong.';

  @override
  String get commonSimulatedError =>
      'Simulated network error (mock repository)';

  @override
  String get commonDelete => 'Delete';

  @override
  String get commonEdit => 'Edit';

  @override
  String get commonClose => 'Close';

  @override
  String get commonNone => 'None';

  @override
  String get commonSide => 'Side';

  @override
  String get commonLeft => 'Left';

  @override
  String get commonRight => 'Right';

  @override
  String get commonBoth => 'Both';

  @override
  String get commonAlternate => 'Alternate';

  @override
  String get phTitle => 'Phantom Hand';

  @override
  String get phSectionStatus => 'Status';

  @override
  String get phSectionDevices => 'Devices';

  @override
  String get phSectionSignals => 'Live signals';

  @override
  String get phSectionControls => 'Controls';

  @override
  String get phPhaseWaiting => 'Waiting to start';

  @override
  String get phPhaseCalibrate => 'Calibrating';

  @override
  String get phPhaseProbePre => 'Pointing check, before';

  @override
  String get phPhaseInduction => 'Brush and touch';

  @override
  String get phPhaseSelfTouch => 'Self-touch';

  @override
  String get phPhaseAgency => 'Hand control';

  @override
  String get phPhaseThreat => 'Stone drop';

  @override
  String get phPhaseProbePost => 'Pointing check, after';

  @override
  String get phPhaseQuestionnaire => 'Questions';

  @override
  String get phPhaseDissolve => 'Fading out';

  @override
  String get phPhaseReveal => 'Reveal';

  @override
  String get phPhaseWitness => 'Results';

  @override
  String get phPhaseDone => 'Done';

  @override
  String get phCondSync => 'SYNC';

  @override
  String get phCondAsync => 'ASYNC';

  @override
  String get phCondNone => 'No condition';

  @override
  String get phTimeLeft => 'Time left';

  @override
  String get phNodeHaptic => 'Sleeve';

  @override
  String get phNodeBio => 'Muscle sensor';

  @override
  String get phConnected => 'Connected';

  @override
  String get phOffline => 'Offline';

  @override
  String get phEmgLevel => 'Muscle activity';

  @override
  String get phTraceEmg => 'Muscle signal';

  @override
  String get phTraceAccel => 'Arm movement';

  @override
  String get phTraceWindow => 'Last 10 s';

  @override
  String get phMarkerImpact => 'Stone lands';

  @override
  String get phMarkerBurst => 'Muscle burst';

  @override
  String get phNoSignal => 'Waiting for signal';

  @override
  String get phCmdStart => 'Start';

  @override
  String get phCmdNext => 'Next phase';

  @override
  String get phCmdAbort => 'Abort phase';

  @override
  String get phCmdPause => 'Pause';

  @override
  String get phCmdResume => 'Resume';

  @override
  String get phCmdEnd => 'End';

  @override
  String get phCmdNextPerson => 'Next person';

  @override
  String get phCondOrder => 'Condition order';

  @override
  String get phOrderSyncFirst => 'Sync first';

  @override
  String get phOrderAsyncFirst => 'Async first';

  @override
  String get phSent => 'Sent';

  @override
  String get phAcked => 'Confirmed';

  @override
  String get phFailed => 'Failed';

  @override
  String get phObserver => 'Observer view';

  @override
  String get phObserverExit => 'Exit observer view';

  @override
  String get phDemo => 'Demo, no headset';

  @override
  String get phHeadsetOffline => 'Headset offline';

  @override
  String get phConfirmEnd => 'End this session?';

  @override
  String get phConfirmNextPerson => 'Start the next person?';

  @override
  String get phConfirm => 'Confirm';

  @override
  String get phTryDemo => 'Try demo';
}
