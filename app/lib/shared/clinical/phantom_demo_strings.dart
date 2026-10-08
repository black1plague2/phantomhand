/// EN + HI texts of the Phantom Hand in-app demo: the button, the badge, the
/// Stop / Skip controls, the "Simulated run" label and one plain-words caption
/// per phase for someone who has never seen the project. A table in this file
/// (like the witness mirror's and the embodiment report's), not the arb files.
///
/// Wording (`docs/agent-briefs/ph/03-SPEC.md` D11): nothing here says the demo
/// proves, measures or creates consciousness, and simulated data is called
/// simulated wherever it is shown. The Hindi lines are new and need a native
/// check.
library;

import 'package:opus_app/data/models/phantom_live.dart';

class PhantomDemoStrings {
  const new _(this._column);

  /// `hi` gives Hindi; anything else gives English.
  factory forLang(String lang) => lang == 'hi' ? hi : en;

  static const en = PhantomDemoStrings._(0);
  static const hi = PhantomDemoStrings._(1);

  final int _column;

  static const _table = <String, List<String>>{
    'button': ['Run Phantom Hand demo', 'फैंटम हैंड डेमो चलाएँ'],
    'buttonHint': [
      'A simulated run from start to finish, about 1.5 minutes. No headset needed.',
      'शुरू से अंत तक एक सिमुलेटेड रन, लगभग 1.5 मिनट। हेडसेट की ज़रूरत नहीं।',
    ],
    'badge': ['Demo, simulated data', 'डेमो, सिमुलेटेड डेटा'],
    'skip': ['Skip', 'आगे बढ़ें'],
    'stop': ['Stop demo', 'डेमो रोकें'],
    'loading': ['Getting the demo ready', 'डेमो तैयार हो रहा है'],
    'unplayable': ['The recorded run could not be read.', 'रिकॉर्ड किया गया रन पढ़ा नहीं जा सका।'],
    'simulatedRun': ['Simulated run', 'सिमुलेटेड रन'],
    'simulatedNote': [
      'A scripted participant and a simulated sleeve. No real person.',
      'स्क्रिप्ट किया गया प्रतिभागी और सिमुलेटेड स्लीव। कोई असली व्यक्ति नहीं।',
    ],
    'cap_calibrate': [
      'Setting up. The headset finds where your real arm is.',
      'तैयारी। हेडसेट पता लगाता है कि आपकी असली बाँह कहाँ है।',
    ],
    'cap_probe_pre': [
      'Point to where your hand feels. This first answer is the starting point.',
      'बताइए, आपका हाथ कहाँ महसूस होता है। यह पहला जवाब शुरुआती बिंदु है।',
    ],
    'cap_induction': [
      'The brush touches the virtual hand and the sleeve touches your real arm.',
      'ब्रश वर्चुअल हाथ को छूता है और स्लीव आपकी असली बाँह को।',
    ],
    'cap_induction_sync': [
      'Touch seen and felt together. The brush touches the virtual hand while the sleeve touches your real arm at the same moment.',
      'स्पर्श दिखता भी है और महसूस भी होता है। ब्रश वर्चुअल हाथ को छूता है, उसी पल स्लीव आपकी असली बाँह को।',
    ],
    'cap_induction_async': [
      'The touch arrives late. The brush touches the virtual hand, the sleeve touches your real arm a moment afterwards.',
      'स्पर्श देर से आता है। ब्रश वर्चुअल हाथ को छूता है, और स्लीव कुछ पल बाद आपकी असली बाँह को।',
    ],
    'cap_threat': [
      'A stone falls on the virtual hand. The muscle sensor and the arm sensor show any flinch.',
      'वर्चुअल हाथ पर एक पत्थर गिरता है। मांसपेशी सेंसर और बाँह का सेंसर किसी भी झटके को दिखाते हैं।',
    ],
    'cap_probe_post': [
      'Point again to where your hand feels. How far the answer moved is the shift.',
      'फिर से बताइए, आपका हाथ कहाँ महसूस होता है। जवाब कितना खिसका, यही बदलाव है।',
    ],
    'cap_questionnaire': [
      'A rating: how much did the virtual hand feel like your own hand?',
      'एक रेटिंग: वर्चुअल हाथ कितना आपके अपने हाथ जैसा लगा?',
    ],
    'cap_dissolve': [
      'The virtual hand fades away.',
      'वर्चुअल हाथ धीरे-धीरे धुँधला होकर ओझल हो जाता है।',
    ],
    'cap_reveal': [
      'The reveal. The room comes back and your real hand is in view.',
      'असली कमरा लौट आता है और आपका असली हाथ दिखने लगता है।',
    ],
    'cap_witness': [
      'What changed? The results of this run come next.',
      'क्या बदला? इस दौर के नतीजे आगे दिखते हैं।',
    ],
    'cap_mirror': [
      'The audience screen: what changed for the participant in this run.',
      'दर्शकों की स्क्रीन: इस रन में प्रतिभागी के लिए क्या बदला।',
    ],
  };

  String t(String key) {
    final v = _table[key];
    return v == null ? key : v[_column];
  }

  /// The caption of [phase] in plain words; the induction has one for each
  /// condition. Null for a phase the demo has no caption for.
  String? caption(String? phase, PhantomCondition? condition) {
    if (phase == null) return null;
    if (phase == 'induction') {
      return switch (condition) {
        PhantomCondition.sync => t('cap_induction_sync'),
        PhantomCondition.async => t('cap_induction_async'),
        null => t('cap_induction'),
      };
    }
    final key = 'cap_$phase';
    return _table.containsKey(key) ? t(key) : null;
  }
}
