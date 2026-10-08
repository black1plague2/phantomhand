import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_localizations/flutter_localizations.dart';
import 'package:intl/intl.dart' as intl;

import 'app_localizations_en.dart';
import 'app_localizations_hi.dart';

// ignore_for_file: type=lint

/// Callers can lookup localized strings with an instance of AppLocalizations
/// returned by `AppLocalizations.of(context)`.
///
/// Applications need to include `AppLocalizations.delegate()` in their app's
/// `localizationDelegates` list, and the locales they support in the app's
/// `supportedLocales` list. For example:
///
/// ```dart
/// import 'l10n/app_localizations.dart';
///
/// return MaterialApp(
///   localizationsDelegates: AppLocalizations.localizationsDelegates,
///   supportedLocales: AppLocalizations.supportedLocales,
///   home: MyApplicationHome(),
/// );
/// ```
///
/// ## Update pubspec.yaml
///
/// Please make sure to update your pubspec.yaml to include the following
/// packages:
///
/// ```yaml
/// dependencies:
///   # Internationalization support.
///   flutter_localizations:
///     sdk: flutter
///   intl: any # Use the pinned version from flutter_localizations
///
///   # Rest of dependencies
/// ```
///
/// ## iOS Applications
///
/// iOS applications define key application metadata, including supported
/// locales, in an Info.plist file that is built into the application bundle.
/// To configure the locales supported by your app, you’ll need to edit this
/// file.
///
/// First, open your project’s ios/Runner.xcworkspace Xcode workspace file.
/// Then, in the Project Navigator, open the Info.plist file under the Runner
/// project’s Runner folder.
///
/// Next, select the Information Property List item, select Add Item from the
/// Editor menu, then select Localizations from the pop-up menu.
///
/// Select and expand the newly-created Localizations item then, for each
/// locale your application supports, add a new item and select the locale
/// you wish to add from the pop-up menu in the Value field. This list should
/// be consistent with the languages listed in the AppLocalizations.supportedLocales
/// property.
abstract class AppLocalizations {
  AppLocalizations(String locale)
    : localeName = intl.Intl.canonicalizedLocale(locale.toString());

  final String localeName;

  static AppLocalizations? of(BuildContext context) {
    return Localizations.of<AppLocalizations>(context, AppLocalizations);
  }

  static const LocalizationsDelegate<AppLocalizations> delegate =
      _AppLocalizationsDelegate();

  /// A list of this localizations delegate along with the default localizations
  /// delegates.
  ///
  /// Returns a list of localizations delegates containing this delegate along with
  /// GlobalMaterialLocalizations.delegate, GlobalCupertinoLocalizations.delegate,
  /// and GlobalWidgetsLocalizations.delegate.
  ///
  /// Additional delegates can be added by appending to this list in
  /// MaterialApp. This list does not have to be used at all if a custom list
  /// of delegates is preferred or required.
  static const List<LocalizationsDelegate<dynamic>> localizationsDelegates =
      <LocalizationsDelegate<dynamic>>[
        delegate,
        GlobalMaterialLocalizations.delegate,
        GlobalCupertinoLocalizations.delegate,
        GlobalWidgetsLocalizations.delegate,
      ];

  /// A list of this localizations delegate's supported locales.
  static const List<Locale> supportedLocales = <Locale>[
    Locale('en'),
    Locale('hi'),
  ];

  /// App title shown in the OS task switcher / web tab
  ///
  /// In en, this message translates to:
  /// **'OPUS Clinician'**
  String get appTitle;

  /// No description provided for @navPatients.
  ///
  /// In en, this message translates to:
  /// **'Patients'**
  String get navPatients;

  /// No description provided for @navPrograms.
  ///
  /// In en, this message translates to:
  /// **'Programs'**
  String get navPrograms;

  /// No description provided for @navSessions.
  ///
  /// In en, this message translates to:
  /// **'Sessions'**
  String get navSessions;

  /// No description provided for @navProgress.
  ///
  /// In en, this message translates to:
  /// **'Progress'**
  String get navProgress;

  /// No description provided for @navLive.
  ///
  /// In en, this message translates to:
  /// **'Live'**
  String get navLive;

  /// No description provided for @navSettings.
  ///
  /// In en, this message translates to:
  /// **'Settings'**
  String get navSettings;

  /// No description provided for @loginTitle.
  ///
  /// In en, this message translates to:
  /// **'Sign in'**
  String get loginTitle;

  /// No description provided for @loginSubtitle.
  ///
  /// In en, this message translates to:
  /// **'Mock authentication -- pick a role to continue'**
  String get loginSubtitle;

  /// No description provided for @loginRoleAdmin.
  ///
  /// In en, this message translates to:
  /// **'Administrator'**
  String get loginRoleAdmin;

  /// No description provided for @loginRoleClinician.
  ///
  /// In en, this message translates to:
  /// **'Clinician'**
  String get loginRoleClinician;

  /// No description provided for @loginRoleTherapist.
  ///
  /// In en, this message translates to:
  /// **'Therapist'**
  String get loginRoleTherapist;

  /// No description provided for @loginRoleNurse.
  ///
  /// In en, this message translates to:
  /// **'Nurse'**
  String get loginRoleNurse;

  /// No description provided for @loginRolePatient.
  ///
  /// In en, this message translates to:
  /// **'Patient'**
  String get loginRolePatient;

  /// No description provided for @loginButton.
  ///
  /// In en, this message translates to:
  /// **'Continue'**
  String get loginButton;

  /// No description provided for @signOut.
  ///
  /// In en, this message translates to:
  /// **'Sign out'**
  String get signOut;

  /// No description provided for @signedInAs.
  ///
  /// In en, this message translates to:
  /// **'Signed in as {name}, {role}'**
  String signedInAs(String name, String role);

  /// No description provided for @patientListTitle.
  ///
  /// In en, this message translates to:
  /// **'Patients'**
  String get patientListTitle;

  /// No description provided for @patientSearchHint.
  ///
  /// In en, this message translates to:
  /// **'Search by name or ID'**
  String get patientSearchHint;

  /// No description provided for @patientFilterAffectedSide.
  ///
  /// In en, this message translates to:
  /// **'Affected side'**
  String get patientFilterAffectedSide;

  /// No description provided for @patientFilterDiagnosis.
  ///
  /// In en, this message translates to:
  /// **'Diagnosis'**
  String get patientFilterDiagnosis;

  /// No description provided for @patientFilterAll.
  ///
  /// In en, this message translates to:
  /// **'All'**
  String get patientFilterAll;

  /// No description provided for @patientNoResults.
  ///
  /// In en, this message translates to:
  /// **'No patients match these filters.'**
  String get patientNoResults;

  /// No description provided for @patientProfileTitle.
  ///
  /// In en, this message translates to:
  /// **'Patient profile'**
  String get patientProfileTitle;

  /// No description provided for @patientAge.
  ///
  /// In en, this message translates to:
  /// **'Age {age}'**
  String patientAge(int age);

  /// No description provided for @patientAffectedSide.
  ///
  /// In en, this message translates to:
  /// **'Affected side: {side}'**
  String patientAffectedSide(String side);

  /// No description provided for @patientDiagnosis.
  ///
  /// In en, this message translates to:
  /// **'Diagnosis: {diagnosis}'**
  String patientDiagnosis(String diagnosis);

  /// No description provided for @patientTabOverview.
  ///
  /// In en, this message translates to:
  /// **'Overview'**
  String get patientTabOverview;

  /// No description provided for @patientTabPrograms.
  ///
  /// In en, this message translates to:
  /// **'Programs'**
  String get patientTabPrograms;

  /// No description provided for @patientTabSessions.
  ///
  /// In en, this message translates to:
  /// **'Sessions'**
  String get patientTabSessions;

  /// No description provided for @patientTabProgress.
  ///
  /// In en, this message translates to:
  /// **'Progress'**
  String get patientTabProgress;

  /// No description provided for @patientTabOutcomes.
  ///
  /// In en, this message translates to:
  /// **'Outcome measures'**
  String get patientTabOutcomes;

  /// No description provided for @programBuilderTitle.
  ///
  /// In en, this message translates to:
  /// **'Build program'**
  String get programBuilderTitle;

  /// No description provided for @programBuilderNewBlock.
  ///
  /// In en, this message translates to:
  /// **'Add block'**
  String get programBuilderNewBlock;

  /// No description provided for @programBuilderSelectGame.
  ///
  /// In en, this message translates to:
  /// **'Select a game'**
  String get programBuilderSelectGame;

  /// No description provided for @programBuilderSchedule.
  ///
  /// In en, this message translates to:
  /// **'Schedule'**
  String get programBuilderSchedule;

  /// No description provided for @programBuilderFrequency.
  ///
  /// In en, this message translates to:
  /// **'{count} times / week for {weeks} weeks'**
  String programBuilderFrequency(int count, int weeks);

  /// No description provided for @programBuilderSupervision.
  ///
  /// In en, this message translates to:
  /// **'Requires supervision'**
  String get programBuilderSupervision;

  /// No description provided for @programBuilderSave.
  ///
  /// In en, this message translates to:
  /// **'Save program'**
  String get programBuilderSave;

  /// No description provided for @programBuilderPresets.
  ///
  /// In en, this message translates to:
  /// **'Presets'**
  String get programBuilderPresets;

  /// No description provided for @programBuilderNoPreset.
  ///
  /// In en, this message translates to:
  /// **'Custom'**
  String get programBuilderNoPreset;

  /// No description provided for @programBuilderRemoveBlock.
  ///
  /// In en, this message translates to:
  /// **'Remove block'**
  String get programBuilderRemoveBlock;

  /// No description provided for @programBuilderReorderHint.
  ///
  /// In en, this message translates to:
  /// **'Drag to reorder blocks'**
  String get programBuilderReorderHint;

  /// No description provided for @formHelp.
  ///
  /// In en, this message translates to:
  /// **'What is this?'**
  String get formHelp;

  /// No description provided for @formResetToDefault.
  ///
  /// In en, this message translates to:
  /// **'Reset to default'**
  String get formResetToDefault;

  /// No description provided for @formValidationRequired.
  ///
  /// In en, this message translates to:
  /// **'This value is required.'**
  String get formValidationRequired;

  /// No description provided for @formValidationRange.
  ///
  /// In en, this message translates to:
  /// **'Enter a value between {min} and {max}.'**
  String formValidationRange(String min, String max);

  /// No description provided for @sessionListTitle.
  ///
  /// In en, this message translates to:
  /// **'Sessions'**
  String get sessionListTitle;

  /// No description provided for @sessionReportTitle.
  ///
  /// In en, this message translates to:
  /// **'Session report'**
  String get sessionReportTitle;

  /// No description provided for @sessionReportTrials.
  ///
  /// In en, this message translates to:
  /// **'Trials'**
  String get sessionReportTrials;

  /// No description provided for @sessionReportTrialTable.
  ///
  /// In en, this message translates to:
  /// **'Trial table'**
  String get sessionReportTrialTable;

  /// No description provided for @sessionReportSpeedProfile.
  ///
  /// In en, this message translates to:
  /// **'Speed profile'**
  String get sessionReportSpeedProfile;

  /// No description provided for @sessionReportSparcTrend.
  ///
  /// In en, this message translates to:
  /// **'SPARC trend'**
  String get sessionReportSparcTrend;

  /// No description provided for @sessionReportRtTrend.
  ///
  /// In en, this message translates to:
  /// **'Reaction time trend'**
  String get sessionReportRtTrend;

  /// No description provided for @sessionReportWorkspaceHeatmap.
  ///
  /// In en, this message translates to:
  /// **'Workspace heatmap'**
  String get sessionReportWorkspaceHeatmap;

  /// No description provided for @sessionReportLeftHand.
  ///
  /// In en, this message translates to:
  /// **'Left'**
  String get sessionReportLeftHand;

  /// No description provided for @sessionReportRightHand.
  ///
  /// In en, this message translates to:
  /// **'Right'**
  String get sessionReportRightHand;

  /// No description provided for @sessionReportPatientReported.
  ///
  /// In en, this message translates to:
  /// **'Patient-reported'**
  String get sessionReportPatientReported;

  /// No description provided for @sessionReportPain.
  ///
  /// In en, this message translates to:
  /// **'Pain {value}/10'**
  String sessionReportPain(int value);

  /// No description provided for @sessionReportFatigue.
  ///
  /// In en, this message translates to:
  /// **'Fatigue {value}/10'**
  String sessionReportFatigue(int value);

  /// No description provided for @sessionReportEnjoyment.
  ///
  /// In en, this message translates to:
  /// **'Enjoyment {value}/5'**
  String sessionReportEnjoyment(int value);

  /// No description provided for @qualityOk.
  ///
  /// In en, this message translates to:
  /// **'Good quality'**
  String get qualityOk;

  /// No description provided for @qualityDegraded.
  ///
  /// In en, this message translates to:
  /// **'Degraded'**
  String get qualityDegraded;

  /// No description provided for @qualityInvalid.
  ///
  /// In en, this message translates to:
  /// **'Invalid'**
  String get qualityInvalid;

  /// No description provided for @progressTitle.
  ///
  /// In en, this message translates to:
  /// **'Progress'**
  String get progressTitle;

  /// No description provided for @progressMdcBand.
  ///
  /// In en, this message translates to:
  /// **'Minimal detectable change band'**
  String get progressMdcBand;

  /// No description provided for @progressNoChange.
  ///
  /// In en, this message translates to:
  /// **'Within measurement noise (no reliable change yet)'**
  String get progressNoChange;

  /// No description provided for @progressImproved.
  ///
  /// In en, this message translates to:
  /// **'Reliable improvement'**
  String get progressImproved;

  /// No description provided for @progressDeclined.
  ///
  /// In en, this message translates to:
  /// **'Reliable decline'**
  String get progressDeclined;

  /// No description provided for @liveTitle.
  ///
  /// In en, this message translates to:
  /// **'Live session'**
  String get liveTitle;

  /// No description provided for @liveWaitingForStream.
  ///
  /// In en, this message translates to:
  /// **'Waiting for the session to start…'**
  String get liveWaitingForStream;

  /// No description provided for @liveCurrentTrial.
  ///
  /// In en, this message translates to:
  /// **'Trial {index} of {total}'**
  String liveCurrentTrial(int index, int total);

  /// No description provided for @livePause.
  ///
  /// In en, this message translates to:
  /// **'Pause'**
  String get livePause;

  /// No description provided for @liveResume.
  ///
  /// In en, this message translates to:
  /// **'Resume'**
  String get liveResume;

  /// No description provided for @liveStop.
  ///
  /// In en, this message translates to:
  /// **'Stop session'**
  String get liveStop;

  /// No description provided for @liveStopConfirm.
  ///
  /// In en, this message translates to:
  /// **'Stop this session now? This cannot be undone.'**
  String get liveStopConfirm;

  /// No description provided for @liveStatusRunning.
  ///
  /// In en, this message translates to:
  /// **'Running'**
  String get liveStatusRunning;

  /// No description provided for @liveStatusPaused.
  ///
  /// In en, this message translates to:
  /// **'Paused'**
  String get liveStatusPaused;

  /// No description provided for @liveStatusStopped.
  ///
  /// In en, this message translates to:
  /// **'Stopped'**
  String get liveStatusStopped;

  /// No description provided for @outcomesTitle.
  ///
  /// In en, this message translates to:
  /// **'Outcome measures'**
  String get outcomesTitle;

  /// No description provided for @outcomesNewEntry.
  ///
  /// In en, this message translates to:
  /// **'New entry'**
  String get outcomesNewEntry;

  /// No description provided for @outcomesFmaUe.
  ///
  /// In en, this message translates to:
  /// **'Fugl-Meyer Assessment (Upper Extremity)'**
  String get outcomesFmaUe;

  /// No description provided for @outcomesArat.
  ///
  /// In en, this message translates to:
  /// **'Action Research Arm Test'**
  String get outcomesArat;

  /// No description provided for @outcomesBoxBlock.
  ///
  /// In en, this message translates to:
  /// **'Box and Block Test'**
  String get outcomesBoxBlock;

  /// No description provided for @outcomesMas.
  ///
  /// In en, this message translates to:
  /// **'Modified Ashworth Scale'**
  String get outcomesMas;

  /// No description provided for @outcomesDate.
  ///
  /// In en, this message translates to:
  /// **'Date'**
  String get outcomesDate;

  /// No description provided for @outcomesScore.
  ///
  /// In en, this message translates to:
  /// **'Score'**
  String get outcomesScore;

  /// No description provided for @outcomesSave.
  ///
  /// In en, this message translates to:
  /// **'Save entry'**
  String get outcomesSave;

  /// No description provided for @outcomesHistory.
  ///
  /// In en, this message translates to:
  /// **'History'**
  String get outcomesHistory;

  /// No description provided for @commonSave.
  ///
  /// In en, this message translates to:
  /// **'Save'**
  String get commonSave;

  /// No description provided for @commonCancel.
  ///
  /// In en, this message translates to:
  /// **'Cancel'**
  String get commonCancel;

  /// No description provided for @commonRetry.
  ///
  /// In en, this message translates to:
  /// **'Retry'**
  String get commonRetry;

  /// No description provided for @commonLoading.
  ///
  /// In en, this message translates to:
  /// **'Loading…'**
  String get commonLoading;

  /// No description provided for @commonErrorGeneric.
  ///
  /// In en, this message translates to:
  /// **'Something went wrong.'**
  String get commonErrorGeneric;

  /// No description provided for @commonSimulatedError.
  ///
  /// In en, this message translates to:
  /// **'Simulated network error (mock repository)'**
  String get commonSimulatedError;

  /// No description provided for @commonDelete.
  ///
  /// In en, this message translates to:
  /// **'Delete'**
  String get commonDelete;

  /// No description provided for @commonEdit.
  ///
  /// In en, this message translates to:
  /// **'Edit'**
  String get commonEdit;

  /// No description provided for @commonClose.
  ///
  /// In en, this message translates to:
  /// **'Close'**
  String get commonClose;

  /// No description provided for @commonNone.
  ///
  /// In en, this message translates to:
  /// **'None'**
  String get commonNone;

  /// No description provided for @commonSide.
  ///
  /// In en, this message translates to:
  /// **'Side'**
  String get commonSide;

  /// No description provided for @commonLeft.
  ///
  /// In en, this message translates to:
  /// **'Left'**
  String get commonLeft;

  /// No description provided for @commonRight.
  ///
  /// In en, this message translates to:
  /// **'Right'**
  String get commonRight;

  /// No description provided for @commonBoth.
  ///
  /// In en, this message translates to:
  /// **'Both'**
  String get commonBoth;

  /// No description provided for @commonAlternate.
  ///
  /// In en, this message translates to:
  /// **'Alternate'**
  String get commonAlternate;

  /// No description provided for @phTitle.
  ///
  /// In en, this message translates to:
  /// **'Phantom Hand'**
  String get phTitle;

  /// No description provided for @phSectionStatus.
  ///
  /// In en, this message translates to:
  /// **'Status'**
  String get phSectionStatus;

  /// No description provided for @phSectionDevices.
  ///
  /// In en, this message translates to:
  /// **'Devices'**
  String get phSectionDevices;

  /// No description provided for @phSectionSignals.
  ///
  /// In en, this message translates to:
  /// **'Live signals'**
  String get phSectionSignals;

  /// No description provided for @phSectionControls.
  ///
  /// In en, this message translates to:
  /// **'Controls'**
  String get phSectionControls;

  /// No description provided for @phPhaseWaiting.
  ///
  /// In en, this message translates to:
  /// **'Waiting to start'**
  String get phPhaseWaiting;

  /// No description provided for @phPhaseCalibrate.
  ///
  /// In en, this message translates to:
  /// **'Calibrating'**
  String get phPhaseCalibrate;

  /// No description provided for @phPhaseProbePre.
  ///
  /// In en, this message translates to:
  /// **'Pointing check, before'**
  String get phPhaseProbePre;

  /// No description provided for @phPhaseInduction.
  ///
  /// In en, this message translates to:
  /// **'Brush and touch'**
  String get phPhaseInduction;

  /// No description provided for @phPhaseSelfTouch.
  ///
  /// In en, this message translates to:
  /// **'Self-touch'**
  String get phPhaseSelfTouch;

  /// No description provided for @phPhaseAgency.
  ///
  /// In en, this message translates to:
  /// **'Hand control'**
  String get phPhaseAgency;

  /// No description provided for @phPhaseThreat.
  ///
  /// In en, this message translates to:
  /// **'Stone drop'**
  String get phPhaseThreat;

  /// No description provided for @phPhaseProbePost.
  ///
  /// In en, this message translates to:
  /// **'Pointing check, after'**
  String get phPhaseProbePost;

  /// No description provided for @phPhaseQuestionnaire.
  ///
  /// In en, this message translates to:
  /// **'Questions'**
  String get phPhaseQuestionnaire;

  /// No description provided for @phPhaseDissolve.
  ///
  /// In en, this message translates to:
  /// **'Fading out'**
  String get phPhaseDissolve;

  /// No description provided for @phPhaseReveal.
  ///
  /// In en, this message translates to:
  /// **'Reveal'**
  String get phPhaseReveal;

  /// No description provided for @phPhaseWitness.
  ///
  /// In en, this message translates to:
  /// **'Results'**
  String get phPhaseWitness;

  /// No description provided for @phPhaseDone.
  ///
  /// In en, this message translates to:
  /// **'Done'**
  String get phPhaseDone;

  /// No description provided for @phCondSync.
  ///
  /// In en, this message translates to:
  /// **'SYNC'**
  String get phCondSync;

  /// No description provided for @phCondAsync.
  ///
  /// In en, this message translates to:
  /// **'ASYNC'**
  String get phCondAsync;

  /// No description provided for @phCondNone.
  ///
  /// In en, this message translates to:
  /// **'No condition'**
  String get phCondNone;

  /// No description provided for @phTimeLeft.
  ///
  /// In en, this message translates to:
  /// **'Time left'**
  String get phTimeLeft;

  /// No description provided for @phNodeHaptic.
  ///
  /// In en, this message translates to:
  /// **'Sleeve'**
  String get phNodeHaptic;

  /// No description provided for @phNodeBio.
  ///
  /// In en, this message translates to:
  /// **'Muscle sensor'**
  String get phNodeBio;

  /// No description provided for @phConnected.
  ///
  /// In en, this message translates to:
  /// **'Connected'**
  String get phConnected;

  /// No description provided for @phOffline.
  ///
  /// In en, this message translates to:
  /// **'Offline'**
  String get phOffline;

  /// No description provided for @phEmgLevel.
  ///
  /// In en, this message translates to:
  /// **'Muscle activity'**
  String get phEmgLevel;

  /// No description provided for @phTraceEmg.
  ///
  /// In en, this message translates to:
  /// **'Muscle signal'**
  String get phTraceEmg;

  /// No description provided for @phTraceAccel.
  ///
  /// In en, this message translates to:
  /// **'Arm movement'**
  String get phTraceAccel;

  /// No description provided for @phTraceWindow.
  ///
  /// In en, this message translates to:
  /// **'Last 10 s'**
  String get phTraceWindow;

  /// No description provided for @phMarkerImpact.
  ///
  /// In en, this message translates to:
  /// **'Stone lands'**
  String get phMarkerImpact;

  /// No description provided for @phMarkerBurst.
  ///
  /// In en, this message translates to:
  /// **'Muscle burst'**
  String get phMarkerBurst;

  /// No description provided for @phNoSignal.
  ///
  /// In en, this message translates to:
  /// **'Waiting for signal'**
  String get phNoSignal;

  /// No description provided for @phCmdStart.
  ///
  /// In en, this message translates to:
  /// **'Start'**
  String get phCmdStart;

  /// No description provided for @phCmdNext.
  ///
  /// In en, this message translates to:
  /// **'Next phase'**
  String get phCmdNext;

  /// No description provided for @phCmdAbort.
  ///
  /// In en, this message translates to:
  /// **'Abort phase'**
  String get phCmdAbort;

  /// No description provided for @phCmdPause.
  ///
  /// In en, this message translates to:
  /// **'Pause'**
  String get phCmdPause;

  /// No description provided for @phCmdResume.
  ///
  /// In en, this message translates to:
  /// **'Resume'**
  String get phCmdResume;

  /// No description provided for @phCmdEnd.
  ///
  /// In en, this message translates to:
  /// **'End'**
  String get phCmdEnd;

  /// No description provided for @phCmdNextPerson.
  ///
  /// In en, this message translates to:
  /// **'Next person'**
  String get phCmdNextPerson;

  /// No description provided for @phCondOrder.
  ///
  /// In en, this message translates to:
  /// **'Condition order'**
  String get phCondOrder;

  /// No description provided for @phOrderSyncFirst.
  ///
  /// In en, this message translates to:
  /// **'Sync first'**
  String get phOrderSyncFirst;

  /// No description provided for @phOrderAsyncFirst.
  ///
  /// In en, this message translates to:
  /// **'Async first'**
  String get phOrderAsyncFirst;

  /// No description provided for @phSent.
  ///
  /// In en, this message translates to:
  /// **'Sent'**
  String get phSent;

  /// No description provided for @phAcked.
  ///
  /// In en, this message translates to:
  /// **'Confirmed'**
  String get phAcked;

  /// No description provided for @phFailed.
  ///
  /// In en, this message translates to:
  /// **'Failed'**
  String get phFailed;

  /// No description provided for @phObserver.
  ///
  /// In en, this message translates to:
  /// **'Observer view'**
  String get phObserver;

  /// No description provided for @phObserverExit.
  ///
  /// In en, this message translates to:
  /// **'Exit observer view'**
  String get phObserverExit;

  /// No description provided for @phDemo.
  ///
  /// In en, this message translates to:
  /// **'Demo, no headset'**
  String get phDemo;

  /// No description provided for @phHeadsetOffline.
  ///
  /// In en, this message translates to:
  /// **'Headset offline'**
  String get phHeadsetOffline;

  /// No description provided for @phConfirmEnd.
  ///
  /// In en, this message translates to:
  /// **'End this session?'**
  String get phConfirmEnd;

  /// No description provided for @phConfirmNextPerson.
  ///
  /// In en, this message translates to:
  /// **'Start the next person?'**
  String get phConfirmNextPerson;

  /// No description provided for @phConfirm.
  ///
  /// In en, this message translates to:
  /// **'Confirm'**
  String get phConfirm;

  /// No description provided for @phTryDemo.
  ///
  /// In en, this message translates to:
  /// **'Try demo'**
  String get phTryDemo;
}

class _AppLocalizationsDelegate
    extends LocalizationsDelegate<AppLocalizations> {
  const _AppLocalizationsDelegate();

  @override
  Future<AppLocalizations> load(Locale locale) {
    return SynchronousFuture<AppLocalizations>(lookupAppLocalizations(locale));
  }

  @override
  bool isSupported(Locale locale) =>
      <String>['en', 'hi'].contains(locale.languageCode);

  @override
  bool shouldReload(_AppLocalizationsDelegate old) => false;
}

AppLocalizations lookupAppLocalizations(Locale locale) {
  // Lookup logic when only language code is specified.
  switch (locale.languageCode) {
    case 'en':
      return AppLocalizationsEn();
    case 'hi':
      return AppLocalizationsHi();
  }

  throw FlutterError(
    'AppLocalizations.delegate failed to load unsupported locale "$locale". This is likely '
    'an issue with the localizations generation tool. Please file an issue '
    'on GitHub with a reproducible sample app and the gen-l10n configuration '
    'that was used.',
  );
}
