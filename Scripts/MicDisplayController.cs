using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.UIElements;

public class MicDisplayController : MonoBehaviour
{
    public enum WaveState
    {
        Idle,
        Listening,
        Done,
    }
    public GameObject display;
    [Header("Bars")]
    [SerializeField] private RectTransform[] bars;
    [SerializeField] private float minHeight = 6f;
    private float[] maxHeights = new float[]
    {
        22f, 32f, 44f, 55f, 64f, 70f, 75f, 70f, 64f, 55f, 44f, 32f, 22f
    };

    private static readonly float[] PhaseOffsets = new float[]
    {
        0.83f, 0.17f, 0.61f, 0.29f, 0.94f, 0.45f, 0.08f,
        0.72f, 0.38f, 0.55f, 0.12f, 0.67f, 0.91f
    };

    [Header("Idle")]
    [SerializeField] private float idleSpeed = 2.5f;
    [SerializeField] private float idleAmount = 0.25f;

    [Header("Listening")]
    [SerializeField] private float listenSpeed = 8f;
    [SerializeField] private float listenAmount = 1.0f;

    [Header("Transition")]
    [SerializeField] private float transitionDuration = 0.2f;

    [SerializeField] public TextMeshProUGUI statusText;

    private WaveState currentState = WaveState.Idle;
    private Coroutine animRoutine;
    private float[] currentHeights;

    private void Awake()
    {
        currentHeights = new float[bars.Length];
        for (int i = 0; i < bars.Length; i++)
            currentHeights[i] = minHeight;
    }

    private void Start()
    {
        SetState(WaveState.Idle);

        var cg = display.GetComponent<CanvasGroup>();
        cg.alpha = 0f;
        cg.blocksRaycasts = false;
        var rt = display.GetComponent<RectTransform>();
        rt.localScale = Vector3.one * 1.00f;
    }

    public void SetState(WaveState state)
    {
        if (currentState == state) return;
        currentState = state;

        if (animRoutine != null) StopCoroutine(animRoutine);

        switch (state)
        {
            case WaveState.Idle:
                animRoutine = StartCoroutine(AnimateLoop(idleSpeed, idleAmount));
                break;

            case WaveState.Listening:
                animRoutine = StartCoroutine(AnimateLoop(listenSpeed, listenAmount));
                break;

            case WaveState.Done:
                animRoutine = StartCoroutine(TransitionToFlat());
                break;
        }
    }

    public void SetState(string stateName)
    {
        if (System.Enum.TryParse<WaveState>(stateName, true, out var state))
            SetState(state);
    }

    public WaveState GetState() => currentState;
    private IEnumerator AnimateLoop(float speed, float amount)
    {

        while (true)
        {
            for (int i = 0; i < bars.Length; i++)
            {
                float barMax = (i < maxHeights.Length) ? maxHeights[i] : minHeight;
                float range = (barMax - minHeight) * amount;

                float sin = Mathf.Sin(Time.time * speed + PhaseOffsets[i] * Mathf.PI * 2f);
                float target = minHeight + (sin * 0.5f + 0.5f) * range;
                currentHeights[i] = Mathf.Lerp(currentHeights[i], target, Time.deltaTime * 12f);
                ApplyHeight(i, currentHeights[i]);
            }
            yield return null;
        }
    }

    private IEnumerator TransitionToFlat()
    {
        float[] startHeights = (float[])currentHeights.Clone();
        float elapsed = 0f;

        while (elapsed < transitionDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / transitionDuration);

            for (int i = 0; i < bars.Length; i++)
            {
                currentHeights[i] = Mathf.Lerp(startHeights[i], minHeight, t);
                ApplyHeight(i, currentHeights[i]);
            }
            yield return null;
        }

        for (int i = 0; i < bars.Length; i++)
        {
            currentHeights[i] = minHeight;
            ApplyHeight(i, minHeight);
        }
    }

    private void ApplyHeight(int index, float height)
    {
        if (index >= bars.Length || bars[index] == null) return;
        Vector2 size = bars[index].sizeDelta;
        size.y = height;
        bars[index].sizeDelta = size;
    }


    private Coroutine display_routine;
    public void ShowDisplay()
    {
        if (display_routine != null) StopCoroutine(display_routine);
        display.gameObject.SetActive(true);
        display_routine = StartCoroutine(AnimateDisplay(true));
    }

    public void HideDisplay()
    {
        if (display_routine != null) StopCoroutine(display_routine);
        display_routine = StartCoroutine(AnimateDisplay(false));
    }
    private IEnumerator AnimateDisplay(bool show)
    {
        var cg = display.GetComponent<CanvasGroup>();
        var rt = display.GetComponent<RectTransform>();
        float startAlpha =  cg.alpha;
        float endAlpha = show ? 1f : 0f;

        float startScale = rt.localScale.x;
        float endScale = show ? 1.00f : 0.95f;

        cg.blocksRaycasts = show;

        float elapsed = 0f;
        while (elapsed < 0.25f)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / 0.25f);
            float et = EaseOutCubic(t);

            cg.alpha = Mathf.Lerp(startAlpha, endAlpha, et);
            float s = Mathf.Lerp(startScale, endScale, et);
            rt.localScale = Vector3.one * s;

            yield return null;
        }

        cg.alpha = endAlpha;
        rt.localScale = Vector3.one * endScale;

        if (!show) display.gameObject.SetActive(false);
    }

    private float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
}