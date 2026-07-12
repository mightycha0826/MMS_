using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;
using NativeWebSocket;
using System.Threading.Tasks;

/// <summary>
/// gemini-relay Cloudflare Worker와 WebSocket으로 통신하는 클라이언트.
/// 면접 세션 시작, 지원자 답변 전송, 결과 수신 + TTS 재생을 담당합니다.
/// </summary>
public class GeminiClient : MonoBehaviour
{
    [Header("Relay 설정")]
    [SerializeField] private string relayUrl = "wss://gemini-relay.mightycha0826.workers.dev";
    [SerializeField] private string department = "";

    [Header("오디오 재생 (Supertone TTS)")]
    [SerializeField] private AudioSource audioSource;

    public event Action<ServerMsg> OnServerContent;
    public event Action OnReady;
    public event Action OnProcessing;
    public event Action<string> OnError;

    private WebSocket _ws;
    private bool _isReady;

    // relay가 더 이상 세션 상태를 기억하지 않으므로, 마지막으로 받은 질문을
    // Unity가 들고 있다가 매 턴 client_msg에 다시 실어 보낸다.
    private string _lastQuestion = "";

    // ─── JS 브리지 (Web Speech API) ───────────────────────────────────────

#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] private static extern void JS_StartSTT();
    [DllImport("__Internal")] private static extern void JS_StopSTT();
    [DllImport("__Internal")] private static extern void JS_Speak(string text);
#else
    private static void JS_StartSTT() => Debug.Log("[GeminiClient] STT 시작 (에디터 모의)");
    private static void JS_StopSTT() => Debug.Log("[GeminiClient] STT 중지 (에디터 모의)");
    private static void JS_Speak(string text) => Debug.Log($"[GeminiClient] TTS: {text}");
#endif

    // ─── 생명주기 ──────────────────────────────────────────────────────────
    private async Task Start()
    {
        try
        {
            // Connect가 끝날 때까지 기다림
            await Connect();
        }
        catch (System.Exception e)
        {
            // 비동기 함수 내부에서 터진 에러를 강제로 출력
            Debug.LogError($"[테스트] 에러 발생: {e.Message}\n{e.StackTrace}");
        }
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

        _ws.OnOpen += HandleOpen;
        _ws.OnMessage += HandleMessage;
        _ws.OnError += HandleError;
        _ws.OnClose += HandleClose;

        Debug.Log($"[GeminiClient] 연결 중... {relayUrl}");
        await _ws.Connect();
    }

    // ─── 공개 API ──────────────────────────────────────────────────────────

    /// <summary>지원 학과를 설정합니다. 이후 매 client_msg에 함께 실려 갑니다.</summary>
    public void SetDepartment(string dept)
    {
        department = dept;
    }

    /// <summary>
    /// 면접관의 첫 질문(오프닝 멘트)을 초기 last_question 상태로 등록합니다.
    /// 더 이상 별도 네트워크 메시지를 보내지 않습니다 — session_start 타입이 폐지되었기 때문에
    /// 이 값은 다음 SendUserSpeech 호출 때 client_msg.last_question으로 실려 나갑니다.
    /// </summary>
    public void SendSessionStart(string lastQuestion = "")
    {
        _lastQuestion = lastQuestion;
    }

    /// <summary>지원자 답변 텍스트를 relay로 전송합니다 (client_msg 통합 포맷).</summary>
    public void SendUserSpeech(string text)
    {
        if (!_isReady)
        {
            Debug.LogWarning("[GeminiClient] relay가 아직 준비되지 않았습니다.");
            return;
        }

        Send(JsonUtility.ToJson(new ClientMsg
        {
            type = "client_msg",
            department = string.IsNullOrEmpty(department) ? "미지정" : department,
            last_question = _lastQuestion,
            text = text,
        }));
    }

    /// <summary>Web Speech API STT 결과 수신 (WebSpeech.jslib → SendMessage로 호출).</summary>
    public void OnSpeechResult(string text)
    {
        StopListening();
        SendUserSpeech(text);
    }

    /// <summary>TTS 재생 종료 알림 (WebSpeech.jslib → SendMessage로 호출).</summary>
    public void OnTTSEnd()
    {
    }

    /// <summary>마이크 녹음을 시작합니다 (Web Speech API STT).</summary>
    public void StartListening()
    {
        //Helper.Instance.ShowMicOverlay();
        Helper.Instance.SetMicState(MicDisplayController.WaveState.Listening);
        JS_StartSTT();
    }

    /// <summary>마이크 녹음을 중지합니다.</summary>
    public void StopListening()
    {
        Helper.Instance.SetMicState(MicDisplayController.WaveState.Done);
        JS_StopSTT();
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
                    // "AI worker not connected" 등 - Python AI 프로세스가 아직 안 떠 있을 때도 여기로 들어옴
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

        // 서버가 세션을 기억하지 않으므로, 다음 턴 client_msg.last_question으로 다시 실어 보낼 값을 저장
        if (!string.IsNullOrEmpty(msg.content.text))
            _lastQuestion = msg.content.text;

        // emotion.label은 영문 enum(neutral/smile/shy/...)이라 InterviewerMood 이름과 그대로 일치함
        string mood = msg.content.emotion != null ? msg.content.emotion.label : "neutral";
        Helper.Instance.PlayResponse(msg.content.text, mood);

        // decision(follow_up/next_topic) 필드는 프로토콜에서 삭제됨 - 더 이상 주제전환 여부를 서버가 알려주지 않음

        if (!string.IsNullOrEmpty(msg.content.audio))
            PlayTtsAudio(msg.content.audio);
        // audio가 없으면 Helper.PlayResponse가 처리하는 기존 Web Speech TTS 경로로 폴백
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

    // ─── TTS 오디오 (Supertone, base64 WAV) ───────────────────────────────

    private void PlayTtsAudio(string base64Wav)
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[GeminiClient] audioSource가 지정되지 않아 오디오 재생을 건너뜁니다.");
            return;
        }

        try
        {
            byte[] wavBytes = Convert.FromBase64String(base64Wav);
            AudioClip clip = WavToAudioClip(wavBytes, "tts_response");
            if (clip != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[GeminiClient] TTS 오디오 디코딩 실패: {e.Message}");
        }
    }

    private static AudioClip WavToAudioClip(byte[] wav, string clipName)
    {
        int channels = BitConverter.ToInt16(wav, 22);
        int sampleRate = BitConverter.ToInt32(wav, 24);
        int bitsPerSample = BitConverter.ToInt16(wav, 34);

        int dataChunkOffset = FindChunkOffset(wav, "data");
        if (dataChunkOffset < 0 || bitsPerSample != 16)
        {
            Debug.LogWarning($"[GeminiClient] 지원하지 않는 WAV 형식 (bits={bitsPerSample})");
            return null;
        }

        int dataSize = BitConverter.ToInt32(wav, dataChunkOffset - 4);
        int sampleCount = dataSize / 2;
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            short raw = BitConverter.ToInt16(wav, dataChunkOffset + i * 2);
            samples[i] = raw / 32768f;
        }

        AudioClip clip = AudioClip.Create(clipName, sampleCount / Mathf.Max(channels, 1), channels, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static int FindChunkOffset(byte[] data, string chunkId)
    {
        byte[] id = System.Text.Encoding.ASCII.GetBytes(chunkId);
        for (int i = 12; i <= data.Length - 8; i++)
        {
            if (data[i] == id[0] && data[i + 1] == id[1] && data[i + 2] == id[2] && data[i + 3] == id[3])
                return i + 8;
        }
        return -1;
    }

    // ─── 내부 패킷 타입 ────────────────────────────────────────────────────

    [Serializable] private class TypeOnly { public string type; }
    [Serializable] private class ErrorPacket { public string type; public string message; }
}
