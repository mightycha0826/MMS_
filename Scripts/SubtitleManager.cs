using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using TMPro;

public class SubtitleManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI subtitleDisplay;
    [SerializeField] private float fixedSyllableInterval = 0.08f;
    [SerializeField] private bool syncWithAudio = false;

    [SerializeField] private Color subtitleColor = Color.white;

    [SerializeField] private RectTransform dialoguePanel;
    [SerializeField] private float baseAnchorMaxY = 0.35f;
    [SerializeField] private float maxAnchorMaxY = 0.50f;
    [SerializeField] private float resetPauseDuration = 0.4f;

    private float currentAnchorMaxY;

    private Coroutine typingCoroutine;
    private bool isDisplaying = false;

    private void Start()
    {
        if (subtitleDisplay == null)
            subtitleDisplay = GetComponent<TextMeshProUGUI>();

        if (subtitleDisplay != null)
        {
            subtitleDisplay.color = subtitleColor;
        }

        currentAnchorMaxY = baseAnchorMaxY;
        ClearSubtitle();
    }


    public void DisplaySubtitle(string text, float duration = 0f)
    {
        if (string.IsNullOrEmpty(text))
        {
            ClearSubtitle();
            return;
        }

        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        float syllableInterval = fixedSyllableInterval;
        if (syncWithAudio && duration > 0)
        {
            int syllableCount = CountSyllables(text);
            syllableInterval = Mathf.Max(0.03f, duration / syllableCount);
        }

        typingCoroutine = StartCoroutine(TypeSyllableByByllable(text, syllableInterval));
    }

    public void DisplaySubtitleImmediate(string text)
    {
        if (typingCoroutine != null)
            StopCoroutine(typingCoroutine);

        if (subtitleDisplay != null)
        {
            subtitleDisplay.text = text;
            isDisplaying = !string.IsNullOrEmpty(text);
        }
    }

    public void ClearSubtitle()
    {
        if (typingCoroutine != null)
        {
            StopCoroutine(typingCoroutine);
            typingCoroutine = null;
        }

        if (subtitleDisplay != null)
            subtitleDisplay.text = "";

        isDisplaying = false;

        SetPanelAnchorY(baseAnchorMaxY);
    }

    public bool IsDisplaying() => isDisplaying;
    public void SetSyllableInterval(float interval) => fixedSyllableInterval = Mathf.Max(0.01f, interval);
    public void SetSyncWithAudio(bool sync) => syncWithAudio = sync;



    private IEnumerator TypeSyllableByByllable(string text, float interval)
    {
        isDisplaying = true;
        currentAnchorMaxY = baseAnchorMaxY;
        SetPanelAnchorY(currentAnchorMaxY);

        List<string> syllables = SplitIntoSyllables(text);
        StringBuilder displayText = new StringBuilder();

        for (int i = 0; i < syllables.Count; i++)
        {
            string currentText = displayText.ToString();
            string preview = currentText + syllables[i];

            subtitleDisplay.text = preview;
            subtitleDisplay.ForceMeshUpdate();

            if (IsTextOverflowing())
            {
                if (TryExpandPanel())
                {
                    displayText.Clear();
                    displayText.Append(preview);
                }
                else
                {
                    subtitleDisplay.text = currentText;

                    yield return new WaitForSeconds(resetPauseDuration);

                    displayText.Clear();
                    currentAnchorMaxY = baseAnchorMaxY;
                    SetPanelAnchorY(currentAnchorMaxY);

                    // 현재 음절부터 새로 시작
                    displayText.Append(syllables[i]);
                    subtitleDisplay.text = displayText.ToString();
                    subtitleDisplay.ForceMeshUpdate();
                }
            }
            else
            {
                // 안 넘침
                displayText.Clear();
                displayText.Append(preview);
            }

            if (i < syllables.Count - 1)
                yield return new WaitForSeconds(interval);
        }

        isDisplaying = false;
    }

    private bool IsTextOverflowing()
    {
        float textHeight = subtitleDisplay.preferredHeight;
        float availableHeight = subtitleDisplay.rectTransform.rect.height;
        return textHeight > availableHeight;
    }

    private bool TryExpandPanel()
    {
        if (dialoguePanel == null) return false;

        float parentHeight = GetParentHeight();
        if (parentHeight <= 0) return false;

        float textHeight = subtitleDisplay.preferredHeight;
        float availableHeight = subtitleDisplay.rectTransform.rect.height;
        float overflow = textHeight - availableHeight;

        float anchorIncrease = (overflow / parentHeight) + 0.02f;
        float newAnchorMaxY = currentAnchorMaxY + anchorIncrease;

        if (newAnchorMaxY > maxAnchorMaxY)
            return false;

        currentAnchorMaxY = Mathf.Min(newAnchorMaxY, maxAnchorMaxY);
        SetPanelAnchorY(currentAnchorMaxY);
        return true;
    }

    private void SetPanelAnchorY(float anchorMaxY)
    {
        if (dialoguePanel == null) return;

        anchorMaxY = Mathf.Clamp(anchorMaxY, baseAnchorMaxY, maxAnchorMaxY);

        Vector2 min = dialoguePanel.anchorMin;
        Vector2 max = dialoguePanel.anchorMax;
        max.y = anchorMaxY;
        dialoguePanel.anchorMin = min;
        dialoguePanel.anchorMax = max;

        dialoguePanel.offsetMin = new Vector2(dialoguePanel.offsetMin.x, 0f);
        dialoguePanel.offsetMax = new Vector2(dialoguePanel.offsetMax.x, 0f);
    }

    private float GetParentHeight()
    {
        if (dialoguePanel == null) return 0f;
        var parent = dialoguePanel.parent as RectTransform;
        return parent != null ? parent.rect.height : 0f;
    }

    private List<string> SplitIntoSyllables(string text)
    {
        List<string> syllables = new List<string>();

        foreach (char c in text)
        {
            // 한글 (AC00–D7A3)
            if (c >= 0xAC00 && c <= 0xD7A3)
            {
                syllables.Add(c.ToString());
            }
            else if (char.IsLetterOrDigit(c) || char.IsPunctuation(c) || char.IsWhiteSpace(c))
            {
                if (char.IsWhiteSpace(c))
                {
                    if (syllables.Count > 0)
                        syllables[syllables.Count - 1] += c;
                    else
                        syllables.Add(c.ToString());
                }
                else
                {
                    syllables.Add(c.ToString());
                }
            }
            else
            {
                syllables.Add(c.ToString());
            }
        }

        return syllables;
    }

    private int CountSyllables(string text)
    {
        int count = 0;
        foreach (char c in text)
        {
            if (c >= 0xAC00 && c <= 0xD7A3)
                count++;
            else if (!char.IsWhiteSpace(c))
                count++;
        }
        return Mathf.Max(1, count);
    }
}