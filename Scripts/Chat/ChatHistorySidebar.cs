using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class ChatHistorySidebar : MonoBehaviour
{
    [Header("Panel")]
    [SerializeField] private RectTransform sidebarRect;
    [SerializeField] private CanvasGroup sidebarGroup;
    [SerializeField] private CanvasGroup dimGroup;

    [Header("Buttons")]
    [SerializeField] private Button openButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button dimClickToClose;

    [Header("Content")]
    [SerializeField] private RectTransform contentParent;
    [SerializeField] private ScrollRect scrollRect;
    [SerializeField] private ChatBubble interviewerBubblePrefab;
    [SerializeField] private ChatBubble userBubblePrefab;
    [SerializeField] private GameObject emptyHint;

    [Header("Manager")]
    [SerializeField] private ChatHistoryManager historyManager;

    [Header("Animation")]
    [SerializeField] private float slideDuration = 0.35f;
    [SerializeField] private float slideOffset = 380f;

    private bool isOpen = false;
    private Coroutine slideRoutine;
    private Vector2 shownPos;
    private Vector2 hiddenPos;

    private void Start()
    {
        // 위치 캡처 (레이아웃 완료 후)
        Canvas.ForceUpdateCanvases();
        shownPos = sidebarRect.anchoredPosition;
        hiddenPos = shownPos + Vector2.left * slideOffset;

        // 시작 상태: 숨김
        sidebarRect.anchoredPosition = hiddenPos;
        if (sidebarGroup != null)
        {
            sidebarGroup.alpha = 0f;
            sidebarGroup.blocksRaycasts = false;
        }
        if (dimGroup != null)
        {
            dimGroup.alpha = 0f;
            dimGroup.blocksRaycasts = false;
        }

        // 버튼 연결
        if (openButton != null) openButton.onClick.AddListener(Open);
        if (closeButton != null) closeButton.onClick.AddListener(Close);
        if (dimClickToClose != null) dimClickToClose.onClick.AddListener(Close);

        // 매니저 이벤트 구독
        if (historyManager != null)
            historyManager.OnEntryAdded.AddListener(AddBubble);

        UpdateEmptyHint();
    }

    private void OnDestroy()
    {
        if (historyManager != null)
            historyManager.OnEntryAdded.RemoveListener(AddBubble);
    }

    // ── 사이드바 열기/닫기 ───────────────────────────────

    public void Open()
    {
        if (isOpen) return;
        isOpen = true;
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlideTo(shownPos, 1f, true));
    }

    public void Close()
    {
        if (!isOpen) return;
        isOpen = false;
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlideTo(hiddenPos, 0f, false));
    }

    public void Toggle()
    {
        if (isOpen) Close(); else Open();
    }

    private IEnumerator SlideTo(Vector2 targetPos, float targetAlpha, bool blockRaycasts)
    {
        Vector2 startPos = sidebarRect.anchoredPosition;
        float startAlpha = sidebarGroup != null ? sidebarGroup.alpha : 1f;
        float dimStart = dimGroup != null ? dimGroup.alpha : 0f;

        if (sidebarGroup != null) sidebarGroup.blocksRaycasts = blockRaycasts;
        if (dimGroup != null) dimGroup.blocksRaycasts = blockRaycasts;

        float elapsed = 0f;
        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t = Ease(elapsed / slideDuration);

            sidebarRect.anchoredPosition = Vector2.Lerp(startPos, targetPos, t);
            if (sidebarGroup != null) sidebarGroup.alpha = Mathf.Lerp(startAlpha, targetAlpha, t);
            if (dimGroup != null) dimGroup.alpha = Mathf.Lerp(dimStart, targetAlpha * 0.6f, t);

            yield return null;
        }

        sidebarRect.anchoredPosition = targetPos;
        if (sidebarGroup != null) sidebarGroup.alpha = targetAlpha;
        if (dimGroup != null) dimGroup.alpha = targetAlpha * 0.6f;
    }

    // ── 버블 생성 ────────────────────────────────────────

    public void AddBubble(ChatEntry entry)
    {
        ChatBubble prefab = entry.speaker == ChatEntry.SpeakerType.Interviewer
            ? interviewerBubblePrefab : userBubblePrefab;

        if (prefab == null || contentParent == null) return;

        ChatBubble bubble = Instantiate(prefab, contentParent);
        bubble.SetData(entry);

        UpdateEmptyHint();

        // 스크롤 가장 아래로
        StartCoroutine(ScrollToBottom());
    }

    public void ClearAllBubbles()
    {
        if (contentParent == null) return;
        foreach (Transform child in contentParent)
            Destroy(child.gameObject);
        UpdateEmptyHint();
    }

    private IEnumerator ScrollToBottom()
    {
        yield return null;
        yield return null;
        yield return new WaitForEndOfFrame();

        if (contentParent != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(contentParent);

        Canvas.ForceUpdateCanvases();

        if (scrollRect != null)
            scrollRect.verticalNormalizedPosition = 0f;
    }

    private void UpdateEmptyHint()
    {
        if (emptyHint == null) return;
        bool empty = contentParent == null || contentParent.childCount == 0;
        emptyHint.SetActive(empty);
    }

    private float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}