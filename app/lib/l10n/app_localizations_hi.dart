// ignore: unused_import
import 'package:intl/intl.dart' as intl;

import 'app_localizations.dart';

// ignore_for_file: type=lint

/// The translations for Hindi (`hi`).
class AppLocalizationsHi extends AppLocalizations {
  AppLocalizationsHi([String locale = 'hi']) : super(locale);

  @override
  String get appTitle => 'OPUS निदान ऐप';

  @override
  String get navPatients => 'मरीज';

  @override
  String get navPrograms => 'कार्यक्रम';

  @override
  String get navSessions => 'सत्र';

  @override
  String get navProgress => 'प्रगति';

  @override
  String get navLive => 'लाइव';

  @override
  String get navSettings => 'सेटिंग';

  @override
  String get loginTitle => 'साइन इन';

  @override
  String get loginSubtitle =>
      'मॉक प्रमाणीकरण – जारी रखने के लिए एक भूमिका चुनें';

  @override
  String get loginRoleAdmin => 'प्रशासक';

  @override
  String get loginRoleClinician => 'चिकित्सक';

  @override
  String get loginRoleTherapist => 'चिकित्सा विशेषज्ञ';

  @override
  String get loginRoleNurse => 'नर्स';

  @override
  String get loginRolePatient => 'मरीज';

  @override
  String get loginButton => 'जारी रखें';

  @override
  String get signOut => 'साइन आउट';

  @override
  String signedInAs(String name, String role) {
    return '$name के रूप में साइन इन, $role';
  }

  @override
  String get patientListTitle => 'मरीज';

  @override
  String get patientSearchHint => 'नाम या आईडी से खोजें';

  @override
  String get patientFilterAffectedSide => 'प्रभावित साइड';

  @override
  String get patientFilterDiagnosis => 'निदान';

  @override
  String get patientFilterAll => 'सभी';

  @override
  String get patientNoResults => 'इन फ़िल्टर से कोई मरीज मेल नहीं खाती।';

  @override
  String get patientProfileTitle => 'मरीज प्रोफ़ाइल';

  @override
  String patientAge(int age) {
    return 'आयु $age';
  }

  @override
  String patientAffectedSide(String side) {
    return 'प्रभावित साइड: $side';
  }

  @override
  String patientDiagnosis(String diagnosis) {
    return 'निदान: $diagnosis';
  }

  @override
  String get patientTabOverview => 'सारांश';

  @override
  String get patientTabPrograms => 'कार्यक्रम';

  @override
  String get patientTabSessions => 'सत्र';

  @override
  String get patientTabProgress => 'प्रगति';

  @override
  String get patientTabOutcomes => 'परिणाम माप';

  @override
  String get programBuilderTitle => 'कार्यक्रम बनाएं';

  @override
  String get programBuilderNewBlock => 'ब्लॉक जोड़ें';

  @override
  String get programBuilderSelectGame => 'एक गेम चुनें';

  @override
  String get programBuilderSchedule => 'अनुसूची';

  @override
  String programBuilderFrequency(int count, int weeks) {
    return 'सप्ताह में $count बार, $weeks सप्ताह तक';
  }

  @override
  String get programBuilderSupervision => 'निगरानी आवश्यक';

  @override
  String get programBuilderSave => 'कार्यक्रम सहेजें';

  @override
  String get programBuilderPresets => 'प्रीसेट';

  @override
  String get programBuilderNoPreset => 'कस्टम';

  @override
  String get programBuilderRemoveBlock => 'ब्लॉक हटाएं';

  @override
  String get programBuilderReorderHint => 'ब्लॉक क्रम बदलने के लिए खींचें';

  @override
  String get formHelp => 'यह क्या है?';

  @override
  String get formResetToDefault => 'डिफ़ॉल्ट पर रीसेट करें';

  @override
  String get formValidationRequired => 'यह मान आवश्यक है।';

  @override
  String formValidationRange(String min, String max) {
    return '$min और $max के बीच का मान दर्ज करें।';
  }

  @override
  String get sessionListTitle => 'सत्र';

  @override
  String get sessionReportTitle => 'सत्र रिपोर्ट';

  @override
  String get sessionReportTrials => 'ट्रायल';

  @override
  String get sessionReportTrialTable => 'ट्रायल तालिका';

  @override
  String get sessionReportSpeedProfile => 'गति प्रोफ़ाइल';

  @override
  String get sessionReportSparcTrend => 'SPARC प्रवृत्ति';

  @override
  String get sessionReportRtTrend => 'प्रतिक्रिया समय प्रवृत्ति';

  @override
  String get sessionReportWorkspaceHeatmap => 'वर्कस्पेस हीटमैप';

  @override
  String get sessionReportLeftHand => 'बाईं';

  @override
  String get sessionReportRightHand => 'दायां';

  @override
  String get sessionReportPatientReported => 'मरीज द्वारा बताया गया';

  @override
  String sessionReportPain(int value) {
    return 'दर्द $value/10';
  }

  @override
  String sessionReportFatigue(int value) {
    return 'थकान $value/10';
  }

  @override
  String sessionReportEnjoyment(int value) {
    return 'आनंद $value/5';
  }

  @override
  String get qualityOk => 'अच्छी गुणवत्ता';

  @override
  String get qualityDegraded => 'निम्न गुणवत्ता';

  @override
  String get qualityInvalid => 'अमान्य';

  @override
  String get progressTitle => 'प्रगति';

  @override
  String get progressMdcBand => 'न्यूनतम पता लगाने योग्य परिवर्तन पट्टी';

  @override
  String get progressNoChange =>
      'मापन शोर के भीतर (अभी कोई विश्वसनीय बदलाव नहीं)';

  @override
  String get progressImproved => 'विश्वसनीय सुधार';

  @override
  String get progressDeclined => 'विश्वसनीय गिरावट';

  @override
  String get liveTitle => 'लाइव सत्र';

  @override
  String get liveWaitingForStream => 'सत्र शुरू होने की प्रतीक्षा …';

  @override
  String liveCurrentTrial(int index, int total) {
    return 'ट्रायल $index / $total';
  }

  @override
  String get livePause => 'रोकें';

  @override
  String get liveResume => 'फिर से शुरू करें';

  @override
  String get liveStop => 'सत्र रोकें';

  @override
  String get liveStopConfirm =>
      'क्या अभी इस सत्र को रोकना है? इसे वापस नहीं लिया जा सकता।';

  @override
  String get liveStatusRunning => 'चल रहा है';

  @override
  String get liveStatusPaused => 'रुका हुआ';

  @override
  String get liveStatusStopped => 'रुक गया';

  @override
  String get outcomesTitle => 'परिणाम माप';

  @override
  String get outcomesNewEntry => 'नई इंट्री';

  @override
  String get outcomesFmaUe => 'फ्ीगल-मेयर मूल्यांकन (ऊपरी अंग)';

  @override
  String get outcomesArat => 'एक्शन रिसर्च आर्म टेस्ट';

  @override
  String get outcomesBoxBlock => 'बॉक्स एंड ब्लॉक टेस्ट';

  @override
  String get outcomesMas => 'मॉडिफ़ाइड ऐशवर्थ स्केल';

  @override
  String get outcomesDate => 'तिथि';

  @override
  String get outcomesScore => 'स्कोर';

  @override
  String get outcomesSave => 'इंट्री सहेजें';

  @override
  String get outcomesHistory => 'इतिहास';

  @override
  String get commonSave => 'सहेजें';

  @override
  String get commonCancel => 'रद्द करें';

  @override
  String get commonRetry => 'पुनः प्रयास करें';

  @override
  String get commonLoading => 'लोड हो रहा है…';

  @override
  String get commonErrorGeneric => 'कुछ गलत हो गया।';

  @override
  String get commonSimulatedError => 'सिमुलेटेड नेटवर्क त्रुटि (मॉक रिपॉजिटरी)';

  @override
  String get commonDelete => 'हटाएं';

  @override
  String get commonEdit => 'संपादित करें';

  @override
  String get commonClose => 'बंद करें';

  @override
  String get commonNone => 'कोई नहीं';

  @override
  String get commonSide => 'साइड';

  @override
  String get commonLeft => 'बाईं';

  @override
  String get commonRight => 'दायां';

  @override
  String get commonBoth => 'दोनों';

  @override
  String get commonAlternate => 'बारी-बारी';

  @override
  String get phTitle => 'फैंटम हैंड';

  @override
  String get phSectionStatus => 'स्थिति';

  @override
  String get phSectionDevices => 'डिवाइस';

  @override
  String get phSectionSignals => 'लाइव संकेत';

  @override
  String get phSectionControls => 'नियंत्रण';

  @override
  String get phPhaseWaiting => 'शुरू होने की प्रतीक्षा';

  @override
  String get phPhaseCalibrate => 'कैलिब्रेशन';

  @override
  String get phPhaseProbePre => 'इशारा जाँच, पहले';

  @override
  String get phPhaseInduction => 'ब्रश और स्पर्श';

  @override
  String get phPhaseSelfTouch => 'स्वयं स्पर्श';

  @override
  String get phPhaseAgency => 'हाथ पर नियंत्रण';

  @override
  String get phPhaseThreat => 'पत्थर गिरना';

  @override
  String get phPhaseProbePost => 'इशारा जाँच, बाद में';

  @override
  String get phPhaseQuestionnaire => 'प्रश्न';

  @override
  String get phPhaseDissolve => 'धुँधला होना';

  @override
  String get phPhaseReveal => 'असली हाथ दिखना';

  @override
  String get phPhaseWitness => 'नतीजे';

  @override
  String get phPhaseDone => 'पूरा हुआ';

  @override
  String get phCondSync => 'SYNC';

  @override
  String get phCondAsync => 'ASYNC';

  @override
  String get phTimeLeft => 'बचा समय';

  @override
  String get phNodeHaptic => 'स्लीव';

  @override
  String get phNodeBio => 'मांसपेशी सेंसर';

  @override
  String get phConnected => 'जुड़ा है';

  @override
  String get phOffline => 'ऑफ़लाइन';

  @override
  String get phEmgLevel => 'मांसपेशी गतिविधि';

  @override
  String get phTraceEmg => 'मांसपेशी संकेत';

  @override
  String get phTraceAccel => 'बाँह की गति';

  @override
  String phTraceWindow(int seconds) {
    return 'पिछले $seconds सेकंड';
  }

  @override
  String phAxisAgo(int seconds) {
    return '$seconds सेकंड पहले';
  }

  @override
  String get phAxisNow => 'अभी';

  @override
  String get phUnitAccel => 'm/s²';

  @override
  String phResting(String ratio) {
    return 'आराम स्तर का $ratio×';
  }

  @override
  String get phMarkerImpact => 'पत्थर गिरा';

  @override
  String get phMarkerBurst => 'मांसपेशी झटका';

  @override
  String get phNoSignal => 'संकेत की प्रतीक्षा';

  @override
  String get phCmdStart => 'शुरू करें';

  @override
  String get phCmdNext => 'अगला चरण';

  @override
  String get phCmdAbort => 'चरण रोकें';

  @override
  String get phCmdPause => 'रोकें';

  @override
  String get phCmdResume => 'फिर शुरू करें';

  @override
  String get phCmdEnd => 'समाप्त करें';

  @override
  String get phCmdNextPerson => 'अगला व्यक्ति';

  @override
  String get phCondOrder => 'स्थितियों का क्रम';

  @override
  String get phOrderSyncFirst => 'पहले SYNC';

  @override
  String get phOrderAsyncFirst => 'पहले ASYNC';

  @override
  String get phSent => 'भेजा गया';

  @override
  String get phAcked => 'पुष्टि हुई';

  @override
  String get phFailed => 'विफल';

  @override
  String get phObserver => 'दर्शक दृश्य';

  @override
  String get phObserverExit => 'दर्शक दृश्य बंद करें';

  @override
  String get phDemo => 'डेमो, हेडसेट नहीं';

  @override
  String get phLinkQuest => 'क्वेस्ट';

  @override
  String phLinkQuestMs(int ms) {
    return 'क्वेस्ट $ms ms';
  }

  @override
  String phLinkStale(int seconds) {
    return '$seconds सेकंड से कोई अपडेट नहीं';
  }

  @override
  String get phNodeFix => 'पहुँच से बाहर। इसकी पावर केबल और हॉटस्पॉट जाँचें।';

  @override
  String get phConfirmEnd => 'यह सत्र समाप्त करें?';

  @override
  String get phConfirmNextPerson => 'अगले व्यक्ति को शुरू करें?';

  @override
  String get phConfirm => 'पुष्टि करें';

  @override
  String get phTryDemo => 'डेमो देखें';
}
