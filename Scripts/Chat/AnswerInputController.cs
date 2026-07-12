using UnityEngine;
using UnityEngine.UI;
using TMPro;


public class AnswerInputController : MonoBehaviour
{
    [Header("UI")]
    [SerializeField] private TMP_InputField answerInput;
    [SerializeField] private Button sendButton;

    [Header("Options")]
    [SerializeField] private bool clearOnSubmit = true;
    [SerializeField] private bool refocusAfterSubmit = true;

    private void Start()
    {
        if (sendButton != null)
            sendButton.onClick.AddListener(Submit);

        if (answerInput != null)
            answerInput.onSubmit.AddListener(_ => Submit()); // 엔터

        UpdateSendButtonState();
    }

    private void Update()
    {
        UpdateSendButtonState();
    }

    private void OnDestroy()
    {
        if (sendButton != null)
            sendButton.onClick.RemoveListener(Submit);
    }

    private void Submit()
    {
        if (answerInput == null) return;

        string text = answerInput.text?.Trim();
        if (string.IsNullOrEmpty(text)) return;

        if (ChatHistoryManager.Instance != null)
            ChatHistoryManager.Instance.AddUserEntry(text);

        if (clearOnSubmit)
            answerInput.text = "";

        if (refocusAfterSubmit)
        {
            answerInput.ActivateInputField();
            answerInput.Select();
        }
    }




    private void UpdateSendButtonState()
    {
        if (sendButton == null || answerInput == null) return;
        bool hasText = !string.IsNullOrWhiteSpace(answerInput.text);
        sendButton.interactable = hasText;
    }

    public void SetInteractable(bool interactable)
    {
        if (answerInput != null) answerInput.interactable = interactable;
        if (sendButton != null) sendButton.interactable = interactable;
    }
}