using UnityEngine;

/// <summary>
/// 씬 전역에서 면접 UI(면접관 표정/발화, 자막, 마이크 디스플레이)를 제어하는 싱글톤 헬퍼.
/// GeminiClient는 STT/TTS/WebSocket 통신을 담당하고, 화면 표현은 Helper로 위임합니다.
/// </summary>
public class Helper : MonoBehaviour
{
    public static Helper Instance { get; private set; }

    [Header("연결 컴포넌트")]
    [SerializeField] private InterviewerController interviewer;
    [SerializeField] private SubtitleManager      subtitleManager;
    [SerializeField] private MicDisplayController  micDisplay;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ─── 마이크 디스플레이 ─────────────────────────────────────────────────

    /// <summary>마이크 파형 상태를 설정합니다 (Idle / Listening / Done).</summary>
    public void SetMicState(MicDisplayController.WaveState state)
    {
        micDisplay?.SetState(state);
    }

    /// <summary>마이크 오버레이를 표시합니다.</summary>
    public void ShowMicOverlay() => micDisplay?.ShowDisplay();

    /// <summary>마이크 오버레이를 숨깁니다.</summary>
    public void HideMicOverlay() => micDisplay?.HideDisplay();

    // ─── 면접관 응답 재생 ──────────────────────────────────────────────────

    /// <summary>
    /// 면접관 응답을 화면에 재생합니다. 감정 적용 + 자막 출력 + 입 모양 시작.
    /// 실제 음성(TTS)은 GeminiClient의 JS_Speak가 담당합니다.
    /// </summary>
    public void PlayResponse(string text, string mood)
    {
        if (!string.IsNullOrEmpty(mood))
            interviewer?.SetMood(mood);

        if (!string.IsNullOrEmpty(text))
        {
            subtitleManager?.DisplaySubtitle(text);
            interviewer?.StartSpeaking();
        }
    }

    /// <summary>TTS 재생이 끝났을 때 면접관 입 모양을 멈춥니다.</summary>
    public void StopSpeaking()
    {
        interviewer?.StopSpeaking();
    }
}
