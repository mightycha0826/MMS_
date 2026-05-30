using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using NativeWebSocket;

/// <summary>
/// gemini-relay Cloudflare Worker와 WebSocket으로 통신하는 클라이언트.
/// 면접 세션 시작, 지원자 답변 전송, 결과 수신 + TTS 재생을 담당합니다.
/// </summary>
public class GeminiClient : MonoBehaviour
{
    [Header("Relay 설정")]
    [SerializeField] private string relayUrl = "wss://gemini-relay.mightycha0826.workers.dev";

    [Header("연결 컴포넌트")]
    [SerializeField] private InterviewerController interviewer;
    [SerializeField] private SubtitleManager       subtitleManager;
    [SerializeField] private MicDisplayController  micDisplay;

    public event Action<ServerMsg> OnServerContent;
    public event Action           OnReady;
    public event Action           OnProcessing;
    public event Action<string>   OnError;

    private WebSocket _ws;
    private bool      _isReady;

    // ─── JS 브리지 (Web Speech API) ───────────────────────────────────────

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void JS_StartSTT();
    [DllImport("__Internal")] private static extern void JS_StopSTT();
    [DllImport("__Internal")] private static extern void JS_Speak(string text);
#else
    private static void JS_StartSTT()           => Debug.Log("[GeminiClient] STT 시작 (에디터 모의)");
    private static void JS_StopSTT()            => Debug.Log("[GeminiClient] STT 중지 (에디터 모의)");
    private static void JS_Speak(string text)   => Debug.Log($"[GeminiClient] TTS: {text}");
#endif

    // ─── 생명주기 ──────────────────────────────────────────────────────────

    private async void Start()
    {
        await Connect();
    }

    private void Update()
    {
#if !UNITY_WEBGL || UNITY_EDITOR
        _ws?.DispatchMessageQueue();
#endif
    }

    private async void OnDestroy()
    {
        if (_ws != null)
            await _ws.Close();
    }

    // ─── 연결 ──────────────────────────────────────────────────────────────

    private async System.Threading.Tasks.Task Connect()
    {
        _ws = new WebSocket(relayUrl);

        _ws.OnOpen    += HandleOpen;
        _ws.OnMessage += HandleMessage;
        _ws.OnError   += HandleError;
        _ws.OnClose   += HandleClose;

        Debug.Log($"[GeminiClient] 연결 중... {relayUrl}");
        await _ws.Connect();
    }

    // ─── 공개 API ──────────────────────────────────────────────────────────

    /// <summary>면접 세션을 시작합니다.</summary>
    public void SendSessionStart(string lastQuestion = "")
    {
        Send(JsonUtility.ToJson(new SessionStartPacket
        {
            type          = "session_start",
            last_question = lastQuestion,
        }));
    }

    /// <summary>지원자 답변 텍스트를 relay로 전송합니다.</summary>
    public void SendUserSpeech(string text)
    {
        if (!_isReady)
        {
            Debug.LogWarning("[GeminiClient] relay가 아직 준비되지 않았습니다.");
            return;
        }
        Send(JsonUtility.ToJson(new UserSpeechPacket
        {
            type = "user_speech",
            text = text,
        }));
    }

    /// <summary>마이크 녹음을 시작합니다 (Web Speech API STT).</summary>
    public void StartListening()
    {
        micDisplay?.SetState(MicDisplayController.WaveState.Listening);
        JS_StartSTT();
    }

    /// <summary>마이크 녹음을 중지합니다.</summary>
    public void StopListening()
    {
        micDisplay?.SetState(MicDisplayController.WaveState.Done);
        JS_StopSTT();
    }

    /// <summary>
    /// JS TTS 재생이 끝났을 때 호출됩니다 (WebSpeech.jslib → SendMessage).
    /// </summary>
    public void OnTTSEnd(string _)
    {
        interviewer?.StopSpeaking();
    }

    /// <summary>
    /// JS에서 STT 결과를 받을 때 호출됩니다.
    /// WebGL JS → C# 브리지 메서드 (SendMessage 방식).
    /// </summary>
    public void OnSpeechResult(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Debug.Log($"[GeminiClient] STT 결과: {text}");
        StopListening();
        SendUserSpeech(text);
    }

    // ─── 송신 ──────────────────────────────────────────────────────────────

    private async void Send(string json)
    {
        if (_ws == null || _ws.State != WebSocketState.Open)
        {
            Debug.LogWarning("[GeminiClient] WebSocket이 열려 있지 않습니다.");
            return;
        }
        await _ws.SendText(json);
    }

    // ─── 수신 핸들러 ───────────────────────────────────────────────────────

    private void HandleOpen()
    {
        Debug.Log("[GeminiClient] 연결됨");
    }

    private void HandleMessage(byte[] data)
    {
        string json = System.Text.Encoding.UTF8.GetString(data);
        Debug.Log($"[GeminiClient] 수신: {json}");

        try
        {
            var typeOnly = JsonUtility.FromJson<TypeOnly>(json);

            switch (typeOnly.type)
            {
                case "ready":
                    _isReady = true;
                    OnReady?.Invoke();
                    Debug.Log("[GeminiClient] relay 준비 완료");
                    break;

                case "processing":
                    OnProcessing?.Invoke();
                    break;

                case "server_content":
                    var msg = JsonUtility.FromJson<ServerMsg>(json);
                    HandleServerContent(msg);
                    OnServerContent?.Invoke(msg);
                    break;

                case "error":
                    var errPkt = JsonUtility.FromJson<ErrorPacket>(json);
                    Debug.LogError($"[GeminiClient] 서버 오류: {errPkt.message}");
                    OnError?.Invoke(errPkt.message);
                    break;

                default:
                    Debug.LogWarning($"[GeminiClient] 알 수 없는 메시지 타입: {typeOnly.type}");
                    break;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[GeminiClient] 메시지 파싱 실패: {e.Message}\n{json}");
        }
    }

    private void HandleServerContent(ServerMsg msg)
    {
        if (msg.content == null) return;

        // 감정 적용
        if (!string.IsNullOrEmpty(msg.content.emotionLabel) && interviewer != null)
            interviewer.SetMood(EmotionLabelToMood(msg.content.emotionLabel));

        // 자막 + TTS
        if (!string.IsNullOrEmpty(msg.content.text))
        {
            subtitleManager?.DisplaySubtitle(msg.content.text);
            interviewer?.StartSpeaking();
            JS_Speak(msg.content.text);
        }
    }

    private void HandleError(string errorMsg)
    {
        Debug.LogError($"[GeminiClient] WebSocket 오류: {errorMsg}");
        OnError?.Invoke(errorMsg);
    }

    private void HandleClose(WebSocketCloseCode code)
    {
        _isReady = false;
        Debug.Log($"[GeminiClient] 연결 종료: {code}");
    }

    // ─── 감정 레이블 → InterviewerMood 변환 ───────────────────────────────

    private static string EmotionLabelToMood(string label)
    {
        if (label.Contains("압박"))  return "Pressuring";
        if (label.Contains("호기심")) return "Neutral";
        if (label.Contains("기쁨"))  return "Smile";
        if (label.Contains("당혹"))  return "Confused";
        if (label.Contains("정중"))  return "Satisfied";
        return "Neutral";
    }

    // ─── 내부 패킷 타입 ────────────────────────────────────────────────────

    [Serializable] private class TypeOnly    { public string type; }
    [Serializable] private class ErrorPacket { public string type; public string message; }

    [Serializable]
    private class SessionStartPacket
    {
        public string type;
        public string last_question;
    }

    [Serializable]
    private class UserSpeechPacket
    {
        public string type;
        public string text;
    }
}
