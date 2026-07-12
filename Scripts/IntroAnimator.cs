using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class IntroAnimator : MonoBehaviour
{
    [Header("Character")]
    [SerializeField] private CanvasGroup characterGroup;

    [Header("Dialogue")]
    [SerializeField] private RectTransform dialogueRect;
    [SerializeField] private CanvasGroup dialogueGroup;
    [SerializeField] private float dialogueOffsetY = 60f;

    [Header("Timing")]
    [SerializeField] private float overlayFadeDur = 2.2f;

    private void Awake()
    {
        if (characterGroup != null) characterGroup.alpha = 0f;
        if (dialogueGroup != null) dialogueGroup.alpha = 0f;
    }

    public IEnumerator PlayIntro(Image fadeOverlay)
    {
        Vector3 dialogueOrigin = dialogueRect != null
            ? dialogueRect.localPosition
            : Vector3.zero;

        if (dialogueRect != null)
            dialogueRect.localPosition = dialogueOrigin + Vector3.down * dialogueOffsetY;

        float elapsed = 0f;

        while (elapsed < overlayFadeDur)
        {
            elapsed += Time.deltaTime;
            float t = Ease(elapsed / overlayFadeDur);

            SetAlpha(fadeOverlay, 1f - t);

            if (characterGroup != null) characterGroup.alpha = t;

            if (dialogueRect != null)
                dialogueRect.localPosition = Vector3.LerpUnclamped(
                    dialogueOrigin + Vector3.down * dialogueOffsetY,
                    dialogueOrigin, t);
            if (dialogueGroup != null) dialogueGroup.alpha = t;

            yield return null;
        }

        // 정확히 마무리
        SetAlpha(fadeOverlay, 0f);
        fadeOverlay.gameObject.SetActive(false);
        if (characterGroup != null) characterGroup.alpha = 1f;
        if (dialogueRect != null) dialogueRect.localPosition = dialogueOrigin;
        if (dialogueGroup != null) dialogueGroup.alpha = 1f;
    }

    private void SetAlpha(Image img, float a)
    {
        if (img == null) return;
        Color c = img.color; c.a = a; img.color = c;
    }

    private float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
}