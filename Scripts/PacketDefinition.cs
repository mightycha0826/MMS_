using System;

// ─── Unity -> relay (보내는 메시지) ───────────────────────────
// 매 턴마다 이 하나로 통합해서 보냄 (서버가 세션 상태를 기억하지 않으므로
// last_question을 매번 다시 실어 보내야 함)
[Serializable]
public class ClientMsg
{
    public string type; // "client_msg" 고정
    public string department;    // 지원 학과, 없으면 서버가 "미지정" 처리
    public string last_question; // 직전 면접관 질문
    public string text;          // 지원자 답변 텍스트
}

// ─── relay -> Unity (받는 메시지) ───────────────────────────
[Serializable]
public class ServerMsg
{
    public string type; // "ready", "processing", "server_content", "error"
    public string message_id;
    public string client_session_id; // 라우팅용 ID, 보통 참조 불필요
    public ServerContent content;
    public GeminiAnalysis gemini_analysis;
    public string message; // error용
}

[Serializable]
public class ServerContent
{
    public string text;
    public EmotionInfo emotion;
    public string audio; // base64 WAV (Supertone TTS), 없을 수 있음
}

[Serializable]
public class EmotionInfo
{
    public string label;     // neutral | smile | shy | serious | confused | pressuring | satisfied
    public float score;      // 0.0 ~ 1.0
    public string intensity; // low | medium | high
    public string action;    // avatar_neutral | avatar_smile | avatar_shy | avatar_serious | avatar_tilt | avatar_stern | avatar_nod
}

[Serializable]
public class GeminiAnalysis
{
    public string dept;
    public string dept_reasoning;
    public string[] keywords;
    public string summary;
}
