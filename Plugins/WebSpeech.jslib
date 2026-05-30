mergeInto(LibraryManager.library, {

  // ─── STT (SpeechRecognition) ─────────────────────────────────────────────

  JS_StartSTT: function () {
    var SpeechRecognition = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SpeechRecognition) {
      console.warn('[WebSpeech] SpeechRecognition not supported');
      return;
    }

    if (window._mmsRecognition) {
      window._mmsRecognition.abort();
    }

    var rec = new SpeechRecognition();
    rec.lang           = 'ko-KR';
    rec.interimResults = false;
    rec.maxAlternatives = 1;

    rec.onresult = function (e) {
      var text = e.results[0][0].transcript;
      // GeminiClient.OnSpeechResult(text) 호출
      SendMessage('GeminiClient', 'OnSpeechResult', text);
    };

    rec.onerror = function (e) {
      console.error('[WebSpeech] STT error:', e.error);
    };

    rec.onend = function () {
      window._mmsRecognition = null;
    };

    window._mmsRecognition = rec;
    rec.start();
  },

  JS_StopSTT: function () {
    if (window._mmsRecognition) {
      window._mmsRecognition.stop();
      window._mmsRecognition = null;
    }
  },

  // ─── TTS (SpeechSynthesis) ───────────────────────────────────────────────

  JS_Speak: function (textPtr) {
    var text = UTF8ToString(textPtr);
    if (!window.speechSynthesis) {
      console.warn('[WebSpeech] SpeechSynthesis not supported');
      return;
    }

    window.speechSynthesis.cancel();

    var utter = new SpeechSynthesisUtterance(text);
    utter.lang  = 'ko-KR';
    utter.rate  = 1.0;
    utter.pitch = 1.0;

    // 한국어 음성 선택 (있을 경우)
    var voices = window.speechSynthesis.getVoices();
    var koVoice = voices.find(function (v) { return v.lang === 'ko-KR'; });
    if (koVoice) utter.voice = koVoice;

    utter.onend = function () {
      // TTS 끝나면 Unity에 알림 — 필요 시 GeminiClient에 OnTTSEnd 메서드 추가
      SendMessage('GeminiClient', 'OnTTSEnd', '');
    };

    window.speechSynthesis.speak(utter);
  },

});
