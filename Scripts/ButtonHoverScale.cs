using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;

[RequireComponent(typeof(RectTransform))]
public class ButtonHoverScale : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private float normalScale = 1.0f;
    [SerializeField] private float hoverScale = 1.06f;
    [SerializeField] private float pressedScale = 0.98f;
    [SerializeField] private float duration = 0.15f;

    private RectTransform rt;
    private Coroutine routine;
    private bool isHovering = false;

    private void Awake()
    {
        rt = GetComponent<RectTransform>();
        rt.localScale = Vector3.one * normalScale;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        isHovering = true;
        AnimateTo(hoverScale);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        isHovering = false;
        AnimateTo(normalScale);
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        AnimateTo(pressedScale);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        AnimateTo(isHovering ? hoverScale : normalScale);
    }

    private void AnimateTo(float target)
    {
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(ScaleTo(target));
    }

    private IEnumerator ScaleTo(float target)
    {
        float startScale = rt.localScale.x;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            t = 1f - Mathf.Pow(1f - t, 3f);
            float s = Mathf.Lerp(startScale, target, t);
            rt.localScale = Vector3.one * s;
            yield return null;
        }

        rt.localScale = Vector3.one * target;
    }
}