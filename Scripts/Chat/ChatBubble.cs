using UnityEngine;
using TMPro;

public class ChatBubble : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI bubbleText;
    [SerializeField] private TextMeshProUGUI timeText;

    public void SetData(ChatEntry entry)
    {
        if (bubbleText != null) bubbleText.text = entry.text;
        if (timeText != null) timeText.text = entry.GetTimeString();
    }
}