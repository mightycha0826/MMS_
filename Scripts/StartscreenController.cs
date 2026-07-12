using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class StartScreenController : MonoBehaviour
{
    [Header("Start Screen")]
    [SerializeField] private CanvasGroup startScreenGroup;
    [SerializeField] private RawImage backgroundImage;
    [SerializeField] private Texture2D[] backgroundTextures;
    [SerializeField] private CanvasGroup blurPanel;
    [SerializeField] private Button startButton;

    [Header("Main Screen")]
    [SerializeField] private GameObject mainScreen;
    [SerializeField] private IntroAnimator introAnimator;

    [Header("Fade Overlay")]
    [SerializeField] private Image fadeOverlay;

    [Header("Timing")]
    [SerializeField] private float fadeOutDuration = 0.5f;
    [SerializeField] private float blackHoldDuration = 0.2f;

    private void Start()
    {
        StartCoroutine(start_());
    }
    private IEnumerator start_()
    {
        yield return new WaitForSeconds(0.5f);
        SetOverlayAlpha(1f);
        mainScreen.SetActive(false);
        StartCoroutine(FadeOverlay(1f, 0f, 0.5f));

        if (backgroundTextures != null && backgroundTextures.Length > 0)
            backgroundImage.texture = backgroundTextures[Random.Range(0, backgroundTextures.Length)];

    }

    public void OnStartButtonClicked()
    {
        startButton.interactable = false;
        StartCoroutine(TransitionSequence());
    }

    private IEnumerator TransitionSequence()
    {
        // 1. 암전
        yield return StartCoroutine(FadeOverlay(0f, 1f, fadeOutDuration));

        // 2. 암전 유지
        yield return new WaitForSeconds(blackHoldDuration);

        // 3. 시작화면 끄고 메인화면 켜기
        startScreenGroup.gameObject.SetActive(false);
        mainScreen.SetActive(true);

        gameObject.GetComponent<HUDController>().StartTimer();
        // 4. 오버레이 해제 + 등장 애니메이션
        yield return StartCoroutine(introAnimator.PlayIntro(fadeOverlay));
    }

    private IEnumerator FadeOverlay(float from, float to, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            SetOverlayAlpha(Mathf.Lerp(from, to, Ease(elapsed / duration)));
            yield return null;
        }
        SetOverlayAlpha(to);
    }

    private void SetOverlayAlpha(float a)
    {
        if (fadeOverlay == null) return;
        Color c = fadeOverlay.color;
        c.a = a;
        fadeOverlay.color = c;
    }

    private float Ease(float t) => 1f - Mathf.Pow(1f - Mathf.Clamp01(t), 3f);
}