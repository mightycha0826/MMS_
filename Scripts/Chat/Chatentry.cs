using System;

[Serializable]
public class ChatEntry
{
    public enum SpeakerType { Interviewer, User }

    public SpeakerType speaker;
    public string text;
    public DateTime timestamp;

    public ChatEntry(SpeakerType speaker, string text)
    {
        this.speaker = speaker;
        this.text = text;
        this.timestamp = DateTime.Now;
    }

    public string GetTimeString() => timestamp.ToString("HH:mm");
}