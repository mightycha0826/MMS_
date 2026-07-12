using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

/// <summary>
/// Supertone TTS를 Unity에서 직접 호출하는 클라이언트.
/// relay는 더 이상 TTS를 처리하지 않으므로, content.text를 받은 직후 여기서 오디오를 생성/재생한다.
///
/// API 키는 StreamingAssets/{configFileName} (기본값: supertone_config.json)에서 런타임에 읽어온다.
/// 이 파일은 git에 커밋하지 않는다 (.gitignore 처리됨) — supertone_config.sample.json을 복사해서 채워 넣을 것.
/// WebGL 빌드 특성상 클라이언트에 노출되는 API 키 자체를 막을 수는 없으므로,
/// Supertone 콘솔에서 도메인 제한/사용량 제한을 걸어두는 걸 권장한다.
/// </summary>
public class SupertoneTtsClient : MonoBehaviour
{
    [Header("Supertone 설정 파일 (StreamingAssets, git-ignored)")]
    [SerializeField] private string configFileName = "supertone_config.json";

    [Header("오디오 재생")]
    [SerializeField] private AudioSource audioSource;

    private const string ApiBaseUrl = "https://supertoneapi.com/v1/text-to-speech";

    private string _apiKey;
    private string _voiceId;
    private bool _configLoaded;
    private bool _configLoadAttempted;

    /// <summary>주어진 텍스트로 TTS 오디오를 생성해 재생한다. 실패해도 예외를 던지지 않고 조용히 건너뛴다.</summary>
    public void PlayTts(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        StartCoroutine(PlayTtsRoutine(text));
    }

    private IEnumerator PlayTtsRoutine(string text)
    {
        if (!_configLoaded && !_configLoadAttempted)
            yield return StartCoroutine(LoadConfig());

        if (!_configLoaded)
        {
            Debug.LogWarning("[SupertoneTtsClient] Supertone 설정이 없어 TTS를 건너뜁니다 (자막만 표시됨).");
            yield break;
        }

        string url = $"{ApiBaseUrl}/{_voiceId}/stream";
        string bodyJson = JsonUtility.ToJson(new TtsRequestPayload
        {
            text = text,
            language = "ko",
            model = "sona_speech_1",
        });
        byte[] bodyRaw = System.Text.Encoding.UTF8.GetBytes(bodyJson);

        using (var req = new UnityWebRequest(url, "POST"))
        {
            req.uploadHandler = new UploadHandlerRaw(bodyRaw);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "application/json");
            req.SetRequestHeader("Accept", "audio/wav");
            req.SetRequestHeader("x-sup-api-key", _apiKey);

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                // 네트워크 오류, voice 접근 불가, CORS 차단 등 - 면접 진행을 막지 않도록 자막만 표시된 상태로 계속 진행
                Debug.LogWarning($"[SupertoneTtsClient] TTS 요청 실패 ({req.responseCode}): {req.error} — 자막만 표시됩니다.");
                yield break;
            }

            PlayWav(req.downloadHandler.data);
        }
    }

    private void PlayWav(byte[] wavBytes)
    {
        if (audioSource == null)
        {
            Debug.LogWarning("[SupertoneTtsClient] audioSource가 지정되지 않아 오디오 재생을 건너뜁니다.");
            return;
        }

        AudioClip clip = TryDecodeWav(wavBytes);
        if (clip == null)
        {
            Debug.LogWarning("[SupertoneTtsClient] WAV 디코딩 실패 — 자막만 표시됩니다.");
            return;
        }

        audioSource.clip = clip;
        audioSource.Play();
    }

    // ─── 설정 로드 (StreamingAssets) ───────────────────────────────────────

    private IEnumerator LoadConfig()
    {
        _configLoadAttempted = true;

        string url = Path.Combine(Application.streamingAssetsPath, configFileName);
        if (!url.Contains("://"))
            url = "file://" + url;

        using (var req = UnityWebRequest.Get(url))
        {
            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[SupertoneTtsClient] 설정 파일을 불러오지 못했습니다 ({url}): {req.error}");
                yield break;
            }

            try
            {
                var config = JsonUtility.FromJson<SupertoneConfig>(req.downloadHandler.text);
                if (config == null || string.IsNullOrEmpty(config.apiKey) || string.IsNullOrEmpty(config.voiceId))
                {
                    Debug.LogWarning("[SupertoneTtsClient] 설정 파일에 apiKey/voiceId가 비어 있습니다.");
                    yield break;
                }

                _apiKey = config.apiKey;
                _voiceId = config.voiceId;
                _configLoaded = true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SupertoneTtsClient] 설정 파싱 실패: {e.Message}");
            }
        }
    }

    // ─── WAV -> AudioClip (PCM16) ──────────────────────────────────────────

    private static AudioClip TryDecodeWav(byte[] wav)
    {
        try
        {
            if (wav == null || wav.Length < 44) return null;

            int channels = BitConverter.ToInt16(wav, 22);
            int sampleRate = BitConverter.ToInt32(wav, 24);
            int bitsPerSample = BitConverter.ToInt16(wav, 34);

            int dataChunkOffset = FindChunkOffset(wav, "data");
            if (dataChunkOffset < 0 || bitsPerSample != 16)
            {
                Debug.LogWarning($"[SupertoneTtsClient] 지원하지 않는 WAV 형식 (bits={bitsPerSample})");
                return null;
            }

            int dataSize = BitConverter.ToInt32(wav, dataChunkOffset - 4);
            int sampleCount = dataSize / 2;
            float[] samples = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                short raw = BitConverter.ToInt16(wav, dataChunkOffset + i * 2);
                samples[i] = raw / 32768f;
            }

            AudioClip clip = AudioClip.Create("supertone_tts", sampleCount / Mathf.Max(channels, 1), channels, sampleRate, false);
            clip.SetData(samples, 0);
            return clip;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SupertoneTtsClient] WAV 파싱 중 예외: {e.Message}");
            return null;
        }
    }

    private static int FindChunkOffset(byte[] data, string chunkId)
    {
        byte[] id = System.Text.Encoding.ASCII.GetBytes(chunkId);
        for (int i = 12; i <= data.Length - 8; i++)
        {
            if (data[i] == id[0] && data[i + 1] == id[1] && data[i + 2] == id[2] && data[i + 3] == id[3])
                return i + 8;
        }
        return -1;
    }

    // ─── 내부 타입 ──────────────────────────────────────────────────────────

    [Serializable] private class SupertoneConfig { public string apiKey; public string voiceId; }
    [Serializable] private class TtsRequestPayload { public string text; public string language; public string model; }
}
