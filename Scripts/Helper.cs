using UnityEngine;

public class Helper : MonoBehaviour
{
    public static Helper Instance { get; private set; }

    [Header("캐릭터")]
    [SerializeField] private InterviewerController interviewer;

    [Header("대화창")]
    [SerializeField] private SubtitleManager subtitle;

    [Header("대화 기록")]
    [SerializeField] private ChatHistoryManager chatHistory;
    [SerializeField] private ChatHistorySidebar sidebar;

    [Header("상단 바")]
    [SerializeField] private HUDController hud;

    [Header("마이크 표시")]
    [SerializeField] private MicDisplayController mic;

    [Header("답변 입력")]
    [SerializeField] private AnswerInputController answerInput;

    VoiceGenerator tts;


    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) { Destroy(gameObject); return; }

        tts = GetComponent<VoiceGenerator>();
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }


    // 캐릭터

    /// <summary>면접관 표정 변경</summary>
    public void SetMood(string mood)
    {
        if (interviewer != null) interviewer.SetMood(mood);
    }

    /// <summary>면접관 입뻥긋 시작</summary>
    public void StartSpeaking()
    {
        if (interviewer != null) interviewer.StartSpeaking();
    }

    /// <summary>면접관 입뻥긋 종료</summary>
    public void StopSpeaking()
    {
        if (interviewer != null) interviewer.StopSpeaking();
    }

    public bool IsInterviewerSpeaking()
        => interviewer != null && interviewer.IsSpeaking();

    // 자막

    /// <summary>자막 재생</summary>
    public void DisplaySubtitle(string text, float duration = 0f)
    {
        if (subtitle != null) subtitle.DisplaySubtitle(text, duration);
    }

    /// <summary>자막 즉시 표시</summary>
    public void DisplaySubtitleImmediate(string text)
    {
        if (subtitle != null) subtitle.DisplaySubtitleImmediate(text);
    }

    /// <summary>자막 삭제</summary>
    public void ClearSubtitle()
    {
        if (subtitle != null) subtitle.ClearSubtitle();
    }

    // 한번에 처리

    /// <summary>
    /// mood 설정 + 말하기 시작 + 자막 + 채팅 기록.
    /// </summary>
    public void PlayResponse(string text, string mood)
    {
        // 텍스트, 말하기 전에
        tts.GenerateAndPlaySpeech(text,
        () => {
            SetMood(mood);
            StopSpeaking();
            StartSpeaking();
            DisplaySubtitle(text);
            AddInterviewerEntry(text);
        },
        () => {
            StopSpeaking();
        });
    }

    // 대화 기록

    /// <summary>면접관 발화 기록</summary>
    public void AddInterviewerEntry(string text)
    {
        if (chatHistory != null) chatHistory.AddInterviewerEntry(text);
    }

    /// <summary>사용자 답변 기록</summary>
    public void AddUserEntry(string text)
    {
        if (chatHistory != null) chatHistory.AddUserEntry(text);
    }

    public void ClearChatHistory()
    {
        if (chatHistory != null) chatHistory.ClearHistory();
    }

    // 사이드바

    public void OpenSidebar() { if (sidebar != null) sidebar.Open(); }
    public void CloseSidebar() { if (sidebar != null) sidebar.Close(); }
    public void ToggleSidebar() { if (sidebar != null) sidebar.Toggle(); }

    // HUD

    public void StartTimer() { if (hud != null) hud.StartTimer(); }
    public void StopTimer() { if (hud != null) hud.StopTimer(); }
    public void AddQACount() { if (hud != null) hud.AddQACount(); }

    // ═══════════════════════════════════════════════════════
    // MIC OVERLAY (음성 인식 UI)
    // ═══════════════════════════════════════════════════════

    /// <summary>화면 중앙 하단 마이크 오버레이 표시</summary>
    public void ShowMicOverlay()
    {
        if (mic != null) mic.ShowDisplay();
    }

    public void HideMicOverlay()
    {
        if (mic != null) mic.HideDisplay();
    }

    /// <summary>마이크 웨이브 상태 설정 (Idle / Listening / Done)</summary>
    public void SetMicState(MicDisplayController.WaveState state)
    {
        if (mic != null) mic.SetState(state);
    }

    /// <summary>음성 인식 시작 오버레이 표시</summary>
    public void StartListening()
    {
        ShowMicOverlay();
        SetMicState(MicDisplayController.WaveState.Listening);
        SetInputInteractable(false);
    }

    /// <summary>음성 인식 완료 — 오버레이 숨기기</summary>
    public void StopListening()
    {
        SetMicState(MicDisplayController.WaveState.Done);
        HideMicOverlay();
        SetInputInteractable(true);
    }

    // 답변 입력

    public void SetInputInteractable(bool interactable)
    {
        if (answerInput != null) answerInput.SetInteractable(interactable);
    }
}