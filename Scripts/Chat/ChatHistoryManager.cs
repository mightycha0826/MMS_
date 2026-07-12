using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public class ChatHistoryManager : MonoBehaviour
{
    public static ChatHistoryManager Instance { get; private set; }

    [SerializeField] private ChatHistorySidebar sidebar;

    private readonly List<ChatEntry> entries = new List<ChatEntry>();

    public UnityEvent<ChatEntry> OnEntryAdded = new UnityEvent<ChatEntry>();

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else if (Instance != this) Destroy(gameObject);
    }

    public void AddInterviewerEntry(string text)
    {
        var entry = new ChatEntry(ChatEntry.SpeakerType.Interviewer, text);
        entries.Add(entry);
        OnEntryAdded?.Invoke(entry);
    }

    public void AddUserEntry(string text)
    {
        var entry = new ChatEntry(ChatEntry.SpeakerType.User, text);
        entries.Add(entry);
        OnEntryAdded?.Invoke(entry);
    }

    public IReadOnlyList<ChatEntry> GetAllEntries() => entries;

    public void ClearHistory()
    {
        entries.Clear();
        if (sidebar != null) sidebar.ClearAllBubbles();
    }
}