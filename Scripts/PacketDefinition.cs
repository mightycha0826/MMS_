using System;

// ─── Unity -> Worker (보내는 메시지) ───────────────────────────
[Serializable]
public class ClientMsg
{
    public string last_question; 
    public string text;
}

// ─── Worker -> Unity (받는 메시지) ───────────────────────────
[Serializable]
public class ServerMsg
{
    public string type; // "ready", "processing", "server_content", "error"
    public string message_id;
    public ServerContent content;
    public string message; // error용
}

[Serializable]
public class ServerContent
{
    public string text;
    public string decision; // "follow_up" or "next_topic"
    public string emotionLabel;
}
